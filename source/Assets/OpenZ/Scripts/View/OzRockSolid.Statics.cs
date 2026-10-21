using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, items 331 + 332 for the guns and the buildings (with OzStaticSolid.cs): a tossed turret whose landing lies in a gun or a
    /// building and that finds no free place within <see cref="TurretMaxMoveFar"/> m (a turret thrown off a fort's tower that would come
    /// down inside the fort, a gun between factories) looks a little farther out, up to <see cref="TurretStaticFarM"/> m, first for the
    /// whole turret, then for its core (as the rock search does), so it lies beside the building instead of inside it.
    /// </summary>
    public sealed partial class OzRockSolid
    {
        /// <summary>The farthest a turret moves (m) when a gun or a building is in its way and nothing nearer is free.</summary>
        public static float TurretStaticFarM = 16f;
        /// <summary>Turret landings moved this farther search (a gun or a building in the way).</summary>
        public static int TurretStaticFar;

        /// <summary>TurretLandingCore, before a landing is kept: the farther search (only when guns and buildings are solid). True = moved.</summary>
        static bool TurretLandingFar(Vector3 from, ref Vector3 to, ref float apex, float r, Vector3 dir, Vector3 drawn, string why)
        {
            if (!OzStaticSolid.On) return false;
            var g = ZGame.I; if (!g || g.World == null) return false;
            for (int k = 0; k <= 2; k++)
            {
                float r2 = Mathf.Max(0.5f, r * (1f - 0.25f * k));
                for (float d = TurretMaxMoveFar + 1f; d <= TurretStaticFarM + 1e-3f; d += 1f)
                    for (int s = 0; s < 12; s++)
                    {
                        int turn = (s + 1) / 2 * ((s & 1) == 0 ? 1 : -1);
                        var q = drawn + Quaternion.Euler(0f, turn * 30f, 0f) * -dir * d;
                        q.y = g.GroundY(q.x, q.z) + 0.35f;
                        if (!PlaceFree(g.World, q, r2, 0.3f)) continue;
                        var f2 = q - from; f2.y = 0f; var dir2 = f2.sqrMagnitude > 0.01f ? f2.normalized : dir;
                        float a2 = apex;
                        string w2 = TurretBlocked(from, q, a2, r2, dir2, out bool lc2);
                        if (!lc2) continue;
                        if (w2 != null) RaiseArc(from, q, r2, dir2, ref a2);
                        to = q; if (a2 > apex) TurretRaised++; apex = a2; TurretMoved++; TurretStaticFar++;
                        LastTurret = $"moved {d:0} m{(k > 0 ? ", its core only" : "")}, farther out past a gun or a building ({why})";
                        return true;
                    }
            }
            return false;
        }
    }
}
