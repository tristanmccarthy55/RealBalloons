// RealBalloons - Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// Part of a rewritten derivative of KerBalloons (JoePatrick1; KerBalloons Reinflated (c) 2018 Fengist), MIT.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealBalloons
{
    public partial class ModuleRealBalloon
    {
        // ---- Balloon Info window ----
        bool visibleShowInfo;
        Rect infoRect = new Rect(200, 200, 400, 300);
        string lockId;

        [KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Show Balloon Info")]
        public void ShowInfo()
        {
            visibleShowInfo = !visibleShowInfo;
            if (!visibleShowInfo) Unlock();
        }

        void OnGUI()
        {
            if (!visibleShowInfo) return;
            GUI.skin = HighLogic.Skin;
            infoRect = GUILayout.Window(GetInstanceID(), infoRect, InfoBalloonWin, "RealBalloons: " + (part.partInfo != null ? part.partInfo.title : "balloon"));
            // Clicks on the window shouldn't reach the game behind it.
            if (infoRect.Contains(Event.current.mousePosition)) Lock(); else Unlock();
        }

        void Lock()
        {
            if (lockId != null) return;
            lockId = "RealBalloonsInfo" + GetInstanceID();
            InputLockManager.SetControlLock(HighLogic.LoadedSceneIsEditor ? ControlTypes.EDITOR_LOCK : ControlTypes.ALLBUTCAMERAS, lockId);
        }

        void Unlock()
        {
            if (lockId == null) return;
            InputLockManager.RemoveControlLock(lockId);
            lockId = null;
        }

        void OnDestroy()
        {
            Unlock();
            GameEvents.onMissionFlagSelect.Remove(OnMissionFlag);
        }

        Vector2 infoPos;
        void InfoBalloonWin(int id)
        {
            string moreInfoText;
            // Planning: on the current planet in flight, or the one chosen in the VAB ("Plan for").
            bool flight = HighLogic.LoadedSceneIsFlight && vessel != null && vessel.mainBody != null;
            CelestialBody pb = flight ? vessel.mainBody : PlanBody();
            moreInfoText = "Balloon: " + BalloonMass.ToString("0.###") + " t";
            if (Cushion && CounterweightMass > 0)
                moreInfoText += "\nKeel weight: " + CounterweightMass.ToString("0.###") + " t, " +
                    keelLength.ToString("F0") + " radii down";
            double fillMol = RealBalloonPhysics.FillMoles(this);
            double mGasInfo = RealBalloonPhysics.GasMolarMass(liftGas);
            PartResourceDefinition gd = string.IsNullOrEmpty(liftGas) ? null : PartResourceLibrary.Instance.GetDefinition(liftGas);
            if (fillMol > 0 && mGasInfo > 0)
            {
                double ft = fillMol * mGasInfo / 1e6;
                moreInfoText += "\nGas: " + ft.ToString("F2") + " t " + liftGas +
                    (gd != null && gd.density > 0 ? " (" + (ft / gd.density).ToString("N0") + " units)" : "");
            }
            if (pb != null && envelopeVolume > 0)
            {
                double ceil, a;
                int s0 = RealBalloonPhysics.PredictFloat(this, pb, 0, out a, out ceil);
                if (s0 == 3)
                    moreInfoText += "\n" + pb.bodyName + ": this envelope can't work here";
                else
                {
                    double bottom = RealBalloonPhysics.FloorAltitude(this, pb, ceil);
                    double liftOff = RealBalloonPhysics.CapacityTonnes(this, pb, pb.GetPressure(bottom), pb.GetTemperature(bottom));
                    double heavyAlt, heavyCeil;
                    RealBalloonPhysics.PredictFloat(this, pb, liftOff * 0.999, out heavyAlt, out heavyCeil);
                    double atCeil = RealBalloonPhysics.CapacityTonnes(this, pb, minPressure, pb.GetTemperature(ceil));
                    moreInfoText += "\n" + pb.bodyName + ":";
                    moreInfoText += "\n  max payload " + System.Math.Max(0, liftOff - BalloonMass).ToString("F2") + " t, floats ~" +
                        (heavyAlt / 1000).ToString("F1") + " km";
                    double emptyAlt, emptyCeil;
                    int se = RealBalloonPhysics.PredictFloat(this, pb, BalloonMass, out emptyAlt, out emptyCeil);
                    if (atCeil > BalloonMass)
                        moreInfoText += "\n  under " + (atCeil - BalloonMass).ToString("F2") + " t: pops above " +
                            (ceil / 1000).ToString("F1") + " km";
                    else if (se == 0)
                        moreInfoText += "\n  empty: floats ~" + (emptyAlt / 1000).ToString("F1") + " km";
                    if (bottom > 0 && !AcidProof && RealBalloonPhysics.AcidCloudTop(pb) > 0 &&
                        (acidMaxTemperature <= 0 || pb.GetTemperature(bottom) <= acidMaxTemperature + 0.5) && pb.GetTemperature(bottom) < maxAirTemperature - 0.5)
                        moreInfoText += "\n  fails below " + (bottom / 1000).ToString("F1") + " km (sulfuric acid clouds" +
                            (acidMaxTemperature > 0 ? " warmer than " + (acidMaxTemperature - 273.15).ToString("F0") + " C)" : ")");
                    else if (bottom > 0)
                        moreInfoText += maxAirTemperature > 0
                            ? "\n  fails below " + (bottom / 1000).ToString("F1") + " km (over " + (maxAirTemperature - 273.15).ToString("F0") + " C)"
                            : "\n  pops below " + (bottom / 1000).ToString("F1") + " km";

                    // this craft, under all its balloons together (any sizes)
                    double craftMass;
                    List<ModuleRealBalloon> cluster = CraftBalloons(out craftMass);
                    if (craftMass > 0)
                    {
                        double fa, fc;
                        int st = RealBalloonPhysics.PredictFloat(cluster, pb, craftMass, out fa, out fc);
                        string who = "\nThis craft (" + craftMass.ToString("F2") + " t" + (cluster.Count > 1 ? ", " + cluster.Count + " balloons" : "") + "): ";
                        moreInfoText += st == 0 ? who + "floats ~" + (fa / 1000).ToString("F1") + " km"
                                      : st == 1 ? who + "too heavy"
                                      : st == 2 ? who + "too light, pops above " + (fc / 1000).ToString("F1") + " km"
                                      : "";
                        double v, vTear, la;
                        if (!flight && RealBalloonPhysics.LaunchClimb(cluster, pb, craftMass, out v, out vTear, out la) == 0)
                            moreInfoText += "\n  climbs " + v.ToString("F0") + " m/s after inflating" + (la > 1 ? " at " + (la / 1000).ToString("F0") + " km" : "") +
                                (v > vTear ? ": TEARS, over the " + vTear.ToString("F0") + " m/s it takes there (less gas fill)"
                                           : " (tears above " + vTear.ToString("F0") + " m/s there)");
                    }
                }
            }

            // In flight: what this balloon can hold up right here, in the local air and gravity.
            if (HighLogic.LoadedSceneIsFlight && vessel != null && vessel.mainBody != null)
            {
                float here = (float)FlightGlobals.getStaticPressure(part.transform.position);
                float g = (float)FlightGlobals.getGeeForceAtPosition(part.transform.position).magnitude;
                if (here > 0 && g > 0)
                    moreInfoText += "\nHere: holds " +
                        Mathf.Max(0f, RealBalloonPhysics.LiftKN(this, here) / g - BalloonMass).ToString("F3") + " t payload";
                double gasT, gasU, gasN, gasShare;
                PartResourceDefinition gasDef;
                if (here > 0 && balloonState == Packed && GasNeeded(out gasT, out gasU, out gasN, out gasShare, out gasDef))
                {
                    moreInfoText += GroundCrewFills() ? "\nGas source: ground crew"
                        : double.IsInfinity(gasT) ? "\n" + liftGas + " won't lift here"
                        : "\nGas source: craft's tanks, " + gasT.ToString("F2") + " t";
                    if (!double.IsInfinity(gasT) && gasShare < 0.995)
                        moreInfoText += "\nThe envelope holds " + (gasShare * 100).ToString("F0") + "% of the fill here: inflate lower to put it all in";
                }
                if (Lifting && gasMoles > 0)
                {
                    double fill = RealBalloonPhysics.FillMoles(this);
                    moreInfoText += "\nGas in: " + (gasMoles * mGasInfo / 1e6).ToString("F2") + " t" +
                        (fill > 0 && gasMoles < fill * 0.995 ? " (" + (gasMoles / fill * 100).ToString("F0") + "% of the fill)" : "");
                }
                if (Cushion && Lifting)
                {
                    float h = Vector3.Dot(TrueCoM() - EnvelopeCentre(), vessel.upAxis);
                    moreInfoText += h < 0
                        ? "\nCushion: stable, centre of mass " + (-h).ToString("F1") + " m below the envelope's centre"
                        : "\nCushion: will tip, centre of mass " + h.ToString("F1") + " m above the envelope's centre" +
                          " (heavier weight or longer keel)";
                    if (CounterweightMass > 0)
                        moreInfoText += "\n  tilted " + cushionTilt.ToString("F0") + " degrees: its keel tears it past " + keelTearAngle.ToString("F0");
                }
                if (Hull && Lifting && trimLimit > 0)
                    moreInfoText += "\nBallonet trim: " + System.Math.Abs(trimShift).ToString("F1") + " m " + (trimShift >= 0 ? "forward" : "aft") +
                        " of " + trimLimit.ToString("F1") + " m" + (System.Math.Abs(trimShift) >= trimLimit - 0.01 ? " (at its limit: move weight)" : "");
                if (envelopeRadius > 0)
                {
                    moreInfoText += "\nEnvelope: " + (2 * envelopeRadius).ToString("F0") + " m, " + envelopeAirspeed.ToString("F1") + " m/s, drag " +
                        (envelopeDragKN < 1 ? (envelopeDragKN * 1000).ToString("F0") + " N" : envelopeDragKN.ToString("F2") + " kN") +
                        ", lift " + envelopeLiftKN.ToString("F2") + " kN";
                    if (envelopeRho > 0)
                        moreInfoText += "\nAirflow: " + envelopeQ.ToString("F0") + " of " + TearPressure.ToString("F0") + " Pa that tears it (" +
                            System.Math.Sqrt(2 * TearPressure / envelopeRho).ToString("F0") + " m/s here)";
                }
            }

            GUILayout.BeginVertical();
            infoPos = GUILayout.BeginScrollView(infoPos, GUILayout.MaxHeight(360));
            GUILayout.Label(moreInfoText);
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close"))
            {
                visibleShowInfo = false;
                Unlock();
            }
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
