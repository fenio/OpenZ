using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Shore A/B: A = the live L02_ground_paint.png; B = the same paint with light golden beach sand along about half the shoreline
    /// (irregular stretches 1-3 m wide, widest on inner bends and gentle banks, a slightly damp edge at the water, no dark band).
    /// </summary>
    public static class ShoreBeach
    {
        static readonly int IdBase = Shader.PropertyToID("_BaseMap"), IdMain = Shader.PropertyToID("_MainTex");
        static Texture2D texB;
        public static bool On { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Loaded; UnityEngine.SceneManagement.SceneManager.sceneLoaded += Loaded;
            int pref = 0; try { pref = PlayerPrefs.GetInt("oz_shore_beach", 1); } catch { }
            Apply(pref == 1);
        }
        static void Loaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) { if (On) Apply(true); }

        /// <summary>Switch every L02 ground renderer to B (true) or back to the authored paint A (false).</summary>
        public static int Apply(bool b)
        {
            On = b; int n = 0;
            if (b && !texB) texB = Resources.Load<Texture2D>("OpenZ/Ground/L02_ground_paint_beachB");
            var mpb = new MaterialPropertyBlock();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; int slot = -1;
                for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name.StartsWith("M_L02_Ground")) { slot = i; break; }
                if (slot < 0) continue;
                r.GetPropertyBlock(mpb, slot);
                if (b && texB) { mpb.SetTexture(IdBase, texB); mpb.SetTexture(IdMain, texB); }
                else { mpb.SetTexture(IdBase, mats[slot].GetTexture(IdBase)); if (mats[slot].HasProperty(IdMain)) mpb.SetTexture(IdMain, mats[slot].GetTexture(IdMain)); }
                r.SetPropertyBlock(mpb, slot); n++;
            }
            return n;
        }
    }

    /// <summary>A/B capture: the four shore spots of ShoreCapture x day / dusk at 70 m, A then B, same frame each;
    /// </summary>
    public class ShoreBeachCapture : MonoBehaviour
    {
        public static string Status = "idle";
        static readonly (string id, Vector2 xz)[] Spots = { ("A", new Vector2(70f, -102f)), ("B", new Vector2(70f, -131f)), ("C", new Vector2(56f, -92f)), ("D", new Vector2(137f, -120f)), ("E", new Vector2(36f, -38f)) };
        public static void Run() { Status = "running"; new GameObject("ShoreBeachCapture").AddComponent<ShoreBeachCapture>(); }

        IEnumerator Start()
        {
            var g = ZGame.I; if (g != null && g.World != null) g.World.Bots.Clear();
            var cam = FindFirstObjectByType<RtsCamera>(); var dn = FindFirstObjectByType<DayNight>();
            if (!cam || !dn) { Status = "no camera / DayNight"; yield break; }
            bool wasRunning = dn.running; dn.running = false; bool was = ShoreBeach.On;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/shore_beach"));
            System.IO.Directory.CreateDirectory(dir);
            int n = 0, rends = 0;
            foreach (var t in new[] { 0.08f, 0.31f })
            {
                dn.timeOfDay = t; dn.Apply(t); yield return new WaitForSeconds(1.5f);
                foreach (var s in Spots)
                {
                    cam.focus = new Vector3(s.xz.x, 0f, s.xz.y); cam.SetHeightImmediate(70f);
                    yield return new WaitForSeconds(0.6f);
                    foreach (var ab in new[] { "A", "B" })
                    {
                        rends = ShoreBeach.Apply(ab == "B"); yield return null; yield return null;
                        ScreenCapture.CaptureScreenshot($"{dir}/{ab}_{s.id}_{(t < 0.2f ? "day" : "dusk")}.png"); yield return null; n++;
                    }
                }
            }
            ShoreBeach.Apply(was); dn.running = wasRunning;
            Status = $"done: {n} frames, {rends} ground renderers";
            Destroy(gameObject);
        }
    }
}
