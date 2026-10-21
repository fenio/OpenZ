using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shuttle's two belly searchlights as its LANDING LIGHTS (rule DV118, view only; the rest of the night lights:
    /// OzShuttleNightLights). The ship's night lights hand them their rule (when they burn: OzShuttleNightLights.LandingWant, called in
    /// their Step) and turn their heads onto the landing point; on LOW the second lamp is not offered to the night budget (its lens and
    /// painted pool carry it). Their Kill and Dispose take the other night lights along.
    /// </summary>
    public sealed partial class OzShuttleSearchlights
    {
        /// <summary>The ship's other night lights and the landing lights' rule (set at the view's bind; null = the lamps' own rule).</summary>
        public OzShuttleNightLights Night;

        /// <summary>The landing lamps' heads onto a point on the ground (each one <paramref name="spread"/> m to its own side of it,
        /// lamp 0 to +<paramref name="side"/>), eased at <paramref name="rate"/>, never more than <paramref name="maxDeg"/> off its
        /// housing's direction; on = false: back to the housing's direction.</summary>
        internal void AimLanding(bool on, Vector3 at, Vector3 side, float spread, float maxDeg, float rate, float dt)
        {
            if (dead || dt <= 0f) return;
            float a = 1f - Mathf.Exp(-rate * dt);
            for (int i = 0; i < lamps.Length; i++)
            {
                var L = lamps[i]; if (L == null || !L.head) continue;
                var want = L.aimL;
                if (on)
                {
                    var d = root.InverseTransformDirection(at + side * (i == 0 ? spread : -spread) - L.head.position);
                    if (d.sqrMagnitude > 1e-4f)
                    {
                        d.Normalize();
                        float ang = Vector3.Angle(L.aimL, d);
                        want = ang > maxDeg ? Vector3.Slerp(L.aimL, d, maxDeg / ang) : d;
                    }
                }
                var q = Quaternion.LookRotation(want, Mathf.Abs(want.y) > 0.95f ? Vector3.forward : Vector3.up);
                L.head.localRotation = Quaternion.Slerp(L.head.localRotation, q, a);
            }
        }

        /// <summary>How far lamp i looks off its point beside the landing point (degrees).</summary>
        internal float AimOffDeg(int i, Vector3 at, Vector3 side, float spread)
        {
            if (i < 0 || i >= lamps.Length || lamps[i] == null || !lamps[i].head) return 0f;
            var L = lamps[i]; var to = at + side * (i == 0 ? spread : -spread) - L.head.position;
            return to.sqrMagnitude > 1e-4f ? Vector3.Angle(L.head.forward, to) : 0f;
        }

        /// <summary>Lamp i offered to the night budget as a real light (false: taken out of it at once; its lens and painted pool stay).</summary>
        internal void Offer(int i, bool on)
        {
            if (dead || i < 0 || i >= lamps.Length || lamps[i] == null || lamps[i].fix == null) return;
            var f = lamps[i].fix; bool has = NightBudgetShare.Fixtures.Contains(f);
            if (on && !has) NightBudgetShare.Fixtures.Add(f);
            else if (!on && has) { NightBudgetShare.Fixtures.Remove(f); f.RealK = 0f; f.ShadowK = 0f; f.Budgeted = false; }
        }

        /// <summary>The two lamps never take one of the NEARBY LIGHTS shadows (they draw none): their budget ids begin "vehicle_" as the
        /// headlights' do, so such a shadow goes to a lamp that casts it.</summary>
        internal void KeepOffNearbyShadows()
        {
            foreach (var L in lamps) if (L != null && L.fix != null && L.fix.Id != null && !L.fix.Id.StartsWith("vehicle_")) L.fix.Id = "vehicle_" + L.fix.Id;
        }

        /// <summary>K256e: the lamps' switch click goes through the ship's night lights (one click per change of the lights, at least 0.12
        /// loud); without them the game's searchlight switch as before, at the same loudness.</summary>
        void SwitchClick(bool on)
        {
            var at = root.TransformPoint(lamps[0].posL);
            if (Night != null && !Night.Dead) { Night.SwitchClick(at, on, 1f); return; }
            if (AudioDirector.Has("ngt_searchlight_on")) AudioDirector.Fire("ngt_searchlight_on", at, -1, on ? OzShuttleNightLights.ClickOnVol : OzShuttleNightLights.ClickOffVol, on ? 1f : 0.85f);
        }

        internal bool OfferedNow(int i) => i >= 0 && i < lamps.Length && lamps[i] != null && lamps[i].fix != null && NightBudgetShare.Fixtures.Contains(lamps[i].fix);
        /// <summary>Lamp i's head (world) and the way it looks.</summary>
        public Transform LampHead(int i) => i >= 0 && i < lamps.Length && lamps[i] != null ? lamps[i].head : null;
        /// <summary>Lamp i's real light is lit now / its painted pool is drawn now.</summary>
        public bool LampReal(int i) => i >= 0 && i < lamps.Length && lamps[i] != null && lamps[i].light && lamps[i].light.enabled;
        public bool LampPool(int i) => i >= 0 && i < lamps.Length && lamps[i] != null && lamps[i].pool && lamps[i].pool.enabled;
    }
}
