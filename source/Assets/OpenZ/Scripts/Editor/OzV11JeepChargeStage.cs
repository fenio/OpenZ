using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Staging helper for the clip of DV121 (Sim/ZObject.JeepCharge.cs): a jeep of the player's team charges a Tough and runs him over.
    /// Editor only, Play mode, in a running level, after its first 20 s (the calm start: nothing starts by itself before). Menu:
    /// OpenZ/V1.1/Jeep: DV121 Stage A Charge (Play mode), or OpenZ.EditorTools.OzV11JeepChargeStage.Run().
    /// K322: first the clean clip stage (OzClipStage.Start, OpenZParity.V11ClipStage.cs: the computer players out, every factory stopped,
    /// the robots and vehicles that are not the stage's taken away, held so for the take); then the place (OzClipStagePlace.Find,
    /// Kind.Jeep: plain ground or road with room for the charge and the drive back, 2 tiles behind the jeep to 4 tiles past the Tough and
    /// 2 rows either side, no water, rock, tree, building or prop, no bridge or one-lane area within a tile, a clear line of fire, no flag,
    /// empty vehicle or gun in auto-grab reach of the jeep at home or of the Tough on his spot, no other unit or fort in reach, nothing tall
    /// in front of either; near the camera, else the level's known places). It puts a crewed jeep of the player's side there and an enemy
    /// Tough 6 tiles (96 px) east of it (mid range), gives both their views (as the game's own units made in Play) and sets the normal game
    /// camera (height 70) over the middle. The jeep is idle: it fights the Tough by itself and
    /// drives at him; he stands his ground, lets it come within about 2 to 3 tiles (drawn each time; on a road, where it is 1.689 x as
    /// fast, farther out, as the same time on open ground) and fires; then either his second
    /// rocket 1 s later destroys the jeep, or the jeep runs him over and drives back (about half and half: counted 97 of 200 for the
    /// jeep; run the stage again for the other outcome). The console gets one line when the jeep is home after a win (1 s after it stands
    /// on its spot again: about 7 to 12 s after the stage), 3 s after the jeep's wreck, at the latest 40 s after the kill: charged or not, how close the Tough let it come,
    /// the Tough squashed or not, the jeep destroyed or how far it stands from its spot and when it came home, engine revs played, the frame
    /// rate since the stage. Nothing else of the game is touched.
    /// </summary>
    public static class OzV11JeepChargeStage
    {
        const string Tag = "[OpenZ DV121 stage]";
        const float CamHeight = 70f;
        /// <summary>K322: the report waits for the jeep at home after a win (back within ZWorld.ChargeHomePx of its spot, standing) and then
        /// HomeHold s more; after a Tough's win WreckHold s; at most KillWait s after the kill and MaxWait s after the stage (it was a fixed
        /// 14 s: in run 27B the jeep still stood 102 px from its spot then).</summary>
        const float HomeHold = 1f, WreckHold = 3f, KillWait = 40f, MaxWait = 70f;
        const int StagePx = OzClipStagePlace.JeepToughPx;
        public static string Report = "not run";
        static ZObject jeep, tough;
        static int sx, sy, charges0, revs0, played0;
        static double t0, firePx, killAt, lostAt, homeAt;

        [MenuItem("OpenZ/V1.1/Jeep: DV121 Stage A Charge (Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a running level)"); return; }
            var g = ZGame.I; var w = g ? g.World : null; var rts = Object.FindFirstObjectByType<RtsCamera>();
            string no = w == null ? "no level is running" : !rts ? "no game camera" : g.paused ? "the game is paused: close the menu first" :
                        !ZWorld.JeepChargeDeviation ? "the rule is off ('Zod exact' or the run-over option off)" :
                        w.Time < ZWorld.CalmStartSec + 1 ? "the level's first 20 s (calm start): try again in a few seconds" : null;
            if (no != null) { Report = Tag + " NOT RUN: " + no; Debug.LogError(Report); return; }
            Team me = g.player == Team.Neutral ? Team.Red : g.player;                                   // K322: the player's side (with the computer player out of the session the first selectable object of a 'human' side was Blue's)
            Team foe = me == Team.Red ? Team.Blue : Team.Red;
            var c = rts.focus;
            int ftx = Mathf.RoundToInt(c.x * 8f / 16f), fty = Mathf.RoundToInt(-c.z * 8f / 16f);
            // K322: the clean clip stage (OzClipStage.Start: the computer players out, every factory stopped, the units that are not the stage's
            // taken away, held so for the take), then the place (OzClipStagePlace.Find, Kind.Jeep: open, flat, vehicle-passable ground with room for
            // the charge and the drive back, no lane, nothing to grab, nobody in reach, a clear view; near the camera, else the level's known places)
            if (!OzClipStage.Start(g, Mathf.RoundToInt(c.x * 8f), Mathf.RoundToInt(-c.z * 8f), "DV121 jeep stage")) { Report = Tag + " NOT RUN: the clean clip stage refused (the console says why)"; return; }
            if (!OzClipStagePlace.Find(OzClipStagePlace.Kind.Jeep, w, ftx - 3, fty, 14, me, out int tx, out int ty, out _, out _, out string placeNote))
            { Report = Tag + " NOT RUN: no place for the stage near the camera or at this level's known places: " + placeNote; Debug.LogError(Report); return; }
            OzClipStagePlace.JeepSpots(tx, ty, out int jx, out int jy, out int ux, out int uy);
            jeep = w.CreateObject("vehicle", "jeep", jx - 16, jy - 16, me);
            tough = w.CreateObject("robot", "tough", ux - 8, uy - 8, foe);
            OzClipStage.Stage(g, "DV121 jeep stage", jeep, tough);                                      // K322: their views (ZGame.SpawnUnitView, as for the game's own units made in Play), checked; the hold keeps them
            sx = jeep.CenterX; sy = jeep.CenterY; t0 = w.Time; firePx = -1; killAt = lostAt = homeAt = -1; charges0 = w.JeepCharges; revs0 = OzJeepChargeRev.Revs; played0 = EngineRev.Played;
            rts.focus = ZGame.PxToWorld(jx + StagePx / 2, jy); rts.SetHeightImmediate(CamHeight);
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            Report = $"{Tag} staged: jeep #{jeep.Id} ({me}) at ({jx},{jy}), tile {tx},{ty} ({placeNote}), Tough #{tough.Id} ({foe}) {StagePx} px east; the report follows 1 s after the jeep is home after a win (about 7 to 12 s), 3 s after a Tough's win, at most {MaxWait:0} s";
            Debug.Log(Report);
            EditorApplication.update -= Step; EditorApplication.update += Step;
        }

        // K322: the place is OzClipStagePlace.Find (Kind.Jeep, OpenZParity.V11ClipStage.cs); the old test here (13 x 5 open tiles, no unit within
        // 12 tiles, no object within 8 x 4 tiles) found no place on Level 2 (run 27B: 0 of 442 open blocks)

        static void Step()
        {
            var g = ZGame.I; var w = g ? g.World : null;
            if (!EditorApplication.isPlaying || w == null || jeep == null) { EditorApplication.update -= Step; return; }
            if (tough != null && tough.ChargeToughFirePx > 0) firePx = tough.ChargeToughFirePx;                  // how close he lets the jeep come (drawn when it drives at him)
            // K322: the report (and the take) ends when the jeep is home after a win, or a moment after the wreck after a Tough's win
            double d = Mathf.Sqrt((float)((jeep.CenterX - sx) * (jeep.CenterX - sx) + (jeep.CenterY - sy) * (jeep.CenterY - sy)));
            bool toughDown = tough == null || tough.Destroyed || tough.KillAt >= 0 || tough.Squashed, jeepGone = jeep.Destroyed || jeep.KillAt >= 0;
            if (toughDown && killAt < 0) killAt = w.Time;
            if (jeepGone && lostAt < 0) lostAt = w.Time;
            if (killAt >= 0 && !jeepGone && homeAt < 0 && !jeep.ChargeOn && !jeep.IsMoving && d <= ZWorld.ChargeHomePx) homeAt = w.Time;
            bool done = (homeAt >= 0 && w.Time - homeAt >= HomeHold) || (lostAt >= 0 && w.Time - lostAt >= WreckHold) || (killAt >= 0 && w.Time - killAt >= KillWait) || w.Time - t0 >= MaxWait;
            if (!done) return;
            EditorApplication.update -= Step;
            Report = $"{Tag} after {w.Time - t0:0.0} s: charged {(w.JeepCharges > charges0 ? "yes" : "NO")}, the Tough let it come within {(firePx > 0 ? firePx.ToString("0") + " px of open ground (farther on a road)" : "- (NO stand)")}, the Tough {(tough.Squashed ? "squashed" : tough.Destroyed || tough.KillAt >= 0 ? "dead (not squashed)" : "alive")}, " +
                     $"the jeep {(jeep.Destroyed ? "destroyed" : d.ToString("0.0") + " px from its spot")}{(homeAt >= 0 ? $" (home {homeAt - killAt:0.0} s after the kill, {homeAt - t0:0.0} s after the stage)" : killAt >= 0 && !jeepGone ? " (NOT home " + (w.Time - killAt).ToString("0") + " s after the kill)" : "")}, " +
                     $"charge revs {OzJeepChargeRev.Revs - revs0}, engine revs played {EngineRev.Played - played0}; {OzClipStage.FramesLine()}";
            Debug.Log(Report);
        }
    }
}
