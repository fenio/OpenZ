using System.Collections;
using System.Collections.Generic;
using System.IO;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <list type="bullet">
    /// <item><c>OpenZ.OwnerClipHelpers.FeedFullscreen(true)</c>: the Zod transmission camera drawn full screen (4:3, black bars) at
    /// 1440 x 1080 so the red-button press reads in a 1080p clip; logs the wall-clock time of the press for the cut.</item>
    /// <item><c>OpenZ.OwnerClipHelpers.RunOver(tx, ty, 40f)</c>: three lanes along +x at tile (tx, ty): a red jeep into a blue grunt
    /// squad, a red light tank into a second blue squad, a red jeep through a RED squad (nobody hurt). Nobody shoots (attack radius
    /// 0, damage 0) so only the DV65 run-over shows; the CPU bot is paused for the staging and put back after.</item>
    /// <item><c>OpenZ.OwnerClipHelpers.FindLane()</c>: a free open ground rectangle for RunOver near the RTS camera focus.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(3000)]
    public sealed class OwnerClipHelpers : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("nuke");
        public static string Last = "";
        static OwnerClipHelpers feed;
        RenderTexture rt; bool pressLogged;

        public static void Log(string s)
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(Dir + "/clip_log.txt", System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\n");
            Debug.Log("[OwnerClips] " + s); Last = s;
        }

        public static string FeedFullscreen(bool on)
        {
            if (!on) { if (feed) Destroy(feed.gameObject); feed = null; Log("feed fullscreen off"); return "off"; }
            if (!feed) feed = new GameObject("OwnerClipFeed").AddComponent<OwnerClipHelpers>();
            feed.pressLogged = false;
            Log("feed fullscreen on");
            return "on";
        }

        void LateUpdate()
        {
            if (feed != this) return;
            var st = FindFirstObjectByType<ZodStage>(); if (!st) return;
            if (!rt) { rt = new RenderTexture(1440, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 }; rt.Create(); }
            st.RenderFeedInto(rt);
            if (!pressLogged && st.PressPlaced && st.PressU >= 0f && st.PressU < 3f) { pressLogged = true; Log("press start, PressU " + st.PressU.ToString("F2") + ": " + st.PressStatus()); }
        }

        void OnGUI()
        {
            if (feed != this || Event.current.type != EventType.Repaint) return;
            var full = new Rect(0, 0, Screen.width, Screen.height);
            var c = GUI.color; GUI.color = Color.black; GUI.DrawTexture(full, Texture2D.whiteTexture); GUI.color = c;
            if (rt) GUI.DrawTexture(full, rt, ScaleMode.ScaleToFit, false);
        }

        void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } }

        // ------------------------------------------------------------------------------------------------ run-over staging
        /// <summary>A 22 x 15 tile rectangle of plain ground / road with no object in it, nearest to the RTS camera focus.</summary>
        public static string FindLane()
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            var W = g.World; var m = W.Map;
            var rts = FindFirstObjectByType<RtsCamera>();
            Vector3 f = rts ? rts.focus : Vector3.zero; int ftx = (int)(f.x * 0.5f), fty = (int)(-f.z * 0.5f);
            int best = int.MaxValue, bx = -1, by = -1;
            for (int ty = 8; ty < m.height - 8; ty++)
                for (int tx = 11; tx < m.width - 11; tx++)
                {
                    int d = (tx - ftx) * (tx - ftx) + (ty - fty) * (ty - fty); if (d >= best) continue;
                    if (!Clear(W, tx, ty)) continue;
                    best = d; bx = tx; by = ty;
                }
            return bx < 0 ? "none" : bx + "," + by;
        }

        static bool Clear(ZWorld W, int tx, int ty)
        {
            var m = W.Map;
            for (int y = ty - 7; y <= ty + 7; y++)
                for (int x = tx - 11; x <= tx + 10; x++) { var c = m.ClassAt(x, y); if (c != ZClass.Ground && c != ZClass.Road) return false; }
            int x0 = (tx - 12) * 16, x1 = (tx + 12) * 16, y0 = (ty - 8) * 16, y1 = (ty + 8) * 16;
            foreach (var o in W.Objects)
                if (o != null && !o.Destroyed && o.CenterX + o.WidthPix / 2 > x0 && o.CenterX - o.WidthPix / 2 < x1 && o.CenterY + o.HeightPix / 2 > y0 && o.CenterY - o.HeightPix / 2 < y1) return false;
            return true;
        }

        public static string RunOver(int tx, int ty, float camH)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            var h = new GameObject("OwnerClipRunOver").AddComponent<OwnerClipHelpers>();
            h.StartCoroutine(h.RunOverRoutine(tx, ty, camH));
            return "staging run-over at " + tx + "," + ty;
        }

        static void Calm(ZObject o) { if (o == null) return; o.Damage = 0; o.AttackRadius = 0; foreach (var mn in o.Minions) { mn.Damage = 0; mn.AttackRadius = 0; } }

        IEnumerator RunOverRoutine(int tx, int ty, float camH)
        {
            var g = ZGame.I; var W = g.World;
            var bots = new List<ZBot>(W.Bots); W.Bots.Clear();                                      // the CPU must not order the staged squads away
            int h0 = W.RunOverHits, gl0 = W.RunOverGlances, s0 = W.RunOverSquashes; float snd0 = Time.unscaledTime;
            var sqA = W.CreateRobotGroup("grunt", tx * 16, (ty - 5) * 16, Team.Blue);
            var sqB = W.CreateRobotGroup("grunt", tx * 16, ty * 16, Team.Blue);
            var sqR = W.CreateRobotGroup("grunt", tx * 16, (ty + 5) * 16, Team.Red);
            foreach (var s in new[] { sqA, sqB, sqR }) { g.SpawnUnitView(s); foreach (var mn in s.Minions) g.SpawnUnitView(mn); Calm(s); }
            var jeepA = W.CreateObject("vehicle", "jeep", (tx - 10) * 16, (ty - 5) * 16, Team.Red); g.SpawnUnitView(jeepA); Calm(jeepA);
            var tank = W.CreateObject("vehicle", "light", (tx - 10) * 16, ty * 16, Team.Red); g.SpawnUnitView(tank); Calm(tank);
            var jeepR = W.CreateObject("vehicle", "jeep", (tx - 10) * 16, (ty + 5) * 16, Team.Red); g.SpawnUnitView(jeepR); Calm(jeepR);
            var rts = FindFirstObjectByType<RtsCamera>();
            if (rts) { var c = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); rts.focus = new Vector3(c.x, 0f, c.z); rts.SetHeightImmediate(camH); }
            Log($"run-over staged at tile {tx},{ty}: blue squads {1 + sqA.Minions.Count} + {1 + sqB.Minions.Count}, red squad {1 + sqR.Minions.Count}, camera h {camH:F0}");
            yield return new WaitForSeconds(2f);
            Go(W, jeepA, tx + 10, ty - 5); Log("go: red jeep into blue squad A");
            yield return new WaitForSeconds(5f);
            Go(W, tank, tx + 10, ty); Log("go: red light tank into blue squad B");
            yield return new WaitForSeconds(7f);
            Go(W, jeepR, tx + 10, ty + 5); Log("go: red jeep through the RED squad");
            yield return new WaitForSeconds(6f);
            int redHurt = 0; foreach (var r in Members(sqR)) if (r.Destroyed || r.Health < r.MaxHealth || r.RunOverBy >= 0) redHurt++;
            var es = OzSoundLog.Since(snd0); var keys = new SortedDictionary<string, int>();
            foreach (var e in es) if (e.key != null && (e.key.Contains("squash") || e.key.Contains("crush") || e.key.Contains("runover") || e.key.Contains("thud") || e.key.Contains("clang"))) { int n; keys.TryGetValue(e.key, out n); keys[e.key] = n + 1; }
            var ks = new List<string>(); foreach (var kv in keys) ks.Add(kv.Key + " x" + kv.Value);
            Log($"run-over done: hits {W.RunOverHits - h0}, glancing {W.RunOverGlances - gl0}, squashes {W.RunOverSquashes - s0}, red squad hurt {redHurt}; sounds {string.Join(", ", ks.ToArray())}");
            W.Bots.AddRange(bots);
            Destroy(gameObject);
        }

        static List<ZObject> Members(ZObject l) { var s = new List<ZObject> { l }; s.AddRange(l.Minions); return s; }

        static void Go(ZWorld W, ZObject v, int tx, int ty) =>
            W.SendWaypoints(v.Owner, v, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.ForceMove, x = tx * 16 + 8, y = ty * 16 + 8 } });
    }
}
