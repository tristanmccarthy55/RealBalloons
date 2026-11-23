// RealBalloons - zero-pressure balloon physics.
// Copyright (c) 2026 tristanmccarthy55. MIT licence, see LICENSE.txt.
// Replaces KerBalloons' BalloonProperties (KerBalloons Reinflated (c) 2018 Fengist, MIT).

using System.Collections.Generic;
using UnityEngine;

namespace RealBalloons
{
    // A zero-pressure balloon. Each size holds a fixed amount of gas, n moles: the same for hydrogen
    // or helium on any planet, n = maxLift / (g_home * (M_air,home - M_H2)), so hydrogen on the home
    // world lifts off with maxLift (kN, balloon included). Each mole displaces a mole of the air
    // around it and lifts (M_air - M_gas). While the envelope is slack all the gas lifts; once the gas
    // has expanded to fill the envelope (volume V) the surplus spills out of the open bottom and only
    // P V / (R T) moles stay in:
    //     lift = g * (M_air - M_gas) * min(n, P V / (R T))
    // It climbs until that equals the craft's weight, then floats: heavier loads float lower. g is the
    // local gravity, so where it floats depends only on mass. V = envelopeVolume (per size) times
    // volumeFactor (per envelope material: 1 for Earth envelopes, small for dense-air Venus ones).
    //
    // The gas that goes in stays in: no leaks, and no day and night here to cool it, so a balloon at float
    // stays there (a real zero-pressure balloon sinks at sunset and drops ballast). Only what the envelope
    // holds where it's inflated goes in. One simplification: gas that spills while the balloon is above
    // the height where its envelope is just full isn't lost, so if it later sinks below that height (more
    // load, or an overshoot) it lifts as before. It needs all its gas to lift a full load anyway.
    public static class RealBalloonPhysics
    {
        public const double H2 = 2.016;          // g/mol
        public const double He = 4.0026;
        public const double R = 8.314462618;     // J/(mol K)

        public static double GasMolarMass(string gas)
        {
            if (gas == "Helium" || gas == "Helium4") return He;
            if (gas == "Hydrogen") return H2;
            return 0;
        }

        static double GasKgPerMol(ModuleRealBalloon b)
        {
            double m = GasMolarMass(b.liftGas);
            return (m > 0 ? m : H2) / 1000.0;
        }

        // The home world, or null before the planets exist (part masses are worked out while parts load).
        static CelestialBody Home()
        {
            try { return FlightGlobals.fetch != null ? FlightGlobals.GetHomeBody() : null; }
            catch { return null; }
        }

        // The home world's air (kg/mol) and surface gravity (m/s^2); RSS Earth's until the planets exist.
        static double HomeMolarMass() { CelestialBody h = Home(); return h != null && h.atmosphereMolarMass > 0 ? h.atmosphereMolarMass : 0.0289644; }
        static double HomeGravity() { CelestialBody h = Home(); return h != null && h.GeeASL > 0 ? h.GeeASL * 9.80665 : 9.8203; }

        // The size's fixed fill (mol), with the gas fill set in the VAB.
        public static double FillMoles(ModuleRealBalloon b)
        {
            if (b == null) return 0;
            double mHome = HomeMolarMass(), gHome = HomeGravity();
            if (mHome <= H2 / 1000.0 || gHome <= 0) return 0;
            return b.maxLift * 1000.0 / (gHome * (mHome - H2 / 1000.0)) * b.GasFillFactor;
        }

        // Gas in the envelope (mol): what went in at inflation (only what fitted there), or the fill when
        // that isn't recorded (packed, or inflated before it was).
        public static double GasMoles(ModuleRealBalloon b)
        {
            return b.gasMoles > 0 ? b.gasMoles : FillMoles(b);
        }

        // The gross load the balloon is built for (t): what its fill lifts in the air it's made for.
        public static double RatedLoadTonnes(ModuleRealBalloon b)
        {
            double mAir = b.designAirMolarMass > 0 ? b.designAirMolarMass : HomeMolarMass(), mGas = GasKgPerMol(b);
            return mAir > mGas ? FillMoles(b) * (mAir - mGas) / 1000.0 : 0;
        }

