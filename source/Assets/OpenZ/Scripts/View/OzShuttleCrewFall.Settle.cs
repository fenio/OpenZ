using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle's crew bodies: every landing heard, and none lying on air (rule DV118, view only; K256k).
    /// <list type="bullet">
    /// <item>A body whose landing is marked without a contact of its own (it settled on the ground, or came to rest) gets its metal thud
    /// then (<see cref="LandThuds"/>): every landing is heard.</item>
    /// <item>A body that landed on something up high and later slides or tumbles off it and drops at least <see cref="DropThudFromM"/> m
    /// to the ground gets one more thud there (<see cref="DropThuds"/>).</item>
    /// <item>A curved hull panel's collider is its convex hull: over the panel's hollow it spans the open side like a lid. A body coming
    /// to rest only on such lids, with the drawn metal (or the ground) more than <see cref="HollowFromM"/> m under every bone, settles down
    /// onto that metal before it freezes (<see cref="HollowSettles"/>), so it never lies in the air over a panel's hollow.</item>
    /// <item>The fall measured from the top of its flight (<see cref="FellFromTopM"/>) and the height of where it landed
    /// (<see cref="LandedOverM"/>), for the proofs.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleCrewFall
    {
        public static bool LandSounds = true, DropThud = true, HollowSettle = true;
        public static float DropThudFromM = 1.0f, HollowFromM = 0.15f, HollowMaxM = 2.5f;
        /// <summary>Proof: thuds asked for landings without a contact, thuds of drops after a high landing, bodies settled out of a
        /// panel's hollow and the largest settle (m).</summary>
        public static int LandThuds, DropThuds, HollowSettles; public static float HollowLargest;

        float topY = float.MinValue, landY = float.NaN, fellTop = -1f; bool dropDone;
        /// <summary>The fall (m) from the top of its flight to where it landed (its hips); the plain fall when not tracked.</summary>
        public float FellFromTopM => fellTop >= 0f ? fellTop : FellM;
        /// <summary>Its hips' height over the ground or water when it landed (m; -1 before).</summary>
        public float LandedOverM { get; private set; } = -1f;

        /// <summary>One of the ragdoll's own bones (never a figure's root collider or a trigger): a collider on a rigidbody, or, once the
        /// body is frozen, a switched-off capsule with its own rigidbody (a switched-off collider is attached to no rigidbody).</summary>
        public static bool IsBone(Collider c) => c && !c.isTrigger && (c.attachedRigidbody || (!c.enabled && c is CapsuleCollider && c.GetComponent<Rigidbody>()));

        /// <summary>A bone's world box: its collider's bounds while it is on; from its capsule (centre, radius, height, axis, the bone's scale)
        /// once the body is frozen and the collider is off.</summary>
        public static Bounds BoneBox(Collider c)
        {
            if (c.enabled && c.gameObject.activeInHierarchy) return c.bounds;
            var t = c.transform; var ls = t.lossyScale; ls = new Vector3(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
            if (c is CapsuleCollider cc)
            {
                var ax = cc.direction == 0 ? Vector3.right : cc.direction == 1 ? Vector3.up : Vector3.forward;
                float sa = cc.direction == 0 ? ls.x : cc.direction == 1 ? ls.y : ls.z, so = cc.direction == 0 ? Mathf.Max(ls.y, ls.z) : cc.direction == 1 ? Mathf.Max(ls.x, ls.z) : Mathf.Max(ls.x, ls.y);
                float r = cc.radius * so, half = Mathf.Max(0f, cc.height * 0.5f * sa - r);
                var mid = t.TransformPoint(cc.center); var w = t.TransformDirection(ax).normalized * half;
                var b = new Bounds(mid + w, Vector3.one * (2f * r)); b.Encapsulate(new Bounds(mid - w, Vector3.one * (2f * r)));
                return b;
            }
            if (c is SphereCollider sc) return new Bounds(t.TransformPoint(sc.center), Vector3.one * (2f * sc.radius * Mathf.Max(ls.x, Mathf.Max(ls.y, ls.z))));
            if (c is BoxCollider bc) return new Bounds(t.TransformPoint(bc.center), Vector3.Scale(bc.size, ls));
            return new Bounds(t.position, Vector3.one * 0.2f);
        }

        /// <summary>A hull panel holds the bone up only where its drawn metal lies under the bone (within the measure's 0.35 m under the
        /// bone's bottom); any other piece (closed: a dome, a pod, a leg, the ramp) holds it where its collider does.</summary>
        public static bool DrawnUnder(OzShuttleBreakup piece, Bounds bb)
        {
            if (!piece || piece.Kind != "body") return true;
            float reach = bb.extents.y + 0.35f + 0.05f;
            for (int k = 0; k < 5; k++)
            {
                var o = bb.center + (k == 0 ? Vector3.zero : new Vector3(((k & 1) == 1 ? 0.5f : -0.5f) * bb.extents.x, 0f, ((k & 2) == 2 ? 0.5f : -0.5f) * bb.extents.z));
                if (piece.DrawnDistance(o, Vector3.down, reach) <= reach) return true;
            }
            return false;
        }

        /// <summary>Play check: is the body held up where it lies (a bone near the ground; or under a bone, within 0.35 m of its bottom: the
        /// ground, a piece's drawn metal, still scenery, a landed body)? onLid = held only by a hull panel's collider over its hollow.</summary>
        public static bool Held(OzShuttleCrewFall b, out string by, out bool onLid)
        {
            by = "-"; onLid = false;
            if (!b) return false;
            foreach (var c in b.GetComponentsInChildren<Collider>(true))
            {
                if (!IsBone(c)) continue;
                var bb = BoneBox(c);
                var bottom = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                if (OzShuttleBreakup.OverSurface(bottom) <= 0.3f) { by = "the ground (" + c.name + ")"; return true; }
                foreach (var h in Physics.RaycastAll(bb.center, Vector3.down, bb.extents.y + 0.35f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var o = h.collider; if (!o || o.transform.IsChildOf(b.transform)) continue;
                    if (o.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(o)) { by = "the ground (" + c.name + ")"; return true; }
                    var piece = o.GetComponentInParent<OzShuttleBreakup>();
                    if (piece) { if (!DrawnUnder(piece, bb)) { onLid = true; continue; } by = "the piece " + piece.PieceName + " (" + c.name + ")"; return true; }
                    var other = o.GetComponentInParent<OzShuttleCrewFall>();
                    if (other && other != b && other.Landed) { by = "a landed body (" + c.name + ")"; return true; }
                    if (!o.attachedRigidbody) { by = "scenery " + o.transform.root.name + " (" + c.name + ")"; return true; }
                }
            }
            return false;
        }

        float BonesSpeed()
        {
            float mv = 0f; foreach (var c in cols) { var rb = c ? c.attachedRigidbody : null; if (rb && !rb.isKinematic) mv = Mathf.Max(mv, rb.linearVelocity.magnitude); }
            return mv;
        }

        Vector3 LowPoint()
        {
            var best = BodyPos; float y = float.MaxValue;
            foreach (var c in cols) { if (!c || !c.enabled) continue; var b = c.bounds; if (b.min.y < y) { y = b.min.y; best = new Vector3(b.center.x, b.min.y, b.center.z); } }
            return best;
        }

        /// <summary>The moment Landed turns true (Update, RestCheck): the fall's numbers, and the thud when no contact asked for one.</summary>
        void LandedNow(OzBodyLanding land)
        {
            landY = BodyPos.y; LandedOverM = BodyPos.y - GroundY(BodyPos);
            fellTop = Mathf.Max(FellM, (topY > float.MinValue ? Mathf.Max(topY, startY) : startY) - landY);
            if (!LandSounds || !land || land.landed) return;
            land.landed = true; LandThuds++;
            OzBodySounds.Landed(land, LowPoint(), Mathf.Max(OzBodySounds.ThudFromMps, BonesSpeed()));
        }

        /// <summary>Every frame of a ragdoll body: the top of its flight before it lands; after a high landing, the drop to the ground heard.</summary>
        void DropWatch()
        {
            if (!Landed) { topY = Mathf.Max(topY, BodyPos.y); return; }
            if (!DropThud || dropDone || !rd || rd.Frozen || float.IsNaN(landY)) return;
            if (landY - BodyPos.y < DropThudFromM || LowGap() > 0.25f) return;
            dropDone = true; DropThuds++;
            OzBodySounds.Landed(GetComponent<OzBodyLanding>(), LowPoint(), Mathf.Max(OzBodySounds.ThudFromMps, BonesSpeed()));
        }

        /// <summary>RestCheck, before the body freezes: lying only on panel lids (the drawn metal or the ground more than HollowFromM under
        /// every bone that is held by a panel, and no bone on anything else): it settles onto the metal and freezes there.</summary>
        void SettleInHollow()
        {
            if (!HollowSettle || !rd || rd.Frozen) return;
            float sink = float.MaxValue, freeRoom = float.MaxValue; int onLid = 0;
            foreach (var c in cols)
            {
                if (!c || !c.enabled) continue;
                var bb = c.bounds; var o = bb.center; float reach = bb.extents.y + 0.35f;
                if (o.y - GroundY(o) - bb.extents.y <= 0.3f) return;                                          // this bone is on the ground: nothing to settle
                RaycastHit first = default; bool any = false;
                foreach (var h in Physics.RaycastAll(o, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!h.collider || h.collider.transform.IsChildOf(transform)) continue;
                    if (!any || h.distance < first.distance) { first = h; any = true; }
                }
                if (!any) { freeRoom = Mathf.Min(freeRoom, RoomBelow(bb)); continue; }                          // this bone hangs free: it does not decide, but it never sinks into what is under it
                var piece = first.collider.GetComponentInParent<OzShuttleBreakup>();
                if (!piece || piece.Kind != "body") return;                                                    // held by the ground, scenery, a body or a solid piece: it lies right
                float metal = piece.DrawnDistance(o, Vector3.down, HollowMaxM + reach);
                float floor = o.y - GroundY(o);
                float real = Mathf.Min(metal, floor);
                foreach (var h in Physics.RaycastAll(o, Vector3.down, HollowMaxM + reach, ~0, QueryTriggerInteraction.Ignore))
                {   // something else under the lid (another piece, scenery) before the metal
                    if (!h.collider || h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<OzShuttleBreakup>() == piece) continue;
                    real = Mathf.Min(real, h.distance);
                }
                float extra = real - first.distance;
                if (extra <= HollowFromM) return;                                                              // on the drawn metal (within the skin): it lies right
                onLid++; sink = Mathf.Min(sink, extra);
            }
            if (onLid == 0 || sink == float.MaxValue) return;
            sink = Mathf.Min(Mathf.Min(sink, freeRoom) - 0.03f, HollowMaxM);
            if (sink <= 0f) return;
            transform.position += Vector3.down * sink; Physics.SyncTransforms();
            rd.Freeze();
            HollowSettles++; HollowLargest = Mathf.Max(HollowLargest, sink);
        }

        /// <summary>SettleInHollow: how far a free-hanging bone can go down (m, from its bottom) before it meets the ground, a hull panel's
        /// drawn metal, or anything else under it (another piece, scenery, a body); a panel counts by its metal, not its lid.</summary>
        float RoomBelow(Bounds bb)
        {
            var o = bb.center; float span = HollowMaxM + bb.extents.y + 0.35f;
            float room = o.y - bb.extents.y - GroundY(o);
            foreach (var h in Physics.RaycastAll(o, Vector3.down, span, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!h.collider || h.collider.transform.IsChildOf(transform)) continue;
                var piece = h.collider.GetComponentInParent<OzShuttleBreakup>();
                float d = piece && piece.Kind == "body" ? piece.DrawnDistance(o, Vector3.down, span) : h.distance;
                if (d < float.MaxValue) room = Mathf.Min(room, d - bb.extents.y);
            }
            return Mathf.Max(0f, room);
        }
    }
}
