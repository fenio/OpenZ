#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: the scenario file (one JSON per flyby in Assets/OpenZ/Scripts/Editor/EpicFlybys, editor only).
// Small changes = edit the file and run the take again; nothing here is read in normal play (the whole file is editor-only).
using System;
using System.IO;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        /// <summary>One camera key. A key with cut = true starts a new shot (a hard cut at its second); inside a shot the camera and
        /// its aim run on one smooth spline through the keys at their seconds, with the shot's ease (on its first key).</summary>
        [Serializable] public class Key
        {
            public float t;                    // second of the clip
            public float[] pos;                // camera x, y (up), z (north is negative on the map)
            public float[] look;               // the point it looks at
            public bool look_sun;              // aim at the rising sun instead of `look` (the level's own drawn sun)
            public float sun_up;               // with look_sun: where the disc sits, 0 = frame centre, 1 = top edge
            public float fov;                  // vertical lens angle (deg); 0 = 38
            public bool cut;                   // a new shot starts here (hard cut)
            public string ease;                // first key of a shot: inout (default), smooth, in, out, linear
            public string note;
        }

        /// <summary>A squad (or a vehicle) to place. team r / b, n = an empty vehicle. start: idle, move, attack_move, attack_nearest,
        /// enter (board the nearest empty vehicle or gun near `to`), lounge (the first `lounge` robots of the squad lie on loungers).</summary>
        [Serializable] public class Unit
        {
            public string tag, team, type, start, note;
            public float x, z, hp_k;
            public float[] to;
            public float[] via;                // waypoints (x, z pairs) before `to` for move / attack_move (OzEpicFlyby.Island.cs)
            public int lounge;
        }

        /// <summary>Scenery made for the take only: campfire (a copy of the level's own campfire, burning).</summary>
        [Serializable] public class Prop { public string kind, note; public float x, z; }

        /// <summary>Done once before the take: award_flag (the flag nearest x z goes to `team`), crew_gun (a robot of `team` boards the
        /// empty gun or vehicle nearest x z by the game's own boarding rule).</summary>
        [Serializable] public class Setup
        {
            public string action, team, note; public float x, z;
            // lounge_at (OzEpicFlyby.Island.cs): x z = the lounger's middle; tag, type (default grunt), hp_k; face = the point the lying
            // robot's feet point to (a campfire; none = the camera); parasol = where the parasol stands (none = the antic's own layout);
            // nudge_m = how far the place may move to free flat land (default 1 m); hold = the robot gets the hold-position order
            public string tag, type; public float hp_k, nudge_m; public float[] face, parasol; public bool hold;
        }

        /// <summary>A timed action (t = second of the clip): order:&lt;start mode&gt; (every squad with `tag`, to x z), croc_hunt (a hunt
        /// on the robot of `tag` that stands deepest in attack water, tried for window_s seconds; the crocodile rules do the rest),
        /// destroy (the lead of `tag` dies now: the game's own death and blast; when `tag` has no living unit left, the lead of `alt`),
        /// shake (camera shake: shake_s, shake_m, shake_deg), sfx (a world sound file of the level, 2D, `gain`), log_units.
        /// Only a light / medium / heavy tank, a gun or a map object leaves the game's turret debris blast (the one that throws robots).</summary>
        [Serializable] public class Cue
        {
            public float t, x, z, gain, window_s, shake_s, shake_m, shake_deg;
            public string action, tag, alt, file, note;
            public float[] via;                // order:move / order:attack_move through these points (x, z pairs) first (OzEpicFlyby.Island.cs)
            public string kind, approach, pick;   // croc_catch: roll / fast / any; dive (default) / direct; camera (default) / longest / near
            public float[] box;                // log_landing: x0, x1, z0, z1 of the land counted as ashore (default the island)
        }

        /// <summary>The far flock that crosses the sun disc (the level's sunrise look): count birds, the flock's middle on the disc at
        /// mid_s, cross_s seconds over the disc, far_m from the lens, wing span span_m; two soft calls at gain.</summary>
        [Serializable] public class Birds { public bool on; public int count; public float mid_s, cross_s, far_m, span_m, gain; }

        /// <summary>The mix of the take: these buses at -80 dB (music, UI and EVERY voice: robots, the announcer, the commander),
        /// the voice gate (no voice line starts at all), the music off. World sounds stay on.
        /// silence_clips / silence_prefixes: robot voices that play as world sounds from the level's animal sound pool (the robots'
        /// spoken crocodile lines sd_vo_*, the catch's alarm and cut-off cry): a clip named in silence_clips or starting with one of
        /// silence_prefixes is stopped the moment it starts, before the frame's sound is rendered. No prefixes in the file = the
        /// default three (sd_vo_, sd_robot_cry, sd_robot_croc_alarm), so a new take of those lines is covered without a change.</summary>
        [Serializable] public class Mix { public string[] mute_buses, silence_clips, silence_prefixes; public bool voice_gate, music_off; }

        /// <summary>The default robot-voice prefixes of the level's animal sound pool (see Mix).</summary>
        public static readonly string[] VoicePrefixes = { "sd_vo_", "sd_robot_cry", "sd_robot_croc_alarm" };

        [Serializable] public class Size { public int width, height, fps; }

        [Serializable] public class Scenario
        {
            public string name, title, level, scene, weather, output, about;
            public float seconds, time_of_day, settle_s, floor_m, drift, lounge_wake_m;
            public int seed;
            public bool flyby_sky;
            public Size full, preview;
            public Key[] camera;
            public Unit[] units;
            public Prop[] props;
            public Setup[] setup;
            public Cue[] cues;
            public Birds sun_birds;
            public LensBirds[] lens_birds;     // parrots close past the lens (OzEpicFlyby.LensBirds.cs)
            public Mix audio;
        }

        public const string DataFolder = "OpenZ/Scripts/Editor/EpicFlybys";

        /// <summary>The scenario file for a name ("scenario_a_sunrise", with or without .json, or a full path).</summary>
        public static string ScenarioFile(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) name += ".json";
            return Path.IsPathRooted(name) ? name : Path.Combine(Application.dataPath, DataFolder, name).Replace('\\', '/');
        }

        /// <summary>Reads and checks a scenario file. Null = fine; else the reason it cannot run.</summary>
        public static string Load(string name, out Scenario s)
        {
            s = null;
            var path = ScenarioFile(name);
            if (!File.Exists(path)) return "no scenario file " + path;
            try { s = JsonUtility.FromJson<Scenario>(File.ReadAllText(path)); }
            catch (Exception e) { return "the scenario file " + path + " is not readable: " + e.Message; }
            if (s == null) return "the scenario file " + path + " is empty";
            if (string.IsNullOrEmpty(s.level)) s.level = "L16";
            if (string.IsNullOrEmpty(s.scene)) s.scene = "L16_Jungle";
            if (string.IsNullOrEmpty(s.name)) s.name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(s.output)) s.output = s.name;
            if (s.seconds <= 0f) s.seconds = 25f;
            if (s.settle_s <= 0f) s.settle_s = 3f;
            if (s.floor_m <= 0f) s.floor_m = 2f;
            if (s.full == null || s.full.width <= 0) s.full = new Size { width = 3840, height = 2160, fps = 60 };
            if (s.preview == null || s.preview.width <= 0) s.preview = new Size { width = 1920, height = 1080, fps = 60 };
            if (s.full.fps <= 0) s.full.fps = 60;
            if (s.preview.fps <= 0) s.preview.fps = 60;
            if (s.units == null) s.units = new Unit[0];
            if (s.props == null) s.props = new Prop[0];
            if (s.setup == null) s.setup = new Setup[0];
            if (s.cues == null) s.cues = new Cue[0];
            if (s.audio == null) s.audio = new Mix { voice_gate = true, music_off = true };
            if (s.audio.mute_buses == null || s.audio.mute_buses.Length == 0)
                s.audio.mute_buses = new[] { "Music", "UI", "Voice", "Voice/Announcer", "Voice/Robots", "Voice/Commander" };
            if (s.audio.silence_clips == null) s.audio.silence_clips = new string[0];
            if (s.audio.silence_prefixes == null || s.audio.silence_prefixes.Length == 0) s.audio.silence_prefixes = VoicePrefixes;
            if (s.camera == null || s.camera.Length == 0) return "the scenario " + s.name + " has no camera keys";
            for (int i = 0; i < s.camera.Length; i++)
            {
                var k = s.camera[i];
                if (k.pos == null || k.pos.Length < 3) return "camera key " + i + ": pos needs x, y, z";
                if (!k.look_sun && (k.look == null || k.look.Length < 3)) return "camera key " + i + ": look needs x, y, z (or look_sun)";
                if (i > 0 && k.t < s.camera[i - 1].t) return "camera key " + i + ": its second " + k.t + " is before the key before it";
            }
            if (s.camera[0].t > 0.001f) return "the first camera key must be at second 0";
            foreach (var u in s.units)
                if (string.IsNullOrEmpty(u.team) || string.IsNullOrEmpty(u.type)) return "a unit without team or type";
            return null;
        }

        static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
    }
}
#endif
