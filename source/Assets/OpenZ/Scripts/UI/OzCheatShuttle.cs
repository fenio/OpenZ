using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The testing overlay (P, OzCheatSpawner) offers the playable shuttle too (rule DV118): its own section TRANSPORT in the red and the
    /// blue column, and (K256f) in the EMPTY row (an empty shuttle: no pilot, the null team; only an order puts a squad in, its leader
    /// becomes the pilot), with the shuttle's own portrait. Dragged onto the map it is a
    /// full shuttle at once: the rules' unit (ZWorld.SpawnShuttle: its pilot, its hit points, the nearest free 6 x 6 m pad within 6
    /// tiles, nose up the screen) and the same view as level 16's own ship (OzPlayableShuttle.SpawnView: hull, rear ramp door, jets,
    /// dust, shadow, lights, sounds, voices, the break-up), on any level. The player's one flies on his orders; the CPU never flies one
    /// (a blue one just stands there, a target). Offered only while the shuttle rule is on (not in 'Zod exact').
    /// </summary>
    public static class OzCheatShuttle
    {
        public static readonly string[] Types = { ZWorld.ShuttleType };
        /// <summary>Checks: shuttles dropped, drops refused (and why), the last one made.</summary>
        public static int Spawned, Refused;
        public static string LastWhy = "";
        public static ZObject Last { get; private set; }

        public static bool Offered(Team team) => ZWorld.ShuttlePlayableDeviation;                         // K256f: the EMPTY row too

        /// <summary>K256f: the EMPTY row's picture of the shuttle (it has no icon of its own): the red ship's portrait, drawn grey by the row.</summary>
        public static Texture NeutralArt(string type) => type == ZWorld.ShuttleType ? UnitArt.VehiclePortrait(type, Team.Red) : null;

        /// <summary>OzCheatSpawner.Spawn hook: a shuttle dropped at px for team. Null (with the overlay's NO ROOM toast) when it cannot stand there.</summary>
        public static ZObject Spawn(ZGame game, OpenZHud hud, Team team, Vector2Int px)
        {
            var W = game ? game.World : null;
            if (W == null) return null;
            float heading = -90f;                                                                       // nose up the screen (the camera's way), else north
            var rc = Object.FindFirstObjectByType<RtsCamera>(); if (rc) heading = rc.yaw - 90f;
            var s = W.SpawnShuttle(px.x, px.y, team, heading, out string why);
            if (s == null)
            {
                Refused++; LastWhy = why;
                if (hud) hud.Toast(why == "no room" ? "NO ROOM HERE" : why.ToUpperInvariant(), HudStyle.Danger);
                AudioDirector.UI("ui_error");
                return null;
            }
            var v = OzPlayableShuttle.SpawnView(game, s);
            if (v) OzShuttleEmptyLook.Ensure(v);                                                        // K256f: an empty one is drawn dark, lit once a squad has taken it (View/OzShuttleEmptyLook.cs)
            Spawned++; Last = s; LastWhy = v ? "" : "no view";
            Debug.Log($"[OzCheat] spawned {team} shuttle id {s.Id} at px {s.CenterX},{s.CenterY} ({(v ? "drawn" : "NOT DRAWN: the unit prefab is missing")})");
            var wp = ZGame.PxToWorld(s.CenterX, s.CenterY); wp.y = game.GroundY(wp.x, wp.z);
            OzSoundV3.CheatDrop(ZWorld.ShuttleType, false, false, wp);
            AudioDirector.UI("ui_confirm");
            return s;
        }
    }
}