        // Full envelope volume (m^3).
        public static double EnvelopeVolume(ModuleRealBalloon b)
        {
            return b.envelopeVolume * (b.volumeFactor > 0 ? b.volumeFactor : 1f) * b.EnvelopeSizeFactor;
        }

        // Tonnes the balloon holds up (craft and balloon) at this pressure and temperature on body, with
        // this share of its gas put in: of the fill (planning), or of the given moles (in flight).
        public static double CapacityTonnes(ModuleRealBalloon b, CelestialBody body, double pKPa, double tempK, double fill = 1.0, double moles = -1)
        {
            double mGas = GasKgPerMol(b), mAir = body.atmosphereMolarMass;
            if (mAir <= mGas || pKPa <= 0 || tempK <= 0) return 0;
            double n = (moles >= 0 ? moles : FillMoles(b)) * fill, V = EnvelopeVolume(b);
            if (V > 0) n = System.Math.Min(n, pKPa * 1000.0 * V / (R * tempK));
            return n * (mAir - mGas) / 1000.0;
        }

        // Gas volume right now (m^3): the gas put in at this pressure, up to the envelope's size.
        public static double GasVolume(ModuleRealBalloon b, double pKPa, double tempK, double fill)
        {
            if (pKPa <= 0 || tempK <= 0) return 0;
            double v = GasMoles(b) * fill * R * tempK / (pKPa * 1000.0);
            double V = EnvelopeVolume(b);
            return V > 0 ? System.Math.Min(v, V) : v;
        }

        // Standard air temperature at the part (the planet's curve, not KSP's part heating).
        public static double TemperatureHere(ModuleRealBalloon b)
        {
            CelestialBody body = b.vessel.mainBody;
            return body.GetTemperature(FlightGlobals.getAltitudeAtPos(b.part.transform.position, body));
        }

        // Lift (kN) right here, in the local air and gravity, with this share of the gas put in.
        public static float LiftKN(ModuleRealBalloon b, double pKPa, double fill = 1.0)
        {
            if (b.vessel == null || b.vessel.mainBody == null || b.envelopeVolume <= 0) return 0f;
            double g = FlightGlobals.getGeeForceAtPosition(b.part.transform.position).magnitude;
            return (float)(CapacityTonnes(b, b.vessel.mainBody, pKPa, TemperatureHere(b), fill, GasMoles(b)) * g);
        }

        // Where a craft of totalMass tonnes floats on body under this balloon alone.
        // 0 = floats at alt, 1 = too heavy to lift, 2 = too light (rises past the ceiling and pops),
        // 3 = this envelope can't work in this atmosphere.
        public static int PredictFloat(ModuleRealBalloon b, CelestialBody body, double totalMass, out double alt, out double ceilingAlt)
        {
            return PredictFloat(new List<ModuleRealBalloon> { b }, body, totalMass, out alt, out ceilingAlt);
        }

        // The same under several balloons together (any sizes and envelopes): where their capacities add
        // up to the craft. Between the highest floor and the lowest ceiling of those that work there.
        public static int PredictFloat(List<ModuleRealBalloon> balloons, CelestialBody body, double totalMass, out double alt, out double ceilingAlt)
        {
            alt = ceilingAlt = 0;
            if (body == null || !body.atmosphere || balloons == null) return 3;
            var work = new List<ModuleRealBalloon>();
            double bottom = 0, top = double.MaxValue;
            foreach (ModuleRealBalloon b in balloons)
            {
                if (b == null || EnvelopeVolume(b) <= 0 || body.GetPressure(0) < b.minPressure) continue;
                double t = AltitudeAtPressure(body, b.minPressure), f = FloorAltitude(b, body, t);
                if (f >= t) continue;
                work.Add(b);
                bottom = System.Math.Max(bottom, f);
                top = System.Math.Min(top, t);
            }
            if (work.Count == 0 || bottom >= top) return 3;
            ceilingAlt = top;
            System.Func<double, double> cap = h =>
            {
                double c = 0, p = body.GetPressure(h), T = body.GetTemperature(h);
                foreach (ModuleRealBalloon b in work) c += CapacityTonnes(b, body, p, T, 1.0, b.Lifting ? GasMoles(b) : -1);   // inflated: the gas that went in
                return c;
            };
            if (cap(bottom) < totalMass) { alt = bottom; return 1; }
            if (cap(top) > totalMass) { alt = top; return 2; }
            double lo = bottom, hi = top;
            for (int i = 0; i < 50; i++)
            {
                double mid = (lo + hi) / 2;
                if (cap(mid) > totalMass) lo = mid; else hi = mid;
            }
            alt = lo;
            return 0;
        }

