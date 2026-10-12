using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// View only: CombatView calls <see cref="OnBlast"/> for every MissileExploded after the
    /// IM1 surface point is known. A heavy shot (tough rocket, tank / field gun / howitzer shell, missile cannon / launcher) that bursts
    /// on a vehicle or cannon knocks the nearest small real part off the model (RobotPopDeath.KnockOff: the part vanishes and a copy is
    /// flung away from the blast, smoke trail, metal tear at the hit point, clang when it lands; at most 3 per unit, 3 in 5 hits) and
    /// always throws a few gunmetal / team-colour chips; on a building it throws concrete chunks with a crumble. Grenades, debris
    /// missiles and light guns never do. Nothing here touches the sim.
    /// </summary>
    public static class HeavyChips
    {
        public static bool Enabled = true;
        /// <summary>Share of heavy hits on a vehicle / cannon that tear a real part off (the rest only chip).</summary>
        public static float PartChance = 0.6f;
        static readonly HashSet<string> HeavyShooters = new HashSet<string> { "tough", "light", "medium", "heavy", "howitzer", "gun", "missile_cannon", "missile_launcher" };
        public static int Hits { get; private set; }
        static uint n;

        public static void OnBlast(ZGame game, ZEvent e, ZObject a, ZObject b, Vector3 at)
        {
            if (!Enabled || !game || b == null || a == null || e.flag || e.flag2 || !HeavyShooters.Contains(a.Type)) return;
            bool veh = b.Kind == ZKind.Vehicle || b.Kind == ZKind.Cannon, bld = b.Kind == ZKind.Building && !b.IsBridge;
            if (!veh && !bld) return;
            Transform root = null;
            if (game.Views.TryGetValue(b.Id, out var v) && v && v.gameObject.activeInHierarchy) root = v.transform;
            else if (game.StaticViews.TryGetValue(b.Id, out var sgo) && sgo && sgo.activeInHierarchy) root = sgo.transform;
            if (!root) return;
            // the burst must be ON the body (IM1 moved hits inside the footprint onto the wall; a near miss on the ground chips nothing)
            bool any = false; var bb = new Bounds(root.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled) continue;
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
            }
            if (!any || bb.SqrDistance(at) > 1.2f * 1.2f) return;
            Hits++;
            uint seed = RobotDeathRule.Hash(b.Id, (int)(++n), 31u);
            var from = a != null && game.Views.TryGetValue(a.Id, out var av) && av ? av.transform.position : at - root.forward;
            var away = at - from; away.y = 0f;
            if (veh)
            {
                bool part = (seed & 0xFFFF) / 65536f < PartChance && RobotPopDeath.KnockOff(root, b.Id, at, from, seed);
                var col = Color.Lerp(TeamColors.Of(b.Owner), new Color(0.45f, 0.45f, 0.45f), 0.3f);
                RobotPopDeath.Chips(at, away, col, part ? 2 : 3 + (int)(seed >> 20) % 2, 1f, seed ^ 0x9E37u, false);
                if (!part) AudioDirector.Fire("dst_debris_metal", at, b.Id, 0.6f, 1.15f);                   // the chips tear off with a lighter clank
            }
            else
            {
                RobotPopDeath.Chips(at, away, new Color(0.62f, 0.58f, 0.52f), 3 + (int)(seed >> 20) % 3, 1.2f, seed, true);
                AudioDirector.Fire("dst_debris_concrete", at, b.Id, 0.8f);
                var fxp = OpenZFxPlayer.I;
                if (fxp && fxp.isActiveAndEnabled)
                    for (int i = 0; i < 4; i++)
                        fxp.EmitChannel("m12_dust", at, (away.normalized + Random.insideUnitSphere * 0.6f) * 1.4f + Vector3.up * 0.6f, Random.Range(0.9f, 1.3f), Random.Range(0.8f, 1.2f), new Color(0.62f, 0.57f, 0.5f), 0.55f);
            }
        }
    }
}
