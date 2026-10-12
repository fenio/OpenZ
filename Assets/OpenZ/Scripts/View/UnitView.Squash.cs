using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// AWAY from the impact along the vehicle's travel (a short topple in the vehicle's path; a glancing hit throws him aside, he gets up
    /// hurt) with a body thud + metal clang. When the sim squashes him (ZObject.Squashed, the Death event) his OWN drawn mesh is baked in
    /// its current pose and flattened like a cartoon pancake (x1.45 wide, x0.07 high, a springy wobble), team colours kept, lying on the
    /// ground; an oil / scrap splat decal, a few springs and bolts pop out, the crush death sound (DeathVoices Cause.Crush) and a metal
    /// scrap rattle; after ~15 s the pancake pops out (quick scale-down) like other remains.
    /// </summary>
    public partial class UnitView
    {
        /// <summary>DV65: the sim squashed this robot (CombatView.Death): pancake instead of a ragdoll / pop.</summary>
        public void Squash(ZObject vehicle)
        {
            if (death != Death.None || Obj == null) return;
            if (reactKind != 0) EndReaction();
            if (hold) hold.Suspended = true;
            var dir = new Vector3(Obj.RunOverDirX, 0f, -Obj.RunOverDirY);
            if (dir.sqrMagnitude < 1e-4f) dir = transform.forward; dir.y = 0f; dir.Normalize();
            SquashedRemains.Make(this, dir, vehicle);
            OzDeathTrace.Mode(this, "squash");
            Hide();
        }
    }

    /// <summary>DV65: the flattened robot (baked copy of its drawn renderers) and its little show.
    /// renderer list + property block, the baked meshes come from a small pool (returned when the pancake pops out), the console line
    /// only with Verbose (a Debug.Log with its stack trace costs ~1 ms per call, also in the player log). PerfLegacy = the old path
    /// (session-only switch for the before / after frame-time proof, RunOverPolishProof).</summary>
    public class SquashedRemains : MonoBehaviour
    {
        public static int Made, Live;
        public const float Life = 15f, FlattenTime = 0.16f, WobbleTime = 0.5f, PopOut = 0.35f;
        public const float FlatY = 0.07f, WideXZ = 1.45f;
        /// <summary></summary>
        public static bool Verbose, PerfLegacy;
        /// <summary></summary>
        public static double LastMakeMs, MaxMakeMs;
        /// <summary>
        /// component), bake (renderer copies + BakeMesh), mark (ScorchDecals), fx (springs / bolts), sound (AudioDirector + OzSoundLog) and the
        /// Stored as numbers (no allocation per squash); the text is built only when read.</summary>
        public static string LastSteps => Split(lastNo, LastMakeMs, lastSplit, lastParts, lastGc);
        public static string MaxSteps => Split(maxNo, MaxMakeMs, maxSplit, maxParts, maxGc);
        static readonly double[] lastSplit = new double[5], maxSplit = new double[5];
        static int lastNo, lastParts, lastGc, maxNo, maxParts, maxGc;
        static string Split(int no, double ms, double[] s, int parts, int gc) => no == 0 ? "no squash yet" :
            string.Format(System.Globalization.CultureInfo.InvariantCulture, "make #{0} {1:F2} ms = setup {2:F2} + bake {3:F2} ({4} parts) + mark {5:F2} + fx {6:F2} + sound {7:F2}; gen-0 GCs inside {8}",
                no, ms, s[0], s[1], parts, s[2], s[3], s[4], gc);
        /// <summary>Empty meshes put in the pool at level load (<see cref="Prewarm"/>): the first squashes allocate no Mesh.</summary>
        public static int PrewarmPool = 12;
        static readonly List<SkinnedMeshRenderer> warmBuf = new List<SkinnedMeshRenderer>(16);
        static readonly List<Renderer> rendBuf = new List<Renderer>(32);
        static readonly Stack<Mesh> meshPool = new Stack<Mesh>();
        static MaterialPropertyBlock sharedPb;
        const int MeshPoolMax = 48;
        float t; readonly List<Mesh> baked = new List<Mesh>(); uint s;

        static Mesh TakeMesh(string name)
        {
            if (PerfLegacy || meshPool.Count == 0) return new Mesh { name = name };
            var m = meshPool.Pop(); if (!m) return new Mesh { name = name };
            return m;
        }

        float R() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }

        public static SquashedRemains Make(UnitView v, Vector3 dir, ZObject vehicle)
        {
            var game = ZGame.I; if (!game || !v) return null;
            long sw0 = System.Diagnostics.Stopwatch.GetTimestamp(); int gc0 = System.GC.CollectionCount(0);
            var p = v.transform.position;
            float gy = game.GroundY(p.x, p.z);
            var go = new GameObject("SquashedRobot_" + v.Obj.Id);
            go.transform.SetPositionAndRotation(new Vector3(p.x, gy + 0.04f, p.z), Quaternion.LookRotation(dir, Vector3.up));
            var sr = go.AddComponent<SquashedRemains>();
            sr.s = RobotDeathRule.Hash(v.Obj.Id, vehicle != null ? vehicle.Id : 0, 63u) | 1u;
            int parts = 0;
            long sw1 = System.Diagnostics.Stopwatch.GetTimestamp();
            MaterialPropertyBlock pb;
            IList<Renderer> rends;
            if (PerfLegacy) { pb = new MaterialPropertyBlock(); rends = v.GetComponentsInChildren<Renderer>(); }
            else { if (sharedPb == null) sharedPb = new MaterialPropertyBlock(); pb = sharedPb; pb.Clear(); v.GetComponentsInChildren(false, rendBuf); rends = rendBuf; }
            for (int ri = 0; ri < rends.Count; ri++)
            {
                var r = rends[ri];
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (r.sharedMaterial && r.sharedMaterial.renderQueue >= 2450) continue;                       // glows, rings, decals stay behind
                Mesh m = null; bool skinned = false;
                if (r is SkinnedMeshRenderer sm) { m = TakeMesh("squash_" + sm.name); sm.BakeMesh(m, true); sr.baked.Add(m); skinned = true; }
                else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh) m = mf.sharedMesh;
                if (!m) continue;
                var c = new GameObject(r.name); c.layer = r.gameObject.layer;
                c.transform.SetParent(go.transform, false);
                c.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                c.transform.localScale = skinned ? Vector3.one : r.transform.lossyScale;
                c.AddComponent<MeshFilter>().sharedMesh = m;
                var nr = c.AddComponent<MeshRenderer>();
                nr.sharedMaterials = r.sharedMaterials; nr.shadowCastingMode = r.shadowCastingMode; nr.receiveShadows = r.receiveShadows;
                pb.Clear(); r.GetPropertyBlock(pb); if (!pb.isEmpty) nr.SetPropertyBlock(pb);
                int nm = nr.sharedMaterials.Length;
                for (int i = 0; i < nm; i++) { pb.Clear(); r.GetPropertyBlock(pb, i); if (!pb.isEmpty) nr.SetPropertyBlock(pb, i); }
                parts++;
            }
            rendBuf.Clear();
            long sw2 = System.Diagnostics.Stopwatch.GetTimestamp();
            Made++; Live++;
            // oil / scrap splat (fades like the other ground marks), springs and bolts popping out, the scrap rattle and the hull bump
            ScorchDecals.Add(go.transform.position, 1.05f, ScorchKind.Contact);
            long sw3 = System.Diagnostics.Stopwatch.GetTimestamp();
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled)
            {
                int n = 4 + (int)(sr.s % 3u);
                for (int i = 0; i < n; i++)
                {
                    float a = sr.R() * 6.283f;
                    var vel = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1.2f + sr.R() * 2.2f) + Vector3.up * (4f + sr.R() * 3.5f);
                    fxp.EmitChannel("m12_metal", go.transform.position + Vector3.up * 0.3f, vel, 1.6f + sr.R(), 0.08f + sr.R() * 0.09f, Color.white, 1f);
                }
            }
            long sw4 = System.Diagnostics.Stopwatch.GetTimestamp();
            AudioDirector.Fire("dst_debris_metal", go.transform.position, v.Obj.Id, 0.9f);
            if (vehicle != null) AudioDirector.Fire("veh_suspension", go.transform.position, vehicle.Id, 1f);
            bool talk = PerfLegacy || Verbose;
            OzSoundLog.Add("runover", "squash", "runover_splat + death_robot_crush (voiceless) + dst_debris_metal + veh_suspension", talk ? $"robot {v.Obj.Id} ({v.Obj.Type}, {v.Obj.Owner}) under {(vehicle != null ? vehicle.Type + " " + vehicle.Id : "?")}: {parts} parts flattened" : "pancake", 1f, go.transform.position);
            if (talk) Debug.Log($"[OpenZ RunOver] SQUASH robot {v.Obj.Id} {v.Obj.Type} {v.Obj.Owner} under {(vehicle != null ? vehicle.Type + " " + vehicle.Id + " " + vehicle.Owner : "?")} at {go.transform.position:F1}: pancake of {parts} parts");
            long sw5 = System.Diagnostics.Stopwatch.GetTimestamp(); double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            LastMakeMs = (sw5 - sw0) * toMs;
            lastSplit[0] = (sw1 - sw0) * toMs; lastSplit[1] = (sw2 - sw1) * toMs; lastSplit[2] = (sw3 - sw2) * toMs; lastSplit[3] = (sw4 - sw3) * toMs; lastSplit[4] = (sw5 - sw4) * toMs;
            lastNo = Made; lastParts = parts; lastGc = System.GC.CollectionCount(0) - gc0;
            if (LastMakeMs > MaxMakeMs) { MaxMakeMs = LastMakeMs; System.Array.Copy(lastSplit, maxSplit, 5); maxNo = lastNo; maxParts = parts; maxGc = lastGc; }
            return sr;
        }

        /// <summary>
        /// level-load warm-up, called by OpenZNukePrewarm's objects step (its JIT step also compiles this class, RunOverView and
        /// OzDeathTrace): JITs UnitView.Squash, fills the mesh pool with <see cref="PrewarmPool"/> empty meshes and bakes one robot's skinned
        /// renderers once into pool meshes (the native BakeMesh path and its buffers warm), which go back to the pool cleared. Lossless:
        /// nothing is drawn, spawned or changed in the game. Returns a status line.</summary>
        public static string Prewarm()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { typeof(UnitView).GetMethod(nameof(UnitView.Squash))?.MethodHandle.GetFunctionPointer(); } catch (System.Exception) { }
            int fill = Mathf.Min(PrewarmPool, MeshPoolMax) - meshPool.Count;
            for (int i = 0; i < fill; i++) meshPool.Push(new Mesh { name = "squash_pool" });
            int baked = 0;
            var game = ZGame.I;
            if (game)
                foreach (var kv in game.Views)
                {
                    var v = kv.Value; if (!v || !v.isActiveAndEnabled) continue;
                    v.GetComponentsInChildren(false, warmBuf);
                    if (warmBuf.Count == 0) continue;
                    var used = new List<Mesh>(warmBuf.Count);
                    foreach (var sm in warmBuf) { if (!sm || !sm.enabled || !sm.sharedMesh) continue; var m = TakeMesh("squash_warm"); sm.BakeMesh(m, true); used.Add(m); baked++; }
                    foreach (var m in used) { m.Clear(); if (meshPool.Count < MeshPoolMax) meshPool.Push(m); else Destroy(m); }
                    warmBuf.Clear();
                    if (baked > 0) break;                                                           // one robot is enough to warm the path
                }
            return $"squash prewarm: pool {meshPool.Count} meshes, {baked} skinned renderers baked once, {sw.Elapsed.TotalMilliseconds:F1} ms";
        }

        void Update()
        {
            t += Time.deltaTime;
            float y, xz;
            if (t < FlattenTime)
            {   // pressed flat under the hull (ease-in: the hull comes down on it)
                float k = t / FlattenTime; k *= k;
                y = Mathf.Lerp(1f, FlatY * 0.6f, k); xz = Mathf.Lerp(1f, WideXZ * 1.06f, k);
            }
            else if (t < FlattenTime + WobbleTime)
            {   // a springy cartoon wobble as it settles
                float k = (t - FlattenTime) / WobbleTime, env = (1f - k) * (1f - k), w = Mathf.Sin(k * 26f);
                y = FlatY * (1f + 0.55f * env * w); xz = WideXZ * (1f + 0.05f * env * Mathf.Sin(k * 26f + 1.6f));
            }
            else { y = FlatY; xz = WideXZ; }
            if (t > Life) { float k = Mathf.Clamp01((t - Life) / PopOut); xz *= 1f - k; y *= 1f - k; if (k >= 1f) { Destroy(gameObject); return; } }
            transform.localScale = new Vector3(xz, y, xz);
        }

        void OnDestroy()
        {
            Live--;
            foreach (var m in baked) { if (!m) continue; if (!PerfLegacy && meshPool.Count < MeshPoolMax) { m.Clear(); meshPool.Push(m); } else Destroy(m); }
            baked.Clear();
        }
    }

    /// <summary>DV65: the run-over hit (ZEventType.RunOverHit): the topple away from the impact + its sounds.</summary>
    public static class RunOverView
    {
        public static int Hits, Glances;
        /// <summary></summary>
        public static double LastHitMs, MaxHitMs;

        public static void Hit(ZGame game, ZEvent e, ZObject robot, ZObject vehicle)
        {
            if (game == null || robot == null) return;
            long sw0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Hits++; if (e.flag) Glances++;
            game.Views.TryGetValue(robot.Id, out var v);
            var p = v ? v.transform.position : game.CenterWorld(robot);
            var dir = new Vector3(robot.RunOverDirX, 0f, -robot.RunOverDirY); if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; dir.Normalize();
            if (v && !robot.Destroyed)
            {
                float hold = robot.KnockUntil > game.World.Time ? (float)(robot.KnockUntil - game.World.Time) / Mathf.Max(0.05f, game.gameSpeed) : -1f;
                // head-on: a short topple forward in the vehicle's path (it then drives over him); glancing: thrown aside, gets up hurt
                v.React(p - dir * 2f, 3, RobotDeathRule.Hash(robot.Id, vehicle != null ? vehicle.Id : 0, 63u), 0f, hold, e.flag ? 1f : 0.35f);
            }
            AudioDirector.Fire("runover_push", p + Vector3.up * 0.8f, robot.Id, e.flag ? 0.85f : 1f);
            bool talk = SquashedRemains.PerfLegacy || SquashedRemains.Verbose;
            OzSoundLog.Add("runover", e.flag ? "hit_glancing" : "hit", "runover_push (bucket a / b)", talk ? $"robot {robot.Id} ({robot.Type}, {robot.Owner}) hit by {(vehicle != null ? vehicle.Type + " " + vehicle.Id : "?")}, -{e.value:0} HP" : "knock-down", 1f, p);
            if (talk) Debug.Log($"[OpenZ RunOver] HIT robot {robot.Id} {robot.Type} {robot.Owner} by {(vehicle != null ? vehicle.Type + " " + vehicle.Id + " " + vehicle.Owner : "?")} {(e.flag ? "glancing (thrown aside)" : "head-on (topples in the path)")} -{e.value:0} HP -> {robot.Health}");
            LastHitMs = (System.Diagnostics.Stopwatch.GetTimestamp() - sw0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; if (LastHitMs > MaxHitMs) MaxHitMs = LastHitMs;
        }
    }
}
