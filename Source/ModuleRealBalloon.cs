// RealBalloons - realistic zero-pressure balloons, blimps and cushions for RO / RP-1.
// Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// A rewritten derivative of KerBalloons (originally by JoePatrick1, maintained by linuxgurugamer;
// KerBalloons Reinflated (c) 2018 Fengist): this module began as its ModuleKerBalloon and keeps its
// inflate / deflate / repack framework. The balloon model, texture and sounds are KerBalloons' (MIT).

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealBalloons
{
    // The balloon part: packed in its canister until inflated, then a zero-pressure balloon (see
    // RealBalloonPhysics) on a rope, fixed to the part, as a blimp hull, or as a cushion under the craft.
    public partial class ModuleRealBalloon : PartModule, IPartMassModifier
    {
        // ---- Model ----
        [KSPField] public string envelopeTransform = "Balloon";
        [KSPField] public string ropeTransform = "Rope";
        [KSPField] public string capTransform = "Cap";
        [KSPField] public string liftPointTransform = "liftPoint";
        [KSPField] public string ropeEndTransform = "balloonPoint";

        // ---- Per size (set by the size switcher) ----
        // Lift-off with hydrogen on the home world, balloon included (kN). Sets the size's gas fill.
        [KSPField(isPersistant = true)] public float maxLift = 0f;
        // Full volume of the standard envelope (m^3). Its mass comes from its film, size and load (Mass, below).
        [KSPField(isPersistant = true)] public float envelopeVolume = 0f;

        // ---- Per envelope (set by the envelope switcher) ----
        [KSPField(isPersistant = true)] public string liftGas = "Hydrogen";
        // Volume relative to the standard envelope (small for dense-air Venus envelopes).
        [KSPField(isPersistant = true)] public float volumeFactor = 1f;
        // The film or fabric alone (g/m^2): 18.7 for NASA's standard 0.8 mil polyethylene, 9.4 for the Big 60's
        // 0.4 mil, 240 for VEGA's Teflon-coated fabric. Sets the envelope's mass.
        [KSPField(isPersistant = true)] public float filmDensity = 18.7f;
        // Molar mass of the air the envelope is built for (kg/mol; 0 = the home world's): its tapes are rated
        // for the load its gas lifts there.
        [KSPField(isPersistant = true)] public float designAirMolarMass = 0f;
        // Hottest air the material survives (K), from the planet's standard temperature curve.
        [KSPField(isPersistant = true)] public float maxAirTemperature = 353f;
        // The warmest sulfuric-acid cloud it stands up to (K; Venus's clouds, below ~70 km: RealBalloonPhysics.AcidCloudTop).
        // At or above maxAirTemperature it's acid-proof (the Teflon fabric). Polyethylene: 293 (20 C): resistant to
        // concentrated acid when cold, unsatisfactory at 60 C. 0: none (cotton, which the acid chars).
        [KSPField(isPersistant = true)] public float acidMaxTemperature = 1000f;

        bool AcidProof { get { return acidMaxTemperature >= maxAirTemperature && maxAirTemperature > 0; } }
        // Thinnest air it works in (kPa). Zero-pressure balloons vent rather than pop: near vacuum.
        [KSPField(isPersistant = true)] public float minPressure = 0.001f;
        // Dynamic pressure that tears it (Pa): the film's strength per metre over the ~2 m it spans between
        // load tapes (Forces). 0 = from the film's weight, as polyethylene.
        [KSPField(isPersistant = true)] public float tearPressure = 0f;

        public double TearPressure { get { return tearPressure > 0 ? tearPressure : 400.0 * Mathf.Max(0.5f, filmDensity) / 18.7; } }

        // ---- Part ----
        // No climb limit: it climbs at whatever pace its spare lift and the envelope's drag give (a light
        // load on a full fill, tens of m/s; less gas, slower, as real crews do it). Too fast tears it
        // (tearPressure): the VAB's "Climb at launch" says how fast it leaves the ground and what it takes.
        [KSPField] public float inflateTime = 4f;      // s
        [KSPField] public float deflateTime = 2f;      // s

        // ---- State ----
        public const string Packed = "Packed", Inflating = "Inflating", Inflated = "Inflated", Deflating = "Deflating", Spent = "Spent";
        [KSPField(isPersistant = true)] public string balloonState = Packed;
        [KSPField(isPersistant = true)] public float inflation = 0f;    // share of the gas put in, 0..1
        [KSPField(isPersistant = true)] public double gasMass = 0;      // t of gas taken from the tanks (0 = ground crew)
        [KSPField(isPersistant = true)] public double gasMoles = 0;     // gas put in (mol): only what fitted; 0 = not recorded

        public bool Lifting { get { return balloonState == Inflating || balloonState == Inflated || balloonState == Deflating; } }

        public GameObject balloonObject, ropeObject, capObject, liftPointObject, balloonPointObject;
        Vector3 initialBalloonScale, initialBalloonPos, initialRopeScale;
        bool modelOk;

        // ---- Gas fill (VAB) ----
        // How much of the size's gas to put in. Less gas lifts less (the most payload scales with it),
        // needs less from the tanks, and a light load then climbs gently on its own. The size sets the
        // most it can hold; this picks anything below that. It's also the load the balloon is built for:
        // less gas, lighter tapes and caps, as on real high-altitude balloons (big and lightly loaded). Down to
        // 2%: the Big 60's 48.5 km flight took about 6% of what it could lift.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Gas fill", guiUnits = "%", guiFormat = "F0"),
         UI_FloatRange(minValue = 2f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor)]
        public float gasFillPercent = 100f;

        public float GasFillFactor { get { return Mathf.Clamp(gasFillPercent, 1f, 100f) / 100f; } }

        // ---- Envelope volume (VAB) ----
        // A smaller envelope for the same gas floats lower: the gas fills it sooner, in denser air. Real
        // low-altitude balloons are smaller for the same lift. It weighs less too (fabric area, volume^2/3),
        // so it lifts off with a little more payload. 100% = the size's standard envelope.
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Envelope volume", guiUnits = "%", guiFormat = "F0"),
         UI_FloatRange(minValue = 2f, maxValue = 100f, stepIncrement = 1f, scene = UI_Scene.Editor)]
        public float envelopeSizePercent = 100f;

        public float EnvelopeSizeFactor { get { return Mathf.Clamp(envelopeSizePercent, 1f, 100f) / 100f; } }

        // ---- Planning (VAB) ----
        // The planet the VAB readouts and the info window plan for: any with an atmosphere. On each, the
        // balloon is inflated at the lowest height it survives (the ground, or above the heat on Venus).
        [KSPField(isPersistant = true, guiActiveEditor = true, guiActive = false, guiName = "Plan for"),
         UI_ChooseOption(scene = UI_Scene.Editor, options = new string[] { "" })]
        public string planBody = "";

        [KSPField(guiActiveEditor = true, guiActive = false, guiName = "Floats")]
        public string floatText = "";

        // The lowest height it survives on the planned planet and why (heat, or Venus's acid clouds); shown
        // only when that's above the ground.
        [KSPField(guiActiveEditor = false, guiActive = false, guiName = "Fails below")]
        public string failText = "";

        // This craft under all its balloons: how fast it climbs right after inflating, and what tears them.
        [KSPField(guiActiveEditor = true, guiActive = false, guiName = "Climb at launch")]
        public string launchText = "";

        // ---- Mass ----
        // The envelope: its film over its surface (taken as a sphere's of its volume, 4.84 V^(2/3)), plus caps
        // and fittings (capsShare of the film), plus load tapes and caps for the load it's built for: the
        // gross its gas fill lifts in the air it's made for. They carry that load over the balloon's height,
        // so they scale with load x V^(1/3), and what they hold up includes themselves. Fitted to NASA's 40
        // million cu ft standard balloon (0.8 mil film; 3,600 kg to 37 km, so ~2.6 t) and the Big 60 (60
        // million cu ft, 0.4 mil; 748 kg to 48.5 km, so ~1.15 t): the model gives both flights within 15%.
        // The part itself (0.005 t) is the canister; no size adds mass of its own.
        const double capsShare = 0.2995, tapeShare = 0.3586, tapeRefVolume = 1.1205e6;   // -, per t carried at 40 MCF's height, m^3

        public static double EnvelopeMassTonnes(double volume, double filmGsm, double ratedLoadT)
        {
            if (!(volume > 0)) return 0;
            double film = System.Math.Max(0, filmGsm) * 4.84 * System.Math.Pow(volume, 2.0 / 3.0) / 1e6;
            double s = System.Math.Pow(volume / tapeRefVolume, 1.0 / 3.0);
            return ((1 + capsShare) * film + tapeShare * s * System.Math.Max(0, ratedLoadT)) / (1 + tapeShare * s);
        }

        public float BalloonMass
        {
            get { return (float)EnvelopeMassTonnes(RealBalloonPhysics.EnvelopeVolume(this), filmDensity, RealBalloonPhysics.RatedLoadTonnes(this)); }
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            return BalloonMass + CounterweightMass;
        }
        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.FIXED; }

        // ---- Start ----
        public override void OnStart(StartState st)
        {
            balloonObject = FindChild(envelopeTransform);
            ropeObject = FindChild(ropeTransform);
            capObject = FindChild(capTransform);
            liftPointObject = FindChild(liftPointTransform);
            balloonPointObject = FindChild(ropeEndTransform);
            modelOk = balloonObject != null && ropeObject != null && capObject != null && liftPointObject != null && balloonPointObject != null;
            if (!modelOk)
            {
                Debug.LogError("[RealBalloons] " + part.partInfo?.name + ": model transforms not found, balloon disabled");
                return;
            }
            EvenEnvelopeUVs();      // normally done at load already (OnLoad); this only checks
            ApplyEnvelopeFlag();    // the flag picked for the envelope, if any (again next frame, after FlagDecal starts)
            flagPending = true;
            if (HighLogic.LoadedSceneIsEditor) GameEvents.onMissionFlagSelect.Add(OnMissionFlag);

            if (HighLogic.LoadedSceneIsEditor)
            {
                SetUpPlanBodies();
                UpdateMountPreview();
                return;
            }
            if (!HighLogic.LoadedSceneIsFlight) return;

            // The model gives the envelope a sphere collider, part of the part's solid shape. As the
            // envelope inflates it would shove the craft off the ground or anything near it, and on a
            // blimp it's a sphere as big as the hull is long. Gas and fabric aren't solid: remove it.
            foreach (Collider col in balloonObject.GetComponentsInChildren<Collider>(true)) { col.enabled = false; Destroy(col); }

            SetUpEvenFrame();       // so the envelope and rope can turn upright without shearing (big sizes)
            balloonObject.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);   // packed in the canister
            initialBalloonScale = balloonObject.transform.localScale;
            initialRopeScale = ropeObject.transform.localScale;
            UpdateMountPreview();

            if (balloonState != Packed && balloonState != Inflating && balloonState != Inflated && balloonState != Deflating && balloonState != Spent)
                balloonState = Packed;
            ShowState();
        }

        GameObject FindChild(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform t in part.transform.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.gameObject;
            return null;
        }

        // Which pieces of the model show, and which actions are offered, in the current state.
        void ShowState()
        {
            bool packed = balloonState == Packed;
            bool out_ = Lifting;
            capObject.SetActive(packed);
            balloonObject.SetActive(packed || out_);
            ropeObject.SetActive(packed || out_);
            if (packed)
            {
                balloonObject.transform.localScale = initialBalloonScale;
                balloonObject.transform.position = EnvelopeHome();
                ropeObject.transform.localScale = initialRopeScale;
                PlaceRopeFoot();
            }
            if (!out_) envelopeRadius = 0;
            Events["InflateBalloon"].active = packed;
            Events["DeflateBalloon"].active = balloonState == Inflating || balloonState == Inflated;
            Events["RepackBalloon"].active = balloonState == Spent;
        }

        // ---- Flight ----
        public void FixedUpdate()
        {
            if (!modelOk) return;
            if (flagPending && !HighLogic.LoadedSceneIsEditor) { flagPending = false; ApplyEnvelopeFlag(); }
            if (HighLogic.LoadedSceneIsEditor)
            {
                EditorUpdate();
                return;
            }
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;

            UpdateCushionCollider();
            if (keelShown && !Lifting)
            {
                if (keelWeight != null) keelWeight.SetActive(false);
                HideRope();
                keelProgress = 0f;
                keelShown = false;
            }
            if (!Lifting) return;

            float dt = TimeWarp.fixedDeltaTime;
            if (balloonState == Inflating)
            {
                inflation = Mathf.Min(1f, inflation + dt / Mathf.Max(0.1f, inflateTime));
                if (inflation >= 1f) { balloonState = Inflated; ShowState(); }
            }
            else if (balloonState == Deflating)
            {
                inflation -= dt / Mathf.Max(0.1f, deflateTime);
                if (inflation <= 0f) { inflation = 0f; Spend(); return; }
            }
            else inflation = 1f;

            float pressure = (float)FlightGlobals.getStaticPressure(part.transform.position);
            PlaceEnvelope();
            // forces after the envelope is in place, so they act where it really is this frame
            ApplyLiftAndAero(LiftNow(pressure), pressure, inflation);
            if (Lifting) CheckFailure(pressure);
            if (Lifting) CheckKeelTilt();
        }

        void PlaceEnvelope()
        {
            Transform bt = balloonObject.transform, rt = ropeObject.transform;
            SyncEvenFrame();
            Quaternion up = Quaternion.LookRotation(vessel.upAxis, vessel.upAxis);
            PlaceRopeFoot();
            rt.rotation = Quaternion.Slerp(rt.rotation, up, 0.1f);
            bt.rotation = Quaternion.Slerp(bt.rotation, up, 0.12f);
            bt.localScale = EnvelopeScale(inflation, 1f);       // after turning: it reads the envelope's rotation
            rt.localScale = new Vector3(1f, 1f, inflation * RopeDepth);
            bt.position = balloonPointObject.transform.position;
            if (rigidMount) ApplyFixedMount(inflation);
        }

        // Lift now: the gas's buoyancy (RealBalloonPhysics).
        float LiftNow(float pressure)
        {
            float lift = RealBalloonPhysics.LiftKN(this, pressure, inflation);
            return float.IsNaN(lift) ? 0f : Mathf.Max(0f, lift);
        }

        // Air too hot for the material, from the planet's standard curve.
        bool TooHot(out double tempK)
        {
            tempK = 0;
            if (maxAirTemperature <= 0 || vessel == null || vessel.mainBody == null) return false;
            tempK = RealBalloonPhysics.TemperatureHere(this);
            return tempK > maxAirTemperature;
        }

        void CheckFailure(float pressure)
        {
            double airK;
            if (TooHot(out airK))
            {
                ScreenMessages.PostScreenMessage("Balloon failed: air " + (airK - 273.15).ToString("F0") +
                    " C, over its " + (maxAirTemperature - 273.15).ToString("F0") + " C rating", 6, ScreenMessageStyle.UPPER_CENTER);
                Burst();
            }
            else if (pressure < minPressure)
            {
                ScreenMessages.PostScreenMessage("Balloon burst: air too thin", 6, ScreenMessageStyle.UPPER_CENTER);
                Burst();
            }
            else if (!AcidProof && vessel != null && vessel.altitude < RealBalloonPhysics.AcidCloudTop(vessel.mainBody) &&
                     (acidMaxTemperature <= 0 || (airK = RealBalloonPhysics.TemperatureHere(this)) > acidMaxTemperature))
            {
                ScreenMessages.PostScreenMessage("Balloon eaten by " + vessel.mainBody.bodyName + "'s sulfuric acid clouds: " +
                    (acidMaxTemperature <= 0 ? "this fabric has no resistance to them"
                     : "this film only resists them below " + (acidMaxTemperature - 273.15).ToString("F0") + " C (air here " + (airK - 273.15).ToString("F0") + " C)"),
                    6, ScreenMessageStyle.UPPER_CENTER);
                Burst();
            }
        }

        void Burst()
        {
            part.Effect("burst");
            Spend();
        }

        void Spend()
        {
            balloonState = Spent;
            inflation = 0f;
            gasMoles = 0;
            envelopeRadius = envelopeDragKN = envelopeAirspeed = envelopeLiftKN = 0;
            ShowState();
        }

        // ---- Actions ----
        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Inflate Balloon")]
        public void InflateBalloon()
        {
            if (!modelOk || balloonState != Packed || vessel == null) return;
            float pressure = (float)FlightGlobals.getStaticPressure(part.transform.position);
            double airK;
            if (pressure <= 0)
                ScreenMessages.PostScreenMessage("Cannot inflate in vacuum", 3, ScreenMessageStyle.UPPER_CENTER);
            else if (pressure < minPressure)
                ScreenMessages.PostScreenMessage("Cannot inflate: air too thin", 3, ScreenMessageStyle.UPPER_CENTER);
            else if (TooHot(out airK))
                ScreenMessages.PostScreenMessage("Cannot inflate: air " + (airK - 273.15).ToString("F0") +
                    " C, over its " + (maxAirTemperature - 273.15).ToString("F0") + " C rating", 4, ScreenMessageStyle.UPPER_CENTER);
            else if (DrawLiftGas())
            {
                part.Effect("inflate");
                inflation = 0f;
                balloonState = Inflating;
                ShowState();
            }
        }

        [KSPEvent(guiActive = true, guiActiveEditor = false, guiName = "Deflate Balloon", active = false)]
        public void DeflateBalloon()
        {
            if (balloonState != Inflating && balloonState != Inflated) return;
            part.Effect("deflate");
            balloonState = Deflating;
            ShowState();
        }

        [KSPEvent(guiActive = false, guiActiveEditor = false, guiActiveUnfocused = true, unfocusedRange = 4, externalToEVAOnly = true,
                  guiName = "Repack Balloon", active = false)]
        public void RepackBalloon()
        {
            if (balloonState != Spent) return;
            balloonState = Packed;
            inflation = 0f;
            gasMass = 0;
            gasMoles = 0;
            ShowState();
        }

        [KSPAction("Inflate Balloon")]
        public void InflateAction(KSPActionParam param) { InflateBalloon(); }

        [KSPAction("Deflate Balloon")]
        public void DeflateAction(KSPActionParam param) { DeflateBalloon(); }

        // ---- Lifting gas ----
        // Each size holds a fixed amount of gas: the same number of moles (and resource units) of hydrogen
        // or helium on any planet. Free from the ground crew on the home world's surface; anywhere else it
        // comes from the craft's tanks. Only what the envelope holds where it's inflated goes in (the rest
        // would spill straight out of its open bottom): inflated higher than its full load floats, as on a
        // descent into Venus, it takes less and the rest stays in the tanks. Deploy at or below where the
        // load should float to put it all in.

        // Gas to put in here, in tonnes, resource units and moles, and its share of the fill. False if no
        // gas is configured; tonnes is infinite if the gas won't lift in this air.
        bool GasNeeded(out double tonnes, out double units, out double moles, out double share, out PartResourceDefinition def)
        {
            tonnes = units = moles = 0; share = 1; def = null;
            if (string.IsNullOrEmpty(liftGas) || vessel == null || vessel.mainBody == null) return false;
            def = PartResourceLibrary.Instance.GetDefinition(liftGas);
            double mGas = RealBalloonPhysics.GasMolarMass(liftGas);
            if (def == null || def.density <= 0 || mGas <= 0) return false;
            double mAir = vessel.mainBody.atmosphereMolarMass * 1000.0;
            if (mAir <= mGas) { tonnes = double.PositiveInfinity; return true; }
            double mol = RealBalloonPhysics.FillMoles(this);
            if (mol <= 0) return false;
            double p = FlightGlobals.getStaticPressure(part.transform.position), t = RealBalloonPhysics.TemperatureHere(this);
            double fits = p > 0 && t > 0 ? p * 1000.0 * RealBalloonPhysics.EnvelopeVolume(this) / (RealBalloonPhysics.R * t) : mol;
            moles = System.Math.Min(mol, fits);
            share = moles / mol;
            tonnes = moles * mGas / 1e6;
            units = tonnes / def.density;
            return true;
        }

        bool GroundCrewFills()
        {
            return vessel != null && vessel.mainBody == FlightGlobals.GetHomeBody() && vessel.LandedOrSplashed;
        }

        static string FitsText(double share)
        {
            return share < 0.995 ? " (the envelope holds " + (share * 100).ToString("F0") + "% of the fill here)" : "";
        }

        // Put the gas in: from the ground crew or the craft's tanks. False (and says why) if there isn't enough.
        bool DrawLiftGas()
        {
            double tonnes, units, moles, share;
            PartResourceDefinition def;
            if (!GasNeeded(out tonnes, out units, out moles, out share, out def)) return true;
            if (GroundCrewFills())
            {
                gasMass = 0;
                gasMoles = moles;
                ScreenMessages.PostScreenMessage("Ground crew fills the balloon with " + liftGas + FitsText(share), 4, ScreenMessageStyle.UPPER_CENTER);
                return true;
            }
            if (double.IsInfinity(tonnes))
            {
                ScreenMessages.PostScreenMessage(liftGas + " won't lift in this atmosphere", 5, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            double amount, maxAmount;
            part.GetConnectedResourceTotals(def.id, ResourceFlowMode.ALL_VESSEL, out amount, out maxAmount, true);
            if (amount < units * 0.999)
            {
                ScreenMessages.PostScreenMessage("Not enough " + liftGas + " to inflate: need " + tonnes.ToString("F2") + " t (" +
                    units.ToString("N0") + " units), have " + (amount * def.density).ToString("F2") + " t", 6, ScreenMessageStyle.UPPER_CENTER);
                return false;
            }
            part.RequestResource(def.id, units, ResourceFlowMode.ALL_VESSEL);
            gasMass = tonnes;
            gasMoles = moles;
            ScreenMessages.PostScreenMessage("Inflating with " + tonnes.ToString("F2") + " t of " + liftGas +
                (share < 0.995 ? FitsText(share) + "; the rest stays in the tanks" : ""), share < 0.995 ? 6 : 3, ScreenMessageStyle.UPPER_CENTER);
            return true;
        }

        public override string GetInfo()
        {
            return "Zero-pressure balloon: floats where the air can just hold it up.\nShow Balloon Info (right-click in the VAB) gives lift and float heights for each size and envelope.";
        }
    }
}
