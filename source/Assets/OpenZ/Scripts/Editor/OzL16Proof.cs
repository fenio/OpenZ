using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 proof helpers for Play mode (editor only, not in the build): put the normal game camera somewhere, spawn a
    /// vehicle and send it, count vehicles and robots on the stream step tiles, read the bridge wobble and the sound log.
    /// </summary>
    public static class OzL16Proof
    {
        public static readonly int[] StepTiles = { 44, 43, 44, 57, 45, 57, 46, 57, 44, 58, 45, 58, 46, 58, 45, 77, 59, 50, 60, 50, 58, 55, 59, 55, 58, 58, 59, 58, 58, 65, 59, 65, 58, 74, 59, 74, 60, 74, 59, 75 };

        public static string Cam(float x, float z, float h)
        {
            var rts = Object.FindFirstObjectByType<RtsCamera>(); if (!rts) return "no camera";
            var g = ZGame.I; rts.focus = new Vector3(x, 0f, z); rts.SetHeightImmediate(h);
            return "camera at " + x + ", " + z + " height " + h;
        }

        public static string Shot(string path)
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }

        public static int Spawn(string type, bool red, int tx, int ty)
        {
            var g = ZGame.I; if (!g || g.World == null) return -1;
            bool robot = ZBuildLists.IsRobot(type); var team = red ? Team.Red : Team.Blue;
            ZObject o;
            if (robot) { o = g.World.CreateRobotGroup(type, tx * 16, ty * 16, team); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m); }
            else { o = g.World.CreateObject("vehicle", type, tx * 16, ty * 16, team); g.SpawnUnitView(o); }
            return o.Id;
        }

        public static string Move(int id, int tx, int ty)
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            var o = g.World.Objects.FirstOrDefault(x => x.Id == id); if (o == null) return "no object " + id;
            bool ok = g.World.SendWaypoints(o.Owner, o, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, x = tx * 16 + 8, y = ty * 16 + 8 } });
            return (ok ? "sent " : "REFUSED ") + o.Type + " #" + id + " to tile " + tx + "," + ty;
        }

        public static string Where(int id)
        {
            var g = ZGame.I; var o = g && g.World != null ? g.World.Objects.FirstOrDefault(x => x.Id == id) : null;
            if (o == null) return "no object " + id;
            return o.Type + " #" + id + " tile " + (o.CenterX >> 4) + "," + (o.CenterY >> 4) + " health " + o.Health + " waypoints " + o.Waypoints.Count;
        }

        public static string StepClasses()
        {
            var g = ZGame.I; if (!g || g.World == null) return "no game";
            int water = 0; var other = new List<string>();
            for (int i = 0; i < StepTiles.Length; i += 2)
            {
                var c = g.World.Map.ClassAt(StepTiles[i], StepTiles[i + 1]);
                if (c == ZClass.Water) water++; else other.Add(StepTiles[i] + "," + StepTiles[i + 1] + "=" + c);
            }
            return "step tiles: " + water + " of " + (StepTiles.Length / 2) + " are shallow water (robots only)" + (other.Count > 0 ? "; others: " + string.Join(" ", other) : "");
        }

        // ---- sampler: every frame while Play runs
        static bool on; static HashSet<int> steps; static int frames, vehOnStep, robOnStep, vehOnWater, deckFrames; static float worstFrame, sumFrame, maxMoved, maxRide, maxPlank, maxSag; static string rideType = "";
        static readonly HashSet<int> robotsSeen = new HashSet<int>();
        public static string SamplerStart()
        {
            steps = new HashSet<int>(); for (int i = 0; i < StepTiles.Length; i += 2) steps.Add(StepTiles[i + 1] * 1000 + StepTiles[i]);
            frames = vehOnStep = robOnStep = vehOnWater = deckFrames = 0; worstFrame = sumFrame = maxMoved = maxRide = maxPlank = maxSag = 0f; rideType = ""; robotsSeen.Clear();
            OzBridgeWobble.ResetProof();
            if (!on) { EditorApplication.update += Tick; on = true; }
            return "sampler started";
        }
        static int lastFrame = -1;
        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; on = false; return; }
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            var g = ZGame.I; if (!g || g.World == null) return;
            frames++; float ft = Time.unscaledDeltaTime; sumFrame += ft; if (ft > worstFrame) worstFrame = ft;
            foreach (var o in g.World.Objects)
            {
                if (o.Destroyed) continue;
                if (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Robot) continue;
                int tx = o.CenterX >> 4, ty = o.CenterY >> 4; bool step = steps.Contains(ty * 1000 + tx);
                if (o.Kind == ZKind.Vehicle)
                {
                    if (step) vehOnStep++;
                    else if (g.World.Map.ClassAt(tx, ty) == ZClass.Water) vehOnWater++;
                }
                else if (step) { robOnStep++; robotsSeen.Add(o.Id); }
            }
            if (OzBridgeWobble.Active)
            {
                deckFrames++; if (OzBridgeWobble.LastRide > maxRide) { maxRide = OzBridgeWobble.LastRide; rideType = OzBridgeWobble.LastType; }
                maxPlank = Mathf.Max(maxPlank, OzBridgeWobble.LastPlank); maxSag = Mathf.Max(maxSag, OzBridgeWobble.LastSag);
            }
            maxMoved = Mathf.Max(maxMoved, OzBridgeWobble.MaxMoved);
        }
        public static string SamplerReport()
        {
            return "frames " + frames + ", mean " + (frames > 0 ? (sumFrame / frames * 1000f).ToString("0.0") : "-") + " ms, worst " + (worstFrame * 1000f).ToString("0") + " ms; vehicle centres on a step tile " + vehOnStep +
                   ", on other water tiles " + vehOnWater + "; robot samples on a step tile " + robOnStep + " (" + robotsSeen.Count + " robots); frames with a vehicle on the deck " + deckFrames +
                   ", plank dip " + (maxPlank * 100f).ToString("0.0") + " cm, span sag " + (maxSag * 100f).ToString("0.0") + " cm, the vehicle rode down " + (maxRide * 100f).ToString("0.0") + " cm (" + rideType + "), largest move of a deck point " + (maxMoved * 100f).ToString("0.0") + " cm";
        }

        public static string Sounds(string keyPart, float sinceSeconds)
        {
            var es = OzSoundLog.Since(Time.time - sinceSeconds).Where(e => e.key != null && e.key.Contains(keyPart)).ToList();
            var by = es.GroupBy(e => e.key + " | " + e.outcome).Select(x => x.Key + " x" + x.Count());
            return es.Count + " entries: " + string.Join("; ", by.Take(12));
        }
    }
}
