using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 378: drives OzTankWreck's main-thread share (reading the baked fragments, building the pieces' meshes, a little each
    /// frame) and, a few seconds after a level is running, makes the break-ups of the tanks on the map (OzTankWreck.WarmAll) so a tank's
    /// death never waits for its cut. One hidden object per level. View only.
    /// </summary>
    public class OzTankWreckRunner : MonoBehaviour
    {
        static OzTankWreckRunner one;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (!OzTankWreck.On || m != LoadSceneMode.Single || s.name == "MainMenu" || s.name == "Intro") return;
            Ensure();
            if (one) one.StartCoroutine(one.Warm());
        }

        /// <summary>The runner of this level (made when missing).</summary>
        internal static void Ensure()
        {
            if (one) return;
            var go = new GameObject("OZ_TankWreckRunner") { hideFlags = HideFlags.HideInHierarchy };
            one = go.AddComponent<OzTankWreckRunner>();
        }

        IEnumerator Warm()
        {
            float until = Time.realtimeSinceStartup + 30f;
            while ((!ZGame.I || ZGame.I.World == null) && Time.realtimeSinceStartup < until) yield return null;
            float at = Time.realtimeSinceStartup + Mathf.Max(0f, OzTankWreck.WarmDelay);
            while (Time.realtimeSinceStartup < at) yield return null;                                     // after the level's own start-up work
            if (ZGame.I && ZGame.I.World != null) OzTankWreck.WarmAll();
        }

        void Update() => OzTankWreck.Tick();

        void OnDestroy() { if (one == this) one = null; }
    }
}
