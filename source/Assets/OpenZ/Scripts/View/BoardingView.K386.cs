using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1 item 386.
    /// View only. Crew figures (the jeep driver, the hatch commanders of the light / medium / heavy tank, the
    /// MLRS, the APC's lone driver, the crane's cab driver) are NOT children of their vehicle: BoardingView poses them in world space
    /// every frame on the seat / hatch anchors, which ARE children of the hull. A vehicle born inside a factory or a fort is drawn
    /// smaller and grows back while it rolls out (UnitView.ExitGrow: ExitScaleNow 0.62 .. 1); the anchors shrank with the hull, the
    /// figure kept its full size, so the driver stuck out of the jeep (head and shoulders too big and too high, hands off the wheel)
    /// until the hull was full size again.
    /// <list type="bullet">
    /// <item>Size: every frame, before anything is posed, each rig's crewScale = its full value x its hull's grow share
    /// (<see cref="HullGrow"/>). The seat / hatch anchors and the figure now shrink about the same hull, so every point of the figure
    /// sits where it sits at full size, scaled with the hull, in every frame of the roll-out. A vehicle that is not growing has the share
    /// 1 (the old sizes, unchanged); guns never grow (share 1).</item>
    /// <item>Place: FactoryViews (execution order 70, the same as this component's) pushes a new unit's hull back into the hall in its
    /// LateUpdate (FactoryView.StageExits). When that ran after the crew was posed, the crew stood at the hull's sim point, metres ahead
    /// of the drawn hull. FactoryView.Place now reports each push (<see cref="FollowHull"/>): a crew already posed this frame moves with
    /// its hull; a crew not posed yet is posed later from the pushed hull anyway. Children of the hull (APC / shuttle benches) follow
    /// by themselves and are left alone.</item>
    /// </list>
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>K386 on (true): the crew grows with its hull and follows the factory's push. False only for the check's BEFORE
        /// pictures (the old full-size crew, not moved with the push).</summary>
        public static bool K386On = true;
        /// <summary>K386 counters: hull pushes followed / the largest push (m) a posed crew followed; frames a crew was drawn smaller
        /// than full because its hull was growing; the smallest share used.</summary>
        public static int K386Follows, K386ScaledFrames; public static float K386FollowMaxM, K386MinShare = 1f;

        readonly Dictionary<Rig, float> k386Full = new Dictionary<Rig, float>();

        /// <summary>The hull's drawn size against its full size: UnitView.ExitScaleNow while a new vehicle rolls out of the building that
        /// made it (factory, fort; the repair bay and a drop inside a footprint take the same path), else 1.</summary>
        static float HullGrow(Rig r)
        {
            if (r == null || !r.view || r.obj == null || r.obj.Kind != ZKind.Vehicle) return 1f;
            return Mathf.Clamp(r.view.ExitScaleNow, 0.05f, 1f);
        }

        /// <summary>Hook (LateUpdate, the first loop, before the boarding ghosts and StepRig use crewScale): crewScale = full x grow.</summary>
        void K386Grow(Rig r)
        {
            if (r == null) return;
            if (!k386Full.TryGetValue(r, out float full))
            {
                full = r.crewScale; k386Full[r] = full;                                                    // the type's value (x a gun's ScaleRatio), set once in RigOf
                if (k386Full.Count > rigs.Count + 64) K386Purge();
            }
            float share = K386On ? HullGrow(r) : 1f;
            r.crewScale = full * share;
            if (share < 0.999f && r.crew) { K386ScaledFrames++; K386MinShare = Mathf.Min(K386MinShare, share); }
        }

        void K386Purge()
        {
            var live = new HashSet<Rig>(rigs.Values); var gone = new List<Rig>();
            foreach (var k in k386Full.Keys) if (!live.Contains(k)) gone.Add(k);
            foreach (var k in gone) k386Full.Remove(k);
        }

        /// <summary>Hook (FactoryView.Place, after it moved a unit's hull by `delta`): a crew this component already posed this frame moves
        /// with the hull (a crew not posed yet is posed from the moved hull later this frame).</summary>
        public static void FollowHull(UnitView v, Vector3 delta)
        {
            if (!K386On || !I || !v || delta.sqrMagnitude < 1e-10f || !I.rigs.TryGetValue(v, out var r)) return;
            if (r.seenFrame != Time.frameCount) return;                                                    // not posed yet this frame: nothing to move
            bool moved = false;
            if (r.crew && !r.crew.transform.IsChildOf(v.transform)) { r.crew.transform.position += delta; moved = true; }
            foreach (var g in I.ghosts)
                if (g.rig == r && !g.done && g.go && !g.go.transform.IsChildOf(v.transform)) { g.go.transform.position += delta; moved = true; }
            if (!moved) return;
            K386Follows++; K386FollowMaxM = Mathf.Max(K386FollowMaxM, delta.magnitude);
        }

        /// <summary>K386 check: one crew sample of vehicle `v` (read at the end of a frame, after every script posed it).</summary>
        public struct CrewSample
        {
            public bool has, shown, seated;
            /// <summary>The anchor (seat: where the hips belong; hatch: the root the commander's pose asks for now) and the drawn point
            /// (the hips bone / the figure's root), world.</summary>
            public Vector3 anchor, drawn;
            /// <summary>The figure's world size, the size its hull asks for now (crewBase x full crewScale x fit x grow: the right size,
            /// whatever <see cref="K386On"/> says), the hull's grow share, the figure's height at full size (m), the full crewScale.</summary>
            public float size, wantSize, grow, heightFull, fullScale;
            /// <summary>The figure's hips and head top in the hull's own space (constant while the hull grows when the crew grows with it).</summary>
            public Vector3 hipsInHull, headInHull;
            public string type;
        }

        public static bool K386Sample(UnitView v, out CrewSample s)
        {
            s = default;
            if (!I || !v || !I.rigs.TryGetValue(v, out var r) || !r.crew) return false;
            var t = r.crew.transform;
            s.has = true; s.shown = r.crew.activeInHierarchy; s.type = r.obj != null ? r.obj.Type : "?";
            s.grow = HullGrow(r);
            s.fullScale = I.k386Full.TryGetValue(r, out float full) ? full : r.crewScale;
            float fit = CrewFit(r, r.crew);
            s.wantSize = r.crewBase * s.fullScale * fit * s.grow;
            s.size = Mathf.Abs(t.lossyScale.x);
            float h = r.cmdTop > 0f ? r.cmdTop : 2.9f;                                                      // helmet top above the root, per unit size
            s.heightFull = h * r.crewBase * s.fullScale * fit;
            var up = r.root.up;
            s.seated = r.visible;
            if (r.visible)
            {
                I.SeatPose(r, out var p, out _, out _);
                s.anchor = p;
                s.drawn = r.crewHips ? r.crewHips.position : t.position + up * (I.pelvisHeight * s.size);
            }
            else
            {   // the hatch commander: the root PlaceCommander asks for now at the right size (no kick / flinch: none while it rolls out)
                float sz = s.wantSize;
                var top = I.Wp(r, I.HatchTopL(r));
                if (r.round) top += up * Vector3.Dot(r.round.RimWorld - top, up);
                float cut = r.cmdHips + I.commanderCut * (r.cmdShoulder - r.cmdHips);
                var shown = top + up * (0.03f * sz) - up * (cut * sz);
                var hidden = shown - up * ((r.cmdTop - cut + 0.35f) * sz);
                s.anchor = Vector3.Lerp(hidden, shown, Mathf.SmoothStep(0, 1, r.crewPop));
                s.drawn = t.position;
            }
            var hips = r.crewHips ? r.crewHips.position : t.position;
            s.hipsInHull = r.root.InverseTransformPoint(hips);
            s.headInHull = r.root.InverseTransformPoint(t.position + up * (h * s.size));
            return true;
        }
    }
}
