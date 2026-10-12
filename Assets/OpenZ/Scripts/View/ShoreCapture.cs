using System.Collections;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// (A corner, B bend, C bridge, D east rock shore) x time of day (0.08 day, 0.31 dusk) x camera height (70 m, 26 m) it parks the
    /// CPU bots removed and the sim paused (Time.timeScale 0 is not used: the camera and lighting must update).
    /// </summary>
    public class ShoreCapture : MonoBehaviour
    {
        public static string Status = "idle";
        public static string Tag = "before";
        static readonly (string id, Vector2 xz)[] Spots = { ("A", new Vector2(70f, -102f)), ("B", new Vector2(70f, -131f)), ("C", new Vector2(56f, -92f)), ("D", new Vector2(137f, -120f)) };
        static readonly float[] Tods = { 0.08f, 0.31f };
        static readonly float[] Heights = { 70f, 26f };

        public static void Run(string tag) { Tag = tag; Status = "running"; new GameObject("ShoreCapture").AddComponent<ShoreCapture>(); }

        IEnumerator Start()
        {
            var g = ZGame.I; if (g != null && g.World != null) g.World.Bots.Clear();
            var cam = FindFirstObjectByType<RtsCamera>(); var dn = FindFirstObjectByType<DayNight>();
            if (!cam || !dn) { Status = "no camera / DayNight"; yield break; }
            bool wasRunning = dn.running; dn.running = false;
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/shore_band_fix"));
            System.IO.Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var t in Tods)
            {
                dn.timeOfDay = t; dn.Apply(t);
                yield return new WaitForSeconds(1.5f);                                              // auto exposure / probes settle
                foreach (var s in Spots)
                    foreach (var h in Heights)
                    {
                        cam.focus = new Vector3(s.xz.x, 0f, s.xz.y); cam.SetHeightImmediate(h);
                        yield return new WaitForSeconds(0.6f);
                        ScreenCapture.CaptureScreenshot($"{dir}/{Tag}_{s.id}_{(t < 0.2f ? "day" : "dusk")}_{h:0}.png");
                        yield return null; n++;
                    }
            }
            dn.running = wasRunning;
            Status = $"done: {n} frames ({Screen.width}x{Screen.height})";
            Destroy(gameObject);
        }
    }
}
