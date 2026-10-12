using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// fort_defender_v1 (DV41): a fort's roof defender is an ordinary grunt UnitView (BS-01, rifle, UnitAim, WeaponsV2 bolt, recoil,
    /// voice, deaths) drawn on his fort instead of the terrain: feet on the lift plate of his hatch in the centre of the sandbag
    /// nest (FortDefenderView.Pose, the drawn fort model), down the well while he climbs in or out (climb_ladder), no view spacing
    /// (he stands 6 m over the ground traffic), his idle heading outward over the bags. On the roof he never gets a knockdown (a
    /// stagger in the nest instead) and never the pop death: a hit throws him as a ragdoll onto the roof or off it (the roof is a
    /// physics surface, FortDefenderView), and his corpse pops out where it lies. Shot while still half down the well, he drops
    /// back in. View only.
    /// </summary>
    public partial class UnitView
    {
        bool fdMan, fdClimb; float fdSink = 1f;
        /// <summary>This view draws a fort's roof defender (DV41).</summary>
        public bool IsFortDefender => fdMan;
        /// <summary>How far down the well he is (0 standing on the roof .. 1 hidden).</summary>
        public float FortDefenderSink => fdMan ? fdSink : 0f;

        /// <summary>Bind: size, no spacing, idle heading outward, first pose (hidden in the well at the start of the climb).</summary>
        void BindFortDefender()
        {
            if (Obj == null || !Obj.IsFortDefender) return;
            fdMan = true;
            radius = 0f; weight = 0f; sepOffset = Vector3.zero;                                          // no view spacing / building push-out on the roof
            if (!Mathf.Approximately(FortDefenderView.ManScale, 1f)) transform.localScale *= FortDefenderView.ManScale;
            if (FortDefenderView.Pose(game, Obj, out var feet, out float outYaw, out fdSink, out fdClimb))
            {
                yaw = outYaw; heading = yaw;
                var p = feet; p.y += yOffset - FortDefenderView.SinkDepth(transform) * fdSink;
                transform.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw + modelYaw, 0f));
            }
        }

        /// <summary>Sync (robots, instead of the terrain height): the man's feet on his hatch's lift plate.</summary>
        bool PlaceFortDefender(ref Vector3 p)
        {
            if (!fdMan) return false;
            if (!FortDefenderView.Pose(game, Obj, out var feet, out _, out fdSink, out fdClimb)) return false;
            p = feet; p.y += yOffset - FortDefenderView.SinkDepth(transform) * fdSink;
            return true;
        }

        /// <summary>Animation: climbing the ladder while he rises out of the well or sinks into it (the upper-body layer eases out).</summary>
        bool FortDefenderAnim(float dt)
        {
            if (!fdMan || !fdClimb || !anim) return false;
            SetState("climb_ladder", 0.1f); anim.speed = 1.25f * animMul;
            if (anim.layerCount > 1) { upWeight = Mathf.MoveTowards(upWeight, 0f, dt * 5f); anim.SetLayerWeight(1, upWeight); }
            return true;
        }

        /// <summary>Die: shot while still half down the well he drops back in (hidden; the lid closes behind him).</summary>
        bool FortDefenderDropsIn()
        {
            if (!fdMan || fdSink < 0.35f) return false;
            OzDeathTrace.Mode(this, "fort defender: dropped back into the hatch");
            Hide();
            return true;
        }
    }
}
