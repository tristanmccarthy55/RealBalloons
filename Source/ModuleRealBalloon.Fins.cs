// RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// Part of a rewritten derivative of KerBalloons (JoePatrick1; KerBalloons Reinflated (c) 2018 Fengist), MIT.

using UnityEngine;

namespace RealBalloons
{
    // ---- Blimp hull aerodynamics: tail fins, rudders and elevators, and the hull's own turning moment ----
    // The hull's tail fins are lifting surfaces of the size they're drawn (they grow with the gas and stretch
    // with the hull), and their rear parts are rudders (top and bottom) and elevators (the sides). Everything
    // goes with the dynamic pressure, so they steer at cruise and do nothing at a hover, as on a real blimp.
    //  - Each fin feels the air where it is, including the tail swinging as the hull turns: that keeps the
    //    hull pointing along its path and damps its turning. Lift slope of a low fin on the hull (the hull
    //    doubling its aspect ratio), raised 30% by the hull's upwash at its root (Pitts, Nielsen & Kaattari),
    //    straight to about 15 degrees and levelling off to a flat plate's broadside; flaps a third of the fin.
    //  - A streamlined hull at an angle to its path turns further off it (the Munk moment, from the added
    //    masses of a prolate spheroid of its length and width), about 60% of the ideal value as measured on
    //    real hulls, and falling off at large angles as the flow leaves its tail. The fins as drawn
    //    outweigh it: the hull is stable, like most blimps.
    //  - The hull's own crossflow drag as it turns: each length of it pushed sideways through the air as the
    //    nose and tail swing (Allen & Perkins' crossflow drag, eta Cdc 0.5), so it's damped in pitch and yaw
    //    even at a hover, where the fins do nothing; quadratic, and more so while it drifts sideways too.
    //  - The controls deflect each surface towards the turn asked for, as KSP's own control surfaces do (from
    //    the craft's control point, SAS and trim included), up to 20 degrees at 40 degrees a second, the
    //    elevators and rudders each limited by its own authority slider; pitch and yaw only, as on a real
    //    blimp, which the gondola rights in roll. A flap of a third of the fin on a
    //    low fin, losing a quarter of its bite at full throw as the flow leaves it: full deflection adds
    //    about half a fin's broadside force, a real elevator's. No charge.
    // The envelope, its gas and the air in and around it weigh far more than KSP's parts. About the vertical
    // the hull turns against its whole inertia (fabric, gas, ballonet air and the air it carries round, a
    // prolate spheroid's); about the horizontal these forces are scaled as the gondola's righting is, so the
    // two weigh against each other truly, and the sway's 4 s swing counts the fins' stiffness, so the hull
    // swings no quicker at speed than at a hover; the push by the share of the real mass. They're spread
    // over the craft's parts as it would move whole, so no joint takes them, and the fins' damping is taken
    // implicitly against the craft's inertia and its own turn rate, so it can't overshoot.
    public partial class ModuleRealBalloon : ITorqueProvider
    {
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Elevators", guiUnits = "%"),
         UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 5f)]
        public float elevatorAuthority = 100f;
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Rudders", guiUnits = "%"),
         UI_FloatRange(minValue = 0f, maxValue = 100f, stepIncrement = 5f)]
        public float rudderAuthority = 100f;

        const float flapMaxDeg = 20f, flapRateDeg = 40f;
        const double flapEffect = 0.45;            // a flap of a third of a low fin, viscous losses included
        const double flapFullLoss = 0.25;          // the share of that lost at full throw, as the flow separates
        const double finCnMax = 1.2;               // a flat fin broadside
        const double finUpwash = 1.3;              // the hull speeding the crossflow at the fins' roots
        const double hullMomentShare = 0.6;        // the hull's real turning moment against the ideal (Munk) one
        const double finMaxTurnPerStep = 0.3;      // rad/s: a last guard on what one physics step can do
        const double hullCrossflowCd = 0.5;        // eta Cdc: a cylinder's crossflow drag, less for its finite length

        readonly float[] flapDeg = new float[4];
        readonly Vector3[] flapTorque = new Vector3[4];     // kN m at full deflection, as applied (for SAS)
        bool finsAero;
        [System.NonSerialized] public double aeroPitchStiffness;      // kN m/rad, for SwayScale
        double finArea = -1, finCz, finCr, finCNa;          // one fin as exposed, envelope frame (model units)

        void UpdateFinFields()
        {
            bool hull = Hull;
            if (!trimLevel) { if (trimMode == TrimLevel) trimMode = TrimHold; trimLevel = true; }   // a 1.0 save's "Hold"
            if (trimMode != TrimLevel && trimMode != TrimHold && trimMode != TrimOff) trimMode = TrimLevel;
            foreach (string name in new[] { "elevatorAuthority", "rudderAuthority", "trimMode" })
            {
                BaseField f = Fields[name];
                if (f == null) continue;
                if (f.guiActive != hull) f.guiActive = hull;
                if (f.guiActiveEditor != hull) f.guiActiveEditor = hull;
            }
        }

        // The fin as it stands out of the fabric: its area and the centre of it (where its lift acts), and its
        // lift slope.
        bool FinGeometry()
        {
            if (finArea >= 0) return finArea > 0;
            if (profileR == null) BuildProfile();
            finArea = 0;
            if (profileR.Length < 2) return false;
            const int N = 200;
            float dz = (finRootLeadZ - finTrailZ) / N, rootLead = EnvelopeRadiusAt(finRootLeadZ) - finEmbed;
            double a = 0, az = 0, ar = 0;
            for (int i = 0; i < N; i++)
            {
                float z = finTrailZ + (i + 0.5f) * dz;
                float outer = z <= finTipLeadZ ? finTipRadius : Mathf.Lerp(finTipRadius, rootLead, (z - finTipLeadZ) / (finRootLeadZ - finTipLeadZ));
                float inner = Mathf.Max(FinRootAt(z), EnvelopeRadiusAt(z));
                float w = Mathf.Max(0f, outer - inner);
                a += w * dz; az += w * z * dz; ar += w * 0.5 * (outer + inner) * dz;
            }
            if (a <= 0) return false;
            finArea = a; finCz = az / a; finCr = ar / a;
            double span = finTipRadius - EnvelopeRadiusAt((float)finCz);
            double aspect = 2 * span * span / a;                           // the hull is a wall at its root
            finCNa = 2 * System.Math.PI * aspect / (2 + System.Math.Sqrt(aspect * aspect + 4));
            return true;
        }

        // Added-mass coefficients of a prolate spheroid of this length over width (Lamb): along it, across
        // it, and turning about a cross axis.
        static void Lamb(double fineness, out double k1, out double k2, out double kr)
        {
            double f = System.Math.Max(1.001, fineness);
            double e = System.Math.Sqrt(1 - 1 / (f * f)), L = System.Math.Log((1 + e) / (1 - e));
            double a0 = 2 * (1 - e * e) / (e * e * e) * (0.5 * L - e);
            double b0 = 1 / (e * e) - (1 - e * e) / (2 * e * e * e) * L;
            k1 = a0 / (2 - a0);
            k2 = b0 / (2 - b0);
            kr = e * e * e * e * (b0 - a0) / ((2 - e * e) * (2 * e * e - (2 - e * e) * (b0 - a0)));
        }

        // The craft as KSP has it this step, whole: its parts' inertia about its centre of mass (t m^2), its
        // velocity and its turn rate (from its angular momentum, so a part wobbling on its joint doesn't count).
        bool CraftNow(Vector3 com, out double[,] inertia, out Vector3 velocity, out Vector3 omega, out double mass)
        {
            inertia = new double[3, 3];
            velocity = omega = Vector3.zero;
            mass = 0;
            Vector3 p = Vector3.zero;
            foreach (Part q in vessel.parts)
            {
                if (q == null || q.rb == null) continue;
                mass += q.rb.mass;
                p += q.rb.velocity * q.rb.mass;
            }
            if (!(mass > 0)) return false;
            velocity = p / (float)mass;
            var L = new double[3];
            foreach (Part q in vessel.parts)
            {
                if (q == null || q.rb == null) continue;
                Rigidbody rb = q.rb;
                Vector3 r = rb.worldCenterOfMass - com;
                double m = rb.mass;
                double[,] own = OwnInertia(rb);
                double[] rr = { r.x, r.y, r.z }, w = { rb.angularVelocity.x, rb.angularVelocity.y, rb.angularVelocity.z };
                double r2 = r.sqrMagnitude;
                Vector3 h = Vector3.Cross(r, rb.velocity - velocity) * (float)m;
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        inertia[i, j] += own[i, j] + m * ((i == j ? r2 : 0) - rr[i] * rr[j]);
                        L[i] += own[i, j] * w[j];
                    }
                }
                L[0] += h.x; L[1] += h.y; L[2] += h.z;
            }
            double[,] inv;
            if (!Inverse(inertia, out inv)) return false;
            omega = Mul(inv, new Vector3((float)L[0], (float)L[1], (float)L[2]));
            return true;
        }

        // A part's own inertia in world axes.
        static double[,] OwnInertia(Rigidbody rb)
        {
            var m = new double[3, 3];
            Quaternion q = rb.rotation * rb.inertiaTensorRotation;
            Vector3 it = rb.inertiaTensor;
            for (int k = 0; k < 3; k++)
            {
                Vector3 e = q * (k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward);
                double ik = it[k];
                double[] v = { e.x, e.y, e.z };
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) m[i, j] += ik * v[i] * v[j];
            }
            return m;
        }

        void ApplyHullAero(double rho, double hullVolume, float pressure, float progress)
        {
            finsAero = false;
            if (fins == null || finFlaps == null || !fins.activeInHierarchy || !FinGeometry() || ringZ == null || ringZ.Length < 2) return;
            Transform bt = balloonObject.transform;
            float dt = TimeWarp.fixedDeltaTime;
            Vector3 com = frameCoM;
            Vector3 fwd = bt.TransformVector(Vector3.forward);
            float sz = fwd.magnitude, sx = bt.TransformVector(Vector3.right).magnitude;
            if (!(sz > 0f) || !(sx > 0f) || !(dt > 0f)) return;
            Vector3 axis = fwd / sz;
            double[,] iCraft;
            Vector3 vCraft, omega;
            double M;
            if (!CraftNow(com, out iCraft, out vCraft, out omega, out M)) return;
            Vector3 vCom = vCraft + (Vector3)Krakensbane.GetFrameVelocity();

            // what KSP doesn't see: the envelope's fabric and gas spread over the hull, the air in its
            // ballonets and the air it moves (t, m, t m^2), about the vertical
            double a = 0.5 * (ringZ[ringZ.Length - 1] - ringZ[0]) * sz, b = ringRMax * sx;
            double k1, k2, kr;
            Lamb(a / System.Math.Max(1e-3, b), out k1, out k2, out kr);
            double vGas = pressure > 0 ? RealBalloonPhysics.GasVolume(this, pressure, RealBalloonPhysics.TemperatureHere(this), progress) : 0;
            double mAir = rho * hullVolume / 1000.0;
            double mBal = BallonetsFull ? rho * System.Math.Max(0, hullVolume - vGas) / 1000.0 : 0;
            double mGas = RealBalloonPhysics.GasMoles(this) * progress * RealBalloonPhysics.GasMolarMass(liftGas) / 1e6;
            Vector3 centre = EnvelopeCentre(), up = vessel.upAxis, d = centre - com, dh = d - Vector3.Dot(d, up) * up;
            double mT = BalloonMass + mGas + mBal + k2 * mAir;
            double iYaw = (BalloonMass / 3 + (mGas + mBal) / 5 + kr * mAir / 5) * (a * a + b * b) + mT * dh.sqrMagnitude;
            double[] u3 = { up.x, up.y, up.z };
            double iu = 0;
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) iu += u3[i] * iCraft[i, j] * u3[j];
            if (!(iu > 1e-9)) return;
            // turning about the vertical: by the share of the real inertia KSP sees. About the horizontal: as the
            // gondola's righting is (swayScale, whose 4 s swing counts these fins' stiffness too, so the hull
            // swings no quicker at speed), so the elevators and the gondola weigh against each other truly.
            double sV = iu / (iu + iYaw), sH = Mathf.Clamp(swayScale, 1e-4f, 1f);
            double mShare = M / (M + mBal + k2 * mAir);
            var S = new double[3, 3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) S[i, j] = sH * ((i == j ? 1 : 0) - u3[i] * u3[j]) + sV * u3[i] * u3[j];
            double[,] iInv;
            if (!Inverse(iCraft, out iInv)) return;
            double[,] reply = Mul(iInv, S);              // turn rate change per kN m of the real turning force, per second

            // the fins now: where their lift acts, which way, how big (as much as is out of the fabric yet)
            var pts = new Vector3[4];
            var nrm = new Vector3[4];
            var area = new double[4];
            double exposed = FinExposure();
            for (int k = 0; k < 4; k++)
            {
                pts[k] = bt.TransformPoint(FinOut(k) * (float)finCr + Vector3.forward * (float)finCz);
                nrm[k] = bt.TransformVector(FinSide(k)).normalized;
                area[k] = finArea * exposed * bt.TransformVector(FinOut(k)).magnitude * sz;
            }
            Vector3 hUp = bt.TransformVector(Vector3.right).normalized, hSide = bt.TransformVector(Vector3.up).normalized;

            // The fins' damping, implicitly: the turn rate they'd leave at the end of the step, as the craft
            // (as KSP has it, with the shares above) would really answer.
            var C = new double[3, 3];
            for (int k = 0; k < 4; k++)
            {
                Vector3 r = pts[k] - com;
                double c = 0.5 * rho * (vCom + Vector3.Cross(omega, r)).magnitude * area[k] * finUpwash * finCNa / 1000.0;
                Vector3 m = Vector3.Cross(r, nrm[k]);
                double[] m3 = { m.x, m.y, m.z };
                for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) C[i, j] += c * m3[i] * m3[j];
            }
            // and the hull's crossflow drag, each length of it as it swings about the hull's centre (where its
            // sideways drag acts already), the drift's crossflow adding to the swing's
            var Ch = new double[3, 3];
            Vector3 vCentre = vCom + Vector3.Cross(omega, centre - com);
            for (int i = 0; i + 1 < ringZ.Length; i++)
            {
                float dzl = ringZ[i + 1] - ringZ[i];
                if (!(dzl > 0f)) continue;
                Vector3 r = bt.TransformPoint(new Vector3(0f, 0f, 0.5f * (ringZ[i] + ringZ[i + 1]))) - centre;
                Vector3 u = vCentre + Vector3.Cross(omega, r);
                u -= Vector3.Dot(u, axis) * axis;
                double c = 0.5 * rho * hullCrossflowCd * (ringR[i] + ringR[i + 1]) * sx * dzl * sz * u.magnitude / 1000.0;
                if (!(c > 0)) continue;
                for (int n = 0; n < 2; n++)
                {
                    Vector3 m = Vector3.Cross(r, n == 0 ? hUp : hSide);
                    double[] m3 = { m.x, m.y, m.z };
                    for (int a1 = 0; a1 < 3; a1++) for (int a2 = 0; a2 < 3; a2++) Ch[a1, a2] += c * m3[a1] * m3[a2];
                }
            }
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) C[i, j] += Ch[i, j];
            double[,] A = Mul(reply, C);
            for (int i = 0; i < 3; i++) { for (int j = 0; j < 3; j++) A[i, j] *= dt; A[i, i] += 1; }
            double[,] aInv;
            Vector3 wEff = Inverse(A, out aInv) ? Mul(aInv, omega) : omega;

            // what the controls ask for (the turn KSP's own torque would make: ModuleReactionWheel's sign), each
            // of the hull's own turns limited by its slider: yaw about its up (local X), pitch about its side
            // (local Y), roll about its axis
            Vector3 cmd = Vector3.zero;
            Transform rf = vessel.ReferenceTransform;
            if (rf != null && vessel.ctrlState != null)
            {
                Vector3 c = -(rf.rotation * new Vector3(vessel.ctrlState.pitch, vessel.ctrlState.roll, vessel.ctrlState.yaw));
                cmd = Authority(c, hUp, hSide, axis);
            }

            Vector3 force = Vector3.zero, torque = -Mul(Ch, wEff);             // the hull's crossflow, at the step's end
            for (int k = 0; k < 4; k++)
            {
                Vector3 r = pts[k] - com;
                Vector3 vk = vCom + Vector3.Cross(wEff, r);
                float s = vk.magnitude;
                float ca = s > 0.1f ? Vector3.Dot(vk, axis) / s : 1f;
                // each surface towards the turn asked for (its lever from the hull, so the top rudder, far above
                // the gondola, still answers yaw in full): positive deflection pushes the fin to its -side
                Vector3 per = Vector3.Cross(pts[k] - centre, -nrm[k]) * (ca < 0f ? -1f : 1f);
                float want = per.sqrMagnitude > 1e-8f ? Mathf.Clamp(Vector3.Dot(cmd, per.normalized), -1f, 1f) * flapMaxDeg : 0f;
                flapDeg[k] = Mathf.MoveTowards(flapDeg[k], want, flapRateDeg * dt);
                flapTorque[k] = Vector3.zero;
                if (s < 0.1f) continue;
                double q = 0.5 * rho * s * s;                                   // Pa
                // the air across the fin, and the flap's turn as more of it (it works with the air along the fin)
                double x = Vector3.Dot(vk, nrm[k]) / s + FlapAngle(flapDeg[k]) * ca;
                double lin = finCNa * x;
                double cn = finUpwash * lin / System.Math.Pow(1 + Sq(Sq(lin / finCnMax)), 0.25);   // levels off at finCnMax
                Vector3 f = -(float)(q * area[k] * cn / 1000.0) * nrm[k];       // kN
                force += f;
                torque += Vector3.Cross(r, f);
                Vector3 full = Vector3.Cross(r, -nrm[k]) * (float)(q * area[k] * finUpwash * finCNa * FlapAngle(flapMaxDeg) * System.Math.Abs(ca) / 1000.0);
                flapTorque[k] = Mul(S, Authority(full, hUp, hSide, axis));
            }
            PoseFlaps(flapDeg);

            // the hull turning further off its path (Munk): nose away from the air it meets, falling off at
            // large angles as the flow leaves its tail
            float sp = vCom.magnitude;
            {
                // how stiffly the fins (less the hull's own turn) hold its pitch now, per radian, for the sway's 4 s
                double sides = area[1] + area[3], arm = Vector3.Dot(centre - 0.5f * (pts[1] + pts[3]), axis);
                aeroPitchStiffness = 0.5 * rho * sp * sp * System.Math.Max(0, sides * finUpwash * finCNa * arm
                    - 2 * hullVolume * (k2 - k1) * hullMomentShare) / 1000.0;
            }
            if (sp > 0.1f)
            {
                Vector3 vh = vCom / sp;
                float c = Vector3.Dot(vh, axis);
                double qh = 0.5 * rho * sp * sp;
                torque -= (float)(2 * qh * hullVolume * (k2 - k1) * hullMomentShare * c * System.Math.Abs(c) / 1000.0) * Vector3.Cross(axis, vh - c * axis);
            }

            // as the craft answers it (the shares above): the turn rate and speed it changes per second, spread over its parts
            Vector3 spin = Mul(reply, torque);                                  // rad/s^2
            float most = (float)(finMaxTurnPerStep / dt);
            if (spin.magnitude > most) spin *= most / spin.magnitude;
            Spread(com, spin, force * (float)(mShare / M));
            finsAero = true;
        }

        // The share of the fins' area out of the fabric: all of it, but while they grow out of the swelling hull.
        double FinExposure()
        {
            if (finGrowth >= 0.999f) return 1;
            double root = EnvelopeRadiusAt((float)finCz), tip = finGrowth * finTipRadius;
            return finTipRadius > root ? System.Math.Max(0, System.Math.Min(1, (tip - root) / (finTipRadius - root))) : 0;
        }

        // What a flap's deflection (degrees) is worth as more air across the fin (radians).
        static double FlapAngle(float deg)
        {
            double f = deg / flapMaxDeg;
            return flapEffect * deg * Mathf.Deg2Rad * (1 - flapFullLoss * f * f);
        }

        // Speed up the craft's turn (rad/s^2) and its motion (m/s^2) as it would whole: each part pushed by
        // what its own mass and inertia need, so no joint carries it.
        void Spread(Vector3 com, Vector3 spin, Vector3 push)
        {
            foreach (Part q in vessel.parts)
            {
                if (q == null || q.rb == null) continue;
                Rigidbody rb = q.rb;
                Vector3 r = rb.worldCenterOfMass - com;
                Vector3 f = (push + Vector3.Cross(spin, r)) * rb.mass;
                if (f.sqrMagnitude > 0f) q.AddForceAtPosition(f, rb.worldCenterOfMass);
                Vector3 t = Mul(OwnInertia(rb), spin);
                if (t.sqrMagnitude > 0f) q.AddTorque(t);
            }
        }

        // A turn, as the hull's own yaw (about its up) and pitch (about its side), each taken by its authority.
        // No roll: real blimps don't roll on their fins; the gondola hanging under them rights them.
        Vector3 Authority(Vector3 t, Vector3 hUp, Vector3 hSide, Vector3 axis)
        {
            return Vector3.Dot(t, hUp) * hUp * (rudderAuthority / 100f) + Vector3.Dot(t, hSide) * hSide * (elevatorAuthority / 100f);
        }

        static double[,] Mul(double[,] a, double[,] b)
        {
            var c = new double[3, 3];
            for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) for (int k = 0; k < 3; k++) c[i, j] += a[i, k] * b[k, j];
            return c;
        }

        static Vector3 Mul(double[,] m, Vector3 v)
        {
            return new Vector3((float)(m[0, 0] * v.x + m[0, 1] * v.y + m[0, 2] * v.z),
                               (float)(m[1, 0] * v.x + m[1, 1] * v.y + m[1, 2] * v.z),
                               (float)(m[2, 0] * v.x + m[2, 1] * v.y + m[2, 2] * v.z));
        }

        static bool Inverse(double[,] m, out double[,] inv)
        {
            inv = new double[3, 3];
            double det = m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                       + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            if (!(System.Math.Abs(det) > 1e-15) || double.IsNaN(det)) return false;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    int i1 = (j + 1) % 3, i2 = (j + 2) % 3, j1 = (i + 1) % 3, j2 = (i + 2) % 3;
                    inv[i, j] = (m[i1, j1] * m[i2, j2] - m[i1, j2] * m[i2, j1]) / det;
                }
            return true;
        }

        // For SAS: the turn the surfaces could make now, about the craft's pitch, roll and yaw axes.
        public void GetPotentialTorque(out Vector3 pos, out Vector3 neg)
        {
            pos = neg = Vector3.zero;
            if (!finsAero || !Lifting || vessel == null || vessel.ReferenceTransform == null) return;
            Transform rf = vessel.ReferenceTransform;
            foreach (Vector3 t in flapTorque)
            {
                pos.x += Mathf.Abs(Vector3.Dot(t, rf.right));
                pos.y += Mathf.Abs(Vector3.Dot(t, rf.up));
                pos.z += Mathf.Abs(Vector3.Dot(t, rf.forward));
            }
            neg = pos;
        }
    }
}
