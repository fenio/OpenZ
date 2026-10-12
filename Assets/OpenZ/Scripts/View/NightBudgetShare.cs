using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// vehicle_lights package: real lights owned by other view systems (VehicleLights: the headlight spots of the vehicles
    /// nearest the camera) compete for the SAME real-light budget as the night package's lamps, floods and searchlights.
    /// NightLightBudget.Tick merges this list into the one it ranks (a one-line patch: <c>all = NightBudgetShare.Merge(all);</c>),
    /// so the total number of real URP lights, the heat levels (74 / 77 C) and the frame-time and combat-FX scaling stay
    /// exactly the night budget's. The owner of an external fixture sets LightOut / Intensity / Range / Priority, reads
    /// Budgeted / RealK back and drives its own Light (the budget never touches the Light itself).
    /// </summary>
    public static class NightBudgetShare
    {
        /// <summary>External fixtures (VehicleLights adds / removes them).</summary>
        public static readonly List<NightFixture> Fixtures = new List<NightFixture>(64);
        /// <summary>Frame of the last merge done by the patched NightLightBudget.Tick (-1 = never): VehicleLights runs its
        /// own fallback budget when this is stale (night package absent, or re-imported without the hook).</summary>
        public static int HookFrame { get; private set; } = -1;

        static readonly List<NightFixture> merged = new List<NightFixture>(512);

        /// <summary>The list NightLightBudget.Tick ranks: its own fixtures + the external ones (no allocation).</summary>
        public static List<NightFixture> Merge(List<NightFixture> own)
        {
            if (ReferenceEquals(own, Fixtures)) return own;            // a fallback budget ranking only the external list
            HookFrame = Time.frameCount;
            if (Fixtures.Count == 0 || own == null) return own;
            merged.Clear();
            merged.AddRange(own);
            merged.AddRange(Fixtures);
            return merged;
        }
    }
}
