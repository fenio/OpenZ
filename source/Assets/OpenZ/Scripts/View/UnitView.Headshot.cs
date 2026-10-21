using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// DV116 sniper headshot, the view (the rule: Sim/ZObject.Headshot.cs). A robot a sniper's headshot destroyed neither pops nor flies:
    /// <list type="bullet">
    /// <item>its head comes off and falls with physics like the robots' tossed rocket-fuel can (AnticCan: it tumbles, clatters on every
    /// real bounce, splashes and sinks in water, and pops away 15 s after it landed);</item>
    /// <item>the body drops where it stood as a ragdoll with only a small push away from the shooter (no death fling, no flailing limbs);
    /// with every ragdoll slot taken it plays its fall-back death clip;</item>
    /// <item>its own head pop (K248b, UnitView.HeadPop.cs); the usual death sound stays (CombatView.Death); the killing round ends at the head
    /// (<see cref="HeadshotShotEnd"/>, CombatView.DrawShot);</item>
    /// <item>the player's sniper calls it now and then (pool <see cref="HeadshotPool"/>): only while the head is on camera, at most one
    /// call in <see cref="CallEvery"/> such headshots (the first one may call), never twice within <see cref="CallGap"/> s.</item>
    /// </list>
    /// The head piece is the robot type's own baked head part (Resources/OpenZ/RobotParts: the pop death's and the crocodile's helmet);
    /// a rig without baked parts (the hero squads' own models) gets a copy of the body's skin in which only the head's bones move. The
    /// body's own head is hidden by scaling its neck bone to nothing (its ragdoll head body loses its collider).
    /// </summary>
    public partial class UnitView
    {
        public const string HeadshotPool = "vo_headshot_sniper";
        /// <summary>The body's push (x the robot's size factor, RagdollStrength's) and its lift; the head's throw (m/s): away from the shooter, up.</summary>
        public static float HeadshotPush = 0.22f, HeadshotLift = 0.05f, HeadAway = 2.4f, HeadUp = 3.6f;
        /// <summary>The call: at most one in CallEvery of the player's on-camera headshots, never twice within CallGap s.</summary>
        public static int CallEvery = 3; public static float CallGap = 20f;
        /// <summary>Proof counters (Play checks): headshot deaths drawn, heads thrown, heads made from the baked part / from the skin copy,
        /// calls made / skipped by the one-in-three or the 20 s rule.</summary>
        public static int HeadshotDeaths, HeadsThrown, HeadsBaked, HeadsSkinned, HeadCalls, HeadCallsSkipped;
        static float lastHeadCall = -999f; static int sinceHeadCall = 1 << 20;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HeadshotStatics() { HeadshotDeaths = HeadsThrown = HeadsBaked = HeadsSkinned = HeadCalls = HeadCallsSkipped = 0; lastHeadCall = -999f; sinceHeadCall = 1 << 20; }

        /// <summary>Die() hook: true = this robot's death is a headshot and is drawn here (Die() then only takes it off the live list).</summary>
        bool HeadshotDeath(Vector3 from, int killerId)
        {
            if (Obj == null || Obj.Kind != ZKind.Robot || Obj.HeadshotBy < 0) return false;
            HeadshotDeaths++;
            var body = BodySkin();
            var head = HeadBone(body);
            var cut = NeckOf(head);
            float rad = RobotV3.RagdollRadius(gameObject, "Head", 0.2f) * Mathf.Abs(transform.lossyScale.y);
            var hp = head ? HeadCentre(head, rad) : transform.position + Vector3.up * RobotHeight() * 0.9f;
            var away = hp - from; away.y = 0f; if (away.sqrMagnitude < 1e-4f) away = -transform.forward; away.Normalize();
            var piece = head ? HeadPiece(body, head, cut, hp) : null;                                   // made from the pose of this frame
            uint seed = RobotDeathRule.Hash(Obj.Id, killerId, 11u);
            if (RobotRagdoll.CanStart() && (ragdoll = RobotRagdoll.Launch(gameObject, away, HeadshotPush * Mathf.Sqrt(RobotHeight() / 1.8f), HeadshotLift, seed)))
                death = Death.Ragdoll;                                                                  // a plain collapse: no RagdollFlight, no RagdollFlail
            else { death = Death.Anim; if (anim) { anim.speed = 1; anim.CrossFadeInFixedTime("death_back", 0.1f); } }
            if (cut)
            {   // the body's own head is gone (its ragdoll head body keeps the joint, without a collider)
                foreach (var c in head.GetComponents<Collider>()) c.enabled = false;
                cut.localScale = Vector3.one * 0.001f;
            }
            if (piece)
            {
                float u = (seed & 1023u) / 1023f;
                var v = Quaternion.Euler(0f, (u - 0.5f) * 50f, 0f) * away * HeadAway + Vector3.up * HeadUp * (0.9f + 0.2f * (((seed >> 10) & 1023u) / 1023f));
                if (HeadThrown(AnticCan.Throw(piece, v, Obj.Id, Mathf.Max(1f, rad / AnticCan.Radius)))) HeadsThrown++;   // K248b: its clatter louder than a can's (UnitView.HeadPop.cs)
                else Destroy(piece.gameObject);
            }
            HeadPop(hp);                                                                         // K248b: its own pop, not the armour-hit ping (UnitView.HeadPop.cs)
            HeadshotCall(game.World.GetAny(Obj.HeadshotBy), hp);
            OzDeathTrace.Mode(this, death == Death.Ragdoll ? "headshot, ragdoll collapse" : "headshot, death clip");
            return true;
        }

        /// <summary>The call of the player's sniper (on camera only, at most one in CallEvery, never twice within CallGap s).</summary>
        void HeadshotCall(ZObject sniper, Vector3 at)
        {
            if (sniper == null || sniper.Owner != game.player || !WreckFx.OnScreen(at) || !AudioDirector.Has(HeadshotPool)) return;
            sinceHeadCall++;
            if (sinceHeadCall < CallEvery || Time.time - lastHeadCall < CallGap) { HeadCallsSkipped++; return; }
            sinceHeadCall = 0; lastHeadCall = Time.time; HeadCalls++;
            HeadCallLater(HeadshotPool);                                                  // K248b: a moment after the pop (UnitView.HeadPop.cs)
        }

        /// <summary>CombatView.DrawShot: the round of a sniper's headshot ends at the victim's head (every other shot: <paramref name="to"/>).</summary>
        public static Vector3 HeadshotShotEnd(ZEvent e, ZObject a, ZObject t, UnitView tv, Vector3 to)
        {
            if (!e.flag || a == null || t == null || !tv || t.HeadshotBy != a.Id) return to;
            var head = tv.HeadBone(tv.BodySkin());
            return head ? HeadCentre(head, RobotV3.RagdollRadius(tv.gameObject, "Head", 0.2f) * Mathf.Abs(tv.transform.lossyScale.y)) : to;
        }

        SkinnedMeshRenderer BodySkin()
        {
            SkinnedMeshRenderer best = null;
            foreach (var s in GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.enabled && s.sharedMesh && (!best || s.sharedMesh.vertexCount > best.sharedMesh.vertexCount)) best = s;
            return best;
        }

        /// <summary>The head bone: the humanoid one, else the body's bone whose name ends in "Head".</summary>
        Transform HeadBone(SkinnedMeshRenderer body)
        {
            if (anim && anim.isHuman) { var h = anim.GetBoneTransform(HumanBodyBones.Head); if (h) return h; }
            if (body) foreach (var b in body.bones) if (b && b.name.EndsWith("Head", System.StringComparison.OrdinalIgnoreCase)) return b;
            return null;
        }

        /// <summary>The bone that is cut (neck and head go together, as in the baked head part): the head's parent when it is a neck.</summary>
        static Transform NeckOf(Transform head) => head && head.parent && head.parent.name.IndexOf("Neck", System.StringComparison.OrdinalIgnoreCase) >= 0 ? head.parent : head;

        /// <summary>The middle of the head: one head radius beyond the head bone along the neck.</summary>
        static Vector3 HeadCentre(Transform head, float rad)
        {
            var up = head.parent ? head.position - head.parent.position : head.up;
            if (up.sqrMagnitude < 1e-6f) up = Vector3.up;
            return head.position + up.normalized * rad;
        }

        /// <summary>The loose head: the type's baked head part on the head bone's pose (the robot types' own models), else a copy of the
        /// body's skins that only the cut bones move (the heroes' models); the rigid pieces on the head go with it.</summary>
        Transform HeadPiece(SkinnedMeshRenderer body, Transform head, Transform cut, Vector3 centre)
        {
            if (!body) return null;
            var block = new MaterialPropertyBlock();
            Transform root = null;
            var tint = new System.Collections.Generic.List<Renderer>();
            var ps = Obj.IsHero ? null : RobotPopDeath.PartsOf(Obj.Type);                               // baked from the type's own model: not for the heroes' models
            if (ps != null)
                for (int i = 0; i < ps.mesh.Length && !root; i++)
                {
                    if (ps.bone[i] == null || !ps.bone[i].EndsWith("Head") || !ps.mesh[i]) continue;
                    Transform b = null; foreach (var t in body.bones) if (t && t.name == ps.bone[i]) { b = t; break; }
                    if (!b) break;
                    var go = new GameObject("OZ_HeadshotHead_" + Obj.Id);
                    go.transform.SetPositionAndRotation(b.TransformPoint(ps.centre[i]), b.rotation); go.transform.localScale = b.lossyScale;
                    go.AddComponent<MeshFilter>().sharedMesh = ps.mesh[i];
                    var mats = body.sharedMaterials;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.On;
                    for (int m = 0; m < mats.Length; m++) { body.GetPropertyBlock(block, m); mr.SetPropertyBlock(block, m); }
                    tint.Add(mr); root = go.transform; HeadsBaked++;
                }
            if (!root)
            {   // no baked part for this model: its own skins, every bone outside the cut collapsed into one point at the neck
                root = new GameObject("OZ_HeadshotHead_" + Obj.Id).transform;
                root.SetPositionAndRotation(centre, cut.rotation);
                var rest = new GameObject("Rest").transform; rest.SetParent(root, false);
                rest.position = cut.position; rest.localScale = Vector3.one * 0.0001f;
                var copies = new System.Collections.Generic.Dictionary<Transform, Transform>();
                foreach (var sk0 in GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!sk0.enabled || !sk0.sharedMesh) continue;
                    var src = sk0.bones; var map = new Transform[src.Length]; bool any = false;
                    for (int i = 0; i < src.Length; i++)
                    {
                        var b = src[i];
                        if (!b || !b.IsChildOf(cut)) { map[i] = rest; continue; }
                        if (!copies.TryGetValue(b, out var c))
                        {
                            c = new GameObject(b.name).transform; c.localScale = b.lossyScale;
                            c.SetPositionAndRotation(b.position, b.rotation); c.SetParent(root, true);
                            copies[b] = c;
                        }
                        map[i] = c; any = true;
                    }
                    if (!any) continue;                                                                 // this skin has nothing on the head
                    var skinGo = new GameObject("Skin"); skinGo.transform.SetParent(root, false);
                    var sk = skinGo.AddComponent<SkinnedMeshRenderer>();
                    sk.sharedMesh = sk0.sharedMesh; sk.bones = map; sk.rootBone = root; sk.quality = sk0.quality;
                    sk.localBounds = new Bounds(Vector3.zero, Vector3.one * 3f); sk.sharedMaterials = sk0.sharedMaterials; sk.shadowCastingMode = ShadowCastingMode.On;
                    for (int m = 0; m < sk0.sharedMaterials.Length; m++) { sk0.GetPropertyBlock(block, m); sk.SetPropertyBlock(block, m); }
                    tint.Add(sk);
                }
                HeadsSkinned++;
            }
            // rigid pieces on the head (lamps, visors, antennas): copied as they are (no glow cards: transparent queues stay behind)
            foreach (var mf in cut.GetComponentsInChildren<MeshFilter>())
            {
                var mr0 = mf.GetComponent<MeshRenderer>();
                if (!mr0 || !mr0.enabled || !mf.sharedMesh || (mr0.sharedMaterial && mr0.sharedMaterial.renderQueue >= 2450)) continue;
                var g = new GameObject(mf.name).transform; g.localScale = mf.transform.lossyScale;
                g.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); g.SetParent(root, true);
                g.gameObject.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var mr = g.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterials = mr0.sharedMaterials; mr.shadowCastingMode = ShadowCastingMode.On;
                mr0.GetPropertyBlock(block); if (!block.isEmpty) mr.SetPropertyBlock(block);
                for (int m = 0; m < mr0.sharedMaterials.Length; m++) { mr0.GetPropertyBlock(block, m); if (!block.isEmpty) mr.SetPropertyBlock(block, m); }
                tint.Add(mr);
            }
            root.gameObject.AddComponent<HeadshotTint>().rs = tint.ToArray();
            return root;
        }

        /// <summary>The loose head's colour drains like a dead robot's parts (OzDeadTint, from its age).</summary>
        sealed class HeadshotTint : MonoBehaviour
        {
            public Renderer[] rs; float age; int step = -1;
            void Update()
            {
                if (rs == null) return;
                age += Time.deltaTime;
                int s = Mathf.Min(OzDeadTint.Steps, Mathf.FloorToInt(age / Mathf.Max(0.05f, OzDeadTint.Seconds) * OzDeadTint.Steps));
                if (s == step) return;
                step = s;
                foreach (var r in rs) if (r) OzDeadTint.Set(r, (float)s / OzDeadTint.Steps);
                if (s >= OzDeadTint.Steps) enabled = false;
            }
        }
    }
}
