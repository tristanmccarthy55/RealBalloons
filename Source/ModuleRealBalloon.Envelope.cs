// RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// Part of a rewritten derivative of KerBalloons (JoePatrick1; KerBalloons Reinflated (c) 2018 Fengist), MIT.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealBalloons
{
    public partial class ModuleRealBalloon
    {
        // ---- VAB options (lift always acts on the balloon part) ----
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Balloon mount"),
         UI_Toggle(disabledText = "On a rope", enabledText = "Fixed to the part", scene = UI_Scene.Editor)]
        public bool rigidMount = false;

        // Fixed mount only: the cushion. Fit the balloon part cap-down under the craft (its base node up,
        // on the craft): the envelope comes out of the cap, below the craft, slightly squashed by the load,
        // and the craft sits on it. Buoyancy then acts at the envelope's centre below the craft, so it's
        // stable only if the centre of mass is lower still: the built-in keel and counterweight hanging
        // from the envelope see to that. KSP's own physics decides whether it tips.
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Fixed mount"),
         UI_Toggle(disabledText = "Balloon", enabledText = "Cushion (fit the part cap-down)", scene = UI_Scene.Editor)]
        public bool envelopeBelow = false;

        bool Cushion { get { return rigidMount && envelopeBelow; } }

        // Cushion only: a built-in keel. A rope can't give leverage (its pull acts where it's tied),
        // so this is a rigid boom hanging from the bottom of the envelope with a counterweight at the
        // end, drawn with the model's rope. The weight is part of the balloon part's mass; once the keel
        // is out, the module applies the turning force that a weight that far down produces.
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Keel counterweight", guiUnits = "% of lift", guiFormat = "F1"),
         UI_FloatRange(minValue = 0f, maxValue = 30f, stepIncrement = 2.5f, scene = UI_Scene.Editor)]
        public float counterweightPercent = 15f;

        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Keel length", guiUnits = " radii", guiFormat = "F0"),
         UI_FloatRange(minValue = 2f, maxValue = 12f, stepIncrement = 1f, scene = UI_Scene.Editor)]
        public float keelLength = 8f;

        // Cushion envelope's height over its width (same volume as the round one).
        [KSPField] public float cushionShape = 0.65f;
        float CushionShape { get { return Cushion ? Mathf.Clamp(cushionShape, 0.3f, 1f) : 1f; } }

        // Counterweight mass (t): a share of the size's lift-off rating on the home world.
        public float CounterweightMass { get { return Cushion ? counterweightPercent / 100f * maxLift / 9.80665f : 0f; } }

        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "Counterweight mass")]
        public string counterweightText = "";

        // The editor's up is the launch's up in both the VAB and the SPH.
        string CounterweightText()
        {
            string txt = CounterweightMass.ToString("0.###") + " t";
            if (Cushion && Vector3.Dot(part.transform.up, Vector3.up) > 0.3f) txt += ", cap up: flip the part";
            return txt;
        }

        GameObject keelWeight;
        float keelProgress;      // 0 stowed .. 1 fully out
        bool keelShown;
        Renderer[] weightRenderers;

        // Where the buoyancy acts: the envelope's centre on the cushion and the blimp (with ballonets, the gas
        // bubble's centre), the model's lift point otherwise.
        Vector3 LiftPosition()
        {
            if (Hull) return EnvelopeCentre() + GasBubbleOffset();
            return Cushion ? EnvelopeCentre() : liftPointObject.transform.position;
        }

        // With ballonets the gas lies along the top of the hull over the ballonet air. The hull's buoyancy less
        // the ballonet air's weight comes to the gas's lift acting at the gas's centre, well above the hull's:
        // low down, where the gas is a thin layer along the top, that hangs the gondola much more firmly (the
        // bottom-heaviness real blimps fly with); at the pressure height, where the gas fills the hull, it's the
        // hull's centre. Taken as a level layer in a cylinder of the hull's length and volume, held fore and aft
        // by the curtains inside (so it doesn't run to the high end when the hull pitches). Pitched, the layer
        // lies across its compartments and its centre comes in towards the axis, by the cosine of the pitch:
        // none of it standing on end, and no sudden change on the way there.
        Vector3 GasBubbleOffset()
        {
            if (!BallonetsFull || vessel == null || !Lifting || ringZ == null || ringZ.Length < 2) return Vector3.zero;
            double pk = FlightGlobals.getStaticPressure(part.transform.position);
            double vHull = ShapeVolume(0, inflation);
            if (!(pk > 0) || !(vHull > 0)) return Vector3.zero;
            double share = RealBalloonPhysics.GasVolume(this, pk, RealBalloonPhysics.TemperatureHere(this), inflation) / vHull;
            if (!(share < 0.999)) return Vector3.zero;
            share = System.Math.Max(0, share);
            Vector3 axis = HullAxis(), across = vessel.upAxis - Vector3.Dot(vessel.upAxis, axis) * axis;   // up, off the axis: cos(pitch) long
            if (across.sqrMagnitude < 1e-8f) return Vector3.zero;            // standing on end
            double length = (ringZ[ringZ.Length - 1] - ringZ[0]) * balloonObject.transform.TransformVector(Vector3.forward).magnitude;
            if (!(length > 0)) return Vector3.zero;
            double r = System.Math.Sqrt(vHull / (System.Math.PI * length));
            // the circular segment over a level chord holding this share of the circle: its half-angle, then
            // its centre's height above the circle's centre
            double lo = 0, hi = System.Math.PI;
            for (int i = 0; i < 40; i++)
            {
                double a = 0.5 * (lo + hi);
                if ((a - System.Math.Sin(a) * System.Math.Cos(a)) / System.Math.PI < share) lo = a; else hi = a;
            }
            double al = 0.5 * (lo + hi), den = 3 * (2 * al - System.Math.Sin(2 * al));
            double h = den > 1e-9 ? 4 * r * System.Math.Pow(System.Math.Sin(al), 3) / den : r;
            return across * (float)h;
        }

        // Blimp ballonet trim. A real blimp trims its pitch by blowing air between its ballonets fore and aft:
        // tonnes of it low down, where the ballonets are big, and none at the pressure height, where the gas
        // fills the hull. Here up to 40% of the ballonet air moves half the hull's length, plus (at any height)
        // the turn of the lift sliding ballonetRange of the hull's length, as the old gas bubble did. It starts
        // balancing the craft's centre of mass under the hull, once it's fully inflated (each inflation). "Hold
        // level" then trims the hull level, slowly (a full swing takes trimSwing seconds), so dropping tanks or
        // the fins' drag on a climb are trimmed out (it waits while the pilot steers, so it doesn't undo the
        // elevators); "Hold" keeps it as it is; "Off" never trims (the ballonets just keep the hull full, and it
        // pitches wherever its weight puts it). A weight moved fore and aft, so it turns the hull by its lever
        // across the vertical (all of it level, none standing on end) and is applied as the gondola's righting is.
        [KSPField] public float ballonetRange = 0.15f;
        [KSPField] public float trimSwing = 60f;
        public const string TrimLevel = "Hold level", TrimHold = "Hold", TrimOff = "Off";
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Ballonet trim"),
         UI_ChooseOption(options = new[] { TrimLevel, TrimHold, TrimOff }, scene = UI_Scene.All)]
        public string trimMode = TrimLevel;
        [KSPField(isPersistant = true)] public bool trimLevel = true;  // 1.0's on/off, read once into trimMode
        [KSPField(isPersistant = true)] public float trimState;        // -1..1 of the most it can do, + nose up
        [KSPField(isPersistant = true)] public bool trimSet;
        [System.NonSerialized] public double trimMoment, trimMax;     // kN m, for the info window

        // The blimp's nose: the part's +Z, so placed unrotated in the VAB it points forward and the tail back.
        // Rotate the part in the VAB to aim it.
        Vector3 HullAxis() { return part.transform.forward; }

        // The pilot's own hands on the controls (not SAS's): trimming waits, so it doesn't undo the elevators.
        bool PilotSteering()
        {
            if (vessel != FlightGlobals.ActiveVessel || FlightInputHandler.state == null) return false;
            FlightCtrlState st = FlightInputHandler.state;
            return Mathf.Abs(st.pitch) + Mathf.Abs(st.yaw) + Mathf.Abs(st.roll) > 0.01f;
        }

        void ApplyBallonetTrim(float liftKN, float pressure, float progress)
        {
            trimMoment = trimMax = 0;
            if (trimMode == TrimOff) { trimState = 0f; return; }
            if (!Hull || vessel == null || part.rb == null || balloonObject == null || !(liftKN > 0)) return;
            if (progress < 0.999f) return;                                 // nothing to trim until it's full
            Vector3 axis = HullAxis(), up = vessel.upAxis, c = EnvelopeCentre();
            Vector3 lever = Vector3.Cross(axis, up);                       // nose up; cos(pitch) long
            float cosPitch = lever.magnitude;
            float length = -LowestPointAlong(-axis, c) - LowestPointAlong(axis, c);
            double g = FlightGlobals.getGeeForceAtPosition(c).magnitude;
            double air = 0;
            if (BallonetsFull && part.atmDensity > 0 && pressure > 0)
            {
                double vg = RealBalloonPhysics.GasVolume(this, pressure, RealBalloonPhysics.TemperatureHere(this), progress);
                air = part.atmDensity * System.Math.Max(0, ShapeVolume(vg, progress) - vg) / 1000.0;      // t
            }
            double max = liftKN * ballonetRange * length + g * 0.4 * air * 0.5 * length;    // level
            if (!(max > 0)) return;
            float dt = TimeWarp.fixedDeltaTime;
            if (!trimSet)
            {
                // as the craft hangs: its centre of mass this far ahead of the hull's centre takes this much
                trimState = Mathf.Clamp((float)(liftKN * Vector3.Dot(TrueCoM() - c, axis) / max), -1f, 1f);
                trimSet = true;
            }
            else if (trimMode == TrimLevel && !PilotSteering())
            {
                float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(axis, up), -1f, 1f)) * Mathf.Rad2Deg;   // + nose up
                float rate = cosPitch > 1e-3f ? Vector3.Dot(part.rb.angularVelocity, lever / cosPitch) * Mathf.Rad2Deg : 0f;
                float ask = Mathf.Clamp(pitch / 2f + rate / 0.5f, -1f, 1f);             // 2 degrees, or half a degree a second
                trimState = Mathf.Clamp(trimState - ask * 2f / Mathf.Max(1f, trimSwing) * dt, -1f, 1f);
            }
            trimMax = max;
            trimMoment = trimState * max;
            Vector3 com = frameCoM;
            double[,] iCraft;
            Vector3 v, w;
            double m;
            double[,] inv;
            if (!CraftNow(com, out iCraft, out v, out w, out m) || !Inverse(iCraft, out inv)) return;
            Spread(com, Mul(inv, lever * (float)(trimMoment * swayScale)), Vector3.zero);
        }

        // Fixed mount, above 1: the envelope lies on its side as an airship hull, round nose forward along
        // HullAxis (the part's front) and its point as the tail, longer and slimmer by this factor (same
        // volume). Fixed balloon only: hidden for the rope and the cushion.
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Blimp length", guiFormat = "F2", guiUnits = "x"),
         UI_FloatRange(minValue = 1f, maxValue = 4f, stepIncrement = 0.25f, scene = UI_Scene.Editor)]
        public float blimpStretch = 1f;

        bool Hull { get { return rigidMount && !envelopeBelow && blimpStretch > 1.001f; } }

        // Blimp ballonets: air bags inside the hull that keep it full, as on real blimps. On, the hull is its full
        // size at any height (drawn, in its drag and in its sway), the ballonet air making up what the gas doesn't
        // fill. That air weighs what it displaces, so the lift is still the gas's: constant until the gas fills the
        // hull (its pressure height, where the ballonets are empty), then it vents, the zero-pressure balloon's own
        // law. The envelope volume slider then sizes the hull and so sets the pressure height. Off, it's launched
        // flaccid and takes its shape as the gas expands, as stratospheric airships such as HiSentinel are.
        [KSPField(isPersistant = true, guiActiveEditor = false, guiActive = false, guiName = "Ballonets"),
         UI_Toggle(enabledText = "On: hull always full", disabledText = "Off: launches flaccid", scene = UI_Scene.Editor)]
        public bool ballonets = true;

        bool BallonetsFull { get { return Hull && ballonets; } }

        // The volume the envelope fills (m^3): its gas's, or the whole hull while the ballonets hold it full.
        public double ShapeVolume(double gasVolume, double progress = 1.0)
        {
            return BallonetsFull ? RealBalloonPhysics.EnvelopeVolume(this) * System.Math.Max(0, System.Math.Min(1, progress)) : gasVolume;
        }

        bool previewRigid;
        float previewStretch = -1f;
        bool previewBelow;
        float previewCw = -1f;
        float previewLift = -1f;
        Vector3 capBaseScale;
        bool capBaseSet;
        MeshFilter[] balloonMeshes;
        Renderer[] ropeRenderers;
        Transform[] flagQuads;
        Vector3[] flagBaseScales;

        float previewMass = -1f;
        string floatKey;

        string FloatKey()
        {
            return maxLift + "|" + envelopeVolume + "|" + volumeFactor + "|" + filmDensity + "|" + designAirMolarMass + "|" + envelopeSizePercent + "|" +
                   gasFillPercent + "|" + liftGas + "|" + maxAirTemperature + "|" + planBody + "|" + tearPressure;
        }

        // ---- Planning ----
        // "Plan for": the home world first, then every other planet and moon whose air some balloon here could
        // float in (RealBalloonPhysics.CanFloatOn), outward from the Sun (moons after their planet).
        void SetUpPlanBodies()
        {
            BaseField f = Fields["planBody"];
            if (f == null || FlightGlobals.Bodies == null) return;
            CelestialBody home = FlightGlobals.GetHomeBody();
            var names = new List<string>();
            double k = envelopeVolume > 0 && maxLift > 0 ? maxLift / envelopeVolume : 0;
            if (home != null && home.atmosphere) names.Add(home.bodyName);
            foreach (CelestialBody b in FlightGlobals.Bodies.Where(b => b != null && b.atmosphere && b != home && RealBalloonPhysics.CanFloatOn(b, k))
                                                             .OrderBy(b => SunDistance(PlanetOf(b))).ThenBy(b => b == PlanetOf(b) ? 0 : SunDistance(b)))
                names.Add(b.bodyName);
            if (names.Count == 0) { f.guiActiveEditor = false; return; }
            if (!names.Contains(planBody)) planBody = names[0];
            UI_ChooseOption opt = f.uiControlEditor as UI_ChooseOption;
            if (opt != null) { opt.options = names.ToArray(); opt.display = names.ToArray(); }
        }

        static CelestialBody PlanetOf(CelestialBody b)
        {
            CelestialBody p = b;
            for (int i = 0; i < 8 && p.referenceBody != null && p.referenceBody != p && p.referenceBody.referenceBody != p.referenceBody; i++)
                p = p.referenceBody;
            return p;
        }

        static double SunDistance(CelestialBody b) { return b.orbit != null ? b.orbit.semiMajorAxis : 0; }

        // The planet to plan for: the chosen one in the VAB, else the home world.
        CelestialBody PlanBody()
        {
            CelestialBody home = FlightGlobals.GetHomeBody();
            if (!string.IsNullOrEmpty(planBody) && FlightGlobals.Bodies != null)
                foreach (CelestialBody b in FlightGlobals.Bodies)
                    if (b != null && b.bodyName == planBody) return b;
            return home;
        }

        // The balloons on this craft (any sizes, not spent) and its mass: the vessel in flight, the ship in the VAB.
        List<ModuleRealBalloon> CraftBalloons(out double craftMass)
        {
            craftMass = 0;
            bool flight = HighLogic.LoadedSceneIsFlight && vessel != null;
            List<Part> parts = flight ? vessel.parts
                : EditorLogic.fetch != null && EditorLogic.fetch.ship != null ? EditorLogic.fetch.ship.parts : null;
            var cluster = new List<ModuleRealBalloon>();
            if (parts != null)
            {
                craftMass = flight ? vessel.GetTotalMass() : EditorLogic.fetch.ship.GetTotalMass();
                foreach (Part p in parts)
                {
                    ModuleRealBalloon rb = p.FindModuleImplementing<ModuleRealBalloon>();
                    if (rb != null && rb.balloonState != Spent) cluster.Add(rb);
                }
            }
            if (cluster.Count == 0) cluster.Add(this);
            return cluster;
        }

        static string Tonnes(double t) { return t < 1 ? (t * 1000).ToString("F0") + " kg" : t.ToString("F2") + " t"; }

        // Where a full load and an empty balloon float on the planned planet (VAB readout).
        string FloatReadout()
        {
            failText = "";
            CelestialBody pb = PlanBody();
            if (pb == null || envelopeVolume <= 0 || maxLift <= 0) return "";
            double ceil, a;
            if (RealBalloonPhysics.PredictFloat(this, pb, 0, out a, out ceil) == 3) return "can't work on " + pb.bodyName;
            double bottom = RealBalloonPhysics.FloorAltitude(this, pb, ceil);
            if (bottom > 1)          // "Fails below": the heat, or the acid clouds where they're lower down than that
                failText = (bottom / 1000).ToString("F0") + " km (" +
                           (bottom > RealBalloonPhysics.HeatFloorAltitude(this, pb, ceil) + 1 ? "sulfuric acid clouds"
                                : "air over " + (maxAirTemperature - 273.15).ToString("F0") + " C") + ")";
            double full = RealBalloonPhysics.CapacityTonnes(this, pb, pb.GetPressure(bottom), pb.GetTemperature(bottom)) * 0.999;
            if (full <= BalloonMass) return "can't lift itself on " + pb.bodyName;
            double fullAlt, emptyAlt, c1, c2;
            RealBalloonPhysics.PredictFloat(this, pb, full, out fullAlt, out c1);
            int se = RealBalloonPhysics.PredictFloat(this, pb, BalloonMass, out emptyAlt, out c2);
            return Tonnes(full - BalloonMass) + " payload at " + (fullAlt / 1000).ToString("F0") +
                   " km, " + (se == 0 ? "none at " + (emptyAlt / 1000).ToString("F0") + " km" : "pops with none");
        }

        // How fast this craft leaves the ground (or the height it's inflated at) under all its balloons,
        // against the speed that tears the weakest of them there.
        string LaunchReadout()
        {
            double craftMass, v, vTear, alt;
            List<ModuleRealBalloon> cluster = CraftBalloons(out craftMass);
            if (craftMass <= 0) return "";
            int st = RealBalloonPhysics.LaunchClimb(cluster, HighLogic.LoadedSceneIsFlight && vessel != null ? vessel.mainBody : PlanBody(),
                                                    craftMass, out v, out vTear, out alt);
            if (st == 3) return "";
            if (st == 1) return "won't lift this craft";
            string from = alt > 1 ? " from " + (alt / 1000).ToString("F0") + " km" : "";
            return v > vTear ? v.ToString("F0") + " m/s" + from + ": TEARS above " + vTear.ToString("F0") + " (less gas)"
                             : v.ToString("F0") + " m/s" + from + " (tears above " + vTear.ToString("F0") + ")";
        }

        float launchCheck;

        void EditorUpdate()
        {
            if (flagPending) { flagPending = false; ApplyEnvelopeFlag(); }
            string fk = FloatKey();
            if (fk != floatKey)
            {
                floatKey = fk; floatText = FloatReadout(); launchCheck = 0;
                BaseField ff = Fields["failText"];
                if (ff != null) ff.guiActiveEditor = failText.Length > 0;
            }
            if (Time.realtimeSinceStartup >= launchCheck)          // the craft changes too: a few times a second
            {
                launchCheck = Time.realtimeSinceStartup + 0.3f;
                launchText = LaunchReadout();
            }
            float bm = BalloonMass;      // film, size, envelope volume and gas fill all change it
            if (Mathf.Abs(bm - previewMass) > 1e-6f)
            {
                previewMass = bm;
                if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
            if (rigidMount != previewRigid || blimpStretch != previewStretch || envelopeBelow != previewBelow ||
                counterweightPercent != previewCw || maxLift != previewLift)
            {
                bool massChanged = rigidMount != previewRigid || envelopeBelow != previewBelow || counterweightPercent != previewCw ||
                    (Cushion && maxLift != previewLift);
                UpdateMountPreview();
                if (massChanged && EditorLogic.fetch != null && EditorLogic.fetch.ship != null)
                    GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            }
            if (Cushion)
            {
                string txt = CounterweightText();
                if (txt != counterweightText) counterweightText = txt;
            }
            if (capObject != null) ShapeCap();          // cheap when nothing changed
            UpdateHullPreview();
            UpdateFinFields();
        }

        // ---- Hull preview (VAB) ----
        // A blimp with ballonets is its full hull from the ground up, so the VAB can show it as it will fly:
        // size, shape, fins, colours and flag. Only a picture: it isn't saved, it has no collider (it doesn't
        // get in the way of building), and it's left out of the craft's size for launch-site limits.
        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "Hull preview"),
         UI_Toggle(enabledText = "Inflated", disabledText = "Packed", scene = UI_Scene.Editor)]
        public bool previewHull = false;

        bool previewShown;
        Vector3 packedPos, packedScale;
        Quaternion packedRot;
        List<Collider> previewColliders;
        List<Renderer> previewHidden;       // switched off for the preview: the hull's (drawn by hand) and the cap's
        Dictionary<Renderer, bool> previewWas;
        List<string> previewIgnored;        // names put on the part's size-ignore list for the preview only
        MaterialPropertyBlock previewProps;

        bool HullPreview { get { return previewHull && modelOk && BallonetsFull && HighLogic.LoadedSceneIsEditor; } }

        void UpdateHullPreview()
        {
            BaseField f = Fields["previewHull"];
            if (f != null && f.guiActiveEditor != BallonetsFull) f.guiActiveEditor = BallonetsFull;
            if (HullPreview)
            {
                if (!previewShown) StartHullPreview();
                ApplyFixedMount(1f);
            }
            else if (previewShown) StopHullPreview();
        }

        // Out of the craft's size, which KSP and RP-1 both take from the part's meshes: KSP skips the names on
        // the part's ignore list, RP-1 skips meshes that are switched off (and counts a hidden object's as a
        // point at the VAB's origin). So for the preview the hull's meshes are switched off and drawn by hand
        // each frame, and the cap's are switched off (not the cap hidden). The hull's names stay on the ignore
        // list for good: packed, they're inside the base.
        void StartHullPreview()
        {
            Transform bt = balloonObject.transform, twin = PrefabTwin(bt);     // as packed (a copied part may be mid-preview)
            packedPos = twin != null ? twin.localPosition : bt.localPosition;
            packedRot = twin != null ? twin.localRotation : bt.localRotation;
            packedScale = twin != null ? twin.localScale : bt.localScale;
            initialBalloonPos = packedPos;                                     // EnvelopeHome (no even frame in the VAB)
            previewColliders = balloonObject.GetComponentsInChildren<Collider>(true).Where(c => c.enabled || TwinEnabled(c)).ToList();
            foreach (Collider c in previewColliders) c.enabled = false;
            previewShown = true;
            ApplyFixedMount(1f);                                               // builds the fins
            if (part.partRendererBoundsIgnore == null) part.partRendererBoundsIgnore = new List<string>();
            foreach (Renderer r in balloonObject.GetComponentsInChildren<Renderer>(true))
                if (!part.partRendererBoundsIgnore.Contains(r.name)) part.partRendererBoundsIgnore.Add(r.name);
            previewIgnored = new List<string>();
            foreach (Renderer r in capObject.GetComponentsInChildren<Renderer>(true))
                if (!part.partRendererBoundsIgnore.Contains(r.name)) { part.partRendererBoundsIgnore.Add(r.name); previewIgnored.Add(r.name); }
            previewHidden = new List<Renderer>();
            previewWas = new Dictionary<Renderer, bool>();
            HidePreviewMeshes();
            ShipSizeChanged();
        }

        void StopHullPreview()
        {
            Transform bt = balloonObject.transform;
            ShowFins(false);
            bt.localRotation = packedRot;
            bt.localScale = packedScale;
            bt.localPosition = packedPos;
            if (previewColliders != null) foreach (Collider c in previewColliders) if (c != null) c.enabled = true;
            previewColliders = null;
            if (previewHidden != null)
                foreach (Renderer r in previewHidden) if (r != null) r.enabled = PreviewWants(r);
            previewHidden = null;
            previewWas = null;
            if (!capObject.activeSelf) capObject.SetActive(true);              // hidden by an older preview, on a copied part
            if (previewIgnored != null && part.partRendererBoundsIgnore != null)
                foreach (string n in previewIgnored) part.partRendererBoundsIgnore.Remove(n);
            previewIgnored = null;
            previewShown = false;
            ShipSizeChanged();
        }

        // Switch off every mesh of the hull and the cap, again each frame: FlagDecal switches the flag back on
        // when it's toggled, and the fins are built after the preview starts.
        void HidePreviewMeshes()
        {
            if (previewHidden == null) return;
            foreach (Renderer r in balloonObject.GetComponentsInChildren<Renderer>(true).Concat(capObject.GetComponentsInChildren<Renderer>(true)))
            {
                if (r == null) continue;
                if (!previewWas.ContainsKey(r))
                {
                    previewWas[r] = r.enabled || TwinEnabled(r);           // a part copied mid-preview comes with them off
                    previewHidden.Add(r);
                }
                if (r.enabled) r.enabled = false;
            }
        }

        // Whether a mesh would show if the preview hadn't switched it off.
        bool PreviewWants(Renderer r)
        {
            if (IsFlagQuad(r.transform))
            {
                FlagDecal fd = part.FindModuleImplementing<FlagDecal>();
                return fd == null || fd.flagDisplayed;
            }
            bool was;
            return previewWas == null || !previewWas.TryGetValue(r, out was) || was;
        }

        bool IsFlagQuad(Transform t)
        {
            EnsureFlagQuads();
            foreach (Transform f in flagQuads) if (f != null && t.IsChildOf(f)) return true;
            return false;
        }

        // The same component in the part prefab: on there, unless it's switched off.
        bool TwinEnabled(Component c)
        {
            Transform tw = PrefabTwin(c.transform);
            if (tw == null) return true;
            if (c is Renderer) { Renderer r = tw.GetComponent<Renderer>(); return r == null || r.enabled; }
            if (c is Collider) { Collider k = tw.GetComponent<Collider>(); return k == null || k.enabled; }
            return true;
        }

        // The hull, fins and flag as they'd show, drawn this frame (after everything has moved, so a part being
        // dragged doesn't leave its hull behind). Not the cap: it's where the hull comes out.
        void LateUpdate()
        {
            KeepFlagOffFabric();
            if (!previewShown || balloonObject == null || capObject == null) return;
            HidePreviewMeshes();
            if (previewProps == null) previewProps = new MaterialPropertyBlock();
            foreach (Renderer r in previewHidden)
            {
                if (r == null || !r.gameObject.activeInHierarchy || !r.transform.IsChildOf(balloonObject.transform) || !PreviewWants(r)) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                Material[] mats = r.sharedMaterials;
                if (mesh == null || mats == null || mats.Length == 0) continue;
                r.GetPropertyBlock(previewProps);                              // part highlighting and fading
                Matrix4x4 m = r.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = mats[Mathf.Min(s, mats.Length - 1)];
                    if (mat != null)
                        Graphics.DrawMesh(mesh, m, mat, r.gameObject.layer, null, s, previewProps, r.shadowCastingMode, r.receiveShadows);
                }
            }
        }

        // So the VAB's readouts (RP-1's size against the launch site's limit) measure the craft again.
        void ShipSizeChanged()
        {
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }

        // Fixed mounts: no rope; the envelope turns with the part. Balloon: sitting on the part. Blimp:
        // lying on its side, nose to the part's front (HullAxis; rotate the part in the VAB to aim it).
        // Cushion: coming out of the cap, under the craft, with the keel below it.
        void ApplyFixedMount(float progress)
        {
            if (!Cushion) HideRope();
            Transform pt = part.transform;
            Transform bt = balloonObject.transform;
            // same "up" as the rope mode; the cushion's point faces away from the craft, where the keel hangs;
            // the blimp's axis lies along HullAxis (local X of the envelope is then "up", local Y the part's left)
            bool hull = Hull;
            bt.rotation = Cushion ? Quaternion.LookRotation(-pt.up, pt.forward)
                        : hull ? Quaternion.LookRotation(HullAxis(), -pt.right)
                        : Quaternion.LookRotation(pt.up, pt.forward);
            Vector3 scale = hull ? EnvelopeScale(progress, 1f, 1f, blimpStretch) : EnvelopeScale(progress, 1f, CushionShape);
            bool ripped = balloonState == Deflating;                // collapsing, not running back onto its spar
            if (hull && progress < 0.999f && !ripped) scale = Vector3.Scale(scale, RodScale(progress));
            bt.localScale = scale;
            KeepFlagProportions();
            Vector3 basePos = EnvelopeHome();
            bt.position = basePos;
            bt.position = basePos - pt.up * LowestPointAlong(pt.up, basePos);   // near end of the envelope on the base
            if (hull)
            {
                // the hull's centre of buoyancy over the part, whatever its size
                Vector3 axis = HullAxis();
                bt.position -= axis * Vector3.Dot(EnvelopeCentre() - basePos, axis);
                // resting on the base's rim (the envelope's own seat is down inside the base, which a round
                // envelope rises out of but the hull would sink into)
                bt.position += pt.up * BaseClearance(basePos);
            }
            ShowFins(hull);
            if (hull) PoseInflating(ripped ? 1f : progress);
            if (Cushion)
            {
                keelProgress = Mathf.Clamp01(progress);
                UpdateKeelVisual();
            }
        }

        // ---- A blimp's hull inflating ----
        // It comes out as a telescoping mast in the casing's grey, run out along the hull's axis to its full
        // length over the first rodShare of the inflation, at a steady pace with soft ends, resting on the base's
        // rim. Then the hull swells round it in its own colours, fins growing out of the fabric and flag on, and
        // the mast slides back in. Drawn, not the gas's volume: the skin holds the ballonets' air as
        // well, and the lift comes in with the gas at its steady rate throughout. Ripped, it just collapses.
        const float rodShare = 0.6f;               // of the inflation, running the rod out
        const float rodWidth = 0.035f;             // the rod's width, of the hull's
        static readonly Color casingGrey = new Color(0.61f, 0.6f, 0.61f, 1f);     // the base part's metal

        static float Ease(float s) { s = Mathf.Clamp01(s); return s * s * (3f - 2f * s); }

        // Length and width as shares of the finished hull's, and how far it has swollen (0..1).
        static void RodShape(float progress, out float along, out float across, out float swell)
        {
            float p = Mathf.Clamp01(progress), u = Mathf.Clamp01(p / rodShare);
            along = 0.5f * u + 0.5f * u * u * (3f - 2f * u);              // half linear, half smoothstep: soft ends
            swell = Mathf.Clamp01((p - rodShare) / (1f - rodShare));
            // the fabric comes out a little wider than the spar, so the spar is inside it from the start
            across = swell > 0f ? rodWidth * 1.25f + (1f - rodWidth * 1.25f) * Ease(swell) : rodWidth;
        }

        // As a factor on the evenly grown hull's scale (the cube root of progress).
        static Vector3 RodScale(float progress)
        {
            float p = Mathf.Clamp(progress, 1e-4f, 1f), even = Mathf.Pow(p, 1f / 3f), along, across, swell;
            RodShape(p, out along, out across, out swell);
            along = Mathf.Max(along, 1e-3f);
            return new Vector3(across / even, across / even, along / even);
        }

        float finGrowth = 1f;          // the fins' scale out from the axis while they grow out of the swelling hull
        bool flagStowed;

        // The fins, colours and flag as the hull swells (the fins grow out from the axis, so they come out of
        // the fabric root first).
        void PoseInflating(float progress)
        {
            float along, across, swell;
            RodShape(progress, out along, out across, out swell);
            bool done = progress >= 0.999f;
            finGrowth = done ? 1f : Ease(swell);
            if (fins != null)
            {
                float g = Mathf.Max(finGrowth, 1e-3f);
                Vector3 fs = new Vector3(g, g, 1f);
                if (fins.transform.localScale != fs) fins.transform.localScale = fs;
            }
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (fins != null)
            {
                bool show = finGrowth > 0.02f;
                if (finRenderer != null && finRenderer.enabled != show) finRenderer.enabled = show;
                if (flapRenderers != null)
                    foreach (MeshRenderer r in flapRenderers) if (r != null && r.enabled != show) r.enabled = show;
            }
            PoseRod(along, across, swell, done);
            bool stow = !done && swell <= 0f;
            if (stow != flagStowed)
            {
                flagStowed = stow;
                EnsureFlagQuads();
                FlagDecal fd = part.FindModuleImplementing<FlagDecal>();
                bool flagOn = !stow && (fd == null || fd.flagDisplayed);
                foreach (Transform f in flagQuads)
                    if (f != null) foreach (Renderer r in f.GetComponentsInChildren<Renderer>(true)) r.enabled = flagOn;
            }
        }

        // The mast: a fat centre stage runs out of the base both ways, then two thinner ones slide out of each
        // end of it, the last with domed tips, each to a third of the way out. All on the hull's axis, centred
        // over the base (the hull's centre of buoyancy), the centre stage as wide as the hull is while it's a
        // rod. In the casing's grey: a copy of the envelope's material with a plain grey texture
        // (TexturesUnlimited's own is never changed, its recolouring mask is left off, and no normal map).
        // The fabric is hidden while it runs out; as the fabric swells the stages slide back into the centre
        // one, and that is inside the hull by half way, when it goes. Each piece is its own object, so the
        // domes stay round and the stages keep their widths whatever the hull's scale.
        static readonly float[] rodStageWidth = { 1f, 0.82f, 0.66f };
        GameObject rod;
        Transform[] rodStages, rodTips;
        Material rodMat;
        static Mesh rodMesh, domeMesh;
        static Texture2D casingGreyTex;

        void PoseRod(float along, float across, float swell, bool done)
        {
            bool showRod = !done && swell < 0.5f, showFabric = done || swell > 0f;
            if (envelopeRenderer == null) envelopeRenderer = balloonObject.GetComponent<Renderer>();
            if (envelopeRenderer != null && envelopeRenderer.enabled != showFabric) envelopeRenderer.enabled = showFabric;
            if (!showRod)
            {
                if (rod != null && rod.activeSelf) rod.SetActive(false);
                return;
            }
            if (profileR == null) BuildProfile();
            if (ringZ == null || ringZ.Length < 2 || ringRMax <= 0f) return;
            if (rod == null && !BuildRod()) return;
            if (!rod.activeSelf) rod.SetActive(true);
            Transform bt = balloonObject.transform;
            float kx = bt.TransformVector(Vector3.right).magnitude, kz = bt.TransformVector(Vector3.forward).magnitude;
            if (!(kx > 0f) || !(kz > 0f)) return;
            float ext = swell > 0f ? 1f - (2f / 3f) * Ease(swell / 0.5f) : along;     // how far out, 0..1
            float zc = profileCentroid, fore = ringZ[ringZ.Length - 1] - zc, aft = zc - ringZ[0];
            float r0 = ringRMax * kx * rodWidth / Mathf.Max(across, rodWidth);        // the centre stage's radius, m
            bool tips = false;
            for (int k = 0; k < 3; k++)
            {
                float reach = Mathf.Min(ext, (k + 1) / 3f);
                float r = r0 * rodStageWidth[k];
                float dome = k == 2 ? r / kz : 0f;                       // the domes' height: their tips land at the ends
                float zf = zc + reach * fore - dome, za = zc - reach * aft + dome;
                bool on = ext > k / 3f + 1e-3f && zf > za;
                Transform st = rodStages[k];
                if (st.gameObject.activeSelf != on) st.gameObject.SetActive(on);
                if (!on) continue;
                st.localPosition = new Vector3(0f, 0f, za);
                st.localScale = new Vector3(r / kx, r / kx, zf - za);
                if (k != 2) continue;
                tips = true;
                Vector3 ts = new Vector3(r / kx, r / kx, dome);
                rodTips[0].localPosition = new Vector3(0f, 0f, zf);
                rodTips[1].localPosition = new Vector3(0f, 0f, za);
                rodTips[0].localScale = rodTips[1].localScale = ts;
            }
            foreach (Transform t in rodTips) if (t.gameObject.activeSelf != tips) t.gameObject.SetActive(tips);
        }

        bool BuildRod()
        {
            if (envelopeRenderer == null || envelopeRenderer.sharedMaterial == null) return false;
            Material own = envelopeRenderer.sharedMaterial;
            rodMat = new Material(own) { name = own.name + " (mast)" };
            if (rodMat.HasProperty("_MainTex")) rodMat.mainTexture = CasingGreyTexture();
            if (rodMat.HasProperty("_Color")) rodMat.color = Color.white;
            if (rodMat.HasProperty("_MaskTex")) rodMat.SetTexture("_MaskTex", Texture2D.blackTexture);
            if (rodMat.HasProperty("_BumpMap")) rodMat.SetTexture("_BumpMap", null);
            rod = new GameObject("RBRod");
            rod.layer = balloonObject.layer;
            rod.transform.SetParent(balloonObject.transform, false);
            rodStages = new Transform[3];
            for (int k = 0; k < 3; k++) rodStages[k] = RodPiece("RBRodStage" + k, RodMesh());
            rodTips = new[] { RodPiece("RBRodTipFore", DomeMesh()), RodPiece("RBRodTipAft", DomeMesh()) };
            rodTips[1].localRotation = Quaternion.Euler(180f, 0f, 0f);             // its dome to the tail
            return true;
        }

        Transform RodPiece(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            go.layer = rod.layer;
            go.transform.SetParent(rod.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = rodMat;
            mr.shadowCastingMode = envelopeRenderer.shadowCastingMode;
            mr.receiveShadows = envelopeRenderer.receiveShadows;
            return go.transform;
        }

        void DestroyRod()
        {
            if (rod != null) Destroy(rod);
            if (rodMat != null) Destroy(rodMat);
            rod = null;
            rodMat = null;
            rodStages = rodTips = null;
        }

        // A cylinder of radius 1 from z = 0 to 1, flat ends, every face out (Unity's front: Cross(b - a, c - a)).
        static Mesh RodMesh()
        {
            if (rodMesh != null) return rodMesh;
            const int n = 24;
            var v = new List<Vector3>(); var nm = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            for (int i = 0; i <= n; i++)                                       // the side
            {
                float a = 2f * Mathf.PI * i / n;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d); nm.Add(d); uv.Add(new Vector2((float)i / n, 0f));
                v.Add(d + Vector3.forward); nm.Add(d); uv.Add(new Vector2((float)i / n, 1f));
                if (i < n) { int b = 2 * i; tr.Add(b); tr.Add(b + 2); tr.Add(b + 1); tr.Add(b + 1); tr.Add(b + 2); tr.Add(b + 3); }
            }
            for (int end = 0; end < 2; end++)                                  // the ends
            {
                float z = end;
                Vector3 normal = end == 0 ? Vector3.back : Vector3.forward;
                int c = v.Count;
                v.Add(new Vector3(0f, 0f, z)); nm.Add(normal); uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= n; i++)
                {
                    float a = 2f * Mathf.PI * i / n;
                    v.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), z)); nm.Add(normal);
                    uv.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                    if (i < n)
                    {
                        if (end == 0) { tr.Add(c); tr.Add(c + i + 2); tr.Add(c + i + 1); }
                        else { tr.Add(c); tr.Add(c + i + 1); tr.Add(c + i + 2); }
                    }
                }
            }
            rodMesh = FinishMesh("RealBalloonsMast", v, nm, uv, tr);
            return rodMesh;
        }

        // A dome of radius 1 standing on z = 0, its tip at z = 1, every face out.
        static Mesh DomeMesh()
        {
            if (domeMesh != null) return domeMesh;
            const int n = 24, m = 8;
            var v = new List<Vector3>(); var nm = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
            for (int j = 0; j <= m; j++)
            {
                float t = 0.5f * Mathf.PI * j / m;
                for (int i = 0; i <= n; i++)
                {
                    float a = 2f * Mathf.PI * i / n;
                    Vector3 p = new Vector3(Mathf.Cos(a) * Mathf.Cos(t), Mathf.Sin(a) * Mathf.Cos(t), Mathf.Sin(t));
                    v.Add(p); nm.Add(p); uv.Add(new Vector2((float)i / n, (float)j / m));
                }
            }
            for (int j = 0; j < m; j++)
                for (int i = 0; i < n; i++)
                {
                    int a = j * (n + 1) + i, b = a + 1, c = a + n + 1, d = c + 1;
                    tr.Add(a); tr.Add(b); tr.Add(c); tr.Add(c); tr.Add(b); tr.Add(d);
                }
            domeMesh = FinishMesh("RealBalloonsMastTip", v, nm, uv, tr);
            return domeMesh;
        }

        static Mesh FinishMesh(string name, List<Vector3> v, List<Vector3> nm, List<Vector2> uv, List<int> tr)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v); mesh.SetNormals(nm); mesh.SetUVs(0, uv); mesh.SetTriangles(tr, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static Texture2D CasingGreyTexture()
        {
            if (casingGreyTex != null) return casingGreyTex;
            casingGreyTex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "RealBalloonsRodGrey" };
            Color32 c = casingGrey;
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            casingGreyTex.SetPixels32(px);
            casingGreyTex.Apply(false, true);
            return casingGreyTex;
        }

        // How far the base's rim stands above the envelope's seat, along the part's up (m): the seat is down
        // inside the base, where a round envelope rises out of it. The rim, not the middle of its opening, where
        // a cone (seen only from inside) and the rope's hook stand higher. Measured from the mesh, again only
        // when the base's size changes.
        float baseClearance = -1f;
        Vector3 baseClearanceKey;

        float BaseClearance(Vector3 basePos)
        {
            Vector3 key = capObject != null ? capObject.transform.lossyScale : part.transform.lossyScale;
            if (baseClearance >= 0f && key == baseClearanceKey) return baseClearance;
            Vector3 up = part.transform.up;
            var pts = new List<Vector3>();
            foreach (MeshRenderer r in part.FindModelComponents<MeshRenderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Transform t = r.transform;
                if (t.IsChildOf(balloonObject.transform) || (ropeObject != null && t.IsChildOf(ropeObject.transform)) ||
                    (capObject != null && t.IsChildOf(capObject.transform))) continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh mesh = mf.sharedMesh;
                Matrix4x4 m = t.localToWorldMatrix;
                if (mesh.isReadable)
                    foreach (Vector3 vtx in mesh.vertices) pts.Add(m.MultiplyPoint3x4(vtx) - basePos);
                else
                {
                    Bounds b = mesh.bounds;
                    for (int i = 0; i < 8; i++)
                        pts.Add(m.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y,
                                                               (i & 4) == 0 ? b.min.z : b.max.z)) - basePos);
                }
            }
            float rMax = 0f;
            foreach (Vector3 q in pts) rMax = Mathf.Max(rMax, (q - Vector3.Dot(q, up) * up).magnitude);
            float top = 0f;
            foreach (Vector3 q in pts)
                if ((q - Vector3.Dot(q, up) * up).magnitude >= 0.5f * rMax) top = Mathf.Max(top, Vector3.Dot(q, up));
            baseClearance = top;
            baseClearanceKey = key;
            return top;
        }

        // ---- Evenly scaled frame (flight) ----
        // Sizes 4-7 scale the model deeper than it's wide (the size switcher's scaleOffset: width, depth,
        // width). Anything turned inside an unevenly scaled frame is sheared, and on a rope the envelope
        // and the rope turn to stay upright: whenever the craft tilted (pitching in powered flight) they
        // stretched and skewed. In flight both move into a frame under the model's root that undoes its
        // uneven part: same place, evenly scaled at the base's width, so they can turn any way. The rope
        // keeps its modelled length (the base's depth) through RopeDepth. Other model layouts (envelope and
        // rope not side by side under one parent) are left as they are.
        Transform modelRoot, evenFrame;
        bool ropeInFrame;
        Vector3 ropeHomePos;            // the rope's foot, in the model root's frame
        Quaternion ropeModelRot;        // the rope as modelled, in the model root's frame
        float ropeDepth = 1f;           // the base's scale along the rope over its width

        float RopeDepth { get { return ropeInFrame ? ropeDepth : 1f; } }

        void SetUpEvenFrame()
        {
            Transform bt = balloonObject.transform, rt = ropeObject.transform;
            if (evenFrame != null) return;
            modelRoot = bt.parent;
            initialBalloonPos = bt.localPosition;       // in the model root's frame (EnvelopeHome)
            if (modelRoot == null || rt.parent != modelRoot) return;
            ropeHomePos = rt.localPosition;
            ropeModelRot = rt.localRotation;
            GameObject go = new GameObject("RBEvenFrame");
            go.layer = balloonObject.layer;
            evenFrame = go.transform;
            evenFrame.SetParent(modelRoot, false);
            SyncEvenFrame();
            bt.SetParent(evenFrame, true);
            rt.SetParent(evenFrame, true);
            ropeInFrame = true;
        }

        // Undo the root's uneven scale (it's set by the size switcher, so follow it if it changes).
        void SyncEvenFrame()
        {
            if (evenFrame == null || modelRoot == null) return;
            Vector3 s = modelRoot.localScale;
            float w = Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.z));
            if (!(w > 0f) || s.y == 0f) return;
            Vector3 want = new Vector3(w / s.x, w / s.y, w / s.z);
            if (evenFrame.localScale != want) evenFrame.localScale = want;
            Vector3 a = ropeModelRot * Vector3.forward;                     // the rope's length axis
            ropeDepth = new Vector3(a.x * s.x, a.y * s.y, a.z * s.z).magnitude / w;
        }

        // Where the envelope sits on the part as modelled (world).
        Vector3 EnvelopeHome()
        {
            Transform home = modelRoot != null ? modelRoot : balloonObject.transform.parent;
            return home != null ? home.TransformPoint(initialBalloonPos) : part.transform.position;
        }

        // The rope's foot stays where it's modelled on the part.
        void PlaceRopeFoot()
        {
            if (ropeInFrame && modelRoot != null) ropeObject.transform.position = modelRoot.TransformPoint(ropeHomePos);
        }

        // ---- Envelope colour ----
        // Colours are TexturesUnlimited's, the RO standard: RealBalloon.cfg adds KSPTextureSwitch and
        // SSTURecolorGUI when TU is installed, and "Open Recoloring GUI" in the part menu sets one colour for
        // the whole envelope and cap, with gloss and metal, from TU's palette or the balloon films. TU gives
        // them their own material and recolours it in place; the blimp fins share it (ShowFins). Without TU
        // the envelope is natural film. This module doesn't touch the materials.
        //
        // Craft saved before this kept their colour in envelopeColour (a preset or Custom), colourRed/Green/
        // Blue (%) and colourStrength (% of the way from the natural film). The first time one loads, that
        // colour is written into TU's saved colours before TU reads them; the next save drops the old values.
        static readonly Dictionary<string, Color> legacyPresets = new Dictionary<string, Color>
        {
            { "White", new Color(0.95f, 0.95f, 0.95f) }, { "Silver", new Color(0.72f, 0.74f, 0.77f) },
            { "Black", new Color(0.07f, 0.07f, 0.08f) }, { "Red", new Color(0.72f, 0.10f, 0.09f) },
            { "Orange", new Color(0.93f, 0.43f, 0.09f) }, { "Yellow", new Color(0.95f, 0.80f, 0.18f) },
            { "Green", new Color(0.18f, 0.50f, 0.22f) }, { "Blue", new Color(0.13f, 0.28f, 0.70f) },
            { "Purple", new Color(0.42f, 0.19f, 0.60f) }
        };
        static readonly Color naturalFilm = new Color(247f / 255f, 241f / 255f, 208f / 255f);   // rbNatural in RealBalloon.cfg
        const float filmGloss = 48f / 255f;                                                     // the films' specular there

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            UnrollEnvelope();       // at load on the part prefab, so the parts list icon has it too
            MigrateLegacyColour(node);
        }

        // ---- Envelope UVs ----
        // The model maps its fabric texture straight down the envelope's axis: a disc in KBTexKerbin, where a
        // vertex's distance from the centre is its radius. Patterns drawn on a disc cut across the texels
        // (band edges are circles there), which showed as teeth at any distance. So the envelope is unrolled
        // here: u runs once round it, from the angle the disc gave each vertex (so the gores and seams keep
        // their places: u = k / 8), and v from one end (0) through the widest part (0.5) to the other end (1),
        // evenly along the envelope. Band edges then run along texel rows and gore seams along columns. The
        // envelope gets its own material with the fabric unrolled the same way (RealBalloons_Envelope), and
        // the TU masks are drawn on that grid (Source/Tools/make_masks.py). Where u wraps the mesh gets a
        // seam, and the vertices at its tips are split per triangle. Done once, on the model's own mesh and
        // the envelope's material, which every copy of the part shares.
        const string unrolledTag = " (RB unrolled)";
        const string envelopeTexture = "RealBalloons/Parts/RealBalloon/RealBalloons_Envelope";
        const int evenUVBins = 180;

        // The packed cap keeps its shape but gets a texture of its own as well: its UVs (a disc in KBTexKerbin, which
        // maps the flat cap exactly) are spread to fill RealBalloons_Cap, the cap's art from KBTexKerbin at four and
        // a half times the texels, so its pattern (TU's capmask, the same square) stays sharp up close. (Unrolled
        // like the envelope, its few segments bent the pattern near the middle.)
        const string capTexture = "RealBalloons/Parts/RealBalloon/RealBalloons_Cap";
        const float capFill = 0.48f;            // the cap's disc radius in its own texture

        void UnrollEnvelope()
        {
            Texture2D envTex = GameDatabase.Instance != null ? GameDatabase.Instance.GetTexture(envelopeTexture, false) : null;
            Texture2D capTex = GameDatabase.Instance != null ? GameDatabase.Instance.GetTexture(capTexture, false) : null;
            if (envTex == null || capTex == null) { Debug.LogError("[RealBalloons] envelope or cap texture not found: left as modelled"); return; }
            GameObject env = balloonObject != null ? balloonObject : FindChild(envelopeTransform);
            GameObject cap = capObject != null ? capObject : FindChild(capTransform);
            MeshFilter mf = env != null ? env.GetComponent<MeshFilter>() : null;
            MeshFilter cmf = cap != null ? cap.GetComponent<MeshFilter>() : null;
            Mesh mesh = mf != null ? mf.sharedMesh : null, cmesh = cmf != null ? cmf.sharedMesh : null;
            if (mesh == null) return;
            // Done when the UVs say so, not by name: anything that takes a mesh of its own from a renderer gets a
            // renamed copy of the unrolled one, and unrolling that again put the poles on the envelope's side.
            if (UVSpan(mesh) < 0.75f)
            {
                if (!mesh.isReadable || !UnrollMesh(mesh)) { Debug.LogError("[RealBalloons] couldn't unroll the envelope mesh"); return; }
                mesh.name += unrolledTag;
            }
            if (cmesh != null && cmesh.isReadable && UVSpan(cmesh) < 0.5f) SpreadCapUVs(cmesh);
            // the materials only on the prefab, at load: later loads (B9PartSwitch reloads the module on a size
            // switch) would otherwise replace TexturesUnlimited's materials with copies and cut off its recolouring
            if (HighLogic.LoadedScene != GameScenes.LOADING) return;
            UseOwnTexture(env.GetComponent<Renderer>(), envTex);
            if (cmesh != null && UVSpan(cmesh) >= 0.5f) UseOwnTexture(cap.GetComponent<Renderer>(), capTex);
        }

        // A copy of the renderer's material with this texture (the base, rope and sticker keep the model's).
        static void UseOwnTexture(Renderer r, Texture2D tex)
        {
            if (r == null || r.sharedMaterial == null || r.sharedMaterial.name.EndsWith(unrolledTag)) return;
            Material m = new Material(r.sharedMaterial);
            m.name = r.sharedMaterial.name + unrolledTag;
            m.mainTexture = tex;
            r.sharedMaterial = m;
        }

        // The larger side of a mesh's UV box: under half for the model's discs, about 1 once spread or unrolled.
        static float UVSpan(Mesh mesh)
        {
            Vector2[] uv = mesh.uv;
            if (uv.Length == 0) return 0f;
            Vector2 lo = uv[0], hi = uv[0];
            for (int i = 1; i < uv.Length; i++) { lo = Vector2.Min(lo, uv[i]); hi = Vector2.Max(hi, uv[i]); }
            return Mathf.Max(hi.x - lo.x, hi.y - lo.y);
        }

        // The cap's disc, centred and spread to capFill of its own square.
        static void SpreadCapUVs(Mesh mesh)
        {
            Vector2[] uv = mesh.uv;
            Vector2 lo = uv[0], hi = uv[0];
            for (int i = 1; i < uv.Length; i++) { lo = Vector2.Min(lo, uv[i]); hi = Vector2.Max(hi, uv[i]); }
            Vector2 c = (lo + hi) / 2f;
            float half = Mathf.Max(hi.x - lo.x, hi.y - lo.y) / 2f;
            if (!(half > 0f)) return;
            float k = capFill / half;
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(0.5f, 0.5f) + (uv[i] - c) * k;
            mesh.uv = uv;
        }

        // Rebuild an envelope mesh (its axis local Z, the model's disc UVs) with unrolled UVs.
        static bool UnrollMesh(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int n = v.Length;
            float[] frac; bool[] firstHalf;
            if (n == 0 || uv.Length != n || !ProfileFractions(v, out frac, out firstHalf)) return false;

            Vector2 lo = uv[0], hi = uv[0];
            for (int i = 1; i < n; i++) { lo = Vector2.Min(lo, uv[i]); hi = Vector2.Max(hi, uv[i]); }
            Vector2 c = (lo + hi) / 2f;
            float rmax = 0f;
            for (int i = 0; i < n; i++) rmax = Mathf.Max(rmax, (uv[i] - c).magnitude);
            float tipR = 0.01f * rmax;              // within 1% of the axis: a tip, whose angle means nothing
            float[] u = new float[n], vv = new float[n];
            bool[] tip = new bool[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 d = uv[i] - c;
                tip[i] = d.magnitude < tipR;
                u[i] = tip[i] ? 0f : Mathf.Repeat(Mathf.Atan2(d.y, d.x) / (2f * Mathf.PI), 1f);
                vv[i] = firstHalf[i] ? 0.5f * frac[i] : 1f - 0.5f * frac[i];
            }

            // every vertex once as is, then copies where a triangle needs another u (across the seam, at a tip)
            Vector3[] nrm = mesh.normals; Vector4[] tan = mesh.tangents; Color[] col = mesh.colors; Vector2[] uv2 = mesh.uv2;
            var pos = new List<Vector3>(v); var outUV = new List<Vector2>(n);
            var outN = nrm.Length == n ? new List<Vector3>(nrm) : null;
            var outT = tan.Length == n ? new List<Vector4>(tan) : null;
            var outC = col.Length == n ? new List<Color>(col) : null;
            var outUV2 = uv2.Length == n ? new List<Vector2>(uv2) : null;
            for (int i = 0; i < n; i++) outUV.Add(new Vector2(u[i], vv[i]));
            var copies = new Dictionary<long, int>();
            System.Func<int, float, int> vertexWithU = (i, want) =>
            {
                if (Mathf.Abs(want - u[i]) < 1e-6f) return i;
                long key = ((long)i << 32) | (uint)Mathf.RoundToInt(want * 1e6f);
                int k;
                if (copies.TryGetValue(key, out k)) return k;
                k = pos.Count;
                pos.Add(v[i]); outUV.Add(new Vector2(want, vv[i]));
                if (outN != null) outN.Add(nrm[i]);
                if (outT != null) outT.Add(tan[i]);
                if (outC != null) outC.Add(col[i]);
                if (outUV2 != null) outUV2.Add(uv2[i]);
                copies[key] = k;
                return k;
            };
            int subs = mesh.subMeshCount;
            var tris = new List<int[]>();
            float[] tu = new float[3];
            for (int s = 0; s < subs; s++)
            {
                int[] t = mesh.GetTriangles(s);
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    float min = 2f, max = -1f;
                    for (int j = 0; j < 3; j++)
                        if (!tip[t[k + j]]) { min = Mathf.Min(min, u[t[k + j]]); max = Mathf.Max(max, u[t[k + j]]); }
                    bool wraps = max - min > 0.5f;
                    float sum = 0f; int cnt = 0;
                    for (int j = 0; j < 3; j++)
                    {
                        int i = t[k + j];
                        if (tip[i]) continue;
                        tu[j] = wraps && u[i] < 0.5f ? u[i] + 1f : u[i];
                        sum += tu[j]; cnt++;
                    }
                    for (int j = 0; j < 3; j++)
                    {
                        int i = t[k + j];
                        if (tip[i]) tu[j] = cnt > 0 ? sum / cnt : 0f;
                        t[k + j] = vertexWithU(i, tu[j]);
                    }
                }
                tris.Add(t);
            }
            if (pos.Count > 65000) return false;
            mesh.Clear();
            mesh.SetVertices(pos);
            if (outN != null) mesh.SetNormals(outN);
            if (outT != null) mesh.SetTangents(outT);
            if (outC != null) mesh.SetColors(outC);
            mesh.SetUVs(0, outUV);
            if (outUV2 != null) mesh.SetUVs(1, outUV2);
            mesh.subMeshCount = subs;
            for (int s = 0; s < subs; s++) mesh.SetTriangles(tris[s], s);
            mesh.RecalculateBounds();
            return true;
        }

        // Each vertex's distance along the envelope from the nearer end (tail or top), as a share of that end's
        // way to the widest part, and which half it's in (an envelope mesh, its axis local Z).
        static bool ProfileFractions(Vector3[] v, out float[] frac, out bool[] firstHalf)
        {
            int n = v.Length;
            frac = new float[n]; firstHalf = new bool[n];
            // the profile: radius and height against the angle about the widest ring's centre
            float[] r = new float[n], phi = new float[n];
            float rmax = 0f;
            for (int i = 0; i < n; i++) { r[i] = Mathf.Sqrt(v[i].x * v[i].x + v[i].y * v[i].y); rmax = Mathf.Max(rmax, r[i]); }
            double zsum = 0; int zcount = 0;
            for (int i = 0; i < n; i++) if (r[i] > rmax - 1e-3f) { zsum += v[i].z; zcount++; }
            if (zcount == 0) return false;
            float zc = (float)(zsum / zcount);
            double[] br = new double[evenUVBins], bz = new double[evenUVBins], bp = new double[evenUVBins];
            int[] cnt = new int[evenUVBins];
            for (int i = 0; i < n; i++)
            {
                phi[i] = Mathf.Atan2(v[i].z - zc, r[i]);
                int b = Mathf.Clamp((int)((phi[i] + Mathf.PI / 2f) / Mathf.PI * evenUVBins), 0, evenUVBins - 1);
                br[b] += r[i]; bz[b] += v[i].z; bp[b] += phi[i]; cnt[b]++;
            }
            // distance along the profile at each bin's mean angle
            var angles = new List<float>(); var dist = new List<float>();
            float acc = 0f, pr = 0f, pz = 0f;
            for (int b = 0; b < evenUVBins; b++)
            {
                if (cnt[b] == 0) continue;
                float mr = (float)(br[b] / cnt[b]), mz = (float)(bz[b] / cnt[b]);
                if (angles.Count > 0) acc += Mathf.Sqrt((mr - pr) * (mr - pr) + (mz - pz) * (mz - pz));
                angles.Add((float)(bp[b] / cnt[b])); dist.Add(acc);
                pr = mr; pz = mz;
            }
            if (angles.Count < 2) return false;
            float sEq = Interp(0f, angles, dist), sEnd = acc;
            if (sEq <= 0f || sEnd - sEq <= 0f) return false;
            for (int i = 0; i < n; i++)
            {
                float s = Interp(phi[i], angles, dist);
                firstHalf[i] = phi[i] <= 0f;
                frac[i] = Mathf.Clamp01(firstHalf[i] ? s / sEq : (sEnd - s) / (sEnd - sEq));
            }
            return true;
        }

        // Piecewise-linear lookup, held at the ends (as numpy's interp).
        static float Interp(float x, List<float> xs, List<float> ys)
        {
            int last = xs.Count - 1;
            if (x <= xs[0]) return ys[0];
            if (x >= xs[last]) return ys[last];
            int lo = 0, hi = last;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (xs[mid] <= x) lo = mid; else hi = mid; }
            return Mathf.Lerp(ys[lo], ys[hi], (x - xs[lo]) / (xs[hi] - xs[lo]));
        }

        void MigrateLegacyColour(ConfigNode node)
        {
            string preset = node.GetValue("envelopeColour");
            if (string.IsNullOrEmpty(preset) || preset == "Natural") return;
            Color target;
            if (preset == "Custom")
                target = new Color(LegacyFraction(node, "colourRed"), LegacyFraction(node, "colourGreen"), LegacyFraction(node, "colourBlue"));
            else if (!legacyPresets.TryGetValue(preset, out target)) return;
            PartModule tu = null;
            foreach (PartModule pm in part.Modules)
                if (pm != null && pm.GetType().Name == "KSPTextureSwitch") { tu = pm; break; }
            BaseField saved = tu != null ? tu.Fields["persistentData"] : null;
            if (saved == null || !string.IsNullOrEmpty(saved.GetValue(tu) as string)) return;   // no TU, or it has colours already
            Color c = Color.Lerp(naturalFilm, target, LegacyFraction(node, "colourStrength"));
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            // TU's format: r,g,b,specular,metal,detail (0-1) for each of its three channels
            string one = string.Join(",", new[] { c.r, c.g, c.b, filmGloss, 0f, 1f }.Select(v => v.ToString("0.###", ci)).ToArray());
            saved.SetValue(one + ";" + one + ";" + one, tu);
        }

        static float LegacyFraction(ConfigNode node, string name)
        {
            float v;
            string s = node.GetValue(name);
            if (s == null || !float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) v = 100f;
            return Mathf.Clamp01(v / 100f);
        }

        // ---- Cushion collider ----
        // The model's sphere collider is removed in flight: it shoved the craft about as the envelope grew,
        // and on a blimp it was a sphere as long as the hull. The cushion gets its own instead, a convex hull
        // of the envelope's real shape: it pushes the craft up off the ground as it inflates, the craft can
        // rest and land on it, and kerbals can walk on it. Parented to the envelope, so it grows, squashes and
        // turns with it, and KSP is told to keep it out of the craft's own parts (its same-vessel collision
        // ignores, refreshed by OnCollisionIgnoreUpdate). Only on the cushion, only while it's out: the rope
        // balloon and the blimp stay without one. The keel and its weight have none (they hang underground
        // while the cushion sits on the ground).
        const int hullSegments = 12, hullRings = 8;           // 86 points: well inside PhysX's 255-polygon convex limit
        GameObject cushionCollider;

        void UpdateCushionCollider()
        {
            bool want = Cushion && Lifting && inflation > 0.02f && balloonObject != null && balloonObject.activeInHierarchy;
            if (want && cushionCollider == null) BuildCushionCollider();
            if (cushionCollider == null || cushionCollider.activeSelf == want) return;
            cushionCollider.SetActive(want);
            GameEvents.OnCollisionIgnoreUpdate.Fire();
        }

        // Index of ring i's vertex k in the hull (ring 1 is nearest the tail; k wraps round).
        static int HullRing(int i, int k) { return 2 + (i - 1) * hullSegments + (k % hullSegments); }

        void BuildCushionCollider()
        {
            if (profileR == null) BuildProfile();
            int n = profileR.Length;
            if (n < 2) return;
            float z0 = profileZ0, z1 = profileZ0 + profileDz * (n - 1);
            // a lathe of the envelope's profile in its own frame: the two ends on the axis, rings between
            var verts = new List<Vector3> { new Vector3(0f, 0f, z0), new Vector3(0f, 0f, z1) };
            for (int i = 1; i < hullRings; i++)
            {
                float z = Mathf.Lerp(z0, z1, i / (float)hullRings), r = EnvelopeRadiusAt(z);
                for (int k = 0; k < hullSegments; k++)
                {
                    float a = k * 2f * Mathf.PI / hullSegments;
                    verts.Add(new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z));
                }
            }
            var tris = new List<int>();
            for (int k = 0; k < hullSegments; k++)
            {
                tris.AddRange(new[] { 0, HullRing(1, k + 1), HullRing(1, k) });                                 // tail cap
                tris.AddRange(new[] { 1, HullRing(hullRings - 1, k), HullRing(hullRings - 1, k + 1) });         // top cap
                for (int i = 1; i < hullRings - 1; i++)
                    tris.AddRange(new[] { HullRing(i, k), HullRing(i, k + 1), HullRing(i + 1, k + 1), HullRing(i, k), HullRing(i + 1, k + 1), HullRing(i + 1, k) });
            }
            Mesh hull = new Mesh { name = "RBCushionHull" };
            hull.SetVertices(verts);
            hull.SetTriangles(tris, 0);
            hull.RecalculateBounds();
            cushionCollider = new GameObject("RBCushionCollider");
            cushionCollider.layer = part.gameObject.layer;
            cushionCollider.SetActive(false);
            cushionCollider.transform.SetParent(balloonObject.transform, false);
            MeshCollider mc = cushionCollider.AddComponent<MeshCollider>();
            mc.sharedMesh = hull;
            mc.convex = true;
        }

        // ---- Blimp fins ----
        // Four tail fins in a + (top, bottom, both sides) on the tapered end of the hull, each with its rear
        // part hinged: rudders on the top and bottom fins, elevators on the side ones (the forces are in
        // ModuleRealBalloon.Forces). Built once as meshes in the envelope's own frame and parented to it, so
        // they grow with the gas and stretch with the hull. They use the envelope's material, sampled at its
        // fabric. Fin outline in the envelope's frame (local Z along the axis from the tail point, radius from it):
        const float finTrailZ = 0.3f, finRootLeadZ = 1.25f, finTipLeadZ = 0.66f;   // model units
        const float finTipRadius = 2.0f;            // about the hull's widest radius (1.98)
        const float finRootThick = 0.06f, finTipThick = 0.035f, finEmbed = 0.05f;
        const float finHingeZ = 0.5f;               // the control surfaces run from here to the trailing edge
        // A texel of the envelope texture with the fabric's average brightness, mid-gore: off the seams
        // (load tapes) and inside the first gore, which is the main colour in every TU pattern.
        [KSPField] public Vector2 finTexel = new Vector2(0.0625f, 0.345f);
        GameObject fins;
        MeshRenderer finRenderer;
        MeshRenderer[] flapRenderers;
        Transform[] finFlaps;                       // hinge frames: X along the hinge (out from the axis), Y the fin's side, Z forward
        Quaternion[] flapRest;
        Renderer envelopeRenderer;

        // Out from the axis, and the fin's side, for fin k: +X (up on a blimp), +Y, -X, -Y.
        static Vector3 FinOut(int k) { float a = k * Mathf.PI / 2f; return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f); }
        static Vector3 FinSide(int k) { float a = k * Mathf.PI / 2f; return new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f); }

        // The root runs straight between the fabric at the root's ends, sunk into it a little.
        float FinRootAt(float z)
        {
            float r0 = EnvelopeRadiusAt(finTrailZ) - finEmbed, r1 = EnvelopeRadiusAt(finRootLeadZ) - finEmbed;
            return Mathf.Lerp(r0, r1, (z - finTrailZ) / (finRootLeadZ - finTrailZ));
        }

        void ShowFins(bool show)
        {
            if (show && fins == null) BuildFins();
            if (fins == null) return;
            if (fins.activeSelf != show) fins.SetActive(show);
            // whatever material the envelope has now (TexturesUnlimited gives it its own)
            if (show && envelopeRenderer != null)
            {
                Material m = envelopeRenderer.sharedMaterial;
                if (finRenderer.sharedMaterial != m) finRenderer.sharedMaterial = m;
                foreach (MeshRenderer r in flapRenderers) if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
            }
        }

        void BuildFins()
        {
            MeshFilter envMf = balloonObject.GetComponent<MeshFilter>();
            Renderer envR = balloonObject.GetComponent<Renderer>();
            if (envMf == null || envMf.sharedMesh == null || envR == null) return;
            envelopeRenderer = envR;
            // a part copied in the VAB mid-preview comes with the original's fins: use those
            Transform copied = balloonObject.transform.Find("RBFins");
            if (copied != null)
            {
                MeshRenderer copiedR = copied.GetComponent<MeshRenderer>();
                var flaps = Enumerable.Range(0, 4).Select(k => copied.Find("RBFinFlap" + k)).ToArray();
                if (copiedR != null && flaps.All(f => f != null && f.GetComponent<MeshRenderer>() != null))
                {
                    fins = copied.gameObject;
                    finRenderer = copiedR;
                    finFlaps = flaps;
                    flapRenderers = flaps.Select(f => f.GetComponent<MeshRenderer>()).ToArray();
                    flapRest = Enumerable.Range(0, 4).Select(k => Quaternion.LookRotation(Vector3.forward, FinSide(k))).ToArray();
                    return;
                }
                Destroy(copied.gameObject);
            }
            fins = new GameObject("RBFins");
            fins.layer = balloonObject.layer;
            fins.transform.SetParent(balloonObject.transform, false);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            finFlaps = new Transform[4];
            flapRenderers = new MeshRenderer[4];
            flapRest = new Quaternion[4];
            for (int k = 0; k < 4; k++)
            {
                Vector3 u = FinOut(k), w = FinSide(k), f = Vector3.forward;
                Vector3 rootLead = u * (EnvelopeRadiusAt(finRootLeadZ) - finEmbed) + f * finRootLeadZ;
                Vector3 rootHinge = u * FinRootAt(finHingeZ) + f * finHingeZ;
                Vector3 rootTrail = u * (EnvelopeRadiusAt(finTrailZ) - finEmbed) + f * finTrailZ;
                Vector3 tipTrail = u * finTipRadius + f * finTrailZ;
                Vector3 tipHinge = u * finTipRadius + f * finHingeZ;
                Vector3 tipLead = u * finTipRadius + f * finTipLeadZ;
                float[] th = { finRootThick, finRootThick, finTipThick, finTipThick };
                // the fixed fin, ahead of the hinge
                AddSlab(verts, tris, new[] { rootLead, rootHinge, tipHinge, tipLead }, th, w);
                // the control surface, in its hinge frame: X out along the hinge, Y the fin's side, Z forward
                Vector3 hinge = f * finHingeZ;
                Vector3[] o = { rootHinge, rootTrail, tipTrail, tipHinge };
                for (int i = 0; i < 4; i++) { Vector3 d = o[i] - hinge; o[i] = new Vector3(Vector3.Dot(d, u), Vector3.Dot(d, w), Vector3.Dot(d, f)); }
                var fv = new List<Vector3>();
                var ft = new List<int>();
                AddSlab(fv, ft, o, th, Vector3.up);
                GameObject flap = new GameObject("RBFinFlap" + k);
                flap.layer = balloonObject.layer;
                flap.transform.SetParent(fins.transform, false);
                flap.transform.localPosition = hinge;
                flapRest[k] = Quaternion.LookRotation(Vector3.forward, w);
                flap.transform.localRotation = flapRest[k];
                flap.AddComponent<MeshFilter>().sharedMesh = FinMesh("RBFinFlap", fv, ft);
                flapRenderers[k] = flap.AddComponent<MeshRenderer>();
                flapRenderers[k].sharedMaterial = envR.sharedMaterial;
                finFlaps[k] = flap.transform;
            }
            fins.AddComponent<MeshFilter>().sharedMesh = FinMesh("RBFins", verts, tris);
            finRenderer = fins.AddComponent<MeshRenderer>();
            finRenderer.sharedMaterial = envR.sharedMaterial;
        }

        Mesh FinMesh(string name, List<Vector3> verts, List<int> tris)
        {
            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, Enumerable.Repeat(finTexel, verts.Count).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A flat plate with this outline (4 points), its thickness th across 'side', faces and edges.
        static void AddSlab(List<Vector3> verts, List<int> tris, Vector3[] o, float[] th, Vector3 side)
        {
            Vector3[] p = new Vector3[8];
            for (int i = 0; i < 4; i++) { p[i] = o[i] + side * (th[i] / 2f); p[i + 4] = o[i] - side * (th[i] / 2f); }
            AddQuad(verts, tris, p[0], p[1], p[2], p[3], side);                 // the two faces
            AddQuad(verts, tris, p[4], p[5], p[6], p[7], -side);
            Vector3 centre = (o[0] + o[1] + o[2] + o[3]) / 4f;
            for (int i = 0; i < 4; i++)                                           // the edges
            {
                int j = (i + 1) % 4;
                AddQuad(verts, tris, p[i], p[j], p[j + 4], p[i + 4], (o[i] + o[j]) / 2f - centre);
            }
        }

        // Swing each control surface to its deflection (degrees, as seen: positive moves its trailing edge to
        // the fin's +side). The hull is stretched along its axis, so the turn in the hinge frame is set to look
        // that angle after the stretch.
        void PoseFlaps(float[] deg)
        {
            if (finFlaps == null) return;
            Transform bt = balloonObject.transform;
            float sz = bt.TransformVector(Vector3.forward).magnitude;
            for (int k = 0; k < 4; k++)
            {
                if (finFlaps[k] == null) continue;
                float sn = bt.TransformVector(FinSide(k)).magnitude * Mathf.Max(finGrowth, 0.01f);
                float d = deg != null ? deg[k] : 0f;
                float local = sn > 0f ? Mathf.Atan(Mathf.Tan(d * Mathf.Deg2Rad) * sz / sn) * Mathf.Rad2Deg : d;
                Quaternion want = flapRest[k] * Quaternion.AngleAxis(local, Vector3.right);
                if (finFlaps[k].localRotation != want) finFlaps[k].localRotation = want;
            }
        }

        // A flat quad facing 'outward' (its own vertices, so it shades flat).
        static void AddQuad(List<Vector3> v, List<int> tr, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int n = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            // Unity draws the side where (b - a) x (c - a) points
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f;
            if (!flip) { tr.Add(n); tr.Add(n + 1); tr.Add(n + 2); tr.Add(n); tr.Add(n + 2); tr.Add(n + 3); }
            else { tr.Add(n); tr.Add(n + 2); tr.Add(n + 1); tr.Add(n); tr.Add(n + 3); tr.Add(n + 2); }
        }

        // The flag quads painted on the envelope (the FlagDecal module's).
        void EnsureFlagQuads()
        {
            if (flagQuads != null) return;
            string quad = "flagDecal";
            FlagDecal fd = part.FindModuleImplementing<FlagDecal>();
            if (fd != null && !string.IsNullOrEmpty(fd.textureQuadName)) quad = fd.textureQuadName;
            flagQuads = balloonObject.GetComponentsInChildren<Transform>(true).Where(t => t.name == quad).ToArray();
            flagBaseScales = flagQuads.Select(t => t.localScale).ToArray();
        }

        // ---- Envelope flag ----
        // The flag painted on the envelope: any from KSP's flag browser ("Choose envelope flag" in the VAB), or
        // the mission flag when none is picked. Stock FlagDecal paints the part's flagURL, so the pick goes
        // there; it's kept here too, because the VAB sets every part's flag again when the mission flag changes.
        [KSPField(isPersistant = true)] public string envelopeFlag = "";
        bool flagPending;

        [KSPEvent(guiActive = false, guiActiveEditor = true, guiName = "Choose envelope flag")]
        public void ChooseEnvelopeFlag()
        {
            GameObject prefab = new FlagBrowserGUIButton(null, null, null, null).FlagBrowserPrefab;
            GameObject go = prefab != null ? Instantiate(prefab) as GameObject : null;
            FlagBrowser fb = go != null ? go.GetComponent<FlagBrowser>() : null;
            if (fb == null) { if (go != null) Destroy(go); return; }
            fb.OnFlagSelected = e => SetEnvelopeFlag(e != null && e.textureInfo != null ? e.textureInfo.name : "");
            fb.OnDismiss = () => { };
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiName = "Use mission flag on envelope")]
        public void UseMissionFlag() { SetEnvelopeFlag(""); }

        // On this part and its symmetry counterparts.
        void SetEnvelopeFlag(string url)
        {
            foreach (Part p in new[] { part }.Concat(part.symmetryCounterparts))
            {
                ModuleRealBalloon m = p != null ? p.FindModuleImplementing<ModuleRealBalloon>() : null;
                if (m == null) continue;
                m.envelopeFlag = url ?? "";
                m.ApplyEnvelopeFlag();
                if (m.envelopeFlag.Length == 0 && HighLogic.LoadedSceneIsEditor) m.PaintFlag(EditorLogic.FlagURL);
            }
        }

        void ApplyEnvelopeFlag()
        {
            BaseEvent use = Events["UseMissionFlag"];
            if (use != null) use.guiActiveEditor = !string.IsNullOrEmpty(envelopeFlag);
            if (!string.IsNullOrEmpty(envelopeFlag)) PaintFlag(envelopeFlag);
        }

        void PaintFlag(string url)
        {
            if (string.IsNullOrEmpty(url) || GameDatabase.Instance == null || GameDatabase.Instance.GetTexture(url, false) == null) return;
            part.flagURL = url;
            FlagDecal fd = part.FindModuleImplementing<FlagDecal>();
            if (fd != null) fd.UpdateFlagTexture();
        }

        // The mission flag changed: FlagDecal repaints it, then this puts the envelope's own back (next frame).
        void OnMissionFlag(string url) { flagPending = true; }

        // The envelope's own meshes, without the flag painted on it (the flag's quad can reach past
        // the fabric, which left a gap between the cushion and the part).
        bool OnFlag(Transform t)
        {
            if (fins != null && t.IsChildOf(fins.transform)) return true;   // nor the blimp's fins
            if (rod != null && t.IsChildOf(rod.transform)) return true;     // nor its spar
            EnsureFlagQuads();
            foreach (Transform f in flagQuads) if (f != null && t.IsChildOf(f)) return true;
            return false;
        }

        // Stretched or squashed envelopes: the flag is painted on the fabric, so it's reshaped in its own
        // mesh. This envelope is round about its axis (local Z), so a flag point moved around that axis,
        // keeping its height and its distance from the axis, stays exactly on the fabric however the
        // envelope is scaled. Squeezing each flag around the axis by the envelope's height-to-width
        // scale keeps its proportions, at the same height on the envelope as on the round one.
        MeshFilter[] flagFilters;
        Mesh[] flagCopies;
        Vector3[][] flagVerts;                 // in the envelope's frame, as modelled
        Matrix4x4[] flagToEnv;
        float[][] flagCentres;                 // azimuth of each side's flag (+Y side, -Y side)
        float[][] flagHeights;                 // height on the axis of each side's flag
        Vector3[] flagShape;                   // what each flag mesh is shaped for: (hull?, a, b)

        void KeepFlagProportions()
        {
            EnsureFlagQuads();
            Transform bt = balloonObject.transform;
            for (int i = 0; i < flagQuads.Length; i++)
                if (flagQuads[i] != null && flagQuads[i].localScale != flagBaseScales[i]) flagQuads[i].localScale = flagBaseScales[i];
            if (!SetUpFlagMeshes(bt)) return;
            float sz = bt.TransformVector(Vector3.forward).magnitude;
            if (sz <= 0f) return;
            bool hull = Hull;
            float sAx = hull ? sz / Mathf.Max(1e-6f, bt.TransformVector(Vector3.right).magnitude) : 0f;
            for (int i = 0; i < flagFilters.Length; i++)
            {
                if (flagFilters[i] == null || flagCentres[i] == null) continue;
                // round or squashed envelope: squeeze each flag around the axis by the envelope's world scale
                // along its surface there against its scale along the axis. Blimp hull: turn it a quarter turn
                // so it stays upright along the hull's side, shortened along the axis by the hull's stretch.
                Vector3 key = hull ? new Vector3(1f, sAx, 0f)
                    : new Vector3(0f, SqueezeAt(bt, flagCentres[i][0], sz), SqueezeAt(bt, flagCentres[i][1], sz));
                if ((key - flagShape[i]).sqrMagnitude < 1e-6f) continue;
                flagShape[i] = key;
                ReshapeFlag(i, key);
            }
        }

        static float SqueezeAt(Transform bt, float c, float sz)
        {
            float st = bt.TransformVector(new Vector3(Mathf.Cos(c), -Mathf.Sin(c), 0f)).magnitude;
            return st > 0f ? Mathf.Clamp(sz / st, 0.05f, 2f) : 1f;
        }

        bool SetUpFlagMeshes(Transform bt)
        {
            if (flagFilters != null) return flagFilters.Length > 0;
            var filters = new List<MeshFilter>();
            foreach (Transform q in flagQuads)
                if (q != null) filters.AddRange(q.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null));
            flagFilters = filters.ToArray();
            int n = flagFilters.Length;
            flagCopies = new Mesh[n];
            flagVerts = new Vector3[n][];
            flagToEnv = new Matrix4x4[n];
            flagCentres = new float[n][];
            flagHeights = new float[n][];
            flagShape = Enumerable.Repeat(new Vector3(-1f, -1f, -1f), n).ToArray();      // shaped on first use (set off the fabric)
            for (int i = 0; i < n; i++)
            {
                MeshFilter mf = flagFilters[i];
                // the flag's place in the envelope's frame, from the modelled (unscaled) transforms
                Matrix4x4 m;
                if (!ModelledChain(mf.transform, bt, out m)) { flagFilters[i] = null; continue; }
                flagToEnv[i] = m;
                // shaped from the part prefab's mesh: a part copied in the VAB starts with this one's reshaped copy,
                // and shaping that again turned the flag a second quarter turn (sideways on a blimp)
                Transform twin = PrefabTwin(mf.transform);
                MeshFilter twf = twin != null ? twin.GetComponent<MeshFilter>() : null;
                Mesh src = twf != null && twf.sharedMesh != null ? twf.sharedMesh : mf.sharedMesh;
                Mesh orig;
                while (meshSources.TryGetValue(src, out orig) && orig != null) src = orig;
                // finer than modelled, so the flag bends with the fabric rather than cutting across it
                Mesh copy = SubdividedCopy(src, flagSubdivisions);
                copy.name = src.name + "_RB";
                meshSources[copy] = src;
                Vector3[] v = copy.vertices;
                flagVerts[i] = v.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                float[] c = new float[2], h = new float[2];
                for (int side = 0; side < 2; side++)
                {
                    float sx = 0f, sy = 0f, zs = 0f;
                    int cnt = 0;
                    foreach (Vector3 p in flagVerts[i])
                        if ((p.y >= 0f) == (side == 0)) { float a = Mathf.Atan2(p.x, p.y); sx += Mathf.Sin(a); sy += Mathf.Cos(a); zs += p.z; cnt++; }
                    c[side] = (sx != 0f || sy != 0f) ? Mathf.Atan2(sx, sy) : (side == 0 ? 0f : Mathf.PI);
                    h[side] = cnt > 0 ? zs / cnt : 0f;
                }
                flagCentres[i] = c;
                flagHeights[i] = h;
                mf.sharedMesh = copy;
                flagCopies[i] = copy;
            }
            return n > 0;
        }

        // Where the flag sits: this share of the envelope's radius off the fabric, on the fabric's exact rings, in a
        // mesh with each modelled triangle split in flagSubdivisions^2. As modelled (5 x 5 vertices a side, 1% off)
        // its flat facets cut the corners between vertices and came within 0.06% of the fabric on a blimp, where
        // the envelope showed through in flecks; lifting it further looked as if it floated.
        const float flagOffset = 0.0035f;
        const int flagSubdivisions = 4;

        // Far away that's a fraction of a pixel, and the fabric won the depth test on some frames (it flickered):
        // with temporal anti-aliasing (Scatterer's) the fabric is drawn shifted by up to half a pixel each frame
        // and the flag, a transparent thing, isn't, and further out the depth buffer runs short. So the flag is
        // lifted to at least this many pixels off the fabric, which can't be seen; close up it's flagOffset.
        const float flagLiftPixels = 1.25f, flagLiftMax = 0.1f;
        float flagLift = flagOffset;           // off the fabric now, as a share of the envelope's radius

        void KeepFlagOffFabric()
        {
            if (flagFilters == null || flagShape == null || ringR == null || ringRMax <= 0f || balloonObject == null || !balloonObject.activeInHierarchy) return;
            Camera cam = null;
            if (HighLogic.LoadedSceneIsFlight && FlightCamera.fetch != null) cam = FlightCamera.fetch.mainCamera;
            else if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null) cam = EditorLogic.fetch.editorCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null || cam.pixelHeight <= 0) return;
            Transform bt = balloonObject.transform;
            float radial = Mathf.Min(bt.TransformVector(Vector3.right).magnitude, bt.TransformVector(Vector3.up).magnitude);
            if (radial <= 0f) return;
            float pixel = 2f * Vector3.Distance(cam.transform.position, bt.position) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / cam.pixelHeight;
            float want = Mathf.Clamp(flagLiftPixels * pixel / (radial * ringRMax), flagOffset, flagLiftMax);
            // only once it's moved on by a fifth (each change reshapes the flag), and back to flagOffset close up
            bool home = want <= flagOffset && flagLift > flagOffset;
            if (!home && want <= flagLift * 1.2f && want >= flagLift / 1.2f) return;
            flagLift = want;
            for (int i = 0; i < flagFilters.Length; i++)
                if (flagFilters[i] != null && flagShape[i].x >= 0f) ReshapeFlag(i, flagShape[i]);
        }

        // A copy of a mesh with each triangle split into n x n (vertices interpolated; edges shared in position).
        static Mesh SubdividedCopy(Mesh src, int n)
        {
            Vector3[] v = src.vertices, nr = src.normals; Vector2[] uv = src.uv, uv2 = src.uv2;
            Vector4[] tg = src.tangents; Color[] col = src.colors;
            int cnt = v.Length;
            bool hasN = nr.Length == cnt, hasUV = uv.Length == cnt, hasUV2 = uv2.Length == cnt, hasT = tg.Length == cnt, hasC = col.Length == cnt;
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pu = new List<Vector2>(); var pu2 = new List<Vector2>();
            var pt = new List<Vector4>(); var pc = new List<Color>();
            var subs = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                int[] t = src.GetTriangles(s);
                var outT = new List<int>();
                for (int k = 0; k + 2 < t.Length; k += 3)
                {
                    int a = t[k], b = t[k + 1], cc = t[k + 2], start = pv.Count;
                    var index = new Dictionary<int, int>();
                    for (int i = 0; i <= n; i++)
                        for (int j = 0; j <= n - i; j++)
                        {
                            float wa = (float)i / n, wb = (float)j / n, wc = 1f - wa - wb;
                            index[i * (n + 1) + j] = pv.Count;
                            pv.Add(v[a] * wa + v[b] * wb + v[cc] * wc);
                            if (hasN) pn.Add((nr[a] * wa + nr[b] * wb + nr[cc] * wc).normalized);
                            if (hasUV) pu.Add(uv[a] * wa + uv[b] * wb + uv[cc] * wc);
                            if (hasUV2) pu2.Add(uv2[a] * wa + uv2[b] * wb + uv2[cc] * wc);
                            if (hasT) { Vector4 q = tg[a] * wa + tg[b] * wb + tg[cc] * wc; Vector3 d = new Vector3(q.x, q.y, q.z).normalized; pt.Add(new Vector4(d.x, d.y, d.z, tg[a].w)); }
                            if (hasC) pc.Add(col[a] * wa + col[b] * wb + col[cc] * wc);
                        }
                    // same winding as the modelled triangle
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < n - i; j++)
                        {
                            outT.Add(index[i * (n + 1) + j]); outT.Add(index[(i + 1) * (n + 1) + j]); outT.Add(index[i * (n + 1) + j + 1]);
                            if (i + j < n - 1) { outT.Add(index[(i + 1) * (n + 1) + j]); outT.Add(index[(i + 1) * (n + 1) + j + 1]); outT.Add(index[i * (n + 1) + j + 1]); }
                        }
                }
                subs.Add(outT.ToArray());
            }
            Mesh m = Instantiate(src);
            m.Clear();
            m.SetVertices(pv);
            if (hasN) m.SetNormals(pn);
            if (hasT) m.SetTangents(pt);
            if (hasC) m.SetColors(pc);
            if (hasUV) m.SetUVs(0, pu);
            if (hasUV2) m.SetUVs(1, pu2);
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
            m.RecalculateBounds();
            return m;
        }

        void ReshapeFlag(int i, Vector3 key)
        {
            if (flagFilters[i] == null || flagCopies[i] == null) return;
            bool hull = key.x > 0.5f;
            if (profileR == null) BuildProfile();
            float offset = flagLift * ringRMax;
            Matrix4x4 inv = flagToEnv[i].inverse;
            // normals as the fabric's under it: the model's, turned with the flag onto a blimp's side, kept the
            // tilt of the round envelope where the flag was, up to 29 degrees out; seen at a shallow angle they
            // faced away from the camera before the flag did, and the flag's shader drops those pixels
            Matrix4x4 toFlagNormal = flagToEnv[i].transpose;
            Vector3[] src = flagVerts[i];
            Vector3[] v = new Vector3[src.Length];
            Vector3[] nr = new Vector3[src.Length];
            float[] c = flagCentres[i], zc = flagHeights[i];
            for (int k = 0; k < src.Length; k++)
            {
                Vector3 p = src[k];
                int side = p.y >= 0f ? 0 : 1;
                float a = Mathf.Atan2(p.x, p.y);
                float d = Mathf.DeltaAngle(c[side] * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                Vector3 np;
                if (!hull)
                {
                    // move it around the axis only, at the same height, set off the fabric
                    float turn = ((side == 0 ? key.y : key.z) - 1f) * d;
                    float cs = Mathf.Cos(turn), sn = Mathf.Sin(turn);
                    np = new Vector3(p.x * cs + p.y * sn, -p.x * sn + p.y * cs, p.z);
                    float r0 = Mathf.Sqrt(np.x * np.x + np.y * np.y);
                    if (r0 > 1e-4f) { float up = (RingRadiusAt(np.z) + offset) / r0; np.x *= up; np.y *= up; }
                }
                else
                {
                    // quarter turn on the surface: its width runs along the hull's axis (shortened by the hull's
                    // stretch so it keeps its shape), its height around the hull towards the envelope's local X,
                    // which is up on the blimp. Same side of the fabric, set off it.
                    float sg = Mathf.Cos(c[side]) >= 0f ? 1f : -1f;
                    float rho = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                    float u = rho * d, h = p.z - zc[side];
                    float z2 = zc[side] - sg * u / Mathf.Max(1f, key.y);
                    float rho2 = Mathf.Max(0.01f, RingRadiusAt(z2) + offset);
                    float a2 = c[side] + sg * h / rho2;
                    np = new Vector3(rho2 * Mathf.Sin(a2), rho2 * Mathf.Cos(a2), z2);
                }
                v[k] = inv.MultiplyPoint3x4(np);
                nr[k] = toFlagNormal.MultiplyVector(FabricNormal(np)).normalized;
            }
            Mesh m = flagCopies[i];
            m.vertices = v;
            m.normals = nr;
            m.RecalculateBounds();
        }

        // The fabric's outward normal at a point in the envelope's frame (round about its axis, local Z): out from
        // the axis, leaning along it by the slope of the envelope's side there.
        Vector3 FabricNormal(Vector3 p)
        {
            if (profileR == null) BuildProfile();
            float h = Mathf.Max(1e-4f, profileDz);
            float slope = (RingRadiusAt(p.z + h) - RingRadiusAt(p.z - h)) / (2f * h);
            float rho = Mathf.Sqrt(p.x * p.x + p.y * p.y);
            Vector3 radial = rho > 1e-6f ? new Vector3(p.x / rho, p.y / rho, 0f) : Vector3.zero;
            return (radial - slope * Vector3.forward).normalized;
        }

        // Local transform chain from a model transform up to (not including) root, with the flag quads at
        // their modelled scale.
        bool ModelledChain(Transform from, Transform root, out Matrix4x4 m)
        {
            m = Matrix4x4.identity;
            Transform tr = from;
            while (tr != null && tr != root)
            {
                int qi = flagQuads != null ? System.Array.IndexOf(flagQuads, tr) : -1;
                Vector3 sc = qi >= 0 ? flagBaseScales[qi] : tr.localScale;
                m = Matrix4x4.TRS(tr.localPosition, tr.localRotation, sc) * m;
                tr = tr.parent;
            }
            return tr != null;
        }

        // The envelope's radius at each height along its axis, from its mesh (envelope frame, as modelled).
        float[] profileR, ringZ, ringR;
        float ringRMax;

        // The envelope's radius at a height, exactly: straight between its rings, as its meridians run.
        float RingRadiusAt(float z)
        {
            if (profileR == null) BuildProfile();
            int n = ringZ.Length;
            if (n == 0) return 0f;
            if (z <= ringZ[0]) return ringR[0];
            if (z >= ringZ[n - 1]) return ringR[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (ringZ[mid] <= z) lo = mid; else hi = mid; }
            return Mathf.Lerp(ringR[lo], ringR[hi], (z - ringZ[lo]) / Mathf.Max(1e-6f, ringZ[hi] - ringZ[lo]));
        }
        float profileZ0, profileDz, profileCentroid, profileVolume;

        float EnvelopeRadiusAt(float z)
        {
            if (profileR == null) BuildProfile();
            int n = profileR.Length;
            if (n < 2) return 0f;
            float x = (z - profileZ0) / profileDz;
            int i = Mathf.Clamp(Mathf.FloorToInt(x), 0, n - 2);
            return Mathf.Lerp(profileR[i], profileR[i + 1], Mathf.Clamp01(x - i));
        }

        void BuildProfile()
        {
            Transform bt = balloonObject.transform;
            var pts = new List<Vector2>();
            foreach (MeshFilter mf in balloonObject.GetComponentsInChildren<MeshFilter>(true))
            {
                Matrix4x4 m;
                if (mf == null || mf.sharedMesh == null || OnFlag(mf.transform) || !ModelledChain(mf.transform, bt, out m)) continue;
                foreach (Vector3 v in mf.sharedMesh.vertices)
                {
                    Vector3 p = m.MultiplyPoint3x4(v);
                    pts.Add(new Vector2(p.z, Mathf.Sqrt(p.x * p.x + p.y * p.y)));
                }
            }
            if (pts.Count == 0) { profileR = new float[0]; ringZ = ringR = new float[0]; return; }
            float z0 = pts.Min(q => q.x), z1 = pts.Max(q => q.x);
            // the rings exactly (each height's largest radius), for the flag to sit at a set height off the fabric
            var rz = new List<float>(); var rr = new List<float>();
            float eps = 1e-4f * Mathf.Max(1e-3f, z1 - z0);
            foreach (Vector2 q in pts.OrderBy(q => q.x))
            {
                if (rz.Count > 0 && q.x - rz[rz.Count - 1] < eps) { rr[rr.Count - 1] = Mathf.Max(rr[rr.Count - 1], q.y); continue; }
                rz.Add(q.x); rr.Add(q.y);
            }
            ringZ = rz.ToArray(); ringR = rr.ToArray(); ringRMax = ringR.Length > 0 ? ringR.Max() : 0f;
            const int N = 64;
            profileZ0 = z0;
            profileDz = Mathf.Max(1e-4f, (z1 - z0) / (N - 1));
            float[] r = Enumerable.Repeat(-1f, N).ToArray();
            foreach (Vector2 q in pts)
            {
                int i = Mathf.Clamp(Mathf.RoundToInt((q.x - z0) / profileDz), 0, N - 1);
                if (q.y > r[i]) r[i] = q.y;
            }
            // fill any empty bins from their neighbours
            for (int i = 0; i < N; i++)
            {
                if (r[i] >= 0f) continue;
                int lo = i - 1, hi = i + 1;
                while (lo >= 0 && r[lo] < 0f) lo--;
                while (hi < N && r[hi] < 0f) hi++;
                if (lo < 0 && hi >= N) r[i] = 0f;
                else if (lo < 0) r[i] = r[hi];
                else if (hi >= N) r[i] = r[lo];
                else r[i] = Mathf.Lerp(r[lo], r[hi], (float)(i - lo) / (hi - lo));
            }
            profileR = r;
            double num = 0, den = 0;
            for (int i = 0; i < N; i++) { double a = (double)r[i] * r[i]; num += a * (z0 + i * profileDz); den += a; }
            profileCentroid = den > 0 ? (float)(num / den) : 0.5f * (z0 + z1);
            profileVolume = (float)(System.Math.PI * den * profileDz);          // local units^3
        }

        // Hide only the rope's own meshes (in case the envelope is parented under the rope).
        void HideRope()
        {
            if (ropeObject == null) return;
            if (ropeRenderers == null)
                ropeRenderers = ropeObject.GetComponentsInChildren<Renderer>(true)
                    .Where(r => !r.transform.IsChildOf(balloonObject.transform)).ToArray();
            foreach (Renderer r in ropeRenderers)
                if (r != null && r.enabled) r.enabled = false;
        }

        // Local scale for the envelope: the size of the gas it holds (GasWorldScale), reshaped for the
        // cushion or the blimp. In flight its parent is the evenly scaled frame; the per-axis correction
        // below only matters if that couldn't be set up (it's exact only for turns by right angles).
        Vector3 EnvelopeScale(float progress, float stretchX, float squash = 1f, float stretchZ = 1f)
        {
            float k = Mathf.Max(0.01f, progress);            // only if there's no gas model
            float kGas = GasWorldScale(progress);            // world scale: the gas it really holds
            // Every reshaping keeps the envelope's volume (it holds the same gas):
            //  squash   = height / width along its axis (local Z): the cushion
            //  stretchZ = longer along its axis and slimmer across it: the blimp hull
            //  stretchX = longer along local X and slimmer across it: a stretched cushion
            float sx = Mathf.Max(1f, stretchX), sz = Mathf.Max(1f, stretchZ);
            float x = k, y = k, z = k;
            if (squash > 0f && squash < 0.999f) { float wq = Mathf.Pow(squash, -1f / 3f); x *= wq; y *= wq; z *= Mathf.Pow(squash, 2f / 3f); }
            if (sz > 1.001f) { float thin = Mathf.Pow(sz, -1f / 3f); x *= thin; y *= thin; z *= Mathf.Pow(sz, 2f / 3f); }
            if (sx > 1.001f) { float thin = Mathf.Pow(sx, -1f / 3f); y *= thin; z *= thin; x *= Mathf.Pow(sx, 2f / 3f); }
            Transform bt = balloonObject.transform;
            Transform parent = bt.parent;
            Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
            ps = new Vector3(Mathf.Abs(ps.x), Mathf.Abs(ps.y), Mathf.Abs(ps.z));
            float w = Mathf.Min(ps.x, ps.z);
            if (kGas > 0f && w > 0f) { float f = kGas / (w * k); x *= f; y *= f; z *= f; }
            if (parent == null || w <= 0f || ps.y <= 0f) return new Vector3(x, y, z);
            Quaternion rel = Quaternion.Inverse(parent.rotation) * bt.rotation;
            return new Vector3(x * AxisFactor(rel * Vector3.right, ps, w),
                               y * AxisFactor(rel * Vector3.up, ps, w),
                               z * AxisFactor(rel * Vector3.forward, ps, w));
        }

        static float AxisFactor(Vector3 dirInParent, Vector3 parentScale, float width)
        {
            float x = Mathf.Abs(dirInParent.x), y = Mathf.Abs(dirInParent.y), z = Mathf.Abs(dirInParent.z);
            float along = (x >= y && x >= z) ? parentScale.x : (y >= z ? parentScale.y : parentScale.z);
            return along > 0f ? width / along : 1f;
        }

        // Lowest point of the envelope meshes along dir, relative to origin (works for every size).
        float LowestPointAlong(Vector3 dir, Vector3 origin)
        {
            if (balloonMeshes == null) balloonMeshes = balloonObject.GetComponentsInChildren<MeshFilter>(true).Where(m => !OnFlag(m.transform)).ToArray();
            float min = 0f;
            bool any = false;
            foreach (MeshFilter mf in balloonMeshes)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds b = mf.sharedMesh.bounds;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                            (i & 2) == 0 ? b.min.y : b.max.y,
                                            (i & 4) == 0 ? b.min.z : b.max.z);
                    float d = Vector3.Dot(m.MultiplyPoint3x4(c) - origin, dir);
                    if (!any || d < min) { min = d; any = true; }
                }
            }
            return any ? min : 0f;
        }

        // Stretch the packed balloon's cap the same way, so the VAB shows which way it will inflate.
        void UpdateMountPreview()
        {
            previewRigid = rigidMount;
            previewStretch = blimpStretch;
            previewBelow = envelopeBelow;
            previewCw = counterweightPercent;
            previewLift = maxLift;
            BaseField below = Fields["envelopeBelow"];
            if (below != null) below.guiActiveEditor = rigidMount;
            BaseField blimp = Fields["blimpStretch"];
            if (blimp != null) blimp.guiActiveEditor = rigidMount && !envelopeBelow;
            BaseField bn = Fields["ballonets"];
            if (bn != null) bn.guiActiveEditor = Hull;
            BaseField cw = Fields["counterweightPercent"], kl = Fields["keelLength"];
            if (cw != null) cw.guiActiveEditor = Cushion;
            if (kl != null) kl.guiActiveEditor = Cushion;
            BaseField ct2 = Fields["counterweightText"];
            if (ct2 != null) ct2.guiActiveEditor = Cushion;
            counterweightText = CounterweightText();
            if (capObject == null) return;
            ShapeCap();
        }

        // ---- Packed cap ----
        // Blimp: the cap is reshaped (in its own mesh) into a small airship outline seen from above, the
        // round nose forward along HullAxis (the part's front) and tapering to the tail, the same for any
        // blimp length. Otherwise the cap as modelled.
        // The shapes are built from the part prefab's meshes, so copied parts (symmetry) don't compound.
        const float capNose = 1.15f, capTail = 1.7f, capTaper = 0.5f;
        static readonly Dictionary<Mesh, Mesh> meshSources = new Dictionary<Mesh, Mesh>();
        MeshFilter[] capFilters;
        Mesh[] capCopies;
        Vector3[][] capV, capN;
        Matrix4x4[] capM;
        float capRadius;
        int capMode = -1;
        Vector3 capAxis;

        void ShapeCap()
        {
            Transform ct = capObject.transform;
            if (!capBaseSet) { Transform twin = PrefabTwin(ct); capBaseScale = twin != null ? twin.localScale : ct.localScale; capBaseSet = true; }
            if (capFilters != null)
                for (int i = 0; i < capFilters.Length; i++)
                    if (capFilters[i] != null && capFilters[i].sharedMesh != capCopies[i]) { capFilters = null; capMode = -1; break; }
            if (ct.localScale != capBaseScale) ct.localScale = capBaseScale;
            int mode = Hull ? 1 : 0;
            Vector3 a = ct.InverseTransformDirection(HullAxis());
            a.z = 0f;                                   // the cap lies in its local X-Y plane, domed along Z
            a = a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.right;
            if (mode == capMode && (mode == 0 || (a - capAxis).sqrMagnitude < 1e-6f)) return;
            if (mode == 0 && capFilters == null) { capMode = 0; return; }      // never reshaped: as modelled
            if (!SetUpCapMeshes(ct)) return;
            capMode = mode;
            capAxis = a;
            Vector3 b = new Vector3(-a.y, a.x, 0f);
            for (int i = 0; i < capFilters.Length; i++)
            {
                if (capFilters[i] == null) continue;
                Matrix4x4 inv = capM[i].inverse;
                Vector3[] src = capV[i], nsrc = capN[i];
                Vector3[] v = new Vector3[src.Length];
                Vector3[] nr = nsrc != null ? new Vector3[src.Length] : null;
                for (int k = 0; k < src.Length; k++)
                {
                    Vector3 p = src[k];
                    float s = p.x * a.x + p.y * a.y, w = p.x * b.x + p.y * b.y;
                    float js = 1f, jw = 1f;
                    if (mode == 1)
                    {
                        if (s >= 0f) js = capNose;
                        else { js = capTail; jw = 1f - capTaper * Mathf.Clamp01(-s / capRadius); }
                    }
                    Vector3 q = a * (s * js) + b * (w * jw);
                    v[k] = inv.MultiplyPoint3x4(new Vector3(q.x, q.y, p.z));
                    if (nr != null)
                    {
                        Vector3 n = nsrc[k];
                        Vector3 m = a * ((n.x * a.x + n.y * a.y) / js) + b * ((n.x * b.x + n.y * b.y) / jw);
                        nr[k] = inv.MultiplyVector(new Vector3(m.x, m.y, n.z)).normalized;
                    }
                }
                Mesh mesh = capCopies[i];
                mesh.vertices = v;
                if (nr != null) mesh.normals = nr;
                mesh.RecalculateBounds();
            }
        }

        bool SetUpCapMeshes(Transform ct)
        {
            if (capFilters != null) return capFilters.Length > 0;
            capFilters = ct.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null).ToArray();
            int n = capFilters.Length;
            capCopies = new Mesh[n];
            capV = new Vector3[n][];
            capN = new Vector3[n][];
            capM = new Matrix4x4[n];
            capRadius = 0f;
            for (int i = 0; i < n; i++)
            {
                MeshFilter mf = capFilters[i];
                Matrix4x4 m;
                if (!ModelledChain(mf.transform, ct, out m)) { capFilters[i] = null; continue; }
                // the modelled mesh: from the prefab, or what a copied part's mesh was made from
                Transform tw = PrefabTwin(mf.transform);
                MeshFilter twf = tw != null ? tw.GetComponent<MeshFilter>() : null;
                Mesh src = twf != null && twf.sharedMesh != null ? twf.sharedMesh : mf.sharedMesh;
                Mesh orig;
                while (meshSources.TryGetValue(src, out orig) && orig != null) src = orig;
                capM[i] = m;
                Vector3[] v = src.vertices, nr = src.normals;
                capV[i] = v.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                capN[i] = nr != null && nr.Length == v.Length ? nr.Select(x => m.MultiplyVector(x)).ToArray() : null;
                if (mf.transform == ct)
                    foreach (Vector3 p in capV[i]) capRadius = Mathf.Max(capRadius, Mathf.Sqrt(p.x * p.x + p.y * p.y));
                Mesh copy = Instantiate(src);
                copy.name = src.name + "_RB";
                meshSources[copy] = src;
                mf.sharedMesh = copy;
                capCopies[i] = copy;
            }
            if (capRadius <= 0f) capRadius = 0.5f;
            if (meshSources.Count > 512)
                foreach (Mesh k in meshSources.Keys.Where(k => k == null).ToList()) meshSources.Remove(k);
            return n > 0;
        }

        // The same transform in the part prefab: by name down the hierarchy (by position where names
        // repeat), skipping the evenly scaled frame. Positions alone won't do: moving the envelope and
        // rope into that frame in flight shifts their siblings, such as the cap.
        Transform PrefabTwin(Transform t)
        {
            Part pf = part.partInfo != null ? part.partInfo.partPrefab : null;
            if (pf == null || pf == part) return null;
            var names = new List<string>();
            var index = new List<int>();
            Transform x = t;
            while (x != null && x != part.transform)
            {
                if (x != evenFrame) { names.Add(x.name); index.Add(x.GetSiblingIndex()); }
                x = x.parent;
            }
            if (x == null) return null;
            Transform y = pf.transform;
            for (int i = names.Count - 1; i >= 0; i--)
            {
                Transform next = index[i] < y.childCount && y.GetChild(index[i]).name == names[i] ? y.GetChild(index[i]) : null;
                for (int c = 0; next == null && c < y.childCount; c++)
                    if (y.GetChild(c).name == names[i]) next = y.GetChild(c);
                if (next == null) return null;
                y = next;
            }
            return y;
        }

    }
}
