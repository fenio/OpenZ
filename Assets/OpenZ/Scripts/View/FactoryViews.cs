using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Owner of the factory rigs (FactoryView): builds one per fort / robot factory / vehicle factory that has a static
    /// view, routes the Sim events (UnitBuilt, CannonStored; destruction / revival / capture are read from the ZObject each
    /// frame) to them, ticks them after the Sim ran, stages
    /// new units after UnitView placed them, draws the vent smoke (instanced FxLit puffs, the FxManager smoke look) and
    /// resolves factory sounds against Resources/OpenZ/Sfx (missing ones are logged once for the sound coverage check).
    /// Execution order 70: Update sees this frame's Sim events before CombatView (50) clears them in LateUpdate, and
    /// LateUpdate runs after UnitView (60). Boots itself in every level scene (ZGame present); see README_IMPORT.md.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class FactoryViews : MonoBehaviour
    {
        public static FactoryViews I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add FactoryViews / RallyPoints by hand instead of the auto boot.</summary>
        public static bool AutoBoot = true;
        /// <summary>Audio hook for the audio manager / manifest: (event key, building, position). Keys: factory_door_open,
        /// factory_door_close, factory_unit_out, factory_hum_on / factory_hum_off (Zod's single shared factory loop), rally_set.
        /// While anything is subscribed, FactoryViews plays none of its default sounds.</summary>
        public static event System.Action<string, ZObject, Vector3> AudioEvent;

        [Header("Door timing (sim seconds)")]
        [Tooltip("the door starts opening this long before the Sim finishes a unit (BFinal)")] public float doorLead = 1.0f;
        public float openTime = 0.7f, closeTime = 0.9f;
        [Header("Parts")]
        public bool doors = true, lights = true, smoke = true, hum = true, doorLight = true;
        [Tooltip("new units start inside the opening and walk / roll out (view only)")] public bool stageExits = true;
        [Range(0, 1)] public float humVolume = 0.2f;
        [Tooltip("per-model door / light / vent layout, fractions of the model bounds (README_IMPORT.md)")]
        public List<FactoryDoorSpec> specs = FactoryDoorSpec.Defaults();

        [Tooltip("play the built-in factory sounds through CombatAudio; they are skipped anyway as soon as an audio manager subscribes to FactoryViews.AudioEvent")]
        public bool playDefaultSounds = true;

        ZGame game; double lastSim = -1;
        readonly Dictionary<int, FactoryView> views = new Dictionary<int, FactoryView>();
        public IReadOnlyDictionary<int, FactoryView> Views => views;

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); }
            TryAttach();
        }

        /// <summary>Level scenes only (ZGame present): add FactoryViews and put RallyPoints next to PlayerInput.</summary>
        public static void TryAttach()
        {
            if (!AutoBoot || !ZGame.I) return;
            if (!FindFirstObjectByType<FactoryViews>()) new GameObject("FactoryViews").AddComponent<FactoryViews>();
            var pi = FindFirstObjectByType<PlayerInput>();
            if (pi && !pi.GetComponent<RallyPoints>()) pi.gameObject.AddComponent<RallyPoints>();
        }

        void Awake() { I = this; }

        void Start()
        {
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            Build();
        }

        public void Build()
        {
            if (game == null || game.World == null) return;
            int n = 0;
            foreach (var o in game.World.Objects)
            {
                if (o.Kind != ZKind.Building || !o.IsProducer || views.ContainsKey(o.Id)) continue;
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) continue;
                var spec = SpecFor(o.Type); if (spec == null) continue;
                var rig = new GameObject($"FactoryRig_{o.Type}_{o.Id}"); rig.transform.SetParent(transform, false);
                var fv = rig.AddComponent<FactoryView>();
                if (!fv.Init(this, game, o, go.transform, spec)) { Destroy(rig); continue; }
                views[o.Id] = fv; n++;
            }
            lastSim = game.World.Time;
            Debug.Log($"[OpenZ] FactoryViews: {n} factory rigs (doors, lights, progress strip, smoke, rally)");
        }

        [ContextMenu("Rebuild rigs")]
        public void Rebuild()
        {
            foreach (var kv in views) if (kv.Value) Destroy(kv.Value.gameObject);
            views.Clear(); Build();
        }

        public FactoryDoorSpec SpecFor(string type)
        {
            string key = FactoryDoorSpec.KeyOf(type);
            foreach (var s in specs) if (s != null && s.key == key) return s;
            return null;
        }

        public FactoryView For(int buildingId) => views.TryGetValue(buildingId, out var v) && v ? v : null;

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (game == null || game.World == null) return;
            var W = game.World;
            float dtSim = lastSim < 0 ? 0f : Mathf.Max(0f, (float)(W.Time - lastSim)); lastSim = W.Time;
            var ev = W.Events;                                                       // this frame's events, before CombatView clears them
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                switch (e.type)
                {
                    case ZEventType.UnitBuilt: For(e.otherId)?.OnUnitBuilt(W.GetAny(e.objId)); break;   // objId unit, otherId building
                    case ZEventType.CannonStored: For(e.objId)?.OnCannonStored(); break;
                }
            }
            float dtFx = game.paused ? 0f : Time.deltaTime * Mathf.Clamp(game.gameSpeed, 0.25f, 4f);
            foreach (var v in views.Values) if (v) v.Tick(dtSim, dtFx);
            UpdateHum();
            StepPuffs(dtFx);
        }

        // ------------------------------------------------------------------ factory hum (zplayer.cpp:1144-1194)
        // Zod plays ONE ROBOT_FACTORY_SND loop while any robot or vehicle factory in view is alive, owned and not idle
        // (BUILDING or PAUSED at the unit cap); forts never hum. The loop sits at the nearest such factory.
        AudioSource humSrc; bool humState, humTried; ZObject humAt;
        [Tooltip("audio_v2: the permanent fallback hum when no AudioDirector listens (T19 'fan spam'); off, the director spins the fan up on production start")]
        public bool fallbackHum = false;

        void UpdateHum()
        {
            ZObject best = null; float bestD = float.MaxValue;
            var cam = Camera.main;
            if (hum && cam)
                foreach (var v in views.Values)
                {
                    if (!v) continue; var b = v.B;
                    if (b.Destroyed || b.Owner == Team.Neutral || !b.Building || (b.Type != "robot_factory" && b.Type != "vehicle_factory")) continue;
                    var vp = cam.WorldToViewportPoint(v.transform.position);
                    if (vp.z < 0 || vp.x < -0.1f || vp.x > 1.1f || vp.y < -0.1f || vp.y > 1.1f) continue;   // zmap.WithinView
                    float d = (v.transform.position - cam.transform.position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = b; }
                }
            bool on = best != null;
            if (on != humState)
            {
                humState = on; var at = on && views.TryGetValue(best.Id, out var bv) ? bv.transform.position : Vector3.zero;
                if (AudioEvent != null) AudioEvent(on ? "factory_hum_on" : "factory_hum_off", on ? best : humAt, at);
            }
            humAt = best;
            if (AudioEvent != null || !playDefaultSounds || !fallbackHum) { if (humSrc && humSrc.isPlaying) humSrc.Stop(); return; }   // the audio manager owns it (audio_v2: fallback off)
            if (!humSrc && on && !humTried)
            {
                humTried = true;
                var clip = FindClip("factory_hum");
                if (clip)
                {
                    var go = new GameObject("FactoryHum"); go.transform.SetParent(transform, false);
                    humSrc = go.AddComponent<AudioSource>(); humSrc.clip = clip; humSrc.loop = true; humSrc.playOnAwake = false; humSrc.volume = 0;
                    humSrc.spatialBlend = 0.6f; humSrc.rolloffMode = AudioRolloffMode.Linear; humSrc.minDistance = 20f; humSrc.maxDistance = 200f; humSrc.dopplerLevel = 0;
                }
            }
            if (!humSrc) return;
            if (on && views.TryGetValue(best.Id, out var fv) && fv) humSrc.transform.position = fv.transform.position;
            float want = on ? humVolume * (CombatAudio.I ? CombatAudio.I.volume : 1f) : 0f;
            humSrc.volume = Mathf.MoveTowards(humSrc.volume, want, Time.unscaledDeltaTime * 0.5f);
            if (humSrc.volume > 0.001f && !humSrc.isPlaying) humSrc.Play();
            else if (humSrc.volume <= 0.001f && humSrc.isPlaying) humSrc.Pause();
        }

        void LateUpdate()
        {
            if (game == null || game.World == null) return;
            foreach (var v in views.Values) if (v) { v.StageExits(); v.SenseGate(); }
            DrawPuffs();
        }

        // ------------------------------------------------------------------ smoke (instanced, FxLit like FxManager's "smoke")
        class Puff { public Vector3 p, v; public float age, life, s0, s1; public Quaternion rot; }
        class PuffPool { public Material mat; public readonly List<Puff> puffs = new List<Puff>(); public readonly Matrix4x4[] mats = new Matrix4x4[MaxPuffs]; public readonly float[] alpha = new float[MaxPuffs]; public readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock(); }
        const int MaxPuffs = 200;
        readonly Dictionary<string, PuffPool> pools = new Dictionary<string, PuffPool>();
        Mesh puffMesh; float puffNorm = 1f; bool puffInit;
        static readonly Vector3 Wind = new Vector3(0.35f, 0f, 0.12f);

        PuffPool Pool(string key)
        {
            if (pools.TryGetValue(key, out var p)) return p;
            p = new PuffPool();
            var spec = SpecFor(key);
            var sh = FactoryMeshes.FindShader("OpenZ/FxLit");
            var m = new Material(sh) { name = "FactorySmoke_" + key, enableInstancing = true, renderQueue = 3000 };
            var c = spec != null ? spec.smokeColor : new Color(0.44f, 0.41f, 0.38f, 1f); c.a = 1f;
            m.SetColor(FactoryMeshes.BaseColorId, c); m.SetColor("_EmissionColor", Color.black);
            m.SetFloat("_Unlit", 0.15f); m.SetFloat("_SoftEdge", 0.8f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 2f);
            p.mat = m; pools[key] = p; return p;
        }

        void InitPuffMesh()
        {
            puffInit = true;
            var lib = Resources.Load<GameObject>("OpenZ/FX/combat_fx");                 // same smoke mesh as FxManager
            if (lib)
                foreach (var mf in lib.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.name != "FX_Smoke" || !mf.sharedMesh) continue;
                    var m = Instantiate(mf.sharedMesh); var M = lib.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var v = m.vertices; var n = m.normals;
                    for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); if (i < n.Length) n[i] = M.MultiplyVector(n[i]).normalized; }
                    m.vertices = v; if (n.Length == v.Length) m.normals = n; else m.RecalculateNormals(); m.RecalculateBounds();
                    puffMesh = m; break;
                }
            if (!puffMesh) puffMesh = IcoSphere();
            var s = puffMesh.bounds.size; puffNorm = 1f / Mathf.Max(0.01f, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));   // size 1 = 1 m across
        }

        /// <summary>One smoke / steam puff: starts s0 m across, grows to s1, rises at `up` m/s, drifts with the wind.</summary>
        public void EmitPuff(string key, Vector3 at, float s0, float s1, float life, float up)
        {
            if (!smoke) return;
            if (!puffInit) InitPuffMesh();
            var pool = Pool(key);
            Puff pf;
            if (pool.puffs.Count >= MaxPuffs) { pf = pool.puffs[0]; pool.puffs.RemoveAt(0); } else pf = new Puff();
            pf.p = at + new Vector3(Random.Range(-0.08f, 0.08f), 0, Random.Range(-0.08f, 0.08f));
            pf.v = new Vector3(Random.Range(-0.15f, 0.15f), up, Random.Range(-0.15f, 0.15f));
            pf.age = 0; pf.life = life; pf.s0 = s0; pf.s1 = s1; pf.rot = Random.rotation;
            pool.puffs.Add(pf);
        }

        void StepPuffs(float dt)
        {
            if (dt <= 0) return;
            foreach (var pool in pools.Values)
                for (int i = pool.puffs.Count - 1; i >= 0; i--)
                {
                    var p = pool.puffs[i];
                    p.age += dt; if (p.age >= p.life) { pool.puffs.RemoveAt(i); continue; }
                    p.v *= 1f - 0.35f * dt; p.p += (p.v + Wind * Mathf.Clamp01(p.age / p.life * 2f)) * dt;
                }
        }

        void DrawPuffs()
        {
            if (!puffMesh) return;
            foreach (var pool in pools.Values)
            {
                int n = 0;
                foreach (var p in pool.puffs)
                {
                    float k = p.age / p.life;
                    float s = Mathf.Lerp(p.s0, p.s1, 1f - (1f - k) * (1f - k)) * puffNorm;
                    pool.mats[n] = Matrix4x4.TRS(p.p, p.rot, Vector3.one * s);
                    pool.alpha[n] = 0.5f * Mathf.SmoothStep(0f, 1f, k / 0.12f) * (1f - Mathf.SmoothStep(0f, 1f, (k - 0.35f) / 0.65f));
                    if (++n >= MaxPuffs) break;
                }
                if (n == 0) continue;
                pool.mpb.SetFloatArray("_Alpha", pool.alpha);
                Graphics.DrawMeshInstanced(puffMesh, 0, pool.mat, pool.mats, n, pool.mpb, ShadowCastingMode.Off, false);
            }
        }

        static Mesh IcoSphere()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                                        new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized * 0.5f;
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var m = new Mesh { name = "PuffIco" }; m.SetVertices(v); m.SetTriangles(f, 0); m.RecalculateNormals(); m.RecalculateBounds();
            if (Vector3.Dot(m.normals[0], v[0]) < 0) { System.Array.Reverse(f); m.SetTriangles(f, 0); m.RecalculateNormals(); }
            return m;
        }

        void OnDestroy()
        {
            foreach (var p in pools.Values) if (p.mat) Destroy(p.mat);
            if (I == this) I = null;
        }

        // ------------------------------------------------------------------ sounds
        static readonly Dictionary<string, string[]> Candidates = new Dictionary<string, string[]>
        {
            ["factory_door_open"] = new[] { "factory_door_open", "sfx_factory_complete", "sd_door_open", "mech-servo-1", "exit" },
            ["factory_door_close"] = new[] { "factory_door_close", "sd_door_close", "mech-servo-2", "enter" },
            ["factory_hum"] = new[] { "factory_hum", "sd_factory_hum_loop", "sfx_factory_hum" },
            ["rally_set"] = new[] { "rally_set", "sd_ui_rally", "sfx_ui_click" },
        };
        static readonly HashSet<string> missingLogged = new HashSet<string>();
        static HashSet<string> sfxNames;

        static bool HasSfx(string name)
        {
            if (sfxNames == null) { sfxNames = new HashSet<string>(); foreach (var c in Resources.LoadAll<AudioClip>("OpenZ/Sfx")) sfxNames.Add(c.name); }
            return sfxNames.Contains(name);
        }

        /// <summary>First existing clip for a factory sound key (Resources/OpenZ/Sfx), else null (logged once as missing).</summary>
        public static string Resolve(string key)
        {
            if (Candidates.TryGetValue(key, out var list)) foreach (var n in list) if (HasSfx(n)) return n;
            if (missingLogged.Add(key)) Debug.Log($"[OpenZ] sound missing: {key} (FactoryView; add Resources/OpenZ/Sfx/{key})");
            return null;
        }

        public static AudioClip FindClip(string key)
        {
            var n = Resolve(key);
            return n == null ? null : Resources.Load<AudioClip>("OpenZ/Sfx/" + n);
        }

        /// <summary>Audio hook: with a subscriber on AudioEvent (the audio manager) only the event is raised; otherwise the
        /// resolved clip plays through CombatAudio (3D with Zod's in-view rule, or 2D for UI sounds). vol 0 = hook only.</summary>
        public static void Sound(string key, Vector3 pos, int emitter, ZObject b, float vol, bool ui = false)
        {
            if (AudioEvent != null) { AudioEvent(key, b, pos); return; }
            if (vol <= 0f || !CombatAudio.I || (I && !I.playDefaultSounds)) return;
            var n = Resolve(key);
            if (n == null) return;
            if (ui) CombatAudio.I.Play2D(n, vol); else CombatAudio.I.Play(key, pos, vol, 0.3f, emitter, n);
        }
    }
}