        // The steady climb right after inflating, at the lowest height all the balloons survive (the ground
        // on Earth): the spare lift equals the envelopes' drag, 0.5 rho v^2 Cd A, with each gas bubble the
        // size its gas is there (a blimp with ballonets: its whole hull). The airflow is hardest there: higher up it climbs faster, but the bubbles
        // grow and the air thins, so the dynamic pressure falls (a simulated ascent peaks within 1-4% of this).
        // tearSpeed: what tears the weakest envelope there (its tearPressure). 0 = climbs at speed,
        // 1 = can't lift the craft there, 3 = these envelopes can't work in this atmosphere.
        public static int LaunchClimb(List<ModuleRealBalloon> balloons, CelestialBody body, double craftMass,
                                      out double speed, out double tearSpeed, out double alt)
        {
            speed = tearSpeed = alt = 0;
            if (body == null || !body.atmosphere || balloons == null) return 3;
            var work = new List<ModuleRealBalloon>();
            double bottom = 0, top = double.MaxValue;
            foreach (ModuleRealBalloon b in balloons)
            {
                if (b == null || EnvelopeVolume(b) <= 0 || body.GetPressure(0) < b.minPressure) continue;
                double t = AltitudeAtPressure(body, b.minPressure), f = FloorAltitude(b, body, t);
                if (f >= t) continue;
                work.Add(b);
                bottom = System.Math.Max(bottom, f);
                top = System.Math.Min(top, t);
            }
            if (work.Count == 0 || bottom >= top) return 3;
            alt = bottom;
            double p = body.GetPressure(bottom) * 1000.0, T = body.GetTemperature(bottom);     // Pa, K
            double mAir = body.atmosphereMolarMass, rho = p * mAir / (R * T);
            double rr = body.Radius + bottom, g = body.gravParameter / (rr * rr);
            if (!(rho > 0) || !(g > 0)) return 3;
            double liftKN = 0, cdA = 0, qTear = double.MaxValue;
            foreach (ModuleRealBalloon b in work)
            {
                double mGas = GasKgPerMol(b);
                if (mAir <= mGas) continue;
                double n = System.Math.Min(FillMoles(b), p * EnvelopeVolume(b) / (R * T));   // what goes in there
                liftKN += n * (mAir - mGas) * g / 1000.0;
                double r = System.Math.Pow(3.0 * b.ShapeVolume(n * R * T / p) / (4.0 * System.Math.PI), 1.0 / 3.0);   // a blimp with ballonets: its hull
                cdA += b.ClimbCdA(r);
                qTear = System.Math.Min(qTear, b.TearPressure);
            }
            if (qTear == double.MaxValue) return 3;
            tearSpeed = System.Math.Sqrt(2.0 * qTear / rho);
            double spareKN = liftKN - craftMass * g;
            if (spareKN <= 0 || cdA <= 0) return 1;
            speed = System.Math.Sqrt(2.0 * spareKN * 1000.0 / (rho * cdA));
            return 0;
        }

        // Lowest altitude the envelope survives on body: where the standard air temperature drops to
        // its rating, searched upward from the surface to the ceiling. Low down a zero-pressure balloon
        // only shrinks; what destroys it is heat.
        public static double FloorAltitude(ModuleRealBalloon b, CelestialBody body, double top)
        {
            double floor = TempFloor(body, top, b.maxAirTemperature);
            double acidTop = AcidCloudTop(body);
            if (acidTop > 0 && !(b.acidMaxTemperature >= b.maxAirTemperature && b.maxAirTemperature > 0))
                floor = System.Math.Max(floor, b.acidMaxTemperature <= 0 ? acidTop                    // no resistance: above the clouds only
                                             : System.Math.Min(acidTop, TempFloor(body, top, b.acidMaxTemperature)));   // where they're cool enough
            return floor;
        }

        // Lowest altitude the envelope's heat rating allows on body (ignoring acid).
        public static double HeatFloorAltitude(ModuleRealBalloon b, CelestialBody body, double top)
        {
            return TempFloor(body, top, b.maxAirTemperature);
        }

