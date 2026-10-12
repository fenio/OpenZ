using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace OpenZ
{
    /// <summary>
    /// OpenZNukeStrike.Update 68 ms, of which NukeFx.Sequence MoveNext 27 ms and Mono.JIT 21 ms on first call). At level load,
    /// spread over frames (at most <see cref="FrameBudgetMs"/> per frame, after OpenZEnding's own prewarm has started):
    /// <list type="number">
    /// <item><b>JIT</b>: RuntimeHelpers.PrepareMethod on every non-generic method (and the compiler-generated coroutine / lambda
    /// classes) of the fort-fall path: OpenZNukeStrike, NukeFx, OpenZFortShockwave, OpenZHeavyShells, OpenZBloomDirector,
    /// OpenZEnding, OpenZVictory, BuildingBreaker, RulesView, FxManager, OpenZFxPlayer, OpenZFxShake, CombatAudio, AudioDirector,
    /// ScorchDecals, PropBreakables. Mono compiles them now instead of in the impact frame (IL2CPP: already AOT, a no-op).</item>
    /// <item><b>Objects</b>: NukeFx's flash overlay canvas (alpha 0), the shockwave's DEC_ prop cache and shimmer quad, the heavy
    /// shells (shell mesh bake, materials, whistle clips), the whistle / boom audio data.</item>
    /// <item><b>GPU</b>: OpenZ/HeatDistort (both modes) and the heavy-shell materials drawn once at sub-pixel size in front of the
    /// camera (pipeline-state warm-up; OpenZEnding already warms OpenZ/Nuke and OpenZ/FxAdditive).</item>
    /// </list>
    /// an editor script: <c>return OpenZ.OpenZNukePrewarm.Status();</c>
    /// </summary>
    public sealed class OpenZNukePrewarm : MonoBehaviour
    {
        public static bool AutoCreate = true;
        public static float FrameBudgetMs = 4f;
        public static bool Done { get; private set; }
        static int jitted, failed; static float jitMs, objMs; static string lastError, squashStatus = "squash prewarm: not run"; static string ebWarm = "electrobot warm-up: not run";

        static readonly Type[] HotTypes =
        {
            typeof(OpenZNukeStrike), typeof(NukeFx), typeof(OpenZFortShockwave), typeof(OpenZHeavyShells), typeof(OpenZBloomDirector),
            typeof(OpenZEnding), typeof(OpenZVictory), typeof(BuildingBreaker), typeof(RulesView), typeof(FxManager), typeof(OpenZFxPlayer),
            typeof(OpenZFxShake), typeof(CombatAudio), typeof(AudioDirector), typeof(ScorchDecals), typeof(PropBreakables),
            typeof(OpenZCelebrate), typeof(OpenZVoiceV3), typeof(ZodBriefing), typeof(OpenZDamageStates), typeof(OpenZPyroFire), typeof(OpenZEndScreen),
            typeof(SquashedRemains), typeof(RunOverView), typeof(OzDeathTrace),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded; }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoCreate) return;
            if (!ZGame.I && !FindFirstObjectByType<ZGame>()) return;                                 // game levels only
            if (FindFirstObjectByType<OpenZNukePrewarm>()) return;
            Done = false;
            new GameObject("OpenZNukePrewarm").AddComponent<OpenZNukePrewarm>();
        }

        IEnumerator Start()
        {
            for (int i = 0; i < 6; i++) yield return null;                                           // let the level's own Start work pass
            var sw = new Stopwatch();
            // 1) JIT, time-sliced
            var methods = new List<MethodInfo>();
            foreach (var t in HotTypes) Collect(t, methods, 0);
            sw.Start();
            foreach (var m in methods)
            {
                try { RuntimeHelpers.PrepareMethod(m.MethodHandle); m.MethodHandle.GetFunctionPointer(); jitted++; }
                catch (Exception e) { failed++; lastError = m.DeclaringType?.Name + "." + m.Name + ": " + e.GetType().Name; }
                if (sw.Elapsed.TotalMilliseconds >= FrameBudgetMs) { jitMs += (float)sw.Elapsed.TotalMilliseconds; yield return null; sw.Restart(); }
            }
            jitMs += (float)sw.Elapsed.TotalMilliseconds; sw.Reset();
            yield return null;
            // 2) objects (one per frame)
            sw.Start(); NukeFx.Prewarm(); objMs += Lap(sw); yield return null;
            sw.Start(); OpenZFortShockwave.CacheProps(); OpenZFortShockwave.Quad(); objMs += Lap(sw); yield return null;
            sw.Start(); OpenZHeavyShells.Ensure(); objMs += Lap(sw); yield return null;
            sw.Start(); try { squashStatus = SquashedRemains.Prewarm(); } catch (Exception e) { squashStatus = "squash prewarm: failed, " + e.GetType().Name + ": " + e.Message; } objMs += Lap(sw); yield return null;
            sw.Start();
            foreach (var n in new[] { "OpenZ/Audio/Sfx/sd_shell_whistle_a", "OpenZ/Audio/Sfx/sd_shell_whistle_b", "OpenZ/Audio/Sfx/sd_nuke_shockwave" })
            {
                var c = Resources.Load<AudioClip>(n);
                if (c && c.loadState == AudioDataLoadState.Unloaded) c.LoadAudioData();
            }
            var ca = CombatAudio.I; var heavy = ca ? ca.Clip("heavy") : null; if (heavy && heavy.loadState == AudioDataLoadState.Unloaded) heavy.LoadAudioData();
            objMs += Lap(sw);
            yield return null;
            // 3) GPU warm-up
            yield return WarmRender();
            Done = true;
            Debug.Log($"[OpenZ] nuke prewarm: {jitted} methods JIT-compiled ({failed} skipped) in {jitMs:F0} ms, objects {objMs:F0} ms, spread over frames (<= {FrameBudgetMs:F0} ms each)");
        }

        static float Lap(Stopwatch sw) { float ms = (float)sw.Elapsed.TotalMilliseconds; sw.Reset(); return ms; }

        static void Collect(Type t, List<MethodInfo> into, int depth)
        {
            if (t == null || t.ContainsGenericParameters || depth > 2) return;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMethods(F))
                if (!m.IsAbstract && !m.ContainsGenericParameters && m.GetMethodBody() != null) into.Add(m);
            foreach (var nt in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) Collect(nt, into, depth + 1);   // coroutines, lambdas
        }

        IEnumerator WarmRender()
        {
            var c = Camera.main; if (!c) yield break;
            var root = new GameObject("OpenZ_NukeV3_Warmup").transform;
            var mats = new List<Material>();
            try
            {
                var dm = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
                if (dm && dm.shader && dm.shader.isSupported)
                    for (int mode = 0; mode < 2; mode++)
                    {
                        var m = new Material(dm) { name = "warm_HeatDistort" + mode }; m.SetFloat("_Mode", mode); mats.Add(m);
                        var g = new GameObject("warm_distort" + mode); g.transform.SetParent(root, false);
                        g.AddComponent<MeshFilter>().sharedMesh = OpenZFortShockwave.Quad();
                        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                var lit = Resources.Load<Material>("OpenZ/FX/M_FxLit");
                var sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
                if (lit && sphere)
                {
                    var m = new Material(lit) { name = "warm_ShellBody", enableInstancing = true }; mats.Add(m);
                    var g = new GameObject("warm_shell"); g.transform.SetParent(root, false);
                    g.AddComponent<MeshFilter>().sharedMesh = sphere;
                    var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                // 64 ms on later visits = cold meshes / textures / shader variants): one stripped copy of its view model rides the same
                // sub-pixel warm-up (no scripts, no sounds, never visible), so the real spawn finds everything uploaded
                var eb = ZGame.I ? ZGame.I.Prefab("Z_bs01_electrobot") : null;
                if (eb && OpenZ.Sim.ZWorld.ElectrobotDeviation)
                {
                    var hold = new GameObject("warm_electrobot_hold"); hold.SetActive(false); hold.transform.SetParent(root, false);
                    var g = Instantiate(eb, hold.transform, false);                                     // under an inactive parent: no Awake runs
                    for (int pass = 0; pass < 2; pass++)
                        foreach (var mb in g.GetComponentsInChildren<MonoBehaviour>(true)) { try { DestroyImmediate(mb); } catch (Exception) { } }
                    foreach (var a in g.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
                    foreach (var r in g.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    hold.SetActive(true);
                    ebWarm = "electrobot view warmed (" + g.GetComponentsInChildren<Renderer>(true).Length + " renderers)";
                }
                else ebWarm = "electrobot warm-up skipped (" + (eb ? "Electrobot off" : "no Z_bs01_electrobot prefab") + ")";
            }
            catch (Exception e) { Debug.LogWarning("[OpenZ] nuke prewarm render: " + e.Message); }
            for (int f = 0; f < 3; f++)
            {
                if (!c) break;
                root.SetPositionAndRotation(c.transform.position + c.transform.forward * (c.nearClipPlane + 0.6f) + c.transform.right * (f * 1e-4f), c.transform.rotation);
                root.localScale = Vector3.one * 1e-5f;                                                    // a few hundredths of a pixel, still submitted
                yield return null;
            }
            Destroy(root.gameObject);
            foreach (var m in mats) Destroy(m);
        }

        public static string Status()
        {
            var s = $"nuke prewarm: {(Done ? "done" : "running / not started")}, {jitted} methods JIT ({failed} skipped{(lastError != null ? ", last: " + lastError : "")}), {jitMs:F0} ms JIT, {objMs:F0} ms objects; {squashStatus}; {ebWarm}";
            Debug.Log("[OpenZ] " + s);
            return s;
        }
    }
}
