// RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// Part of a rewritten derivative of KerBalloons (JoePatrick1; KerBalloons Reinflated (c) 2018 Fengist), MIT.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealBalloons
{
    public partial class ModuleRealBalloon
    {
        // ---- Envelope aerodynamics ----
        // The envelope is only a visual, so KSP's own aerodynamics never sees it. Model it as a
        // sphere holding the gas's volume: the fill at the local pressure and temperature, up to
        // the envelope's full size. It grows as the balloon climbs into thinner air, like a real one.
        //  - Drag 0.5 rho v^2 Cd A against the air-relative velocity. With a rope it acts where the
        //    rope is tied (a rope carries no torque); with the fixed mount, at the envelope's centre.
        //    A blimp hull is streamlined: nose-on it has an airship's drag (volumetric coefficient on
        //    V^(2/3), 0.023 for a long hull, rising to the sphere's for a stubby one), broadside the
        //    sphere's coefficient on the hull's side area.
        //  - Skin friction on the turning envelope damps spin (about the vertical only with a rope).
        //  - Airflow tears the envelope once its dynamic pressure, 0.5 rho v^2, passes the envelope's
        //    tearPressure: the film bulges between load tapes about 2 m apart, so its stress goes with
        //    q times that span, whatever the envelope's size or shape. The same q is a slower airspeed in
        //    dense air: standard film (400 Pa) takes 25 m/s at sea level, 44 m/s at 10 km, 93 m/s at 20 km,
        //    about a real blimp's top speed low down. Climbing counts: a big balloon with much more lift
        //    than load climbs fast enough to tear, as real crews avoid by putting in less gas.
        // Each force is limited so it can't remove more than half the motion in one physics step,
        // which keeps light craft stable under big envelopes.
        const double envelopeCd = 0.4;
        const double sphereCdV = 0.4836;          // 0.4 on a sphere's cross-section, as a coefficient on V^(2/3)
        const double hullCdV = 0.023;             // a long airship hull nose-on (Hoerner)
        const double spinCm = 0.03;

        // Drag area (Cd x area, m^2) climbing straight up with a gas bubble of radius r: a blimp hull
        // climbs broadside (the lateral drag below), anything else as a sphere.
        public double ClimbCdA(double r)
        {
            double a = envelopeCd * System.Math.PI * r * r;
            return Hull ? a * System.Math.Pow(blimpStretch, 1.0 / 3.0) : a;
        }
        [System.NonSerialized] public double envelopeRadius;     // m, for the info window
        [System.NonSerialized] public double envelopeDragKN, envelopeAirspeed, envelopeLiftKN, envelopeQ, envelopeRho;
        Renderer[] envelopeRenderers;

        // What you see is the gas it holds: the world scale at which the envelope's mesh has the volume of
        // its gas (the fill at the local pressure and temperature, up to the envelope's full size). So it
        // starts as a small bubble on the pad and swells as it climbs, and envelopes for dense air
        // (Venus) are as small as their gas. 0 if there's no gas model (it then just grows with inflation).
        float GasWorldScale(float progress)
        {
            bool preview = HullPreview;                              // the VAB's hull preview: the whole hull
            if (envelopeVolume <= 0f || part == null || (!preview && (vessel == null || vessel.mainBody == null))) return 0f;
            if (profileR == null) BuildProfile();
            if (!(profileVolume > 0f)) return 0f;
            progress = Mathf.Clamp01(progress);
            double v;
            if (preview) v = ShapeVolume(0, progress);
            else
            {
                double p = FlightGlobals.getStaticPressure(part.transform.position);
                v = p > 0 ? RealBalloonPhysics.GasVolume(this, p, RealBalloonPhysics.TemperatureHere(this), progress)
                          : RealBalloonPhysics.EnvelopeVolume(this) * progress;
                v = ShapeVolume(v, progress);                        // a blimp with ballonets: the whole hull
            }
            if (!(v > 0)) return 1e-3f;
            return Mathf.Max(1e-3f, (float)System.Math.Pow(v / profileVolume, 1.0 / 3.0));
        }

        // The centre of buoyancy: the centre of the envelope's volume, on its axis. The teardrop is
        // fuller towards its round end, so this is a little past the middle of its length (55% from the
        // point), about where it's widest.
        Vector3 EnvelopeCentre()
        {
            if (profileR == null) BuildProfile();
            if (profileR.Length > 1) return balloonObject.transform.TransformPoint(new Vector3(0f, 0f, profileCentroid));
            if (envelopeRenderers == null)
                envelopeRenderers = balloonObject.GetComponentsInChildren<Renderer>(true).Where(r => !OnFlag(r.transform)).ToArray();
            Bounds b = new Bounds();
            bool any = false;
            foreach (Renderer r in envelopeRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : balloonObject.transform.position;
        }

        // ---- Fixed mounts: the envelope's own inertia ----
        // On the fixed mounts the envelope turns with the craft, but KSP only knows the small part it
        // sits on. The real envelope (its fabric, its gas and the air it pushes aside) and a keel weight
        // far below make the craft far harder to turn. Every turning force the balloon makes on a fixed
        // mount (lift, drag, the weights' true positions, damping) is applied scaled by the share of the
        // real inertia that KSP can see, so the craft sways at the real, slow rate instead of shaking.
        // The sway is never slower than one swing per swayPeriodCap seconds, so the balloon still
        // rights the craft against reaction wheels or wind on the payload, which KSP applies in full.
        const double swayPeriodCap = 4.0;
        [System.NonSerialized] public double swayInertia;     // t m^2 this envelope adds (summed by the others)
        float swayScale = 1f;
        double swayStiffness, swayVesselInertia;              // kN m/rad as applied, t m^2
        double swayPhys = 1;                                  // share of the real inertia KSP sees
        Vector3 frameCoM;

        void ApplyLiftAndAero(float lift, float pressure, float progress)
        {
            frameCoM = VesselCoMNow();
            Vector3 liftAt = LiftPosition();
            swayScale = rigidMount ? SwayScale(lift, progress, liftAt) : 1f;
            part.AddForceAtPosition(vessel.upAxis * lift, Scaled(liftAt));
            if (rigidMount) ApplyMassCorrection();
            ApplyBallonetTrim(lift, pressure, progress);
            ApplyEnvelopeAero(lift, pressure, progress);
        }

        Vector3 Scaled(Vector3 p) { return swayScale >= 1f ? p : frameCoM + (p - frameCoM) * swayScale; }

        // This physics step's centre of mass, from the rigidbodies themselves.
        Vector3 VesselCoMNow()
        {
            Vector3 s = Vector3.zero;
            double m = 0;
            foreach (Part p in vessel.parts)
            {
                if (p == null || p.rb == null) continue;
                float pm = p.rb.mass;
                s += p.rb.worldCenterOfMass * pm;
                m += pm;
            }
            return m > 0 ? s / (float)m : vessel.CoM;
        }

        static double Sq(double x) { return x * x; }

        float SwayScale(float liftKN, float progress, Vector3 liftAt)
        {
            swayInertia = 0;
            swayStiffness = 0;
            swayVesselInertia = 0;
            swayPhys = 1;
            if (vessel == null || part.rb == null) return 1f;
            Vector3 up = vessel.upAxis, com = frameCoM, pc = part.rb.worldCenterOfMass;
            Vector3 pe = EnvelopeCentre();
            double de2 = (pe - com).sqrMagnitude;
            double r = envelopeRadius > 0 ? envelopeRadius : System.Math.Max(0.1, -LowestPointAlong(up, pe));
            double rho = part.atmDensity > 0 ? part.atmDensity : 0;
            double gasT = envelopeVolume > 0 ? RealBalloonPhysics.GasMoles(this) * progress * RealBalloonPhysics.GasMolarMass(liftGas) / 1e6 : 0;
            double airT = 0.5 * rho * (4.0 / 3.0 * System.Math.PI * r * r * r) / 1000.0;     // added mass of a sphere
            if (BallonetsFull && rho > 0)                                                     // the ballonets' air turns with it
            {
                double pk = FlightGlobals.getStaticPressure(part.transform.position);
                double vg = pk > 0 ? RealBalloonPhysics.GasVolume(this, pk, RealBalloonPhysics.TemperatureHere(this), progress) : 0;
                gasT += rho * System.Math.Max(0, ShapeVolume(vg, progress) - vg) / 1000.0;
            }
            double I = BalloonMass * (2.0 / 3.0 * r * r + de2) + gasT * (0.4 * r * r + de2) + airT * de2;
            Vector3 top, end = pc;
            bool keel = CounterweightMass > 0 && KeelPoints(out top, out end);
            if (keel) I += CounterweightMass * (end - com).sqrMagnitude;
            swayInertia = I;

            // the craft's own inertia about the horizontal, as KSP has it
            Transform rf = vessel.ReferenceTransform;
            if (rf == null) return 1f;
            Vector3 moi = vessel.MOI;
            double iu = moi.x * Sq(Vector3.Dot(up, rf.right)) + moi.y * Sq(Vector3.Dot(up, rf.up)) + moi.z * Sq(Vector3.Dot(up, rf.forward));
            double iv = 0.5 * (moi.x + moi.y + moi.z - iu);
            if (!(iv > 1e-6)) return 1f;
            swayVesselInertia = iv;

            double iAll = 0;
            int n = 0;
            foreach (ModuleRealBalloon b in RealBalloonPhysics.InflatedBalloons(vessel))
                if (b.rigidMount) { iAll += b == this ? I : b.swayInertia; n++; }
            if (n == 0) { n = 1; iAll = I; }
            double f = iv / (iv + iAll);
            swayPhys = f;

            // how hard this balloon's forces right the craft, per radian of tilt
            double g = FlightGlobals.getGeeForceAtPosition(com).magnitude;
            double k = liftKN * Vector3.Dot(liftAt - com, up) + BalloonMass * g * Vector3.Dot(pc - pe, up);
            if (keel) k += CounterweightMass * g * Vector3.Dot(pc - end, up);
            // the hull's fins hold its pitch too, stiffly at speed: counted, so it doesn't swing quicker then
            double kAll = k + (Hull && finsAero ? aeroPitchStiffness : 0);
            if (kAll * n > 0)
            {
                double w = 2.0 * System.Math.PI / swayPeriodCap;
                f = System.Math.Max(f, iv * w * w / (kAll * n));
            }
            f = System.Math.Min(1.0, System.Math.Max(0.0, f));
            swayStiffness = f * k;
            return (float)f;
        }

        // KSP holds all of the part's mass at the part. The envelope's fabric really hangs at the
        // envelope and the counterweight at the end of the keel: add the turning force that makes.
        void ApplyMassCorrection()
        {
            if (part.rb == null) return;
            Vector3 pc = part.rb.worldCenterOfMass;
            Vector3 arm = (EnvelopeCentre() - pc) * BalloonMass;
            Vector3 top, end;
            if (CounterweightMass > 0 && KeelPoints(out top, out end)) arm += (end - pc) * CounterweightMass;
            Vector3 tq = Vector3.Cross(arm, FlightGlobals.getGeeForceAtPosition(pc)) * swayScale;
            if (tq.sqrMagnitude > 0) part.AddTorque(tq);
        }

        // Where the craft's centre of mass really is, with the envelope and counterweight in place.
        Vector3 TrueCoM()
        {
            Vector3 com = VesselCoMNow();
            float vm = vessel.GetTotalMass();
            if (part.rb == null || vm <= 0) return com;
            Vector3 pc = part.rb.worldCenterOfMass;
            Vector3 arm = rigidMount ? (EnvelopeCentre() - pc) * BalloonMass : Vector3.zero;
            Vector3 top, end;
            if (CounterweightMass > 0 && KeelPoints(out top, out end)) arm += (end - pc) * CounterweightMass;
            return com + arm / vm;
        }

        void ApplyEnvelopeAero(float liftKN, float pressure, float progress)
        {
            envelopeRadius = 0;
            envelopeDragKN = 0;
            envelopeAirspeed = 0;
            envelopeQ = 0;
            envelopeLiftKN = liftKN;
            finsAero = false;
            if (liftKN <= 0 || vessel == null || part.rb == null || vessel.mainBody == null) return;
            double rho = part.atmDensity;
            if (rho <= 0 && pressure > 0)
                rho = FlightGlobals.getAtmDensity(pressure,
                    FlightGlobals.getExternalTemperature(part.transform.position, vessel.mainBody), vessel.mainBody);
            double g = FlightGlobals.getGeeForceAtPosition(part.transform.position).magnitude;
            double mAir = vessel.mainBody.atmosphereMolarMass * 1000.0;
            double mGas = RealBalloonPhysics.GasMolarMass(liftGas);
            if (mGas <= 0) mGas = 2.016;
            if (rho <= 1e-6 || g <= 0 || mAir <= mGas) return;
            envelopeRho = rho;

            double volume = envelopeVolume > 0
                ? ShapeVolume(RealBalloonPhysics.GasVolume(this, pressure, RealBalloonPhysics.TemperatureHere(this), progress), progress)
                : liftKN * 1000.0 / (g * rho * (1.0 - mGas / mAir));
            double r = System.Math.Pow(volume * 3.0 / (4.0 * System.Math.PI), 1.0 / 3.0);
            envelopeRadius = r;
            double dt = TimeWarp.fixedDeltaTime;
            int n = Mathf.Max(1, RealBalloonPhysics.InflatedBalloons(vessel).Count);
            Vector3 moi = vessel.MOI;
            double minMoi = Mathf.Min(moi.x, Mathf.Min(moi.y, moi.z));

            // Drag: from the craft's own velocity through the air (so rocking can't feed back into
            // it), acting where the envelope pulls: the rope's tie point, or the envelope's centre on
            // the fixed mount. A steady sideways airflow still tilts the craft the real way.
            Vector3 com = frameCoM;
            Vector3 point = rigidMount ? EnvelopeCentre() : liftPointObject.transform.position;
            Vector3 v = part.rb.GetPointVelocity(com) + (Vector3)Krakensbane.GetFrameVelocity();
            float speed = v.magnitude;
            envelopeAirspeed = speed;
            double area = System.Math.PI * r * r;
            double M = System.Math.Max(vessel.totalMass, 1e-3);
            if (speed > 0.01f)
            {
                Vector3 drag;      // kN
                if (Hull)
                {
                    Vector3 axis = HullAxis();
                    Vector3 vAx = Vector3.Project(v, axis), vLat = v - vAx;
                    double st = blimpStretch;
                    double cdv = hullCdV + (sphereCdV - hullCdV) / (st * st * st);      // the sphere's at stretch 1
                    double dAx = 0.5 * rho * vAx.sqrMagnitude * cdv * System.Math.Pow(volume, 2.0 / 3.0) / 1000.0;
                    double dLat = 0.5 * rho * vLat.sqrMagnitude * envelopeCd * area * System.Math.Pow(st, 1.0 / 3.0) / 1000.0;
                    drag = Vector3.zero;
                    if (vAx.sqrMagnitude > 1e-8f) drag -= vAx.normalized * (float)dAx;
                    if (vLat.sqrMagnitude > 1e-8f) drag -= vLat.normalized * (float)dLat;
                }
                else drag = -v / speed * (float)(0.5 * rho * speed * speed * envelopeCd * area / 1000.0);
                double dragKN = drag.magnitude;
                double q = 0.5 * rho * speed * speed;                      // Pa
                if (q > TearPressure)
                {
                    ScreenMessages.PostScreenMessage("Balloon torn at " + speed.ToString("F0") + " m/s: dynamic pressure " + q.ToString("F0") + "/" +
                        TearPressure.ToString("F0") + " Pa (tears at " + System.Math.Sqrt(2 * TearPressure / rho).ToString("F0") + " m/s here)",
                        6, ScreenMessageStyle.UPPER_CENTER);
                    Burst();
                    return;
                }
                envelopeQ = q;
                double maxKN = 0.5 * M * speed / (dt * n);                          // never reverses the motion
                if (dragKN > maxKN) { drag *= (float)(maxKN / dragKN); dragKN = maxKN; }
                envelopeDragKN = dragKN;
                part.AddForceAtPosition(drag, rigidMount ? Scaled(point) : point);
            }

            // Rocking and spin, as linear dampers whose strength is capped so one physics step can
            // take off at most half the turn rate: they can't overshoot, so they can't vibrate.
            //  - rocking about the tie point: the envelope dragged sideways on a lever of length d
            //  - spin about that line: skin friction on the turning envelope
            if (minMoi > 0)
            {
                Vector3 w = part.rb.angularVelocity;
                Vector3 rv = point - com;
                float d = rv.magnitude;
                Vector3 axis = d > 0.5f ? rv / d : (Vector3)vessel.upAxis;
                Vector3 wPar = Vector3.Project(w, axis);
                Vector3 wPerp = w - wPar;
                double cMax = 0.5 * minMoi / (dt * n);                                   // kN m s
                double cRock = 0.5 * rho * envelopeCd * area * (speed + wPerp.magnitude * d) * d * d / 1000.0;
                if (rigidMount)
                {
                    // scaled so the sway keeps the real damping ratio when it's sped up (the righting
                    // force is scaled by swayScale, the inertia by swayPhys), plus a little steady
                    // damping (5% of critical) so a small sway dies away instead of rocking on
                    cRock *= System.Math.Sqrt(swayScale * swayPhys);
                    if (swayStiffness > 0 && swayVesselInertia > 0)
                        cRock += 0.1 * System.Math.Sqrt(swayStiffness * n * swayVesselInertia) / n;
                }
                double cSpin = spinCm * 0.5 * rho * System.Math.Pow(r, 5) * wPar.magnitude / 1000.0;
                Vector3 torque = -(float)System.Math.Min(cRock, cMax) * wPerp - (float)System.Math.Min(cSpin, cMax) * wPar;
                if (torque.sqrMagnitude > 0) part.AddTorque(torque);
            }

            if (Hull) ApplyHullAero(rho, volume, pressure, progress);
        }

        // Keel geometry (world): from the envelope's far end, straight on along the part's axis (away
        // from the craft), ending keelLength envelope radii from the envelope's centre. The radius is the
        // round envelope's, before the cushion squash. Scaled by how far the keel has deployed.
        // Cushion keel tearing (a rule of cool, like the cushion): the keel's suspension lines fan out to a load
        // ring under the envelope. Tipped past keelTearAngle from upright, its whole weight ends up on the lines
        // on one side, and they tear the envelope. Only once the keel is mostly out, and only if it stays tipped
        // for half a second, so a jolt doesn't. A warning comes 15 degrees before.
        [KSPField] public float keelTearAngle = 60f;     // degrees
        [System.NonSerialized] public float cushionTilt;
        float tiltHeld, tiltWarnAt;

        void CheckKeelTilt()
        {
            cushionTilt = 0;
            if (!Cushion || CounterweightMass <= 0 || keelProgress < 0.5f || inflation < 0.5f || vessel == null) { tiltHeld = 0; return; }
            cushionTilt = Vector3.Angle(part.transform.up, -vessel.upAxis);       // the keel hangs along the part's up
            if (cushionTilt > keelTearAngle)
            {
                tiltHeld += TimeWarp.fixedDeltaTime;
                if (tiltHeld < 0.5f) return;
                ScreenMessages.PostScreenMessage("Balloon torn by its keel: the cushion tipped " + cushionTilt.ToString("F0") + " degrees, past " +
                    keelTearAngle.ToString("F0"), 6, ScreenMessageStyle.UPPER_CENTER);
                Burst();
                return;
            }
            tiltHeld = 0;
            if (cushionTilt > keelTearAngle - 15f && Time.time > tiltWarnAt)
            {
                tiltWarnAt = Time.time + 10f;
                ScreenMessages.PostScreenMessage("Cushion tipping: " + cushionTilt.ToString("F0") + " degrees; its keel tears it past " +
                    keelTearAngle.ToString("F0"), 4, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        bool KeelPoints(out Vector3 top, out Vector3 end)
        {
            top = end = Vector3.zero;
            if (!Cushion || balloonObject == null) return false;
            Vector3 dir = part.transform.up;
            Vector3 c = EnvelopeCentre();
            float half = -LowestPointAlong(-dir, c);                    // centre to the far end
            float near = -LowestPointAlong(dir, c);                     // centre to the near end
            if (half <= 0.01f) return false;
            float r = 0.5f * (half + Mathf.Max(0f, near)) / Mathf.Pow(CushionShape, 2f / 3f);
            float len = Mathf.Max(half, keelLength * r);
            top = c + dir * half;
            end = c + dir * (half + (len - half) * Mathf.Clamp01(keelProgress));
            return true;
        }

        void UpdateKeelVisual()
        {
            Vector3 top, end;
            bool show = KeelPoints(out top, out end) && keelProgress > 0.005f && CounterweightMass > 0;
            keelShown = show;
            if (ropeObject != null)
            {
                if (ropeRenderers == null)
                    ropeRenderers = ropeObject.GetComponentsInChildren<Renderer>(true)
                        .Where(r => !r.transform.IsChildOf(balloonObject.transform)).ToArray();
                foreach (Renderer r in ropeRenderers) if (r != null && r.enabled != show) r.enabled = show;
                if (show)
                {
                    Transform rt = ropeObject.transform;
                    rt.position = top;
                    rt.rotation = Quaternion.LookRotation(part.transform.up, part.transform.forward);
                    float want = Vector3.Distance(top, end), have = RopeLengthNow();
                    Vector3 ls = rt.localScale;
                    if (have > 1e-3f && ls.z > 1e-4f) ls.z *= want / have; else ls.z = 1f;
                    rt.localScale = new Vector3(1f, 1f, Mathf.Clamp(ls.z, 1e-3f, 1e4f));
                }
            }
            if (keelWeight == null && show && capObject != null)
            {
                // a copy of the balloon's cap, sized to the weight, outside the part's model so
                // nothing else (FAR, highlighting) treats it as part of the part
                keelWeight = Instantiate(capObject, capObject.transform.parent, false);
                keelWeight.name = "RBCounterweight";
                foreach (Collider col in keelWeight.GetComponentsInChildren<Collider>(true)) { col.enabled = false; Destroy(col); }
                keelWeight.transform.SetParent(part.transform, true);
                float size = Mathf.Clamp(Mathf.Pow(CounterweightMass / 0.5f, 1f / 3f), 0.5f, 4f);
                keelWeight.transform.localScale *= size;
            }
            if (keelWeight != null)
            {
                if (keelWeight.activeSelf != show) keelWeight.SetActive(show);
                if (show)
                {
                    // hang it by its top from the end of the tether
                    Transform wt = keelWeight.transform;
                    wt.position = end;
                    if (weightRenderers == null) weightRenderers = keelWeight.GetComponentsInChildren<Renderer>(true);
                    Bounds b = new Bounds();
                    bool any = false;
                    foreach (Renderer r in weightRenderers)
                    {
                        if (r == null) continue;
                        if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                    }
                    if (any)
                    {
                        Vector3 dir = part.transform.up;
                        float half = Mathf.Abs(b.extents.x * dir.x) + Mathf.Abs(b.extents.y * dir.y) + Mathf.Abs(b.extents.z * dir.z);
                        wt.position += (end + dir * half) - b.center;
                    }
                }
            }
        }

        // Current length of the rope's meshes along its own axis (world units).
        float RopeLengthNow()
        {
            Transform rt = ropeObject.transform;
            float max = 0f;
            foreach (MeshFilter mf in ropeObject.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || mf.transform.IsChildOf(balloonObject.transform)) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    max = Mathf.Max(max, Vector3.Dot(m.MultiplyPoint3x4(c) - rt.position, rt.forward));
                }
            }
            return max;
        }

    }
}
