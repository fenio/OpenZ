using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Rare short-circuit zaps on damaged electrical parts.
    /// <list type="bullet">
    /// <item>Where: buildings at damage stage >= 1 (health <= 50 %, OpenZDamageStates' smoke line; not bridges, not ruins), half of
    /// them on the factory / fort gate kit when it is there (gate_* lamp lenses, light strips, door frames: GateKitView), damaged
    /// vehicles (health <= 50 %), broken or fallen street lamps (NightLights: Damage >= 1 or broken glass), and the crane's welder
    /// at work (<see cref="Arc"/>, called by CraneArmRig).</item>
    /// <item>What: a small jagged arc with 0-1 forks between two REAL surface points (vertices of the part's own mesh, 0.25-1 m
    /// apart; the renderer's bounds face when a mesh is not readable), re-shaped every ~40 ms, two bursts with a dark gap ("zzt-zzt",
    /// 0.12-0.28 s); a pale blue-white HDR core + glow (bloom), NOT the Electrobot's big royal-blue bolt; gravity sparks and a white
    /// core flash at the first end (FxManager.WeldSparks) and a few more at the other end on the second burst; a 0.1 s blue-white
    /// point light that lights the ground (FxManager.AddLight); the crackle on the zap frame (sd_robot_death_short_circuit_a/b,
    /// sd_robot_death_arc_crackle, Electro/eb_spark_a-c; lamps: sd_lamp_spark_a-c) through CombatAudio, so OzSoundLog lists it.</item>
    /// <item>How often: every 6-20 s per object (x0.6 under 25 % health; lamps 7-22 s), first zap 1.5-10 s after the damage is seen,
    /// each object on its own random clock (unsynced). Global gates: >= 0.45 s between zap starts, <= 3 at once, on screen only
    /// (an off-screen object tries again 2-6 s later, silently). <see cref="Report"/> logs the zap count and the mean interval
    /// per object (the row's "zap rate per object logged").</item>
    /// </list>
    /// View only (UnityEngine.Random, never the sim's Rng). Arcs are drawn by OpenZElectroFx's one ElectroArcs mesh (pushed in
    /// LateUpdate at 95, rendered at 97); without OpenZElectroFx the sparks, light and sound still play.
    /// </summary>
    [DefaultExecutionOrder(95)]
    public class ElectricZaps : MonoBehaviour
    {
        public static ElectricZaps I { get; private set; }
        /// <summary>Options switch (Batch C menu).</summary>
        public static bool Enabled = true, AutoCreate = true;
        public static float DamagedAt = 0.5f, BadAt = 0.25f, MinGap = 0.45f, EveryMin = 6f, EveryMax = 20f, LampMin = 7f, LampMax = 22f;
        public static int MaxActive = 3;
        public static readonly Color ArcColor = new Color(0.55f, 0.78f, 1f);                   // linear: pale blue-white short circuit
        public static readonly Color LightColor = new Color(0.62f, 0.8f, 1f);
        public enum Source { Building, Gate, Vehicle, Lamp, Crane }
        public static int Zaps, OnBuildings, OnGates, OnVehicles, OnLamps, OnCrane, Gated, OffScreen;

        static readonly string[] ClipsShort = { "sd_robot_death_short_circuit_a", "sd_robot_death_short_circuit_b", "sd_robot_death_arc_crackle", "Electro/eb_spark_a", "Electro/eb_spark_b", "Electro/eb_spark_c" };
        static readonly string[] ClipsLamp = { "sd_lamp_spark_a", "sd_lamp_spark_b", "sd_lamp_spark_c" };
        static readonly string[] ClipsWeld = { "Electro/eb_spark_a", "Electro/eb_spark_b", "Electro/eb_spark_c" };

        sealed class Zap
        {
            public Transform follow; public bool hasFollow; public Vector3 a, b, f, n; public bool fork, second;   // a / b / fork end in follow's local space (world if follow is null)
            public float t0, life, gapAt, gapEnd, nextShape; public uint seed; public float width; public Source src;
        }
        readonly List<Zap> active = new List<Zap>();
        readonly Dictionary<int, float> nextObj = new Dictionary<int, float>();
        readonly Dictionary<NightFixture, float> nextLamp = new Dictionary<NightFixture, float>();
        static readonly Dictionary<string, (int n, float first, float last)> rate = new Dictionary<string, (int, float, float)>();
        float nextScan, lastStart = -10f;
        Camera cam;
        readonly float[] jt = new float[17]; readonly Vector2[] joff = new Vector2[17]; readonly Vector3[] pts = new Vector3[17];

        // ------------------------------------------------------------------ boot (no edit in any existing file)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }
        static void OnSceneLoaded(Scene s, LoadSceneMode m) { if (AutoCreate && Enabled && !I && (ZGame.I || FindFirstObjectByType<ZGame>())) Ensure(); }
        public static ElectricZaps Ensure()
        {
            if (!I && Application.isPlaying) new GameObject("OpenZElectricZaps").AddComponent<ElectricZaps>();
            return I;
        }

        void Awake() { if (I && I != this) { Destroy(gameObject); return; } I = this; rate.Clear(); }
        void OnDestroy() { if (I == this) I = null; }

        // ------------------------------------------------------------------ scheduling
        void Update()
        {
            if (!Enabled) return;
            float now = Time.time;
            if (now < nextScan) return;
            nextScan = now + 0.3f;
            var game = ZGame.I; if (game == null || game.World == null) return;
            if (!cam) cam = Camera.main;
            foreach (var o in game.World.Objects)
            {
                if (o == null || o.Destroyed || o.MaxHealth <= 0) continue;
                bool bld = o.Kind == ZKind.Building && !o.IsBridge, veh = o.Kind == ZKind.Vehicle;
                if (!bld && !veh) continue;
                float frac = o.Health / (float)o.MaxHealth;
                if (frac > DamagedAt) { nextObj.Remove(o.Id); continue; }
                if (!nextObj.TryGetValue(o.Id, out float due)) { nextObj[o.Id] = now + Random.Range(1.5f, 10f); continue; }
                if (now < due) continue;
                var pos = game.CenterWorld(o);
                if (!OnScreen(pos)) { nextObj[o.Id] = now + Random.Range(2f, 6f); OffScreen++; continue; }
                if (!Gate(now)) { nextObj[o.Id] = now + Random.Range(0.5f, 2f); Gated++; continue; }
                bool ok = bld ? ZapBuilding(game, o) : ZapVehicle(game, o);
                nextObj[o.Id] = now + Random.Range(EveryMin, EveryMax) * (frac <= BadAt ? 0.6f : 1f) * (ok ? 1f : 0.3f);
                if (ok) Count((bld ? "bld " : "veh ") + o.Type + "#" + o.Id, now);
            }
            var nl = NightLights.I;
            var fixtures = nl != null ? nl.Fixtures : null;
            if (fixtures != null)
                for (int fi = 0; fi < fixtures.Count; fi++)
                {
                    var f = fixtures[fi];
                    if (f == null || !f.IsLamp || !f.LampGO || !f.LampGO.activeInHierarchy || (f.Damage < 1 && !f.GlassBroken)) continue;
                    if (!nextLamp.TryGetValue(f, out float due)) { nextLamp[f] = now + Random.Range(1.5f, 10f); continue; }
                    if (now < due) continue;
                    var head = f.Head ? f.Head.position : f.LightPos;
                    if (!OnScreen(head)) { nextLamp[f] = now + Random.Range(2f, 6f); OffScreen++; continue; }
                    if (!Gate(now)) { nextLamp[f] = now + Random.Range(0.5f, 2f); Gated++; continue; }
                    var side = Random.insideUnitSphere; side.y = 0f; side = side.sqrMagnitude > 1e-4f ? side.normalized : Vector3.right;
                    var foot = f.Foot; var down = (foot - head).sqrMagnitude > 0.01f ? (foot - head).normalized : Vector3.down;
                    Fire(null, head, head + down * Random.Range(0.3f, 0.55f) + side * 0.12f, Vector3.up, Source.Lamp, 0.04f);
                    nextLamp[f] = now + Random.Range(LampMin, LampMax);
                    Count("lamp " + (f.Id ?? "?"), now);
                }
        }

        bool Gate(float now) => now - lastStart >= MinGap && active.Count < MaxActive;

        bool OnScreen(Vector3 p)
        {
            if (!cam) return false;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f;
        }

        static void Count(string key, float now)
        {
            if (rate.TryGetValue(key, out var r)) rate[key] = (r.n + 1, r.first, now); else rate[key] = (1, now, now);
        }

        // ------------------------------------------------------------------ where
        readonly Dictionary<int, Renderer[]> rendCache = new Dictionary<int, Renderer[]>();
        readonly Dictionary<int, Renderer[]> gateCache = new Dictionary<int, Renderer[]>();

        bool ZapBuilding(ZGame game, ZObject o)
        {
            if (!game.StaticViews.TryGetValue(o.Id, out var g) || !g || !g.activeInHierarchy) return false;
            if (!gateCache.TryGetValue(o.Id, out var gates) || gates == null)
            {
                var l = new List<Renderer>();
                foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true)) if (r.name.StartsWith("gate_") && !r.name.StartsWith("gate_hall")) l.Add(r);
                gateCache[o.Id] = gates = l.ToArray();
            }
            bool gate = gates.Length > 0 && Random.value < 0.5f;
            var set = gate ? gates : Rends(o.Id, g.transform);
            if (!Pair(set, out var r0, out var a, out var b, out var n)) return false;
            Fire(r0.transform, a, b, n, gate ? Source.Gate : Source.Building, 0.05f);
            return true;
        }

        bool ZapVehicle(ZGame game, ZObject o)
        {
            if (!game.Views.TryGetValue(o.Id, out var v) || !v || v.Dying || !v.gameObject.activeInHierarchy) return false;
            if (!Pair(Rends(o.Id, v.transform), out var r0, out var a, out var b, out var n)) return false;
            Fire(r0.transform, a, b, n, Source.Vehicle, 0.04f);
            return true;
        }

        Renderer[] Rends(int id, Transform root)
        {
            if (rendCache.TryGetValue(id, out var rs) && rs != null && rs.Length > 0 && rs[0]) return rs;
            var l = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var n = r.name;
                if (n.StartsWith("WheelSpin") || n.StartsWith("OZ_Crane") || n.Contains("Shadow") || n.Contains("Selection")) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                l.Add(r);
            }
            rendCache[id] = rs = l.ToArray();
            return rs;
        }

        // surface samples per mesh (local vertex + normal), taken once
        static readonly Dictionary<Mesh, (Vector3[] p, Vector3[] n)> samples = new Dictionary<Mesh, (Vector3[], Vector3[])>();

        static (Vector3[] p, Vector3[] n) Samples(Mesh m)
        {
            if (samples.TryGetValue(m, out var s)) return s;
            s = (null, null);
            if (m.isReadable)
            {
                var v = m.vertices; var nn = m.normals;
                if (v.Length > 0)
                {
                    int k = Mathf.Min(64, v.Length); var p = new Vector3[k]; var n = new Vector3[k];
                    for (int i = 0; i < k; i++)
                    {
                        int j = Random.Range(0, v.Length);
                        p[i] = v[j]; n[i] = nn != null && nn.Length == v.Length ? nn[j] : Vector3.up;
                    }
                    s = (p, n);
                }
            }
            samples[m] = s;
            return s;
        }

        /// <summary>Two surface points 0.25-1 m apart on one visible renderer of the set (local to that renderer's transform).</summary>
        static bool Pair(Renderer[] set, out Renderer r0, out Vector3 a, out Vector3 b, out Vector3 nrm)
        {
            r0 = null; a = b = nrm = default;
            if (set == null || set.Length == 0) return false;
            for (int tries = 0; tries < 4; tries++)
            {
                var r = set[Random.Range(0, set.Length)];
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null; if (!m) continue;
                var t = r.transform; var s = Samples(m);
                if (s.p != null)
                {
                    int i = Random.Range(0, s.p.Length); var pa = t.TransformPoint(s.p[i]);
                    int best = -1, far = -1; float bestErr = float.MaxValue, farD = 0f;
                    for (int j = 0; j < s.p.Length; j++)
                    {
                        if (j == i) continue;
                        float d = Vector3.Distance(pa, t.TransformPoint(s.p[j]));
                        if (d > 1.0f) continue;
                        if (d > farD) { farD = d; far = j; }
                        if (d < 0.25f) continue;
                        float err = Mathf.Abs(d - 0.55f); if (err < bestErr) { bestErr = err; best = j; }
                    }
                    var na = t.TransformDirection(s.n[i]).normalized;
                    // 0.45 m along the tangent, i.e. off the part into the air. Now: its farthest own vertex (>= 8 cm), else no zap here.
                    if (best < 0) best = farD >= 0.08f ? far : -1;
                    if (best < 0) continue;
                    a = s.p[i]; b = s.p[best]; nrm = na; r0 = r; return true;
                }
                // not readable: a ray from outside the renderer's bounds onto its collider surface (never a point in the air);
                // no collider hit inside the bounds = no zap this time
                var bb = r.bounds; int face = Random.Range(0, 5);
                Vector3 n = face == 0 ? Vector3.up : face == 1 ? Vector3.right : face == 2 ? Vector3.left : face == 3 ? Vector3.forward : Vector3.back;
                var u = face == 0 ? Vector3.right : Vector3.up; var w = Vector3.Cross(n, u);
                var from = bb.center + Vector3.Scale(n, bb.extents) + n * 1.5f
                         + Vector3.Scale(u, bb.extents) * Random.Range(-0.7f, face == 0 ? 0.7f : 0.2f) + Vector3.Scale(w, bb.extents) * Random.Range(-0.7f, 0.7f);
                float reach = Vector3.Scale(n, bb.size).magnitude + 1.5f;
                if (!Physics.Raycast(from, -n, out var hit, reach, ~0, QueryTriggerInteraction.Ignore)) continue;
                var big = bb; big.Expand(0.1f);
                if (!big.Contains(hit.point) || hit.point.y < bb.min.y + 0.3f) continue;               // the ground or a neighbour, not this part
                var tg = Vector3.Cross(hit.normal, Random.onUnitSphere); if (tg.sqrMagnitude < 1e-4f) tg = Vector3.Cross(hit.normal, Vector3.right);
                var ea = hit.point + hit.normal * 0.02f; var eb = ea + tg.normalized * Random.Range(0.3f, 0.6f);
                a = t.InverseTransformPoint(ea); b = t.InverseTransformPoint(eb); nrm = hit.normal; r0 = r; return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ one zap
        /// <summary>CraneArmRig: an arc-welding zap at the tool tip (global gates apply; true = it started).</summary>
        public static bool Arc(Vector3 a, Vector3 b, Vector3 normal, int emitter, Source src)
        {
            if (!Enabled) return false;
            var z = Ensure(); if (!z) return false;
            float now = Time.time;
            if (!z.Gate(now)) { Gated++; return false; }
            z.Fire(null, a, b, normal, src, 0.035f);
            Count("crane#" + emitter, now);
            return true;
        }

        void Fire(Transform follow, Vector3 a, Vector3 b, Vector3 n, Source src, float width)
        {
            float now = Time.time;
            var z = new Zap
            {
                follow = follow, hasFollow = follow != null, a = a, b = b, n = n, src = src, width = width, t0 = now,
                life = Random.Range(0.12f, 0.28f), seed = (uint)Random.Range(1, int.MaxValue)
            };
            z.gapAt = z.life * Random.Range(0.35f, 0.5f); z.gapEnd = z.gapAt + Random.Range(0.025f, 0.05f);
            var wa = W(z, z.a); var wb = W(z, z.b);
            z.fork = Random.value < 0.6f;
            if (z.fork)
            {
                var mid = (wa + wb) * 0.5f; var off = Random.onUnitSphere * Vector3.Distance(wa, wb) * 0.45f + n * 0.08f;
                z.f = follow ? follow.InverseTransformPoint(mid + off) : mid + off;
            }
            active.Add(z); lastStart = now; Zaps++;
            switch (src) { case Source.Building: OnBuildings++; break; case Source.Gate: OnGates++; break; case Source.Vehicle: OnVehicles++; break; case Source.Lamp: OnLamps++; break; case Source.Crane: OnCrane++; break; }
            // the zap frame: sparks (gravity) + white core at the first end, a 0.1 s ground light, the crackle
            var fx = FxManager.I;
            if (fx)
            {
                fx.WeldSparks(wa, n, src == Source.Crane ? 0.35f : 0.5f);
                fx.AddLight((wa + wb) * 0.5f + n * 0.35f, LightColor, src == Source.Lamp ? 2.6f : 3.2f, 6f, 0.1f, 0.7f, true);
            }
            var au = CombatAudio.I;
            if (au) au.PlayPitched("el_zap", wa, src == Source.Crane ? 0.32f : src == Source.Lamp ? 0.45f : 0.5f, 0f, -1, 1f,
                                   src == Source.Lamp ? ClipsLamp : src == Source.Crane ? ClipsWeld : ClipsShort);
        }

        static Vector3 W(Zap z, Vector3 p) => z.hasFollow && z.follow ? z.follow.TransformPoint(p) : p;

        // ------------------------------------------------------------------ draw (before OpenZElectroFx renders at 97)
        void LateUpdate()
        {
            if (active.Count == 0) return;
            float now = Time.time;
            var efx = OpenZElectroFx.I; var arcs = efx ? efx.Arcs : null;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var z = active[i]; float age = now - z.t0;
                if (age >= z.life || (z.hasFollow && !z.follow)) { active.RemoveAt(i); continue; }   // over, or its part is gone
                var wa = W(z, z.a); var wb = W(z, z.b);
                if (!z.second && age >= z.gapEnd)
                {   // the second burst: a few sparks off the other end
                    z.second = true; FxManager.I?.WeldSparks(wb, z.n, 0.3f);
                }
                if (age >= z.gapAt && age < z.gapEnd) continue;                                      // the stutter: dark for a frame or two
                if (arcs == null) continue;
                if (now >= z.nextShape) { z.seed = z.seed * 1664525u + 1013904223u; z.nextShape = now + Random.Range(0.03f, 0.05f); }
                float env = (age < z.gapAt ? 1f : 0.8f) * (1f - 0.5f * Mathf.Clamp01((age - z.life * 0.7f) / (z.life * 0.3f)));
                var rng = new ElectroBoltShape.Rng(z.seed);
                int n = ElectroBoltShape.Jag(ref rng, 3, 0.22f, 0.62f, jt, joff);
                ElectroBoltShape.Place(wa, wb, jt, joff, n, pts, 0f, 0f, 0f, 0.14f, z.n);
                arcs.Ribbon(pts, n, z.width, 0.32f, 7f, 1.3f, ArcColor, env, z.seed * 1e-6f, 0.6f, 0.6f, 2f);
                if (z.fork)
                {
                    var wf = W(z, z.f); var from = pts[n / 2];
                    int m = ElectroBoltShape.Jag(ref rng, 2, 0.25f, 0.6f, jt, joff);
                    ElectroBoltShape.Place(from, wf, jt, joff, m, pts);
                    arcs.Ribbon(pts, m, z.width * 0.7f, 0.3f, 6f, 1.1f, ArcColor, env * 0.8f, z.seed * 2e-6f, 0.8f, 0.2f, 1.5f);
                }
                arcs.Glow(wa, 0.22f, 0.3f, 6f, 1.4f, ArcColor, env * 0.9f, 3f);
                arcs.Glow(wb, 0.16f, 0.3f, 5f, 1.2f, ArcColor, env * 0.7f, 2f);
            }
        }

        // ------------------------------------------------------------------ report
        public static string Report()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"ElectricZaps: enabled {Enabled}, zaps {Zaps} (buildings {OnBuildings}, gates {OnGates}, vehicles {OnVehicles}, lamps {OnLamps}, crane {OnCrane}), ");
            sb.Append($"held by the global gate {Gated}, off screen {OffScreen}, active {(I ? I.active.Count : 0)}\n");
            sb.Append("  per object (count, mean interval s; the rule: 6-20 s, unsynced):\n");
            foreach (var kv in rate)
            {
                var r = kv.Value; string mean = r.n > 1 ? ((r.last - r.first) / (r.n - 1)).ToString("F1") : "-";
                sb.Append($"    {kv.Key}: {r.n} zaps, mean {mean} s\n");
            }
            return sb.ToString();
        }

        /// <summary>Menu "Zaps: show one now" (Play): one zap on the damaged (else any) building / vehicle nearest the view centre,
        /// ignoring the per-object clock (the global gate still applies).</summary>
        public static string ShowOne()
        {
            var z = Ensure(); var game = ZGame.I;
            if (!z || game == null || game.World == null) return "no game";
            if (!z.cam) z.cam = Camera.main;
            if (!z.cam) return "no camera";
            var ray = z.cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            var c = ray.origin + ray.direction * (ray.direction.y < -0.01f ? -ray.origin.y / ray.direction.y : 60f);
            ZObject best = null; float bd = float.MaxValue;
            foreach (var o in game.World.Objects)
            {
                if (o == null || o.Destroyed || o.MaxHealth <= 0 || !((o.Kind == ZKind.Building && !o.IsBridge) || o.Kind == ZKind.Vehicle)) continue;
                float d = (game.CenterWorld(o) - c).sqrMagnitude * (o.Health <= o.MaxHealth * DamagedAt ? 0.25f : 1f);
                if (d < bd) { bd = d; best = o; }
            }
            if (best == null) return "nothing to zap";
            z.lastStart = -10f;
            bool ok = best.Kind == ZKind.Building ? z.ZapBuilding(game, best) : z.ZapVehicle(game, best);
            return ok ? $"zap on {best.Type}#{best.Id} ({best.Health}/{best.MaxHealth})" : $"no surface on {best.Type}#{best.Id}";
        }
    }
}
