// RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// Part of a rewritten derivative of KerBalloons (JoePatrick1; KerBalloons Reinflated (c) 2018 Fengist), MIT.

using System.Collections.Generic;
using UnityEngine;

namespace RealBalloons
{
    // ---- Gas valve ----
    // A valve at the top of the envelope, as zero-pressure balloons and gas airships have, for coming down. Gas
    // let out is gone. Below the height where the envelope is just full, the gas lifts the same at any height
    // (its moles times the air's molar mass less its own), so it can't set a lower float height: the craft
    // floats where its envelope is just full of the gas that lifts it, whatever more it carries. What the valve
    // does:
    //  - "Hold neutral" lets out the surplus, the gas beyond what lifts the craft (a balloon carries it up and
    //    spills it at its float height, but it isn't lost here, so after sinking it climbs back there), and
    //    stays on: whatever lift the craft gains later (fuel burnt, weight dropped) goes too, as Zeppelins
    //    valved gas as their fuel burnt. The craft still floats where it did, but stays at any height below it's
    //    taken to: a blimp flown down on its elevators and engines holds wherever it levels off. It never makes
    //    the craft heavy: it holds a millionth of its weight light, over the rounding.
    //  - "Vent 0.5%" (an action group) lets out the surplus and then gas lifting ventPulse of the craft's
    //    weight, each press: a known, gentle sink (about 0.9 m/s for a blimp near the ground at 0.5%).
    //  - "Open" lets gas out slowly (ventRate of the fill a second) until closed: the craft grows heavy and
    //    sinks, faster the more it's let out, and only dropping weight (or engines) stops it, as on a real gas
    //    balloon. It closes itself at ventFloor of the fill.
    // Each setting holds until changed (Open shuts itself only at ventFloor). Shared by all the craft's
    // balloons: neutral is all their gas lifting the whole craft.
    public partial class ModuleRealBalloon
    {
        const string ValveClosed = "Closed", ValveNeutral = "Hold neutral", ValveOpen = "Open";

        [KSPField(isPersistant = true, guiActive = false, guiActiveEditor = false, guiName = "Gas valve"),
         UI_ChooseOption(options = new[] { ValveClosed, ValveNeutral, ValveOpen }, scene = UI_Scene.Flight)]
        public string gasValve = ValveClosed;
        [KSPField] public float ventRate = 0.001f;          // of the fill a second, open
        [KSPField] public float ventSurplusRate = 0.01f;    // of the fill a second, letting out the surplus
        [KSPField] public float ventFloor = 0.05f;          // of the fill: the valve shuts before the last of it
        [KSPField] public float ventPulse = 0.005f;         // of the craft's weight, each "sink a little"
        const double neutralMargin = 1e-6;                  // of the craft's weight left spare at neutral
        bool holdVenting;

        void UpdateValveField()
        {
            BaseField f = Fields["gasValve"];
            bool show = HighLogic.LoadedSceneIsFlight && balloonState == Inflated;
            if (f != null && f.guiActive != show) f.guiActive = show;
            if (gasValve == "To neutral") gasValve = ValveNeutral;                // saved by 1.0.1's test builds
            else if (gasValve != ValveClosed && gasValve != ValveNeutral && gasValve != ValveOpen) gasValve = ValveClosed;
            if (!show && balloonState != Inflated && gasValve != ValveClosed) SetValve(ValveClosed);
        }

        // Set from code: the menu's chooser is told, so it shows the valve as it is.
        void SetValve(string state)
        {
            if (gasValve == state) return;
            gasValve = state;
            MonoUtilities.RefreshContextWindows(part);
        }

        // The gas's lift over the craft's mass (t), all its balloons together, wherever the envelopes aren't full:
        // more than 0, it climbs to its float height; less, it sinks.
        double SpareLiftTonnes(out double craftMass)
        {
            List<ModuleRealBalloon> cluster = CraftBalloons(out craftMass);
            double lift = 0;
            CelestialBody body = vessel != null ? vessel.mainBody : null;
            if (body == null) return 0;
            foreach (ModuleRealBalloon b in cluster)
                if (b.Lifting)
                    lift += RealBalloonPhysics.GasMoles(b) * RealBalloonPhysics.LiftPerMoleTonnes(b, body) *
                            (b.balloonState == Inflating ? b.inflation : 1f);
            return lift - craftMass;
        }

