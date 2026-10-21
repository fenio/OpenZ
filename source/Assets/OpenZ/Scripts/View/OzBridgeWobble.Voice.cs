using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The plank bridge's wobble sounds, through the game's sound table (Resources/OpenZ/Audio/openz_sound_events.json) and
    /// AudioDirector, driven by the wobble itself:
    /// - bld_bridge_plank_knock: a loose plank knocks (one knock or two in a row) each time a vehicle's contact reaches a pile row
    ///   (every 2 m, where the water rings start);
    /// - bld_bridge_wood_creak: the planks creak as the span sags under a moving vehicle (the pace of Creak: a jeep every 2.5-4 s,
    ///   tanks about once a second, the heavy tank and the rocket launcher twice a second), and softly under robots on foot (every
    ///   1.6-3 s while any walks on a deck);
    /// - bld_bridge_beam_groan: the long groan of the beams when a tank's weight comes onto the span, and 1 in 4 creaks of the heaviest class;
    /// - bld_bridge_rope_strain: the rope rails strain on the sway of the span (every 2-3.4 s; a jeep's lighter sway every 3.5-5 s).
    /// Weight: robots 0.4, jeep 0.55, tanks 0.8, heavy tank and rocket launcher 1.0 of the event's level, times 0.75-1 with speed;
    /// the rope strain also grows with the span's sway. The earlier clips of this script (the plank creaks, the load creak, the rope
    /// creak) are variations of these events now, and still play on the bridge's own source when no sound table is loaded. The plank
    /// rumble loop and the spring-back behind the last axle stay as they were.
    /// Sound log: AudioDirector's own line for every sound ("AD", the event, the clip, PLAY or why not) and one "L16" line saying why
    /// (what moved, the weight class).
    /// </summary>
    public sealed partial class OzBridgeWobble
    {
        public const string KnockEv = "bld_bridge_plank_knock", CreakEv = "bld_bridge_wood_creak", GroanEv = "bld_bridge_beam_groan", RopeEv = "bld_bridge_rope_strain";
        const float KnockRowM = 2f;

        /// <summary>Checks: sounds the sound table started since the game began, per event.</summary>
        public static int Knocks, Creaks, Groans, Ropes, RobotCreaks;
        /// <summary>Checks: the summed and counted volume factor per weight class (0 robots, 1 jeep, 2 tanks, 3 heavy).</summary>
        public static readonly float[] ClassVolSum = new float[4];
        public static readonly int[] ClassCount = new int[4];

        readonly Dictionary<int, float> knockS = new Dictionary<int, float>();
        readonly Dictionary<int, Vector3> robotAt = new Dictionary<int, Vector3>();
        readonly Dictionary<int, Vector3> robotSeen = new Dictionary<int, Vector3>();
        float nextRobotCreak;

        static float Weight(int cls) => cls < 0 ? 0.4f : cls == 0 ? 0.55f : cls == 1 ? 0.8f : 1f;
        static string ClassName(int cls) => cls < 0 ? "robots on foot" : cls == 0 ? "jeep" : cls == 1 ? "tank" : "heavy";
        static float SpeedVol(float v) => Mathf.Lerp(0.75f, 1f, Mathf.Clamp01(Mathf.Abs(v) / 6f));

        public static void ResetVoiceProof()
        {
            Knocks = Creaks = Groans = Ropes = RobotCreaks = 0;
            for (int i = 0; i < 4; i++) { ClassVolSum[i] = 0f; ClassCount[i] = 0; }
        }

        /// <summary>One wobble sound through the sound table; false when there is no table or no such event (the caller then plays
        /// its own old clip).</summary>
        bool Send(string ev, Vector3 at, int emitter, int cls, float vol, string why)
        {
            var ad = AudioDirector.I;
            if (!ad || ad.Def(ev) == null) return false;
            bool played = ad.Play(ev, at, emitter, vol, 1f);
            OzSoundLog.Add("L16", ev, null, (played ? "SENT World (" : "NOT PLAYED (") + why + ", " + ClassName(cls) + ")", vol, at);
            if (played) { int c = Mathf.Clamp(cls + 1, 0, 3); ClassVolSum[c] += vol; ClassCount[c]++; }
            return true;
        }

        /// <summary>From Creak: the creak (or the beams' groan) of a vehicle through the sound table; false = play the old clip.</summary>
        bool VoiceCreak(Load L, bool comingOn)
        {
            bool groan = (comingOn && L.cls >= 1) || (!comingOn && L.cls == 2 && Random.value < 0.25f);
            if (!Send(groan ? GroanEv : CreakEv, L.pos, L.id, L.cls, Weight(L.cls) * SpeedVol(L.v), comingOn ? "the weight comes on" : "the span sags under it")) return false;
            if (groan) Groans++; else Creaks++;
            return true;
        }

        /// <summary>From Update: the rope rails strain on the sway of the span; false = play the old clip.</summary>
        bool VoiceRope(Load top, int heavy)
        {
            if (heavy == 0) nextRope += 1.5f;                                                         // a jeep sways the span less: every 3.5-5 s
            float vol = Weight(heavy) * Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(Mathf.Abs(top.sway) / 0.03f));
            if (!Send(RopeEv, top.pos + Vector3.up * 1.2f, top.id, heavy, vol, "the span sways")) return false;
            Ropes++;
            return true;
        }

        /// <summary>From Update, every frame once the vehicles on the deck are known: plank knocks at the pile rows, and the soft
        /// creaks under robots on foot (robots do not move the deck; their wooden steps are the step sounds' own).</summary>
        void VoiceStep(ZGame game)
        {
            if (game.paused || Time.timeScale <= 0f) return;
            var dk = loadDeck;
            for (int i = 0; i < loads.Count; i++)
            {
                var L = loads[i];
                if (dk != null && knockS.TryGetValue(L.id, out var was) && Mathf.Abs(L.v) > 0.3f && Mathf.Abs(L.s) <= dk.half
                    && Mathf.FloorToInt(was / KnockRowM) != Mathf.FloorToInt(L.s / KnockRowM)
                    && Send(KnockEv, L.pos, L.id, L.cls, Weight(L.cls) * SpeedVol(L.v), "a plank knocks at a pile row"))
                    Knocks++;
                knockS[L.id] = L.s;
            }
            if (knockS.Count > loads.Count)
            {
                var gone = new List<int>();
                foreach (var kv in knockS) { bool on = false; foreach (var L in loads) if (L.id == kv.Key) { on = true; break; } if (!on) gone.Add(kv.Key); }
                foreach (var g in gone) knockS.Remove(g);
            }

            robotSeen.Clear();
            int walking = 0, first = -1; Vector3 sum = Vector3.zero;
            if (decks.Count > 0)
            {
                var objs = game.World.Objects;
                float dt = Mathf.Max(Time.deltaTime, 1e-4f);
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i]; if (o.Kind != ZKind.Robot || o.Destroyed) continue;
                    var p = game.CenterWorldSmooth(o);
                    foreach (var d in decks)
                    {
                        if (!d.walk || !d.walk.gameObject.activeInHierarchy) continue;
                        float s = (d.alongZ ? p.z : p.x) - d.c, a = (d.alongZ ? p.x : p.z) - d.mid;
                        if (Mathf.Abs(s) > d.half || Mathf.Abs(a) > d.halfWidth) continue;
                        robotSeen[o.Id] = p;
                        if (robotAt.TryGetValue(o.Id, out var before) && (p - before).magnitude / dt > 0.3f) { walking++; sum += p; if (first < 0) first = o.Id; }
                        break;
                    }
                }
            }
            robotAt.Clear(); foreach (var kv in robotSeen) robotAt[kv.Key] = kv.Value;
            if (walking > 0 && Time.time >= nextRobotCreak)
            {
                nextRobotCreak = Time.time + Random.Range(1.6f, 3f);
                float vol = Weight(-1) * Mathf.Min(1.5f, 1f + 0.1f * (walking - 1));                      // a squad creaks a little more than one robot
                if (Send(CreakEv, sum / walking, first, -1, vol, walking + " walking on the planks")) RobotCreaks++;
            }
        }
    }
}
