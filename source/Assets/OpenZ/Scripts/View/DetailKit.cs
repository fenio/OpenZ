using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// l17_details_v1: shared helpers of HarbourDetails (L17 pipes, grates, floor lights, hatches, car wrecks) and
    /// DesertCritters (L02 snakes and rabbits). View side only: nothing here reads or writes the sim except World.Events
    /// (Hitscan segments, read like AmbientLife does).
    /// Models: Resources/OpenZ/L17Details/*.fbx (vertex colours, Bake Axis Conversion, L17DetailsImport sets the import);
    /// materials are made at run time on OpenZ/TeamLit (vertex colour, clean toy look like the critters) and the night
    /// package shaders (NightGlow halos / lenses, NightPool ground pools) through NightMeshes.
    /// Sounds: AudioEvent (the audio manager) first, else AudioDirector events that exist, else CombatAudio clips by name
    /// (Resources/OpenZ/Sfx + OpenZ/Audio/Sfx), else a small pool of 3D sources for the ambience clips.
    /// </summary>
    public static class DetailKit
    {
        public const string Res = "OpenZ/L17Details/";

        /// <summary>Audio hook for the audio manager: (sound event id, position, volume 0-1). While anything is subscribed,
        /// the detail systems play none of their default sounds (same contract as AmbientLife.AudioEvent).</summary>
        public static event Action<string, Vector3, float> AudioEvent;
        public static bool AudioEventActive => AudioEvent != null;

        static Shader lit, fx;
        static readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        // ------------------------------------------------------------------ models
        public static GameObject Model(string name, Transform parent)
        {
            var prefab = Resources.Load<GameObject>(Res + name);
            if (!prefab) { Debug.LogWarning($"[OpenZ] L17 details: model {Res}{name} missing (README_IMPORT.md step 2)"); return null; }
            var go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = name;
            FixForward(go.transform);
            return go;
        }

        /// <summary>The kit's FWD marker sits 1 m in front of the root: turn the model about Y if an import put it elsewhere.</summary>
        public static void FixForward(Transform root)
        {
            var f = Find(root, "FWD"); if (!f) return;
            var d = root.InverseTransformPoint(f.position); d.y = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            float a = Vector3.SignedAngle(d.normalized, Vector3.forward, Vector3.up);
            if (Mathf.Abs(a) > 3f)
            {
                var kids = new List<Transform>(); foreach (Transform t in root) kids.Add(t);
                var q = Quaternion.AngleAxis(a, Vector3.up);
                foreach (var t in kids) { t.localPosition = q * t.localPosition; t.localRotation = q * t.localRotation; }
                Debug.Log($"[OpenZ] L17 details: {root.name} forward corrected by {a:F0} deg (FBX axis)");
            }
        }

        /// <summary>Blender part names may carry a ".001" suffix (one kit scene): match the base name.</summary>
        public static string Base(string n) { int i = n.IndexOf('.'); return i < 0 ? n : n.Substring(0, i); }

        public static Transform Find(Transform root, string baseName)
        {
            if (!root) return null;
            if (Base(root.name) == baseName) return root;
            foreach (Transform c in root) { var r = Find(c, baseName); if (r) return r; }
            return null;
        }

        // ------------------------------------------------------------------ materials
        public static Shader Lit => lit ? lit : lit = Shader.Find("OpenZ/TeamLit") ?? Shader.Find("Universal Render Pipeline/Lit");
        public static Shader Fx => fx ? fx : fx = Shader.Find("OpenZ/FxAdditive");

        /// <summary>Clean toy material from the model's vertex colours (TeamLit, no team paint, environment night class).</summary>
        public static Material ToyMat(string name, float smooth = 0.25f, float metal = 0f, float nightClass = 0f, bool flat = false)
        {
            var m = new Material(Lit) { name = name, enableInstancing = true, hideFlags = HideFlags.DontSave };
            F(m, "_UseVertexColor", 1f); F(m, "_VertexColorSRGB", 1f);
            F(m, "_TeamMode", 0f); F(m, "_Degreen", 0f); F(m, "_Stylize", 1f); F(m, "_Degrime", 0f); F(m, "_FlatShade", flat ? 1f : 0f);
            F(m, "_SoftFill", 0.25f); F(m, "_AmbientBoost", 1f); F(m, "_Smoothness", smooth); F(m, "_Metallic", metal); F(m, "_RimPower", 3.4f);
            F(m, "_NightClass", nightClass);
            C(m, "_KeyWarm", new Color(1.05f, 1f, 0.93f, 1f)); C(m, "_RimColor", new Color(0.46f, 0.43f, 0.37f, 1f));
            C(m, "_BaseColor", Color.white); C(m, "_EmissionColor", Color.black);
            created.Add(m);
            return m;
        }

        /// <summary>OpenZ/FxAdditive instance (procedural shapes; see the shader header for _Shape).</summary>
        public static Material FxMat(string name, float shape, float opaque, float lit, Color tint)
        {
            if (!Fx) return null;
            var m = new Material(Fx) { name = name, hideFlags = HideFlags.DontSave };
            m.SetFloat("_Shape", shape); m.SetFloat("_Opaqueness", opaque); m.SetFloat("_Lit", lit); m.SetFloat("_UseVertexColor", 1f);
            m.SetColor("_Color", tint); m.SetFloat("_Intensity", 1f); m.SetFloat("_CoreMix", 0f); m.SetFloat("_Squareness", 1f);
            m.SetFloat("_Wrap", 0.45f); m.SetColor("_EmissionColor", Color.black); m.SetFloat("_RingAlpha", 1f);
            m.renderQueue = 3004;
            created.Add(m);
            return m;
        }

        public static void F(Material m, string p, float v) { if (m && m.HasProperty(p)) m.SetFloat(p, v); }
        public static void C(Material m, string p, Color v) { if (m && m.HasProperty(p)) m.SetColor(p, v); }

        public static void SetMat(GameObject go, Material m, bool shadows)
        {
            if (!go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
        }

        public static void Cleanup() { foreach (var o in created) if (o) UnityEngine.Object.Destroy(o); created.Clear(); }

        /// <summary>A run-time asset (mesh copy, material) destroyed with the level's other detail assets in <see cref="Cleanup"/>.</summary>
        public static T Track<T>(T o) where T : UnityEngine.Object { if (o) created.Add(o); return o; }

        // ------------------------------------------------------------------ world
        /// <summary>Water surface of the level (the WAT_ renderers, as AmbientLife / ZoneMarkers), else level_terrain_data.</summary>
        public static float WaterY(string level)
        {
            float y = float.NegativeInfinity;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("WAT_") && r.gameObject.activeInHierarchy) y = Mathf.Max(y, r.bounds.min.y + 0.02f);
            if (float.IsNegativeInfinity(y) || y > 0.2f || y < -3f) y = level == "L17" ? -0.8f : -0.35f;
            return y;
        }

        /// <summary>Highest terrain point over a disc (flush fixtures sit on it and never float over a dip).</summary>
        public static float TerrainMax(ZGame g, Vector3 c, float r)
        {
            float y = g.TerrainY(c.x, c.z);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                y = Mathf.Max(y, g.TerrainY(c.x + Mathf.Cos(a) * r, c.z + Mathf.Sin(a) * r));
            }
            return y;
        }

        public static float Night => NightGrade.NightK;

        public static bool OnScreen(Camera cam, Vector3 p, float margin = 0.15f)
        {
            if (!cam) return true;
            var v = cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -margin && v.x < 1f + margin && v.y > -margin && v.y < 1f + margin;
        }

        public static float DistPointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float l = ab.sqrMagnitude;
            float t = l > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>The sim's Hitscan segment muzzle -> target (a miss flies on 30 % further), as AmbientLife reads it.</summary>
        public static bool ShotSegment(ZGame game, ZEvent e, out Vector3 from, out Vector3 to)
        {
            from = to = default;
            if (game.Views.TryGetValue(e.objId, out var av) && av) from = av.MuzzlePos();
            else { var a = game.World.GetAny(e.objId); if (a == null) return false; from = game.CenterWorld(a) + Vector3.up; }
            if (game.Views.TryGetValue(e.otherId, out var tv) && tv) to = tv.AimPos();
            else { var t = game.World.GetAny(e.otherId); if (t == null) return false; to = game.CenterWorld(t) + Vector3.up * 0.9f; }
            if (!e.flag) to = from + (to - from) * 1.3f;
            return true;
        }

        // ------------------------------------------------------------------ sound
        /// <summary>One-shot: the AudioEvent hook, else the AudioDirector event if it exists, else the CombatAudio clips.</summary>
        public static void Sound(string id, Vector3 pos, float vol, string caKey, float throttle, int emitter, params string[] clips)
        {
            if (AudioEvent != null) { AudioEvent(id, pos, vol); return; }
            if (AudioDirector.Has(id)) { AudioDirector.Fire(id, pos, emitter, vol); return; }
            if (clips != null && clips.Length > 0 && CombatAudio.I) CombatAudio.I.Play(caKey, pos, vol, throttle, emitter, clips);
        }

        /// <summary>A bible event if it exists (e.g. the snake rattle "amb_l02_hut_critters"), else a clip from any Resources
        /// path through the local 3D pool (ambience clips are not in CombatAudio's folders).</summary>
        public static void SoundRes(string id, string bibleFallback, Vector3 pos, float vol, float pitch, string resPath)
        {
            if (AudioEvent != null) { AudioEvent(id, pos, vol); return; }
            if (AudioDirector.Has(id)) { AudioDirector.Fire(id, pos, -1, vol, pitch); return; }
            if (bibleFallback != null && AudioDirector.Has(bibleFallback)) { AudioDirector.Fire(bibleFallback, pos, -1, vol, pitch); return; }
            Pool3D.Play(resPath, pos, vol, pitch);
        }

        /// <summary>Four 3D one-shot sources (linear rolloff 3 -> 38 m from the listener rig, SFX volume from CombatAudio).</summary>
        public static class Pool3D
        {
            static AudioSource[] src; static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
            static int next;
            public static AudioClip Clip(string res)
            {
                if (string.IsNullOrEmpty(res)) return null;
                if (!clips.TryGetValue(res, out var c)) { c = Resources.Load<AudioClip>(res); clips[res] = c; }
                return c;
            }
            public static void Play(string res, Vector3 pos, float vol, float pitch)
            {
                var c = Clip(res); if (!c) return;
                if (src == null || src[0] == null)
                {
                    var go = new GameObject("L17Details_Audio"); src = new AudioSource[4];
                    for (int i = 0; i < src.Length; i++)
                    {
                        var s = new GameObject("src" + i).AddComponent<AudioSource>(); s.transform.SetParent(go.transform, false);
                        s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 3f; s.maxDistance = 38f;
                        s.dopplerLevel = 0f; src[i] = s;
                    }
                }
                var a = src[next]; next = (next + 1) % src.Length;
                a.transform.position = pos; a.clip = c; a.pitch = pitch * UnityEngine.Random.Range(0.96f, 1.04f);
                a.volume = vol * (CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f);
                a.Play();
            }
        }

        // ------------------------------------------------------------------ small tumbling debris (own physics, no Rigidbody)
        /// <summary>Loose parts of a broken fixture / wreck: thrown, spin, fall, bounce on the ground or sink in the water with a
        /// splash, then (REVIEW-FIX, "bounce, tumble, settle, fade") ease flat over <see cref="SettleTime"/> s onto the face nearest
        /// pop-out). Fixed pool; the renderer list is taken once per throw, nothing is allocated per frame.</summary>
        public sealed class Chunks
        {
            public const float SettleTime = 0.3f;
            struct C { public Transform t; public Vector3 v, w; public float life, rest, sink, scale0; public bool water, landed, alive, settled; public Quaternion r0, r1; public Renderer[] rs; }
            readonly C[] cs = new C[48];
            public int Active { get; private set; }

            public void Throw(Transform t, Vector3 vel, Vector3 spin, float life = 14f)
            {
                if (!t) return;
                for (int i = 0; i < cs.Length; i++)
                {
                    if (cs[i].alive) continue;
                    t.SetParent(null, true);
                    cs[i] = new C { t = t, v = vel, w = spin, life = life, alive = true, scale0 = t.localScale.x, rs = t.GetComponentsInChildren<Renderer>() };
                    return;
                }
                t.gameObject.SetActive(false);                                                   // pool full: just vanish
            }

            /// <summary>The rotation that lays the piece on the face nearest the ground: its most vertical local axis made exactly
            /// vertical (the yaw it landed with is kept).</summary>
            static Quaternion Flat(Quaternion r)
            {
                Vector3 best = r * Vector3.up; float bd = Mathf.Abs(best.y);
                var ax = r * Vector3.right; if (Mathf.Abs(ax.y) > bd) { best = ax; bd = Mathf.Abs(ax.y); }
                var az = r * Vector3.forward; if (Mathf.Abs(az.y) > bd) { best = az; }
                if (best.y < 0f) best = -best;
                return Quaternion.FromToRotation(best, Vector3.up) * r;
            }

            /// <summary>Lowest point of the piece's renderers (world bounds), +inf when it has none.</summary>
            static float MinY(ref C c)
            {
                float y = float.PositiveInfinity;
                if (c.rs != null) for (int k = 0; k < c.rs.Length; k++) if (c.rs[k]) y = Mathf.Min(y, c.rs[k].bounds.min.y);
                return y;
            }

            public void Step(float dt, ZGame game, float waterY)
            {
                int n = 0;
                for (int i = 0; i < cs.Length; i++)
                {
                    if (!cs[i].alive) continue;
                    ref var c = ref cs[i];
                    if (!c.t) { c.alive = false; continue; }
                    n++;
                    c.life -= dt;
                    var p = c.t.position;
                    if (c.water)
                    {   // sinking: slow, wobbly, fading down under the surface
                        c.sink += dt;
                        p.y -= dt * 0.35f; c.t.position = p;
                        c.t.Rotate(c.w * (dt * 0.15f), Space.World);
                        if (c.sink > 3.5f) { c.t.gameObject.SetActive(false); c.alive = false; }
                        continue;
                    }
                    if (!c.landed)
                    {
                        c.v.y -= 9.81f * dt; c.v *= 1f - 0.08f * dt;
                        p += c.v * dt;
                        c.t.Rotate(c.w * dt, Space.World);
                        bool wet = game && OpenZFxPlayer.IsWaterTile(p) || (game && (p.x < 0f || p.z > 0f || p.x > game.World.Map.width * 2f || p.z < -game.World.Map.height * 2f));
                        float g = game ? game.TerrainY(p.x, p.z) : 0f;
                        float wyp = game ? OzWaterLevel.At(game.level, p.x, p.z, waterY) : waterY;                        // each pool at its own height (level 16)
                        if (game && OzWaterLanding.OpenWaterAt(p, g, out float wyo)) { wet = true; wyp = wyo; }           // b27: also the drawn water over a land tile (the widened river, the water under a bridge, a ravine stream), at the height it is drawn
                        if (wet && g < wyp - 0.05f && p.y <= wyp)
                        {
                            p.y = wyp; c.t.position = p; c.water = true;
                            OzWaterLanding.Splash(p, c.t, "detail part", Mathf.Abs(c.v.y));                 // b27: the shared water landing rule (splash, rings and one sound by its size; it then sinks as before)
                            continue;
                        }
                        if (p.y <= g + 0.02f)
                        {
                            p.y = g + 0.02f;
                            if (c.v.y < -2.2f)
                            {
                                c.v = new Vector3(c.v.x * 0.45f, -c.v.y * 0.3f, c.v.z * 0.45f); c.w *= 0.5f;
                                Sound("dtl_debris_land", p, 0.35f, "debris", 0.12f, -1, "final-metal-land-a", "final-metal-land-b", "final-metal-land-c");
                            }
                            else { c.landed = true; c.rest = 0f; c.r0 = c.t.rotation; c.r1 = Flat(c.r0); c.settled = false; }
                        }
                        c.t.position = p;
                        continue;
                    }
                    c.rest += dt;
                    if (!c.settled)
                    {   // ease flat over SettleTime, then rest on the ground (never on an edge, never half inside the quay)
                        float k = Mathf.Clamp01(c.rest / SettleTime);
                        c.t.rotation = Quaternion.Slerp(c.r0, c.r1, k * k * (3f - 2f * k));
                        float g = game ? game.TerrainY(p.x, p.z) : 0f, lo = MinY(ref c);
                        if (!float.IsInfinity(lo)) { p.y += (g + 0.005f) - lo; c.t.position = p; }
                        if (k >= 1f) c.settled = true;
                    }
                    if (c.life < 0.35f)
                    {
                        float k = Mathf.Clamp01(c.life / 0.35f);
                        c.t.localScale = Vector3.one * (c.scale0 * k);
                        if (c.life <= 0f) { c.t.gameObject.SetActive(false); c.alive = false; }
                    }
                }
                Active = n;
            }

            public void Clear()
            {
                for (int i = 0; i < cs.Length; i++) { if (cs[i].alive && cs[i].t) UnityEngine.Object.Destroy(cs[i].t.gameObject); cs[i].alive = false; }
            }
        }

        // ------------------------------------------------------------------ small coloured bits (fur, feathers of glass, sparks)
        /// <summary>Instanced lozenges (like AmbientLife's feathers): thrown, fall, lie briefly, vanish. One draw per colour.</summary>
        public sealed class Bits
        {
            struct B { public Vector3 p, v; public float life, spin, size; public byte col; public bool alive; public Quaternion r; }
            readonly B[] bs = new B[160];
            readonly Material[] mats; readonly Mesh mesh; readonly Matrix4x4[][] batch; readonly int[] cnt;
            int next;

            public Bits(Color[] colours)
            {
                mesh = new Mesh { name = "DetailBit", hideFlags = HideFlags.DontSave };
                mesh.SetVertices(new[] { new Vector3(0, 0.5f, 0), new Vector3(0.5f, 0, 0.15f), new Vector3(0, -0.5f, 0), new Vector3(-0.5f, 0, -0.15f), new Vector3(0, 0, 0.3f), new Vector3(0, 0, -0.3f) });
                mesh.SetTriangles(new[] { 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4, 1, 0, 5, 2, 1, 5, 3, 2, 5, 0, 3, 5 }, 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                created.Add(mesh);
                mats = new Material[colours.Length]; batch = new Matrix4x4[colours.Length][]; cnt = new int[colours.Length];
                for (int i = 0; i < colours.Length; i++)
                {
                    var m = new Material(Lit) { name = "DetailBit" + i, enableInstancing = true, hideFlags = HideFlags.DontSave };
                    F(m, "_TeamMode", 0f); F(m, "_Degreen", 0f); F(m, "_Stylize", 1f); F(m, "_FlatShade", 1f); F(m, "_NightClass", 0f);
                    C(m, "_BaseColor", colours[i]); created.Add(m);
                    mats[i] = m; batch[i] = new Matrix4x4[bs.Length];
                }
            }

            public void Burst(Vector3 at, int count, byte col, float speed, float size, System.Random rng)
            {
                for (int k = 0; k < count; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, u = 0.6f + (float)rng.NextDouble() * 0.8f;
                    bs[next] = new B
                    {
                        p = at, v = new Vector3(Mathf.Cos(a) * speed * u, speed * (0.9f + (float)rng.NextDouble()), Mathf.Sin(a) * speed * u),
                        life = 1.6f + (float)rng.NextDouble() * 1.2f, spin = 200f + (float)rng.NextDouble() * 500f, size = size * (0.7f + (float)rng.NextDouble() * 0.6f),
                        col = (byte)Mathf.Min(col, mats.Length - 1), alive = true, r = UnityEngine.Random.rotationUniform
                    };
                    next = (next + 1) % bs.Length;
                }
            }

            public void StepAndDraw(float dt, ZGame game)
            {
                for (int i = 0; i < cnt.Length; i++) cnt[i] = 0;
                for (int i = 0; i < bs.Length; i++)
                {
                    if (!bs[i].alive) continue;
                    ref var b = ref bs[i];
                    b.life -= dt; if (b.life <= 0f) { b.alive = false; continue; }
                    b.v.y -= 7.5f * dt; b.v *= 1f - 1.2f * dt;
                    b.p += b.v * dt;
                    float g = game ? game.GroundY(b.p.x, b.p.z) + 0.02f : 0f;
                    if (b.p.y < g) { b.p.y = g; b.v = Vector3.zero; b.spin = 0f; }
                    b.r = Quaternion.AngleAxis(b.spin * dt, Vector3.right) * b.r;
                    float s = b.size * Mathf.Clamp01(b.life / 0.4f);
                    batch[b.col][cnt[b.col]++] = Matrix4x4.TRS(b.p, b.r, new Vector3(s, s, s));
                }
                for (int i = 0; i < mats.Length; i++)
                    if (cnt[i] > 0) Graphics.DrawMeshInstanced(mesh, 0, mats[i], batch[i], cnt[i], null, ShadowCastingMode.Off, false);
            }
        }
    }
}