        void UpdateValve(float dt)
        {
            UpdateValveField();
            if (gasValve == ValveClosed || balloonState != Inflated || vessel == null || vessel.mainBody == null) return;
            double fill = RealBalloonPhysics.FillMoles(this), n = RealBalloonPhysics.GasMoles(this);
            double perMol = RealBalloonPhysics.LiftPerMoleTonnes(this, vessel.mainBody);
            if (!(fill > 0) || !(n > 0) || !(perMol > 0)) { SetValve(ValveClosed); return; }
            double dn;
            string closed = null;
            if (gasValve == ValveNeutral)
            {
                double craftMass, spare = SpareLiftTonnes(out craftMass);
                double surplus = (spare - neutralMargin * craftMass) / perMol;      // mol of this gas
                dn = System.Math.Min(ventSurplusRate * fill * dt, System.Math.Max(0, surplus));
                bool venting = surplus > 0;
                if (holdVenting && dn >= surplus && vessel == FlightGlobals.ActiveVessel)
                    ScreenMessages.PostScreenMessage("Gas valve: neutral, holding", 3, ScreenMessageStyle.UPPER_CENTER);
                holdVenting = venting && dn < surplus;
            }
            else
            {
                dn = ventRate * fill * dt;
                double keep = ventFloor * fill;
                if (n - dn <= keep) { dn = System.Math.Max(0, n - keep); closed = "Gas valve closed: " + (ventFloor * 100).ToString("F0") + "% of the fill left"; }
            }
            if (dn > 0)
            {
                double left = System.Math.Max(0, n - dn);
                if (gasMass > 0) gasMass *= left / n;
                gasMoles = left;
            }
            if (closed != null)
            {
                SetValve(ValveClosed);
                if (vessel == FlightGlobals.ActiveVessel) ScreenMessages.PostScreenMessage(closed, 4, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        // The info window's lines: the valve, and the gas's lift against the craft.
        string ValveInfo()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !Lifting) return "";
            string s = "";
            if (gasValve == ValveOpen)
                s += "\nGas valve: open, " + (ventRate * 100).ToString("0.##") + "% of the fill a second";
            else if (gasValve == ValveNeutral)
                s += "\nGas valve: holding neutral";
            double craftMass, spare = SpareLiftTonnes(out craftMass);
            if (!(craftMass > 0)) return s;
            if (System.Math.Abs(spare) <= 0.0005 * craftMass)
                s += "\nStatic lift: neutral (stays at any height below its float)";
            else if (spare > 0)
                s += "\nStatic lift: " + Tonnes(spare) + " spare (climbs back to its float)";
            else
                s += "\nStatic lift: " + Tonnes(-spare) + " short (sinking)";
            return s;
        }

        // The action's name, at the step set in the part's config.
        void NameVentAction()
        {
            BaseAction a = Actions["VentPulseAction"];
            if (a != null) a.guiName = "Vent " + (ventPulse * 100).ToString("0.##") + "%";
        }

        void VentPulse()
        {
            if (balloonState != Inflated || vessel == null || vessel.mainBody == null) return;
            double fill = RealBalloonPhysics.FillMoles(this), n = RealBalloonPhysics.GasMoles(this);
            double perMol = RealBalloonPhysics.LiftPerMoleTonnes(this, vessel.mainBody);
            if (!(fill > 0) || !(n > 0) || !(perMol > 0)) return;
            double craftMass, spare = SpareLiftTonnes(out craftMass);
            if (!(craftMass > 0)) return;
            double dn = (System.Math.Max(0, spare) + ventPulse * craftMass) / perMol;
            double keep = ventFloor * fill;
            if (n - dn < keep) dn = System.Math.Max(0, n - keep);
            if (!(dn > 0)) { ScreenMessages.PostScreenMessage("Gas valve: only " + (ventFloor * 100).ToString("F0") + "% of the fill left", 3, ScreenMessageStyle.UPPER_CENTER); return; }
            double left = n - dn;
            if (gasMass > 0) gasMass *= left / n;
            gasMoles = left;
            double after = SpareLiftTonnes(out craftMass);
            ScreenMessages.PostScreenMessage("Vented " + Tonnes(dn * RealBalloonPhysics.GasMolarMass(liftGas) / 1e6) + " of " + liftGas + ": " +
                (after < 0 ? (-after / craftMass * 100).ToString("0.0") + "% heavy, sinking" : "neutral"), 4, ScreenMessageStyle.UPPER_CENTER);
        }

        [KSPAction("Vent 0.5%")]
        public void VentPulseAction(KSPActionParam param) { VentPulse(); }

        [KSPAction("Open gas valve")]
        public void OpenValveAction(KSPActionParam param) { if (balloonState == Inflated) SetValve(ValveOpen); }

        [KSPAction("Close gas valve")]
        public void CloseValveAction(KSPActionParam param) { SetValve(ValveClosed); }

        [KSPAction("Toggle gas valve")]
        public void ToggleValveAction(KSPActionParam param)
        {
            SetValve(param.type == KSPActionType.Activate && balloonState == Inflated ? ValveOpen : ValveClosed);
        }

        [KSPAction("Hold neutral")]
        public void NeutralValveAction(KSPActionParam param) { if (balloonState == Inflated) SetValve(ValveNeutral); }
    }
}
