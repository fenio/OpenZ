using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Drawn shots clear the terrain: a shooter standing in water or low ground (any robot or vehicle) fires along a path that
    /// rises from the muzzle over the bank, and tower guns launch from the muzzle above the fort and arc clear of the fort mesh.
    /// VIEW ONLY: the sim's hits, damage and flight times are untouched; only the drawn path bends.
    /// <list type="bullet">
    /// <item><see cref="Obstacle"/>: the height a drawn shot must clear at a point: the walkable ground (ZGame.GroundY: terrain, banks,
    /// bridge decks) and a live fort's body (FortGunView.FortTopAt: walls, tower pads, the guns on them, the big turret, the flag pole;
    /// the shooter's own pad excluded).</item>
    /// <item><see cref="LiftGravity"/>: ballistic shells / rockets / grenades (OpenZFxPlayer.Launch, OpenZHeavyShells): the arc keeps its
    /// end points and flight time; only its height grows (the visual gravity rises until the arc clears every sample by the margin).</item>
    /// <item><see cref="Bend"/>: straight rounds (bolts, tracers, MG, laser, sniper): a waypoint above the highest obstruction; the round
    /// flies muzzle -> waypoint -> target at its own speed (two straight legs, the second emitted when the first arrives).</item>
    /// <item><see cref="ArcHeight"/>: the pyro's flame stream (OpenZPyroFlamePool): the parabola height over the straight stream.</item>
    /// </list>
    /// The first 0.5 m (the muzzle) and the last metre (the target / impact) are never tested.
    /// </summary>
    public static class ShotClearance
    {
        public static bool Enabled = true;
        /// <summary>Air between a drawn shot and the obstacle (m).</summary>
        public const float Margin = 0.4f;
        /// <summary>Highest lift over the straight line (m): a shot never climbs higher than this to clear a bank.</summary>
        public const float MaxLift = 9f;
        const int N = 14;
        /// <summary>Stats (Play proof): arcs raised, rounds bent, flame streams arced.</summary>
        public static int ArcsRaised, RoundsBent, FlamesArced;
        static readonly float[] gaps = new float[N + 1];

        /// <summary>World height a drawn shot from `muzzle` must clear at p (ground or fort body).</summary>
        public static float Obstacle(Vector3 p, Vector3 muzzle)
        {
            var g = ZGame.I;
            float y = g ? g.GroundY(p.x, p.z) : 0f;
            float f = FortGunView.FortTopAt(p, muzzle);
            return f > y ? f : y;
        }

        /// <summary>Fills gaps[1..N-1] (how far the straight line from -> to runs under the obstacle + margin at s = i / N); returns the
        /// largest gap and its index (0 = clear).</summary>
        static float Gaps(Vector3 from, Vector3 to, float margin, out int worst)
        {
            worst = 0; float best = 0f;
            var d = to - from; float h = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            for (int i = 1; i < N; i++)
            {
                gaps[i] = 0f;
                float s = i / (float)N, along = s * h;
                if (along < 0.5f || h - along < 1.0f) continue;
                var c = from + d * s;
                float gap = Obstacle(c, from) + margin - c.y;
                if (gap <= 0f) continue;
                gaps[i] = gap;
                if (gap > best) { best = gap; worst = i; }
            }
            return best;
        }

        /// <summary>The visual gravity (>= g) of the arc from -> to over T s that clears the ground and the forts; the apex over the straight
        /// line stays under MaxLift. The arc's height over the chord at s is g T^2 s (1 - s) / 2.</summary>
        public static float LiftGravity(Vector3 from, Vector3 to, float T, float g, float margin = Margin)
        {
            if (!Enabled || T <= 0.02f || !ZGame.I) return g;
            var d = to - from; if (d.x * d.x + d.z * d.z < 2.25f) return g;
            if (Gaps(from, to, margin, out _) <= 0.02f) return g;
            float need = g;
            for (int i = 1; i < N; i++)
            {
                if (gaps[i] <= 0f) continue;
                float s = i / (float)N;
                float gs = 2f * gaps[i] / (T * T * s * (1f - s));
                if (gs > need) need = gs;
            }
            float cap = 8f * MaxLift / (T * T);
            if (need <= g + 0.01f) return g;
            ArcsRaised++;
            return Mathf.Min(need, Mathf.Max(g, cap));
        }

        /// <summary>A straight round from -> to that would run through the ground / a fort: the waypoint w above the worst obstruction
        /// (both legs clear every sample). False = the straight line is clear.</summary>
        public static bool Bend(Vector3 from, Vector3 to, out Vector3 w, float margin = 0.3f)
        {
            w = default;
            if (!Enabled || !ZGame.I) return false;
            var d = to - from; if (d.x * d.x + d.z * d.z < 4f) return false;
            float best = Gaps(from, to, margin, out int wi);
            if (best <= 0.02f || wi <= 0) return false;
            float sb = wi / (float)N, L = best;
            for (int i = 1; i < N; i++)
            {
                if (gaps[i] <= 0f) continue;
                float s = i / (float)N, tri = s <= sb ? s / sb : (1f - s) / (1f - sb);
                if (tri > 0.05f) L = Mathf.Max(L, gaps[i] / tri);
            }
            L = Mathf.Min(L, MaxLift);
            w = from + d * sb + Vector3.up * L;
            RoundsBent++;
            return true;
        }

        /// <summary>The flame stream's parabola height h (the stream at s rises 4 h s (1 - s) over the straight line) that clears the bank;
        /// 0 = the straight stream is clear.</summary>
        public static float ArcHeight(Vector3 from, Vector3 to, float margin = 0.55f)
        {
            if (!Enabled || !ZGame.I) return 0f;
            var d = to - from; if (d.x * d.x + d.z * d.z < 2.25f) return 0f;
            if (Gaps(from, to, margin, out _) <= 0.02f) return 0f;
            float h = 0f;
            for (int i = 1; i < N; i++)
            {
                if (gaps[i] <= 0f) continue;
                float s = i / (float)N;
                h = Mathf.Max(h, gaps[i] / (4f * s * (1f - s)));
            }
            if (h > 0f) FlamesArced++;
            return Mathf.Min(h, MaxLift * 0.6f);
        }
    }
}
