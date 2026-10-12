using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Watches every on-screen robot for Duration s
    /// after the final pose of each frame and separates the candidate causes:
    ///  * ROOT: frame-to-frame jerk of the robot transform (view smoothing, spacing, ground snap), world and height;
    ///  * FOOT (local): jerk of each foot relative to the root (animation blending / restarts / layer pops);
    ///  * RESTARTS: base-layer and upper-layer state changes per robot per second, with the state pairs that flip;
    ///  * SPEED: anim.speed changes > 25 % between frames (stride-speed pumping).
    /// A jerk = |x(t) - 2 x(t-1) + x(t-2)| (metres per frame^2), which is tiny for smooth motion and spikes on pops / reversals.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    public class FootJitterProbe : MonoBehaviour
    {
        public static float Duration = 6f;
        public static string Report = "idle";
        public const float PopCm = 1.2f, FootPopCm = 10f;  // root: a jerk above 1.2 cm is a visible pop; feet: the clean walk itself peaks at 7.2 cm at 60 fps (heel strike)

        sealed class P
        {
            public UnitView v; public Animator a; public Transform fl, fr; public Renderer r;
            public readonly List<Vector3> root = new List<Vector3>(), l = new List<Vector3>(), rr = new List<Vector3>(); public readonly List<float> dts = new List<float>();
            public readonly List<bool> walk = new List<bool>(); public readonly StringBuilder csv = new StringBuilder();
            public int baseChanges, upChanges, speedPumps, frames, seen, lastBase, lastUp; public float lastSpeed = -1f; public string lastBaseName, lastUpName;
            public readonly Dictionary<string, int> flips = new Dictionary<string, int>();
        }
        readonly List<P> probes = new List<P>();
        float t0;

        public static void Begin() { Report = "running"; new GameObject("FootJitterProbe").AddComponent<FootJitterProbe>(); }

        static string Name(Animator a, int layer)
        {
            var i = a.GetCurrentAnimatorStateInfo(layer);
            foreach (var s in new[] { "idle", "walking", "run_forward", "run_backward", "strafe_left", "strafe_right", "shooting", "bazooka_idle", "bazooka_fire", "aim_hold", "up_shooting", "up_bazooka_idle", "up_bazooka_fire", "grenade_throw" })
                if (i.IsName(s)) return s;
            return i.shortNameHash.ToString();
        }

        void Start()
        {
            t0 = Time.time;
            var g = ZGame.I;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying) continue;
                var a = v.GetComponentInChildren<Animator>(); if (!a) continue;
                var p = new P { v = v, a = a, r = v.GetComponentInChildren<SkinnedMeshRenderer>() };
                if (a.isHuman) { p.fl = a.GetBoneTransform(HumanBodyBones.LeftFoot); p.fr = a.GetBoneTransform(HumanBodyBones.RightFoot); }
                if (!p.fl) foreach (var t in v.GetComponentsInChildren<Transform>()) { var n = t.name.ToLowerInvariant(); if (n.Contains("leftfoot")) p.fl = t; else if (n.Contains("rightfoot")) p.fr = t; }
                if (p.fl && p.fr) probes.Add(p);
            }
        }

        void LateUpdate()
        {
            foreach (var p in probes)
            {
                if (!p.v || p.v.Dying || p.v.Obj == null || p.v.Obj.Destroyed) continue;
                p.frames++;
                if (p.r && p.r.isVisible) p.seen++;
                var tr = p.v.transform;
                p.root.Add(tr.position); p.dts.Add(Mathf.Max(1e-4f, Time.deltaTime));
                p.l.Add(tr.InverseTransformPoint(p.fl.position) * tr.lossyScale.x);
                p.rr.Add(tr.InverseTransformPoint(p.fr.position) * tr.lossyScale.x);
                string b = Name(p.a, 0); int bh = b.GetHashCode();
                bool walking = b != "idle" && b != "shooting" && b != "bazooka_idle" && b != "bazooka_fire" && b != "grenade_throw";
                p.walk.Add(walking);
                if (p.frames > 1 && bh != p.lastBase) { p.baseChanges++; var k = "base " + p.lastBaseName + " -> " + b; p.flips[k] = p.flips.TryGetValue(k, out var n) ? n + 1 : 1; }
                p.lastBase = bh; p.lastBaseName = b;
                if (p.a.layerCount > 1)
                {
                    string u = Name(p.a, 1); int uh = u.GetHashCode();
                    if (p.frames > 1 && uh != p.lastUp) { p.upChanges++; var k = "up " + p.lastUpName + " -> " + u; p.flips[k] = p.flips.TryGetValue(k, out var n) ? n + 1 : 1; }
                    p.lastUp = uh; p.lastUpName = u;
                }
                { var st = p.a.GetCurrentAnimatorStateInfo(0); var lf = p.l[p.l.Count - 1]; var rf = p.rr[p.rr.Count - 1]; var rp = p.root[p.root.Count - 1];
                  p.csv.AppendLine($"{Time.frameCount},{Time.deltaTime:0.0000},{rp.x:0.000},{rp.y:0.000},{rp.z:0.000},{lf.x:0.000},{lf.y:0.000},{lf.z:0.000},{rf.x:0.000},{rf.y:0.000},{rf.z:0.000},{b},{st.normalizedTime:0.000},{p.a.speed:0.000},{(p.a.layerCount > 1 ? p.a.GetLayerWeight(1) : 0f):0.00},{p.a.IsInTransition(0)},{p.v.transform.eulerAngles.y:0.0}"); }
                if (p.lastSpeed > 0f && Mathf.Abs(p.a.speed - p.lastSpeed) > 0.25f * p.lastSpeed) p.speedPumps++;
                p.lastSpeed = p.a.speed;
            }
            if (Time.time - t0 >= Duration) { Finish(); Destroy(gameObject); }
        }

        /// <summary>Velocity change between consecutive frames, scaled to a 60 fps frame: frame-time hitches (a long frame moves
        /// everything further) do not count as pops; a real pose / position discontinuity does.</summary>
        static List<float> curDt;
        static float Jerk(List<Vector3> x, int i, bool yOnly = false)
        {
            var v1 = (x[i] - x[i - 1]) / curDt[i]; var v0 = (x[i - 1] - x[i - 2]) / curDt[i - 1];
            var d = (v1 - v0) * (1f / 60f);                                          // = x(t) - 2x(t-1) + x(t-2) at a steady 60 fps
            return yOnly ? Mathf.Abs(d.y) : d.magnitude;
        }

        void Finish()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Foot jitter probe ({System.DateTime.Now:HH:mm:ss}, {Duration} s, {ZGame.I.name})\n");
            sb.AppendLine("| robot | type | frames | walk % | root pops | root-Y pops | foot pops walk | foot pops idle | max foot jerk cm | base changes/s | up changes/s | speed pumps/s |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            int total = 0, rootPops = 0, footWalk = 0, footIdle = 0, baseCh = 0, upCh = 0; float secs = 0f;
            var allFlips = new Dictionary<string, int>();
            foreach (var p in probes)
            {
                if (p.frames < 30 || p.seen < 0.8f * p.frames) continue;
                total++;
                int rp = 0, ry = 0, fw = 0, fi = 0; float maxF = 0f; curDt = p.dts;
                int hitches = 0; foreach (var d in p.dts) if (d > 0.034f * Time.timeScale) hitches++;
                for (int i = 2; i < p.root.Count; i++)
                {
                    float jr = Jerk(p.root, i) * 100f, jy = Jerk(p.root, i, true) * 100f;
                    float jf = Mathf.Max(Jerk(p.l, i), Jerk(p.rr, i)) * 100f;
                    if (jr > PopCm) rp++; if (jy > PopCm * 0.5f) ry++;
                    if (jf > FootPopCm) { if (p.walk[i]) fw++; else fi++; }
                    maxF = Mathf.Max(maxF, jf);
                }
                secs += Duration; rootPops += rp; footWalk += fw; footIdle += fi; baseCh += p.baseChanges; upCh += p.upChanges;
                foreach (var kv in p.flips) allFlips[kv.Key] = (allFlips.TryGetValue(kv.Key, out var n) ? n : 0) + kv.Value;
                int walkPct = Mathf.RoundToInt(100f * p.walk.Count(w => w) / Mathf.Max(1, p.walk.Count));
                sb.AppendLine($"| {p.v.Obj.Id} (hitches {hitches}) | {p.v.Obj.Type}{(p.v.Obj.IsMinion ? " m" : "")} | {p.frames} | {walkPct} | {rp} | {ry} | {fw} | {fi} | {maxF:0.0} | {p.baseChanges / Duration:0.00} | {p.upChanges / Duration:0.00} | {p.speedPumps / Duration:0.00} |");
            }
            sb.AppendLine($"\nRobots on screen: {total}. Totals: root pops {rootPops}, foot pops walking {footWalk}, idle {footIdle}, base changes {baseCh}, up changes {upCh}.");
            sb.AppendLine("\nState flips (all robots):");
            foreach (var kv in allFlips.OrderByDescending(k => k.Value).Take(20)) sb.AppendLine($"- {kv.Key}: {kv.Value}");
            Report = $"robots {total}, root pops {rootPops}, foot pops walk {footWalk} idle {footIdle}, base changes {baseCh}, up changes {upCh}; top flips: " +
                     string.Join("; ", allFlips.OrderByDescending(k => k.Value).Take(6).Select(k => k.Key + " x" + k.Value));
#if UNITY_EDITOR
            System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/foot_jitter_probe.md")), sb.ToString());
#endif
            var worst = probes.Where(q => q.frames > 30 && q.seen >= 0.8f * q.frames).OrderByDescending(q => q.walk.Count(w => w)).FirstOrDefault();
#if UNITY_EDITOR
            if (worst != null) System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/foot_jitter_walker.csv")), "frame,dt,rx,ry,rz,lx,ly,lz,fx,fy,fz,state,nt,speed,upw,trans,yaw\n" + worst.csv);
#endif
            Debug.Log("[FootJitterProbe] " + Report);
        }
    }

    /// <summary>Slow-motion close-up of one robot's feet: a side camera follows the robot root (after every view script ran)
    /// and saves each rendered frame, so pops between consecutive frames are visible in a contact sheet.</summary>
    [DefaultExecutionOrder(1002)]
    public class FootCapture : MonoBehaviour
    {
        public static string Status = "idle";
        UnitView v; Camera cam; RenderTexture rt; readonly StringBuilder log = new StringBuilder("n,frame,dt,state,nt,trans,next,tt,speed,upw,px,py,pz,yaw,lx,ly,lz,rx,ry,rz,lfy,rfy\n"); Texture2D tex; int left, n; string dir; float oldScale;
        public static void Begin(int unitId, int frames, float timeScale, string dir)
        {
            var g = ZGame.I; if (!g || !g.Views.TryGetValue(unitId, out var v) || !v) { Status = "no unit"; return; }
            var c = new GameObject("FootCapture").AddComponent<FootCapture>();
            c.v = v; c.left = frames; c.dir = dir; c.oldScale = Time.timeScale; Time.timeScale = timeScale;
            System.IO.Directory.CreateDirectory(dir);
            c.cam = c.gameObject.AddComponent<Camera>(); c.cam.enabled = false; c.cam.fieldOfView = 28f; c.cam.nearClipPlane = 0.1f;
            var main = Camera.main; if (main) { c.cam.clearFlags = main.clearFlags; c.cam.backgroundColor = main.backgroundColor; c.cam.cullingMask = main.cullingMask; }
            c.rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; c.tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            Status = "running";
        }
        void LateUpdate()
        {
            if (!v || left <= 0) { Finish(); return; }
            var t = v.transform; var side = t.right;
            var target = t.position + Vector3.up * 1.1f;
            cam.transform.position = target + side * 6.5f + Vector3.up * 0.6f;
            cam.transform.LookAt(target);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = prev;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"f{n:000}.png"), tex.EncodeToPNG());
            var an = v.GetComponentInChildren<Animator>();
            if (an)
            {
                var st = an.GetCurrentAnimatorStateInfo(0); var nx = an.GetNextAnimatorStateInfo(0);
                var lf = an.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
                var ll = t.InverseTransformPoint(lf.position); var rl = t.InverseTransformPoint(rf.position);
                log.AppendLine($"{n},{Time.frameCount},{Time.deltaTime:0.0000},{st.shortNameHash},{st.normalizedTime:0.0000},{an.IsInTransition(0)},{nx.shortNameHash},{an.GetAnimatorTransitionInfo(0).normalizedTime:0.00},{an.speed:0.000},{(an.layerCount > 1 ? an.GetLayerWeight(1) : 0):0.00},{t.position.x:0.000},{t.position.y:0.000},{t.position.z:0.000},{t.eulerAngles.y:0.0},{ll.x:0.0000},{ll.y:0.0000},{ll.z:0.0000},{rl.x:0.0000},{rl.y:0.0000},{rl.z:0.0000},{lf.position.y:0.000},{rf.position.y:0.000}");
            }
            n++; left--;
        }
        void Finish() { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "log.csv"), log.ToString()); Time.timeScale = oldScale; Status = $"done {n} frames"; if (rt) rt.Release(); Destroy(gameObject); }
    }
}
