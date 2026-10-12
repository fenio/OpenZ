using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Scene flow glue for the front end (MainMenu = build index 0, then L02_Desert and L17_CarPark).
    ///
    /// Unity runs [RuntimeInitializeOnLoadMethod(AfterSceneLoad)] once, after the FIRST scene of the session. Most OpenZ
    /// auto-installers also hook SceneManager.sceneLoaded, but a few boot only there. With the main menu as the first scene
    /// they would never see a level (and back in the menu, the audio director would be gone with the level). This helper
    /// re-runs exactly those boot methods one frame after every later scene load (the old scene is destroyed by then), by
    /// name and only if they exist, so no other file needs an edit. Each target guards against doubles itself.
    /// Levels started directly in the editor are the first scene: nothing changes for them.
    /// </summary>
    public sealed class OzSceneFlow : MonoBehaviour
    {
        /// <summary>(type, static method) pairs that only run for the first scene of a session.</summary>
        static readonly (string type, string method, bool levelOnly)[] FirstSceneOnlyBoots =
        {
            ("OpenZ.AudioDirector", "AutoBoot", false),
            ("OpenZ.GameBoot", "ApplyTestMode", true),      // test mode (oz_testmode = 1): atmosphere / SSAO / shadows off in the level
            ("OpenZ.LegacyMatchCheck", "Boot", true),
        };

        static OzSceneFlow runner;
        static int loads;
        bool pending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            loads = 0;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (++loads == 1) return;                                                                      // the first scene: Unity runs the boots itself
            if (!runner)
            {
                var go = new GameObject("OZ_SceneFlow") { hideFlags = HideFlags.HideInHierarchy };
                DontDestroyOnLoad(go);
                runner = go.AddComponent<OzSceneFlow>();
            }
            runner.pending = true;
            runner.StartCoroutine(runner.NextFrame());
        }

        IEnumerator NextFrame()
        {
            yield return null;
            if (!pending) yield break;
            pending = false;
            bool level = FindFirstObjectByType<ZGame>() != null;
            foreach (var (typeName, method, levelOnly) in FirstSceneOnlyBoots)
            {
                if (levelOnly && !level) continue;
                var m = Find(typeName, method);
                if (m == null) continue;
                try { m.Invoke(null, null); }
                catch (Exception e) { Debug.LogWarning($"[OpenZ SceneFlow] {typeName}.{method}: {(e.InnerException ?? e).Message}"); }
            }
        }

        static MethodInfo Find(string typeName, string method)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(typeName);
                if (t == null) continue;
                return t.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            }
            return null;
        }
    }
}
