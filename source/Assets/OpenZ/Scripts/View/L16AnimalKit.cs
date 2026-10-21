using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// What the animals of level 16 share (CrocodileView, JungleFauna): their sounds with the sound log, their materials and a model
    /// with its takes played by hand.
    ///   SOUNDS: every play request is one line of the sound log (source "L16", the event name, the clip, what became of it).
    ///   An event whose file is not in Resources/OpenZ/Audio/Sfx/L16 is logged as "DROP clip missing" and stays silent: never
    ///   another sound in its place. An event without a prepared sound looks for files named after it (sd_ + the event name + _a.wav and on). One-shots come from a small pool of 3D sources on the world bus; loops are sources of their own.
    ///   MODELS: Resources/OpenZ/L16Animals (import settings: Editor/OpenZL16Animals.cs). A rig plays any two takes mixed, at times
    ///   set by hand, and is only evaluated while one of its renderers is on screen.
    /// View only.
    /// </summary>
    public static class L16AnimalKit
    {
        public const string SoundRes = "OpenZ/Audio/Sfx/L16/", ModelRes = "OpenZ/L16Animals/";
        static AudioSource[] pool; static int next;
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly Dictionary<string, int> turn = new Dictionary<string, int>();
        /// <summary>Checks: play requests per event since the level started (played or dropped).</summary>
        public static readonly Dictionary<string, int> Asked = new Dictionary<string, int>();
        public static readonly Dictionary<string, int> Played = new Dictionary<string, int>();

        public static void NewLevel() { Asked.Clear(); Played.Clear(); turn.Clear(); pool = null; }

        static float Bus() => CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;

        public static AudioClip Clip(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            if (!clips.TryGetValue(file, out var c) || !c) { c = Resources.Load<AudioClip>(SoundRes + file); clips[file] = c; }
            return c;
        }

        static void Count(Dictionary<string, int> d, string k) { d.TryGetValue(k, out int n); d[k] = n + 1; }

        static readonly Dictionary<string, string[]> byName = new Dictionary<string, string[]>();
        /// <summary>An event that was given no file plays the files named after it, when they are there: sd_&lt;event&gt;_a to _e (.wav) in
        /// the level's sound folder. So a sound that was missing at first is heard as soon as its file is copied in, with no code change.</summary>
        static string[] ByName(string evt)
        {
            if (!byName.TryGetValue(evt, out var f)) { f = new[] { "sd_" + evt + "_a", "sd_" + evt + "_b", "sd_" + evt + "_c", "sd_" + evt + "_d", "sd_" + evt + "_e" }; byName[evt] = f; }
            return f;
        }

        /// <summary>One shot at a place. files: the takes of this event, used in turn. Returns the clip that played (null = silent).</summary>
        public static AudioClip Play(string evt, Vector3 at, float vol, float minDist, float maxDist, float pitch, params string[] files)
        {
            Count(Asked, evt);
            AudioClip c = null; string picked = null;
            if (files == null || files.Length == 0) files = ByName(evt);
            if (files != null && files.Length > 0)
            {
                turn.TryGetValue(evt, out int t); turn[evt] = t + 1;
                for (int k = 0; k < files.Length && !c; k++) { picked = files[(t + k) % files.Length]; c = Clip(picked); }
            }
            if (!c) { OzSoundLog.Add("L16", evt, null, "DROP clip missing (" + files[0] + ")", vol, at); return null; }
            if (pool == null || !pool[0])
            {
                var go = new GameObject("L16Animals_Audio"); pool = new AudioSource[10];
                for (int i = 0; i < pool.Length; i++)
                {
                    var s = new GameObject("src" + i).AddComponent<AudioSource>(); s.transform.SetParent(go.transform, false);
                    s.playOnAwake = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0f; pool[i] = s;
                }
            }
            var a = pool[next]; next = (next + 1) % pool.Length;
            a.transform.position = at; a.clip = c; a.pitch = pitch; a.minDistance = minDist; a.maxDistance = maxDist; a.volume = vol * Bus() * OzJungleLife.Gain(evt); a.loop = false;
            a.Play();
            Count(Played, evt);
            OzSoundLog.Add("L16", evt, c.name, "PLAY World", vol, at);
            return c;
        }

        /// <summary>A loop of its own (wake, roll): null and one "DROP" line when the file is missing. Stop it with <see cref="StopLoop"/>.</summary>
        public static AudioSource Loop(string evt, Transform parent, Vector3 at, float vol, float minDist, float maxDist, params string[] files)
        {
            Count(Asked, evt);
            AudioClip c = null;
            turn.TryGetValue(evt, out int t); turn[evt] = t + 1;
            for (int k = 0; files != null && k < files.Length && !c; k++) c = Clip(files[(t + k) % files.Length]);
            if (!c) { OzSoundLog.Add("L16", evt, null, "DROP clip missing" + (files != null && files.Length > 0 ? " (" + files[0] + ")" : ""), vol, at); return null; }
            var go = new GameObject("L16Loop_" + evt); go.transform.SetParent(parent, false); go.transform.position = at;
            var s = go.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.spatialBlend = 1f; s.dopplerLevel = 0f; s.playOnAwake = false;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = minDist; s.maxDistance = maxDist; s.volume = vol * Bus(); s.Play();
            Count(Played, evt);
            OzSoundLog.Add("L16", evt, c.name, "LOOP World", vol, at);
            return s;
        }

        public static void LoopVolume(AudioSource s, float vol, float pitch = 1f) { if (s) { s.volume = vol * Bus(); s.pitch = pitch; } }

        public static void StopLoop(string evt, ref AudioSource s)
        {
            if (!s) { s = null; return; }
            OzSoundLog.Add("L16", evt, s.clip ? s.clip.name : null, "LOOP STOP");
            Object.Destroy(s.gameObject); s = null;
        }

        // ------------------------------------------------------------------ materials
        /// <summary>The game's own surface look for a textured animal (no team paint, no grime, dark at night like the scenery).</summary>
        public static Material Mat(string name, string textureRes, float smooth = 0.18f)
        {
            var m = new Material(DetailKit.Lit) { name = name, hideFlags = HideFlags.DontSave };
            var tex = Resources.Load<Texture2D>(ModelRes + textureRes);
            if (tex) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex; }
            else Debug.LogWarning("[OpenZ] level 16 animals: picture " + ModelRes + textureRes + " missing");
            DetailKit.F(m, "_UseVertexColor", 0f); DetailKit.F(m, "_TeamMode", 0f); DetailKit.F(m, "_Degreen", 0f); DetailKit.F(m, "_Stylize", 1f); DetailKit.F(m, "_Degrime", 0f); DetailKit.F(m, "_FlatShade", 0f);
            DetailKit.F(m, "_SoftFill", 0.2f); DetailKit.F(m, "_AmbientBoost", 1f); DetailKit.F(m, "_Smoothness", smooth); DetailKit.F(m, "_Metallic", 0f); DetailKit.F(m, "_RimPower", 3.4f); DetailKit.F(m, "_NightClass", 0f);
            DetailKit.C(m, "_KeyWarm", new Color(1.06f, 1f, 0.92f, 1f)); DetailKit.C(m, "_RimColor", new Color(0.5f, 0.46f, 0.38f, 1f));
            DetailKit.C(m, "_BaseColor", Color.white); DetailKit.C(m, "_EmissionColor", Color.black);
            return DetailKit.Track(m);
        }

        // ------------------------------------------------------------------ a model with its takes
        public sealed class Rig
        {
            public Transform root, orient, model; public Renderer[] rends; public bool ok;
            PlayableGraph graph; AnimationMixerPlayable mix; AnimationClipPlayable[] play; AnimationClip[] clip; string[] names;
            int a = -1, b = -1; bool shown = true;

            /// <summary>res: the model under Resources/OpenZ/L16Animals. Every take of the file becomes an input (a file with one long
            /// take has one input and is posed by time).</summary>
            public static Rig Make(string res, string name, Transform parent, Material[] mats, float scale, bool shadows)
            {
                var prefab = Resources.Load<GameObject>(ModelRes + res);
                if (!prefab) { Debug.LogWarning("[OpenZ] level 16 animals: model " + ModelRes + res + " missing (menu OpenZ/L16 Jungle/10 Animals)"); return null; }
                var r = new Rig { root = new GameObject(name).transform };
                r.root.SetParent(parent, false);
                // the correction of the way a file faces lives on its own node: a take keys the model's own root
                r.orient = new GameObject("Orient").transform; r.orient.SetParent(r.root, false);
                var go = Object.Instantiate(prefab, r.orient, false); go.name = "Model"; r.model = go.transform;
                go.transform.localScale = go.transform.localScale * scale;
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                r.rends = go.GetComponentsInChildren<Renderer>(true);
                foreach (var rd in r.rends)
                {
                    var old = rd.sharedMaterials; var arr = new Material[Mathf.Max(1, old.Length)];
                    for (int i = 0; i < arr.Length; i++)
                    {   // a second material of the file (the crocodile's eyes) by its name, else by its place
                        int pick = 0;
                        if (mats.Length > 1) pick = (i < old.Length && old[i] && old[i].name.ToLowerInvariant().Contains("eye")) || (i == 1 && !(old[0] && old[0].name.ToLowerInvariant().Contains("eye"))) ? 1 : 0;
                        arr[i] = mats[Mathf.Min(pick, mats.Length - 1)];
                    }
                    rd.sharedMaterials = arr; rd.receiveShadows = true;
                    rd.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    if (rd is SkinnedMeshRenderer sk) sk.updateWhenOffscreen = true;
                }
                var found = new List<AnimationClip>();
                foreach (var c in Resources.LoadAll<AnimationClip>(ModelRes + res)) if (c && !c.name.StartsWith("__preview__")) found.Add(c);
                found.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
                if (found.Count == 0) { Debug.LogWarning("[OpenZ] level 16 animals: no take in " + ModelRes + res); return r; }
                if (found.Count == 1) found.Add(found[0]);                                        // one long take: two inputs of it, so two of its moments can be mixed
                var an = go.GetComponent<Animator>(); if (!an) an = go.AddComponent<Animator>();
                an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                r.graph = PlayableGraph.Create("OpenZ." + name);
                r.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(r.graph, "L16Animal", an);
                r.mix = AnimationMixerPlayable.Create(r.graph, found.Count);
                r.clip = found.ToArray(); r.play = new AnimationClipPlayable[found.Count]; r.names = new string[found.Count];
                for (int i = 0; i < found.Count; i++)
                {
                    string n = found[i].name; int bar = n.LastIndexOf('|'); r.names[i] = bar >= 0 ? n.Substring(bar + 1) : n;
                    r.play[i] = AnimationClipPlayable.Create(r.graph, found[i]);
                    r.play[i].SetApplyFootIK(false); r.play[i].SetSpeed(0.0);
                    r.graph.Connect(r.play[i], 0, r.mix, i); r.mix.SetInputWeight(i, i == 0 ? 1f : 0f);
                }
                output.SetSourcePlayable(r.mix);
                r.graph.Play();
                r.ok = true;
                return r;
            }

            /// <summary>The files do not all face the same way: when the head bone lies behind the tail bone in the rig's own space
            /// (+Z is ahead), the model is turned half round. Call once, before the first pose.</summary>
            public void FaceAhead(string headBone, string tailBone)
            {
                var h = Find(model, headBone); var t = Find(model, tailBone);
                if (!h || !t) return;
                var d = root.InverseTransformPoint(h.position) - root.InverseTransformPoint(t.position);
                if (d.z < 0f) orient.localRotation = Quaternion.Euler(0f, 180f, 0f) * orient.localRotation;
            }

            public int Takes => clip != null ? clip.Length : 0;
            public int Take(string name) { if (names != null) for (int i = 0; i < names.Length; i++) if (names[i] == name) return i; return -1; }
            public float Length(int take) => take >= 0 && clip != null && take < clip.Length ? clip[take].length : 0f;
            public bool Visible() { for (int i = 0; i < rends.Length; i++) if (rends[i] && rends[i].isVisible) return true; return false; }
            public void Show(bool on) { if (on == shown) return; shown = on; for (int i = 0; i < rends.Length; i++) if (rends[i]) rends[i].enabled = on; }
            public bool Shown => shown;

            /// <summary>Take A at time ta mixed with take B at time tb (wb = B's share). force: also while off screen (a bone is read).</summary>
            public void Pose(int takeA, float ta, int takeB, float tb, float wb, bool force = false)
            {
                if (!ok || takeA < 0 || !shown) return;
                if (!force && !Visible()) return;
                if (takeB < 0 || takeB == takeA) { takeB = takeA; wb = 0f; }
                if (a < 0 && takeA != 0 && takeB != 0) mix.SetInputWeight(0, 0f);                           // the first pose: the first take stood at full weight since the rig was made (so an animal nobody has looked at yet has a pose); with a second take at full weight on top every bone was doubled along the whole chain
                if (a >= 0 && a != takeA && a != takeB) mix.SetInputWeight(a, 0f);
                if (b >= 0 && b != takeA && b != takeB) mix.SetInputWeight(b, 0f);
                a = takeA; b = takeB;
                play[takeA].SetTime(Mathf.Clamp(ta, 0f, clip[takeA].length)); mix.SetInputWeight(takeA, takeB == takeA ? 1f : 1f - wb);
                if (takeB != takeA) { play[takeB].SetTime(Mathf.Clamp(tb, 0f, clip[takeB].length)); mix.SetInputWeight(takeB, wb); }
                graph.Evaluate(0f);
            }

            public void Dispose() { if (ok && graph.IsValid()) graph.Destroy(); ok = false; if (root) Object.Destroy(root.gameObject); }
        }

        public static Transform Find(Transform t, string n)
        {
            if (!t) return null;
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), n); if (r) return r; }
            return null;
        }

        /// <summary>A place is seen by the game camera, with a rim of `rim` metres round the picture (the camera looks down at the
        /// ground: the rim is laid on the ground plane).</summary>
        public static bool InView(Camera cam, Vector3 p, float rim)
        {
            if (!cam) return true;
            var v = cam.WorldToViewportPoint(p);
            if (v.z <= 0f) return false;
            float perUnit = Mathf.Max(0.0001f, 2f * v.z * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));   // metres the picture is high at that depth
            float my = rim / perUnit, mx = rim / (perUnit * cam.aspect);
            return v.x > -mx && v.x < 1f + mx && v.y > -my && v.y < 1f + my;
        }
    }
}
