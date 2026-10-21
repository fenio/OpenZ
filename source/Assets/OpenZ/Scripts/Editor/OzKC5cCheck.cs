using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit KC5c checks (menu OpenZ/V1.1/KC5c ...), item 205: the fort's big cannon falls apart when it lands.
    /// <list type="bullet">
    /// <item>Toss (Play): tosses the fort cannon nearest the view centre (FortGunView.TestToss, view only); when every piece has come to
    /// rest (about 2-9 s; a pause holds it; 40 s at most) prints: the break on the landing frame, the pieces (each: kind, triangles,
    /// size, mass, launch speed and spin, where it rests, its real lowest vertex to the exact ground under it, the snap, how far it
    /// moved, how much it turned, hits heard), the plates off the top, the small shards, the sounds that really started (counters and
    /// the sound log since the toss), "none floating / sunk", "pieces through each other at rest: 0", the scatter and the time the
    /// cutting took per frame.</item>
    /// <item>Toss + pauses for pictures (Play): the same, and the editor pauses at the landing frame, at +0.5 s and at +2 s (game time):
    /// take a picture, then press Play's pause button again to go on.</item>
    /// </list>
    /// Test tools only: nothing is saved.
    /// </summary>
    public static class OzKC5cCheck
    {
        const string M = "OpenZ/V1.1/KC5c ";
        /// <summary>The heaviest frame of the cutting in the air may take this long (ms).</summary>
        const float PrepareFrameMs = 8f;
        /// <summary>A ground piece may be moved onto the ground by at most this much when it rests (m).</summary>
        const float MaxSnap = 0.10f;
        static float until, t0; static int breaks0, sounds0, scatter0, land0, landSound0, dust0, clanks0, crashes0, knocks0, retried0, forced0, pushed0, released0;
        static bool pictures; static int pauseStep;

        [MenuItem(M + "Toss The Nearest Fort Cannon: It Breaks Apart (Play)")]
        static void Toss() => Start(false);

        [MenuItem(M + "Toss The Nearest Fort Cannon + Pauses For Pictures (Play)")]
        static void TossPictures() => Start(true);

        static void Start(bool pics)
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KC5c] enter Play first, the camera on a fort"); return; }
            var cam = Camera.main; var c = cam && g.GroundPoint(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out var gp) ? gp : Vector3.zero;
            var gun = UnityEngine.Object.FindObjectsByType<FortGunView>(FindObjectsSortMode.None).Where(v => v && v.Bound && !v.Tossed).OrderBy(v => Vector3.Distance(v.transform.position, c)).FirstOrDefault();
            if (!gun) { Debug.LogWarning("[OpenZ KC5c] no standing fort cannon in the scene"); return; }
            breaks0 = OzFortGunBreakup.Breaks; sounds0 = OzFortGunBreakup.BreakSounds; scatter0 = OzFortGunBreakup.ScatterSounds;
            land0 = OzHeavyLanding.Landings; landSound0 = OzHeavyLanding.Sounds; dust0 = OzHeavyLanding.DustBursts;
            clanks0 = OzFortGunBreakup.Clanks; crashes0 = OzFortGunBreakup.Crashes; knocks0 = OzFortGunBreakup.Knocks; retried0 = OzFortGunBreakup.RetriedClanks;
            forced0 = OzFortGunBreakup.ForcedPairs; pushed0 = OzFortGunBreakup.PushedOut; released0 = OzFortGunBreakup.ReleasedPairs;
            t0 = Time.unscaledTime;
            pictures = pics; pauseStep = 0;
            OzFortGunBreakup.Broke -= OnBroke; OzFortGunBreakup.Broke += OnBroke;
            gun.TestToss();
            until = Time.realtimeSinceStartup + (pics ? 240f : 40f);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log($"[OpenZ KC5c] fort cannon tossed (view only, breakup {(OzFortGunBreakup.Enabled ? "ON" : "OFF")}); its pieces' numbers when they have come to rest" + (pics ? "; the editor pauses at the landing frame, +0.5 s and +2 s for the pictures" : ""));
        }

        static void OnBroke(Vector3 at)
        {
            OzFortGunBreakup.Broke -= OnBroke;
            Debug.Log($"[OpenZ KC5c] broke apart on frame {OzFortGunBreakup.LastBreakFrame} at {at}: {OzFortGunBreakup.LastPlan}");
            if (pictures) { EditorApplication.isPaused = true; pauseStep = 1; Debug.Log("[OpenZ KC5c] paused: the LANDING FRAME picture (then un-pause)"); }
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; OzFortGunBreakup.Broke -= OnBroke; return; }
            bool broke = OzFortGunBreakup.Breaks > breaks0;
            if (pictures && broke && !EditorApplication.isPaused)
            {
                float since = Time.time - OzFortGunBreakup.LastBreakAt;
                if (pauseStep == 1 && since >= 0.5f) { EditorApplication.isPaused = true; pauseStep = 2; Debug.Log($"[OpenZ KC5c] paused at +{since:0.00} s: the +0.5 s picture (then un-pause)"); }
                else if (pauseStep == 2 && since >= 2f) { EditorApplication.isPaused = true; pauseStep = 3; Debug.Log($"[OpenZ KC5c] paused at +{since:0.00} s: the +2 s picture (then un-pause)"); }
            }
            bool done = broke && OzFortGunBreakup.LastAllRested() && (!pictures || pauseStep >= 3);
            if (!done && Time.realtimeSinceStartup < until) return;
            EditorApplication.update -= Tick; OzFortGunBreakup.Broke -= OnBroke;
            Debug.Log(Report(broke));
        }

        /// <summary>The sound log since the toss for one event: started plays and drops by reason.</summary>
        static string Logged(string key, out int plays)
        {
            var es = OzSoundLog.Since(t0).Where(e => e.key == key && e.outcome != null).ToList();
            plays = es.Count(e => e.outcome.StartsWith("PLAY"));
            var drops = es.Where(e => e.outcome.StartsWith("DROP")).GroupBy(e => e.outcome.Substring(4).Trim()).Select(k => k.Key + " " + k.Count()).ToList();
            return $"{key}: PLAY {plays}" + (drops.Count > 0 ? ", DROP " + string.Join(", ", drops) : ", no drop");
        }

        public static string Report(bool broke)
        {
            var sb = new StringBuilder("[OpenZ KC5c] the fort cannon breaks apart\n");
            var ps = OzFortGunBreakup.LastPieces.Where(p => p).ToList();
            sb.AppendLine(OzV11Kit.Verdict(broke && ps.Count >= 8 && OzFortGunBreakup.LastLandingsAtBreak > land0,
                $"it broke apart ON its landing frame (frame {OzFortGunBreakup.LastBreakFrame}, right after the heavy landing: landings +{OzHeavyLanding.Landings - land0}), touch-down {OzFortGunBreakup.LastTouchSpeed:0.0} m/s: {ps.Count} pieces (wanted >= 8, expected 15; plan: {OzFortGunBreakup.LastPlan})"));
            var kinds = ps.GroupBy(p => p.Kind).Select(k => k.Key + " " + k.Count());
            bool real = ps.Any(p => p.Kind == "barrel") && ps.Any(p => p.Kind == "head") && ps.Any(p => p.Kind == "ring") && ps.Count(p => p.Kind == "plate") >= 2;
            sb.AppendLine(OzV11Kit.Verdict(real, "the real parts and cut fragments: " + string.Join(", ", kinds) + " (barrel, head, ring, >= 2 armour plates)"));
            foreach (var p in ps.OrderByDescending(p => p.MassKg))
                sb.AppendLine($"      {p.PieceName,-14} {p.Kind,-7} {p.Tris,5} tris{(p.Capped ? " +cap" : "     ")} {p.BoxSize,4:0.00} m {p.MassKg,6:0} kg  launch {p.LaunchVel.magnitude:0.0} m/s spin {p.LaunchSpin.magnitude:0.0} rad/s  " +
                              $"rests {p.RestOn} ({(p.Rested ? $"{p.RestGap * 100f:0.0} cm" : "NOT at rest")}, snapped {p.SnapStep * 100f:0.0} cm{(p.PushedBy > 0f ? $", moved out {p.PushedBy * 100f:0} cm" : "")})  moved {p.Moved:0.0} m ({p.RestDistance:0.0} m from the impact), turned {p.RolledDeg:0} deg, hits {p.Hits} (heard {p.HeardHits}), knocks {p.KnockCount}");
            // the plates come off the top and fly clear; the small shards
            var plates = ps.Where(p => p.PieceName.StartsWith("plate_")).ToList();
            sb.AppendLine(OzV11Kit.Verdict(plates.Count >= 2 && plates.All(p => p.Moved >= 1f && p.StartOutwardY > -0.3f),
                $"the armour plates come off the side facing up and fly clear: {string.Join(", ", plates.Select(p => $"{p.PieceName} faced {(p.StartOutwardY > 0.3f ? "up" : p.StartOutwardY < -0.3f ? "DOWN" : "sideways")} ({p.StartOutwardY:0.00}), moved {p.Moved:0.0} m"))} (each >= 1 m, none from the side underneath)"));
            var shards = ps.Where(p => p.Shard).ToList();
            sb.AppendLine(OzV11Kit.Verdict(shards.Count >= 3 && shards.All(p => p.BoxSize >= 0.25f && p.BoxSize <= 0.9f),
                $"small shards off the plates' and the roof's corners, in the cannon's own paint: {shards.Count} ({string.Join(", ", shards.Select(p => $"{p.PieceName} {p.BoxSize:0.00} m, moved {p.Moved:0.0} m"))}; wanted >= 3, each 0.25-0.9 m); plus the landing's 16 metal chips as before"));
            // the sounds: counted only when they really started; the sound log since the toss beside them
            string lb = Logged(OzFortGunBreakup.SoundBreak, out _), ls = Logged(OzFortGunBreakup.SoundScatter, out _), lc = Logged(OzFortGunBreakup.SoundClank, out int clankLog);
            sb.AppendLine(OzV11Kit.Verdict(OzFortGunBreakup.BreakSounds > sounds0 && OzHeavyLanding.Sounds > landSound0 && OzHeavyLanding.DustBursts > dust0,
                $"on the frame, heard: the metal break sound (+{OzFortGunBreakup.BreakSounds - sounds0}), the debris scatter (+{OzFortGunBreakup.ScatterSounds - scatter0}), the heavy concrete thud (+{OzHeavyLanding.Sounds - landSound0}), the dust burst (+{OzHeavyLanding.DustBursts - dust0}); sound log: {lb}; {ls}"));
            int heardHits = OzFortGunBreakup.Clanks + OzFortGunBreakup.Crashes - clanks0 - crashes0;
            int landed = ps.Count(p => p.Hits > 0), silent = ps.Count(p => p.Hits > 0 && p.HeardHits == 0);
            sb.AppendLine(OzV11Kit.Verdict(heardHits >= 4 && (!OzSoundLog.On || OzFortGunBreakup.Clanks - clanks0 <= clankLog),
                $"the pieces sound when they hit, heard: crashes +{OzFortGunBreakup.Crashes - crashes0}, clanks +{OzFortGunBreakup.Clanks - clanks0} (of them tried again after a drop +{OzFortGunBreakup.RetriedClanks - retried0}), rolling / sliding knocks +{OzFortGunBreakup.Knocks - knocks0}; " +
                $"{landed - silent} of {landed} pieces that hit were heard on their own (the others land together with one that was); sound log: {lc}"));
            // resting: on the ground, not floating, not sunk, no big drop onto the ground, not inside each other
            bool rested = ps.Count > 0 && ps.All(p => p.Rested);
            var ground = ps.Where(p => p.RestOn == "ground").ToList();
            bool groundOk = ground.All(p => p.RestGap <= 0.005f && p.RestGap >= -0.065f && p.SnapStep <= MaxSnap);
            bool otherOk = ps.Where(p => p.RestOn.StartsWith("on ")).All(p => p.RestGap <= 0.08f);
            int floating = ps.Count(p => p.RestOn == "floating"), sunk = ps.Count(p => p.RestOn == "sunk");
            float bigSnap = ground.Count > 0 ? ground.Max(p => p.SnapStep) : 0f;
            sb.AppendLine(OzV11Kit.Verdict(rested && groundOk && otherOk && floating == 0 && sunk == 0,
                $"none floating / sunk: {ground.Count} on the ground (real lowest vertex {(ground.Count > 0 ? $"{ground.Min(p => p.RestGap) * 100f:0.0} .. {ground.Max(p => p.RestGap) * 100f:0.0}" : "-")} cm, wanted -3 +- 3; " +
                $"largest snap onto the ground {bigSnap * 100f:0.0} cm{(ground.Count > 0 ? $" ({ground.OrderByDescending(p => p.SnapStep).First().PieceName})" : "")}, wanted <= {MaxSnap * 100f:0} cm), " +
                $"{ps.Count(p => p.RestOn.StartsWith("on "))} lying on another piece / the ruin (within 8 cm), {ps.Count(p => p.RestOn == "water")} in water, floating {floating}, sunk {sunk}{(rested ? "" : ", NOT all at rest in 40 s")}"));
            int through = OzFortGunBreakup.ThroughEachOther(out float worst, out string which);
            sb.AppendLine(OzV11Kit.Verdict(rested && through == 0,
                $"pieces through each other at rest: {through} (colliders more than {OzFortGunBreakup.HoldDepth * 100f:0} cm inside each other; deepest {worst * 100f:0.0} cm, {which}); held apart at the start: {OzFortGunBreakup.LastHeldText}; " +
                $"apart by themselves +{OzFortGunBreakup.ReleasedPairs - released0}, let collide while resting +{OzFortGunBreakup.ForcedPairs - forced0}, moved out at {OzFortGunBreakup.PushAt:0} s +{OzFortGunBreakup.PushedOut - pushed0}"));
            // the scatter: how far each piece moved over the ground from where it started
            var barrel = ps.FirstOrDefault(p => p.Kind == "barrel");
            int moved1 = ps.Count(p => p.Moved >= 1f); float far = ps.Count > 0 ? ps.Max(p => p.Moved) : 0f;
            sb.AppendLine(OzV11Kit.Verdict(moved1 >= 6 && far <= 12f && barrel && barrel.RolledDeg >= 90f,
                $"scattered a few metres: {moved1} of {ps.Count} pieces moved 1 m or more over the ground (wanted >= 6), the farthest moved {far:0.0} m ({(ps.Count > 0 ? ps.OrderByDescending(p => p.Moved).First().PieceName : "-")}; wanted <= 12); " +
                $"the barrel {(barrel ? $"moved {barrel.Moved:0.0} m and turned {barrel.RolledDeg:0} deg" : "-")} (it rolls: wanted >= 90 deg)"));
            // the work: the cutting is spread over the flight
            sb.AppendLine(OzV11Kit.Verdict(OzFortGunBreakup.LastPrepareSteps > 0 && OzFortGunBreakup.LastPrepareMaxMs <= PrepareFrameMs && OzFortGunBreakup.LastLandingPrepareMs <= PrepareFrameMs,
                $"the cutting in the air: {OzFortGunBreakup.LastPrepareSteps} steps over as many frames, the heaviest {OzFortGunBreakup.LastPrepareMaxMs:0.00} ms (wanted <= {PrepareFrameMs:0}), {OzFortGunBreakup.LastPrepareTotalMs:0.0} ms in all; " +
                $"left for the landing frame {OzFortGunBreakup.LastLandingPrepareMs:0.00} ms (0 = all done in the air); the landing frame's own breakup (bodies, launch) {OzFortGunBreakup.LastBreakMs:0.0} ms (info)"));
            sb.AppendLine($"      the pieces sink away together after the cannon's wreck time (FortGunView.wreckSeconds), as the whole cannon did; faded so far: {OzFortGunBreakup.Faded}");
            return sb.ToString();
        }
    }
}
