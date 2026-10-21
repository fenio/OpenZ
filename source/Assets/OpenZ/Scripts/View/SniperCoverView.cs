using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV122 (Sim/ZObject.ProneTarget.cs): the sniper's stance is a rule now and the view only draws it, so what is seen is what is hit.
    /// <list type="bullet">
    /// <item>BS01Stance (View/BS01Stance.Rules.cs) plays the kneel / prone clips for ZObject.Stance;</item>
    /// <item>RobotWeaponPose's own pose route (a robot without BS01Stance) poses <see cref="Drawn"/> instead of its own pick;</item>
    /// <item>OpenZElectroFx: a zap that missed a kneeling / lying sniper (an ElectroBolt event with no target) strikes the ground at
    /// the event's point (<see cref="ZapMissAt"/>), not the sniper.</item>
    /// </list>
    /// With the rule off (the switch, 'Zod exact') every one of them is as before.
    /// </summary>
    public static class SniperCoverView
    {
        /// <summary>Drawn zaps that missed (Play report).</summary>
        public static int ZapMissesDrawn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Statics() { ZapMissesDrawn = 0; }

        /// <summary>The rules decide this robot's stance (a sniper on foot, DV122 on).</summary>
        public static bool RulesDraw(ZObject o) => o != null && o.ProneTargetRule;

        /// <summary>RobotWeaponPose's pose route: the stance to pose (the rules' one while they decide it, else its own).</summary>
        public static WeaponsV2Logic.Stance Drawn(ZObject o, WeaponsV2Logic.Stance own)
        {
            if (!RulesDraw(o)) return own;
            return o.Stance == ZStance.Prone ? WeaponsV2Logic.Stance.Prone : o.Stance == ZStance.Kneel ? WeaponsV2Logic.Stance.Kneel : WeaponsV2Logic.Stance.Stand;
        }

        /// <summary>OpenZElectroFx.Bolt: a bolt with no target is a zap that missed a kneeling / lying sniper: its end is the ground at the
        /// event's point (px). True = it was one (end set).</summary>
        public static bool ZapMissAt(ZEvent e, ref Vector3 end)
        {
            if (e.type != ZEventType.ElectroBolt || e.otherId >= 0) return false;
            var p = ZGame.PxToWorld(e.x, e.y);
            p.y = OpenZElectroFx.Ground(p.x, p.z) + 0.05f;
            end = p; ZapMissesDrawn++;
            return true;
        }
    }
}
