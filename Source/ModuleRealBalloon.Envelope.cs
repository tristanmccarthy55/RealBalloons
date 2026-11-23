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

        // Where the buoyancy acts: the envelope's centre on the cushion and the blimp, the model's lift point otherwise.
        Vector3 LiftPosition()
        {
            if (Hull) return EnvelopeCentre() + HullAxis() * BallonetTrim();
            return Cushion ? EnvelopeCentre() : liftPointObject.transform.position;
        }

        // Blimp ballonet trim. A real blimp trims its pitch by moving air between ballonets fore and aft,
        // which shifts its centre of buoyancy. Here the lift point slides along the hull's axis to sit over
        // the craft's centre of mass (measured in the craft's own frame, so it doesn't chase the sway), up
        // to ballonetRange of the hull's length either way. Sideways offsets aren't trimmed (real blimps
        // can't either). Returns the shift (m, + towards the nose).
        [KSPField] public float ballonetRange = 0.15f;
        [System.NonSerialized] public double trimShift, trimLimit;

        // The blimp's nose: the part's +Z, so placed unrotated in the VAB it points forward and the tail back.
        // Rotate the part in the VAB to aim it.
        Vector3 HullAxis() { return part.transform.forward; }

        float BallonetTrim()
        {
            trimShift = trimLimit = 0;
            if (!Hull || vessel == null || part.rb == null || balloonObject == null) return 0f;
            Vector3 axis = HullAxis(), c = EnvelopeCentre();
            float length = -LowestPointAlong(-axis, c) - LowestPointAlong(axis, c);
            float lim = Mathf.Max(0f, ballonetRange) * length;
            float shift = Mathf.Clamp(Vector3.Dot(TrueCoM() - c, axis), -lim, lim);
            trimShift = shift;
            trimLimit = lim;
            return shift;
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
                   " km, empty " + (se == 0 ? (emptyAlt / 1000).ToString("F0") + " km" : "too light");
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

        void StartHullPreview()
        {
            Transform bt = balloonObject.transform, twin = PrefabTwin(bt);     // as packed (a copied part may be mid-preview)
            packedPos = twin != null ? twin.localPosition : bt.localPosition;
            packedRot = twin != null ? twin.localRotation : bt.localRotation;
            packedScale = twin != null ? twin.localScale : bt.localScale;
            initialBalloonPos = packedPos;                                     // EnvelopeHome (no even frame in the VAB)
            previewColliders = balloonObject.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToList();
            foreach (Collider c in previewColliders) c.enabled = false;
            capObject.SetActive(false);
            previewShown = true;
            ApplyFixedMount(1f);                                               // builds the fins
            if (part.partRendererBoundsIgnore == null) part.partRendererBoundsIgnore = new List<string>();
            foreach (Renderer r in balloonObject.GetComponentsInChildren<Renderer>(true))
                if (!part.partRendererBoundsIgnore.Contains(r.name)) part.partRendererBoundsIgnore.Add(r.name);
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
            capObject.SetActive(true);
            previewShown = false;
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
            bt.localScale = hull ? EnvelopeScale(progress, 1f, 1f, blimpStretch) : EnvelopeScale(progress, 1f, CushionShape);
            KeepFlagProportions();
            Vector3 basePos = EnvelopeHome();
            bt.position = basePos;
            bt.position = basePos - pt.up * LowestPointAlong(pt.up, basePos);   // near end of the envelope on the base
            if (hull)
            {
                // the hull's centre of buoyancy over the part, whatever its size
                Vector3 axis = HullAxis();
                bt.position -= axis * Vector3.Dot(EnvelopeCentre() - basePos, axis);
            }
            ShowFins(hull);
            if (Cushion)
            {
                keelProgress = Mathf.Clamp01(progress);
                UpdateKeelVisual();
            }
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
            EvenEnvelopeUVs();      // at load on the part prefab, so the parts list icon has them too
            MigrateLegacyColour(node);
        }

        // ---- Envelope UVs ----
        // The model maps its fabric texture straight down the envelope's axis: a vertex's distance from the
        // texture's centre is its radius. The steep middle third, where the radius hardly changes, got only
        // about 5 texels from end to end, so anything drawn there (TU's band edges) smeared along the
        // envelope, most of all on a stretched blimp. Here each vertex's distance from the centre becomes its
        // distance along the envelope from the nearer end (tail or top), as a share of that end's way to the
        // widest part: texels run evenly from each end to the widest part. The angle (gores, seams) and the
        // range are kept, so the same texture fits. Done once, on the model's own mesh, which every copy of
        // the part shares. Source/Tools/make_masks.py (even_uvs) mirrors it to draw the TU masks.
        const string evenUVsTag = " (RB even UVs)";
        const int evenUVBins = 180;

        void EvenEnvelopeUVs()
        {
            GameObject env = balloonObject != null ? balloonObject : FindChild(envelopeTransform);
            MeshFilter mf = env != null ? env.GetComponent<MeshFilter>() : null;
            Mesh mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable || mesh.name.EndsWith(evenUVsTag)) return;
            Vector2[] uv = EvenUVs(mesh.vertices, mesh.uv);
            if (uv == null) return;
            mesh.uv = uv;
            mesh.name += evenUVsTag;
        }

        // The respaced UVs for an envelope mesh (its axis local Z), or null if it can't be done.
        static Vector2[] EvenUVs(Vector3[] v, Vector2[] uvIn)
        {
            int n = v.Length;
            if (n == 0 || uvIn.Length != n) return null;
            Vector2[] uv = (Vector2[])uvIn.Clone();

            // the profile: radius and height against the angle about the widest ring's centre
            float[] r = new float[n], phi = new float[n];
            float rmax = 0f;
            for (int i = 0; i < n; i++) { r[i] = Mathf.Sqrt(v[i].x * v[i].x + v[i].y * v[i].y); rmax = Mathf.Max(rmax, r[i]); }
            double zsum = 0; int zcount = 0;
            for (int i = 0; i < n; i++) if (r[i] > rmax - 1e-3f) { zsum += v[i].z; zcount++; }
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
            if (angles.Count < 2) return null;
            float sEq = Interp(0f, angles, dist), sEnd = acc;
            if (sEq <= 0f || sEnd - sEq <= 0f) return null;

            Vector2 lo = uv[0], hi = uv[0];
            for (int i = 1; i < n; i++) { lo = Vector2.Min(lo, uv[i]); hi = Vector2.Max(hi, uv[i]); }
            Vector2 c = (lo + hi) / 2f;
            float Rmax = 0f;
            for (int i = 0; i < n; i++) Rmax = Mathf.Max(Rmax, (uv[i] - c).magnitude);
            for (int i = 0; i < n; i++)
            {
                float s = Interp(phi[i], angles, dist);
                float frac = Mathf.Clamp01(phi[i] <= 0f ? s / sEq : (sEnd - s) / (sEnd - sEq));
                Vector2 d = uv[i] - c;
                float R = d.magnitude;
                if (R > 1e-6f) uv[i] = c + d * (Rmax * frac / R);
            }
            return uv;
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
        // Four tail fins in a + (top, bottom, both sides) on the tapered end of the hull: a look only, no
        // forces. Built once as a mesh in the envelope's own frame and parented to it, so they grow with
        // the gas and stretch with the hull. They use the envelope's material, sampled at its fabric.
        // Fin outline in the envelope's frame (local Z along the axis from the tail point, radius from it):
        const float finTrailZ = 0.3f, finRootLeadZ = 1.25f, finTipLeadZ = 0.66f;   // model units
        const float finTipRadius = 2.0f;            // about the hull's widest radius (1.98)
        const float finRootThick = 0.06f, finTipThick = 0.035f, finEmbed = 0.05f;
        // A texel of the envelope texture with the fabric's average brightness, mid-gore: off the seams
        // (load tapes) and inside the first gore, which is the main colour in every TU pattern.
        [KSPField] public Vector2 finTexel = new Vector2(0.4071f, 0.8151f);
        GameObject fins;
        MeshRenderer finRenderer;
        Renderer envelopeRenderer;

        void ShowFins(bool show)
        {
            if (show && fins == null) BuildFins();
            if (fins == null) return;
            if (fins.activeSelf != show) fins.SetActive(show);
            // whatever material the envelope has now (TexturesUnlimited gives it its own)
            if (show && envelopeRenderer != null && finRenderer.sharedMaterial != envelopeRenderer.sharedMaterial)
                finRenderer.sharedMaterial = envelopeRenderer.sharedMaterial;
        }

        void BuildFins()
        {
            MeshFilter envMf = balloonObject.GetComponent<MeshFilter>();
            Renderer envR = balloonObject.GetComponent<Renderer>();
            if (envMf == null || envMf.sharedMesh == null || envR == null) return;
            Vector2 uv = finTexel;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI / 2f;                         // +X (up on a blimp), +Y, -X, -Y
                Vector3 u = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f), w = new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0f);
                // outline: root leading edge, root trailing edge, tip trailing edge, tip leading edge
                Vector3[] o = {
                    u * (EnvelopeRadiusAt(finRootLeadZ) - finEmbed) + Vector3.forward * finRootLeadZ,
                    u * (EnvelopeRadiusAt(finTrailZ) - finEmbed) + Vector3.forward * finTrailZ,
                    u * finTipRadius + Vector3.forward * finTrailZ,
                    u * finTipRadius + Vector3.forward * finTipLeadZ };
                float[] th = { finRootThick, finRootThick, finTipThick, finTipThick };
                Vector3[] p = new Vector3[8];
                for (int i = 0; i < 4; i++) { p[i] = o[i] + w * (th[i] / 2f); p[i + 4] = o[i] - w * (th[i] / 2f); }
                AddQuad(verts, tris, p[0], p[1], p[2], p[3], w);                    // the two faces
                AddQuad(verts, tris, p[4], p[5], p[6], p[7], -w);
                for (int i = 0; i < 4; i++)                                           // the edges
                {
                    int j = (i + 1) % 4;
                    Vector3 mid = (o[i] + o[j]) / 2f, centre = (o[0] + o[1] + o[2] + o[3]) / 4f;
                    AddQuad(verts, tris, p[i], p[j], p[j + 4], p[i + 4], mid - centre);
                }
            }
            Mesh mesh = new Mesh { name = "RBFins" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, Enumerable.Repeat(uv, verts.Count).ToList());
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            fins = new GameObject("RBFins");
            fins.layer = balloonObject.layer;
            fins.transform.SetParent(balloonObject.transform, false);
            fins.AddComponent<MeshFilter>().sharedMesh = mesh;
            envelopeRenderer = envR;
            finRenderer = fins.AddComponent<MeshRenderer>();
            finRenderer.sharedMaterial = envR.sharedMaterial;
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
        Vector3[][] flagVerts, flagNorms;      // in the envelope's frame, as modelled
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
            flagNorms = new Vector3[n][];
            flagToEnv = new Matrix4x4[n];
            flagCentres = new float[n][];
            flagHeights = new float[n][];
            flagShape = Enumerable.Repeat(new Vector3(0f, 1f, 1f), n).ToArray();
            for (int i = 0; i < n; i++)
            {
                MeshFilter mf = flagFilters[i];
                // the flag's place in the envelope's frame, from the modelled (unscaled) transforms
                Matrix4x4 m;
                if (!ModelledChain(mf.transform, bt, out m)) { flagFilters[i] = null; continue; }
                flagToEnv[i] = m;
                Mesh src = mf.sharedMesh;
                Vector3[] v = src.vertices, nr = src.normals;
                flagVerts[i] = v.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                flagNorms[i] = nr != null && nr.Length == v.Length ? nr.Select(x => m.MultiplyVector(x)).ToArray() : null;
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
                Mesh copy = Instantiate(src);
                copy.name = src.name + "_RB";
                mf.sharedMesh = copy;
                flagCopies[i] = copy;
            }
            return n > 0;
        }

        void ReshapeFlag(int i, Vector3 key)
        {
            if (flagFilters[i] == null || flagCopies[i] == null) return;
            bool hull = key.x > 0.5f;
            Matrix4x4 inv = flagToEnv[i].inverse;
            Vector3[] src = flagVerts[i], nsrc = flagNorms[i];
            Vector3[] v = new Vector3[src.Length];
            Vector3[] nr = nsrc != null ? new Vector3[src.Length] : null;
            float[] c = flagCentres[i], zc = flagHeights[i];
            for (int k = 0; k < src.Length; k++)
            {
                Vector3 p = src[k];
                int side = p.y >= 0f ? 0 : 1;
                float a = Mathf.Atan2(p.x, p.y);
                float d = Mathf.DeltaAngle(c[side] * Mathf.Rad2Deg, a * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                Vector3 q = nsrc != null ? nsrc[k] : Vector3.zero;
                Vector3 np, nn;
                if (!hull)
                {
                    // move it around the axis only: same height, same distance from the axis
                    float turn = ((side == 0 ? key.y : key.z) - 1f) * d;
                    float cs = Mathf.Cos(turn), sn = Mathf.Sin(turn);
                    np = new Vector3(p.x * cs + p.y * sn, -p.x * sn + p.y * cs, p.z);
                    nn = new Vector3(q.x * cs + q.y * sn, -q.x * sn + q.y * cs, q.z);
                }
                else
                {
                    // quarter turn on the surface: its width runs along the hull's axis (shortened by the hull's
                    // stretch so it keeps its shape), its height around the hull towards the envelope's local X,
                    // which is up on the blimp. Same side of the fabric, same distance off it.
                    float sg = Mathf.Cos(c[side]) >= 0f ? 1f : -1f;
                    float rho = Mathf.Sqrt(p.x * p.x + p.y * p.y);
                    float u = rho * d, h = p.z - zc[side];
                    float off = rho - EnvelopeRadiusAt(p.z);
                    float z2 = zc[side] - sg * u / Mathf.Max(1f, key.y);
                    float rho2 = Mathf.Max(0.01f, EnvelopeRadiusAt(z2) + off);
                    float a2 = c[side] + sg * h / rho2;
                    np = new Vector3(rho2 * Mathf.Sin(a2), rho2 * Mathf.Cos(a2), z2);
                    Vector3 rad = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f), tan = new Vector3(Mathf.Cos(a), -Mathf.Sin(a), 0f);
                    Vector3 rad2 = new Vector3(Mathf.Sin(a2), Mathf.Cos(a2), 0f), tan2 = new Vector3(Mathf.Cos(a2), -Mathf.Sin(a2), 0f);
                    nn = Vector3.Dot(q, rad) * rad2 - sg * Vector3.Dot(q, tan) * Vector3.forward + sg * q.z * tan2;
                }
                v[k] = inv.MultiplyPoint3x4(np);
                if (nr != null) nr[k] = inv.MultiplyVector(nn).normalized;
            }
            Mesh m = flagCopies[i];
            m.vertices = v;
            if (nr != null) m.normals = nr;
            m.RecalculateBounds();
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
        float[] profileR;
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
            if (pts.Count == 0) { profileR = new float[0]; return; }
            float z0 = pts.Min(q => q.x), z1 = pts.Max(q => q.x);
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
