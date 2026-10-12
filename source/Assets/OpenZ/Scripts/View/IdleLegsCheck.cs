using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the final pose of each frame (UnitView, UnitAim, OpenZWading, WeaponHold all ran), and measures:
    ///  * STANDING robots = the base layer shows "idle" (no transition) the whole window: the largest move of each foot and toe
    ///    bone relative to the robot's root (the idle animation itself: must stay under 1 cm) and in the world (planted: under
    ///    1 cm unless the root itself was moved, which is listed as a glide, or the body pivoted toward a pre-aim target);
    ///  * SIM-IDLE robots (no waypoints, not moving, no target the whole window): base-layer state changes (leg flips, must be 0);
    ///  * squads: the spread of the idle phases (desync, never lockstep).
    /// Only robots on screen count (>= 80 % of the frames): off screen the Animator is culled (CullUpdateTransforms) and nothing
    /// the editor; start it with menu OpenZ/BS-01: Idle legs check (Play, 5 s) or IdleLegsCheck.Begin().
    /// and counted culled off-screen robots.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class IdleLegsCheck : MonoBehaviour
    {
        public const float Duration = 5f, LimitCm = 1f, GlideShare = 0.1f;
        public bool Done { get; private set; }
        public bool Passed { get; private set; }
        public string Report { get; private set; } = "";
        static readonly int IdleHash = Animator.StringToHash("idle");

        sealed class Probe
        {
            public UnitView v; public Animator a; public Renderer skin; public Transform[] bones;
            public Vector3[] firstW, firstR; public float[] maxW, maxR;
            public Vector3 root0; public Quaternion rot0; public float rootMax, turn, phase;
            public int hash, flips, frames, seen; public bool alive = true, standing = true, simIdle = true, human;
            public bool OnScreen => frames > 0 && seen >= 0.8f * frames;
            public bool Glide => rootMax >= LimitCm;
            public bool Pivot => turn > 2f;
        }
        static readonly string[] BoneNames = { "L foot", "R foot", "L toes", "R toes" };
        readonly List<Probe> probes = new List<Probe>();
        float t0 = -1f;

        public static IdleLegsCheck Begin()
        {
            var game = ZGame.I;
            if (!game || game.World == null) { Debug.LogWarning("[IdleLegsCheck] no running ZGame"); return null; }
            var c = new GameObject("IdleLegsCheck").AddComponent<IdleLegsCheck>();
            Debug.Log($"[IdleLegsCheck] measuring standing robots for {Duration} s (keep idle squads on screen)");
            return c;
        }

        static bool SimIdle(ZObject o) => o != null && !o.Destroyed && o.Waypoints.Count == 0 && !o.IsMoving && (o.AttackObject == null || o.AttackObject.Destroyed);
        static bool ShowsIdle(Animator a) => a && !a.IsInTransition(0) && a.GetCurrentAnimatorStateInfo(0).shortNameHash == IdleHash;
        static Vector3 Rel(UnitView v, Transform b) => Quaternion.Inverse(v.transform.rotation) * (b.position - v.transform.position);

        static Transform[] Feet(UnitView v, Animator a)
        {
            if (a && a.isHuman)
                return new[] { a.GetBoneTransform(HumanBodyBones.LeftFoot), a.GetBoneTransform(HumanBodyBones.RightFoot),
                               a.GetBoneTransform(HumanBodyBones.LeftToes), a.GetBoneTransform(HumanBodyBones.RightToes) };
            var all = v.GetComponentsInChildren<Transform>(true);
            Transform F(string n) => all.FirstOrDefault(t => t.name.EndsWith(n));
            return new[] { F("LeftFoot"), F("RightFoot"), F("LeftToeBase"), F("RightToeBase") };
        }

        void LateUpdate()
        {
            var game = ZGame.I; if (Done || !game || game.World == null) return;
            if (t0 < 0f)
            {
                t0 = Time.time;
                foreach (var v in game.Views.Values)
                {
                    if (!v || !v.isActiveAndEnabled || v.Dying || v.Obj == null || v.Obj.Kind != ZKind.Robot) continue;
                    var a = v.GetComponentInChildren<Animator>(); if (!a) continue;
                    bool standing = ShowsIdle(a), simIdle = SimIdle(v.Obj);
                    if (!standing && !simIdle) continue;
                    var b = Feet(v, a); if (b.Any(x => !x)) continue;
                    probes.Add(new Probe { v = v, a = a, bones = b, skin = v.GetComponentInChildren<SkinnedMeshRenderer>(), human = a.isHuman,
                                           firstW = b.Select(x => x.position).ToArray(), firstR = b.Select(x => Rel(v, x)).ToArray(),
                                           maxW = new float[b.Length], maxR = new float[b.Length], standing = standing, simIdle = simIdle,
                                           root0 = v.transform.position, rot0 = v.transform.rotation, hash = a.GetCurrentAnimatorStateInfo(0).shortNameHash });
                }
                return;
            }
            foreach (var p in probes)
            {
                if (!p.alive) continue;
                if (!p.v || p.v.Dying || !p.v.isActiveAndEnabled || game.World.Get(p.v.Obj.Id) != p.v.Obj) { p.alive = false; continue; }
                p.frames++; if (p.skin && p.skin.isVisible) p.seen++;
                if (p.simIdle && !SimIdle(p.v.Obj)) p.simIdle = false;
                if (p.standing && !ShowsIdle(p.a)) p.standing = false;
                var st = p.a.GetCurrentAnimatorStateInfo(0);
                if (st.shortNameHash != p.hash) { p.flips++; p.hash = st.shortNameHash; }
                if (!p.standing) continue;
                for (int k = 0; k < p.bones.Length; k++)
                {
                    p.maxW[k] = Mathf.Max(p.maxW[k], (p.bones[k].position - p.firstW[k]).magnitude * 100f);
                    p.maxR[k] = Mathf.Max(p.maxR[k], (Rel(p.v, p.bones[k]) - p.firstR[k]).magnitude * 100f);
                }
                p.rootMax = Mathf.Max(p.rootMax, (p.v.transform.position - p.root0).magnitude * 100f);
                p.turn = Mathf.Max(p.turn, Quaternion.Angle(p.rot0, p.v.transform.rotation));
                p.phase = st.normalizedTime % 1f;
            }
            if (Time.time - t0 >= Duration) Finish();
        }

        void Finish()
        {
            Done = true;
            var stand = probes.Where(p => p.alive && p.standing && p.OnScreen).ToList();
            var sim = probes.Where(p => p.alive && p.simIdle && p.OnScreen).ToList();
            int offScreen = probes.Count(p => p.alive && (p.standing || p.simIdle) && !p.OnScreen);
            var planted = stand.Where(p => !p.Glide && !p.Pivot).ToList();
            var glides = stand.Where(p => p.Glide).ToList();
            float worstRel = stand.Count > 0 ? stand.Max(p => p.maxR.Max()) : 0f;
            float worstW = planted.Count > 0 ? planted.Max(p => p.maxW.Max()) : 0f;
            int flips = sim.Sum(p => p.flips);

            var sb = new StringBuilder();
            sb.AppendLine($"# Idle legs check ({ZGame.I.level}, {Duration} s, play mode)\n");
            sb.AppendLine($"Standing robots on screen (base layer `idle` the whole window): {stand.Count}; of them planted (root still, no pivot): {planted.Count}, " +
                          $"root moved >= {LimitCm} cm (glide, a bump from a neighbour or a vehicle): {glides.Count}, pivoted > 2 deg toward a pre-aim target: {stand.Count(p => p.Pivot && !p.Glide)}. " +
                          $"Sim-idle robots on screen (no order, not moving, no target): {sim.Count}. Off screen (culled Animator, not counted): {offScreen}.\n");
            sb.AppendLine("| robot | rig | feet / toes vs root (cm): the idle animation | feet / toes in the world (cm) | root (cm) | turn (deg) | base-layer flips | sim idle |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var p in stand.Concat(sim.Where(q => !stand.Contains(q))).OrderByDescending(p => p.maxR.Max()))
                sb.AppendLine($"| {p.v.Obj.Owner} {p.v.Obj.Type}#{p.v.Obj.Id}{(p.v.Obj.IsMinion ? " m" + p.v.Obj.MemberIndex : "")} | {(p.human ? "BS-01 humanoid" : "generic")} | " +
                              (p.standing ? string.Join(", ", p.maxR.Select((m, k) => $"{BoneNames[k]} {m:F2}")) : "(left idle)") + " | " +
                              (p.standing ? string.Join(", ", p.maxW.Select((m, k) => $"{BoneNames[k]} {m:F2}")) : "") + $" | {p.rootMax:F2} | {p.turn:F0} | {p.flips} | {(p.simIdle ? "yes" : "no")} |");
            // desync: standing members of one squad must not share one phase
            var squads = stand.GroupBy(p => (p.v.Obj.Leader ?? p.v.Obj).Id).Where(g => g.Count() > 1).ToList();
            int lockstep = 0;
            foreach (var g in squads)
            {
                var ph = g.Select(p => p.phase).OrderBy(x => x).ToList();
                float gap = 0f; for (int i = 0; i < ph.Count; i++) gap = Mathf.Max(gap, (i + 1 < ph.Count ? ph[i + 1] : ph[0] + 1f) - ph[i]);
                if (1f - gap < 0.03f) lockstep++;                                                     // circular spread of the phases
            }
            sb.AppendLine($"\nSquads with 2+ standing members: {squads.Count}, in lockstep (phase spread < 3 %): {lockstep}");
            bool glideOk = glides.Count <= Mathf.FloorToInt(GlideShare * stand.Count);
            Passed = stand.Count >= 3 && worstRel < LimitCm && worstW < LimitCm && glideOk && flips == 0 && lockstep == 0;
            string verdict = stand.Count < 3 ? "INCONCLUSIVE (fewer than 3 standing robots on screen: point the camera at idle squads and run again)"
                           : Passed ? "PASS" : "FAIL";
            sb.AppendLine($"\n**{verdict}**: worst foot / toe move vs root {worstRel:F2} cm and in the world (planted robots) {worstW:F2} cm (limit {LimitCm} cm), " +
                          $"glides {glides.Count} of {stand.Count} (at most {GlideShare * 100f:F0} %), leg flips of sim-idle robots {flips}, lockstep squads {lockstep}");
            Report = sb.ToString();
            Debug.Log("[IdleLegsCheck] " + Report);
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "..", "work", "records"));
            if (Application.isEditor && Directory.Exists(Path.GetDirectoryName(dir))) { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "idle_legs_check.md"), Report); }
            Destroy(gameObject, 0.1f);
        }
    }
}