        // Whether any balloon here could float on body at all: the lightest build (thin film, 9.4 g/m2, with
        // hydrogen) at the best gas fill, any envelope up to about Size 7's, at the lowest height an envelope
        // survives (air no warmer than 160 C). liftPerVolumeKN is the sizes' hydrogen lift per m3 of envelope.
        // Gas giants (air nearly as light as hydrogen), the Sun and near-airless worlds such as Pluto can't.
        public static bool CanFloatOn(CelestialBody body, double liftPerVolumeKN)
        {
            if (body == null || !body.atmosphere || body.atmosphereDepth <= 0) return false;
            double dM = body.atmosphereMolarMass - H2 / 1000.0;                    // kg/mol lifted per mole of hydrogen
            double mHome = HomeMolarMass(), gHome = HomeGravity();
            if (dM <= 0 || mHome <= H2 / 1000.0 || gHome <= 0) return false;
            if (!(liftPerVolumeKN > 0)) liftPerVolumeKN = 2.1735e-4;
            double top = body.atmosphereDepth, h0 = TempFloor(body, top, 433.0);
            if (h0 >= top) return false;
            double p = body.GetPressure(h0) * 1000.0, t = body.GetTemperature(h0);
            if (p <= 0 || t <= 0) return false;
            const double largestVolume = 1.1e7;                                   // m3, about Size 7's envelope
            for (double v = 100; v <= largestVolume; v *= 1.25)
            {
                double full = liftPerVolumeKN * v * 1000.0 / (gHome * (mHome - H2 / 1000.0));   // mol at 100% gas fill
                double fits = p * v / (R * t);
                double fill = System.Math.Max(0.02, System.Math.Min(1.0, fits / full));
                double lift = System.Math.Min(full * fill, fits) * dM / 1000.0;                   // t
                double rated = full * fill * (mHome - H2 / 1000.0) / 1000.0;                       // t, in home air
                if (lift > ModuleRealBalloon.EnvelopeMassTonnes(v, 9.4, rated) * 1.02) return true;
            }
            return false;
        }

        // Sulfuric-acid cloud decks (Venus's: 47-70 km, droplets of 75-92% acid), which eat envelopes not rated for
        // them (acidMaxTemperature): cotton anywhere in them, polyethylene where they're warmer than 20 C.
        public static double AcidCloudTop(CelestialBody body)
        {
            return body != null && body.bodyName == "Venus" ? 70000 : 0;
        }

        // Lowest altitude where the standard air is no warmer than limitK (0 if it's cool enough at the surface).
        static double TempFloor(CelestialBody body, double top, double limitK)
        {
            if (limitK <= 0 || body.GetTemperature(0) <= limitK) return 0;
            const int steps = 400;
            double step = top / steps, prev = 0;
            for (int i = 1; i <= steps; i++)
            {
                double h = i * step;
                if (body.GetTemperature(h) <= limitK)
                {
                    double lo = prev, hi = h;
                    for (int k = 0; k < 30; k++)
                    {
                        double mid = (lo + hi) / 2;
                        if (body.GetTemperature(mid) > limitK) lo = mid; else hi = mid;
                    }
                    return hi;
                }
                prev = h;
            }
            return top;
        }

        // Altitude where a body's pressure equals p (0 if p is at or above the surface pressure).
        public static double AltitudeAtPressure(CelestialBody body, double p)
        {
            if (p >= body.GetPressure(0)) return 0;
            double lo = 0, hi = body.atmosphereDepth;
            for (int i = 0; i < 50; i++)
            {
                double mid = (lo + hi) / 2;
                if (body.GetPressure(mid) > p) lo = mid; else hi = mid;
            }
            return lo;
        }

        // Balloons on this vessel that are inflating or inflated.
        public static List<ModuleRealBalloon> InflatedBalloons(Vessel vessel)
        {
            var list = new List<ModuleRealBalloon>();
            if (vessel == null) return list;
            foreach (Part p in vessel.parts)
            {
                ModuleRealBalloon b = p.FindModuleImplementing<ModuleRealBalloon>();
                if (b != null && b.Lifting) list.Add(b);
            }
            return list;
        }
    }
}
