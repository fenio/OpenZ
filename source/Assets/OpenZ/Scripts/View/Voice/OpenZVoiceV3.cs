// OpenZ (GPL-3.0)
//  1. NUKE, explained, so it is clear why and how it happens. When the ENEMY fort falls, the bomber now waits (hidden, silent) while
//       b) Commander Zod opens a transmission (ZodBriefing.PlayNuke, group "nuke", 3 lines) and slams a big red button on the
//          last word ("Sector clear! Launching the NUKE!"): the slam launches the strike (OpenZNukeStrike.LaunchNow),
//       c) a robot jokes as the bomb drops (vo_nuke_delivery: "Here comes the delivery!", "Special delivery for blue!" ...).
//     Our own fort falling keeps the old lines ("Warning! Nuke incoming!" + "Our fort has fallen"). Watchdog: the strike
//     launches by itself after the planned hold (PlanNuke) whatever happens to the voices. NukeMode: Full / RachelOnly /
//     ZodOnly / Off (Off = the old "Nuke launched." with no wait).
//  2. VEHICLE CAPTURED, varied. AudioDirector.OnEntered
//     asks CaptureLine(): with CaptureQuipChance the capturing robot quips (vo_vehicle_captured: "Got us a ride!", "Keys were
//     still in it!" ...; the event's shuffle bag never repeats a clip twice in a row) and Rachel adds "Vehicle captured." only
//     sometimes (CaptureRachelAlso); otherwise Rachel's line as before.
//  3. FIRE OUT. FireOut() plays vo_fire_out ("Fire extinguished!",
//     "Fire's out!", "Phew! Flames are out!", "All cool now, boss!") when a fire that was burning goes out because of a repair.
//     AudioDirector.OnRevived calls it for a building that is rebuilt while its ruin still burns; the damage-fire view of ZF7
//     (battered hulls, fires on damaged units, not built yet) calls SetBurning(id, true / false) and the line comes when a
//     burning unit is repaired (RepairOut) or its fire is put out by a repair (SetBurning false with repaired = true).
//  4. L17 TIPS. Rachel gives up to 6 one-off tips on L17
//     (ann_l17_tip, played with an exact line filter), each only while it is still true: the car park down south full of empty
//     vehicles (9 s after the level is handed over), any robot can drive one (35 s, none captured yet), hurry (65 s, fewer
//     than 3 captured), snipers empty drivers (100 s, the player has snipers and the enemy a vehicle), cranes rebuild the
//     bridges (the player owns a crane and a bridge is down), and a warning the first time the CPU steals a car-park vehicle.
//     Never over another voice or Commander Zod, >= TipGap s apart, never in bot matches, test mode or automated checks.
// Plain view code: reads the sim, never writes it (the sim stays deterministic; the strike hold is view-only, like the strike).
using System;
using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    [DefaultExecutionOrder(45)]
    public sealed class OpenZVoiceV3 : MonoBehaviour
    {
        public static OpenZVoiceV3 I { get; private set; }
        public static bool Enabled = true;

        // ---------------------------------------------------------------------------------------------------------- nuke
        public enum NukeMode { Full, RachelOnly, ZodOnly, Off }
        /// <summary>Full = Rachel explains, then Zod's transmission + red button; the bomber launches on the slam.</summary>
        public static NukeMode Nuke = NukeMode.ZodOnly;
        /// <summary>Pause after Rachel's line before Zod's window opens; longest waits for Rachel / for Zod's slam.</summary>
        public static float NukeGap = 0.2f, NukeWaitRachel = 6f, NukeZodMax = 6.5f;
        /// <summary>REVIEW: the watchdog's slack over the planned intro (was 0.8 s). Rachel can start up to ~2 s late (she waits for
        /// Alan 0.6 s, a playing rank 1-2 line is never cut); with 0.8 s the watchdog could launch the bomber before Zod's slam
        /// (effect before cause). Only a timeout: LaunchNow() ends the hold first in the normal case, so it costs no time.</summary>
        public static float NukeWatchdogSlack = 2f;
        /// <summary>Chance that a robot jokes when the bomb drops (it happens once per game).</summary>
        public static float NukeJokeChance = 1f;

        // ---------------------------------------------------------------------------------------------------------- capture
        /// <summary>Chance that the capturing robot quips (else Rachel's "Vehicle captured."), and that Rachel still confirms after a quip.</summary>
        public static float CaptureQuipChance = 0.65f, CaptureRachelAlso = 0.3f;
        /// <summary>Seconds after the capture: the robot's quip (after the capture sound), Rachel's line (as before: 0.8 s), Rachel after a quip.</summary>
        public static float CaptureQuipDelay = 0.35f, CaptureRachelDelay = 0.8f, CaptureRachelAfterQuip = 2.1f;

        // ---------------------------------------------------------------------------------------------------------- fire out
        public static float FireOutChance = 0.85f, FireOutCooldown = 8f;
        static float lastFireOut = -100f;
        static readonly HashSet<int> burningUnits = new HashSet<int>();

        // ---------------------------------------------------------------------------------------------------------- L17 tips
        public static bool L17Tips = true;
        /// <summary>Game seconds after the hand-over for the first tip, between two tips, and the window after which no timed tip starts.</summary>
        public static float TipFirst = 9f, TipGap = 22f, TipStealGap = 10f, TipWindow = 360f;
        /// <summary>
        /// The first tip waits TipFirstSlow s after the hand-over, every later tip (the steal warning too)
        /// at least TipGapSlow s after the previous one, and each only after TipQuiet s with no other voice (Rachel, Zod, robots).</summary>
        public static float TipFirstSlow = 18f, TipGapSlow = 45f, TipQuiet = 4f;
        float voiceBusyAt = -100f;

        ZGame game; bool tipsOn, tipsReady; float handAt = -1f, lastTip = -100f;
        readonly HashSet<string> tipsDone = new HashSet<string>();
        readonly HashSet<int> carPark = new HashSet<int>();
        int playerTook; bool enemyStole; float enemyStoleAt = -1f;
        readonly List<string> log = new List<string>();

        // ================================================================================================ boot
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
            planAt = -100f; launched = true; burningUnits.Clear(); impactActions.Clear(); crowdAvoid.Clear();
        }

        /// <summary>
        /// OpenZVoiceV3: the bomber flew at once, no Rachel / Zod). The first scene also installs here (the guard stops doubles).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootFirstScene() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (m != LoadSceneMode.Single) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (g && !g.GetComponent<OpenZVoiceV3>()) g.gameObject.AddComponent<OpenZVoiceV3>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this; game = GetComponent<ZGame>(); if (!game) game = ZGame.I;
            planAt = -100f; launched = true; burningUnits.Clear(); crowdAvoid.Clear();
        }

        void OnEnable() { OpenZNukeStrike.Impacted -= OnImpacted; OpenZNukeStrike.Impacted += OnImpacted; }
        void OnDisable() { OpenZNukeStrike.Impacted -= OnImpacted; }
        void OnDestroy() { if (I == this) I = null; impactActions.Clear(); }

        void Start()
        {
            if (!game) game = ZGame.I;
            tipsOn = Enabled && L17Tips && game && game.level == "L17" && !Bots && !GameBoot.TestMode;
            Debug.Log($"[VoiceV3] live: level {(game ? game.level : "-")}, nuke intro {Nuke}, L17 tips {(tipsOn ? "on" : "off")}");
        }

        bool Bots => game && ((game.blueBot && game.redBot) || PlayerPrefs.GetInt("oz_botmatch", 0) == 1);
        static bool Automated => CaptureKit.Enabled || GateBattery.Running;

        // ================================================================================================ 1. nuke
        static float planAt = -100f, planHold; static bool launched = true, planRachel, planZod;
        static readonly List<Action> impactActions = new List<Action>();

        /// <summary>Called in the frame a fort falls by AudioDirector.Nuke and by OpenZNukeStrike.Begin (either order): the first call
        /// plans the intro and starts it; both get the same answer. Returns the longest time (s) the bomber waits for the voices
        /// (0 = no intro: our own fort, intro off, nothing to play, or the planned intro already launched the strike).</summary>
        public static float PlanNuke(bool ours)
        {
            if (Time.time - planAt < 2f) return launched ? 0f : planHold;                        // same fall (fort_front + fort_back)
            planAt = Time.time; launched = true; planHold = 0f; planRachel = planZod = false;
            if (ours || !Enabled || Nuke == NukeMode.Off || !I || !OpenZNukeStrike.Enabled) return 0f;
            if (GateBattery.Running) return 0f;                                                    // the gate's fort_falls stage times the strike (8 s)
            planRachel = Nuke != NukeMode.ZodOnly && AudioDirector.Has("ann_nuke_explain");
            planZod = Nuke != NukeMode.RachelOnly && ZodBriefing.CanPlayNuke;
            if (!planRachel && !planZod) return 0f;
            float r = planRachel ? MaxClip("ann_nuke_explain") + 0.45f + NukeGap : 0f;           // the queue starts her next frame
            float z = planZod ? ZodBriefing.NukeLeadSeconds + 0.3f : 0f;                          // window preroll + line up to the slam
            planHold = r + z + NukeWatchdogSlack;                                                 // watchdog margin: LaunchNow comes first
            launched = false;
            I.StartCoroutine(I.NukeIntro(planRachel, planZod));
            Debug.Log($"[VoiceV3] enemy fort fell: nuke intro (Rachel {planRachel}, Zod {planZod}), bomber held up to {planHold:F1} s");
            return planHold;
        }

        /// <summary>True from the fort's fall until the strike launched (the intro still talks).</summary>
        public static bool NukeIntroRunning => !launched && Time.time - planAt < planHold + 1f;

        IEnumerator NukeIntro(bool rachel, bool zod)
        {
            float t0 = Time.time;
            string said = null;
            if (rachel)
            {
                said = PickExplain();                                                              // REVIEW: chosen here, so Zod never repeats her words
                AudioDirector.Announce("ann_nuke_explain", said != null ? "=" + said : null);
                float s = Time.time;
                while (!AudioDirector.VoiceBusy && Time.time - s < 0.8f) yield return null;         // queued -> playing
                s = Time.time;
                while (AudioDirector.VoiceBusy && Time.time - s < NukeWaitRachel) yield return null;
                yield return new WaitForSeconds(NukeGap);
            }
            if (zod && !launched && planHold - (Time.time - planAt) < ZodBriefing.NukeLeadSeconds + 0.2f)
            {   // REVIEW: Rachel ran late: his slam could no longer come before the watchdog launch (bomber first, button after)
                zod = false;
                Debug.Log("[VoiceV3] no time left for Zod's slam before the watchdog: launching after Rachel");
            }
            if (zod && !launched)
            {
                bool slammed = false;
                // REVIEW: Rachel "... Launching the nuke ..." + Zod "Sector clear! Launching the nuke!" = the same words twice in a row
                string avoid = said != null && said.IndexOf("launching", StringComparison.OrdinalIgnoreCase) >= 0 ? "launching" : null;
                if (ZodBriefing.PlayNuke(() => slammed = true, avoid))
                {
                    float s = Time.time;
                    while (!slammed && Time.time - s < NukeZodMax && (Time.time - s < 0.8f || ZodBriefing.Speaking)) yield return null;
                    if (!slammed) Debug.Log("[VoiceV3] Zod's nuke line ended without a slam beat: launching");
                }
                else Debug.Log("[VoiceV3] Zod busy / unavailable: launching after Rachel");
            }
            Launch($"intro done after {Time.time - t0:F1} s");
        }

        static void Launch(string why)
        {
            if (launched) return;
            launched = true;
            OpenZNukeStrike.LaunchNow();
            Debug.Log("[VoiceV3] nuke launched: " + why);
        }

        /// <summary>ZodBriefing: the fist hit the red button.</summary>
        public static void ZodSlammed() => Launch("Zod slammed the red button");

        /// <summary>OpenZNukeStrike.Release: the bomb leaves the bomber: a robot of the winning (player) side cracks a joke.</summary>
        public static void OnBombReleased(bool ours)
        {
            if (ours || !Enabled || !I || !AudioDirector.Has("vo_nuke_delivery")) return;
            if (UnityEngine.Random.value > NukeJokeChance) return;
            AudioDirector.Say("vo_nuke_delivery");
        }

        /// <summary>AudioDirector.Nuke: run `a` at the strike's impact (the duck + ear ring used to wait a fixed delay).</summary>
        public static void OnNukeImpact(Action a) { if (a != null) impactActions.Add(a); }

        void OnImpacted(Vector3 at, bool ours)
        {
            launched = true;
            var list = new List<Action>(impactActions); impactActions.Clear();
            foreach (var a in list) { try { a(); } catch (Exception e) { Debug.LogException(e); } }
        }

        const string ExplainPref = "oz_v3_nuke_explain_last";

        /// <summary>REVIEW: Rachel's explanation, random, never the one of the previous nuke (PlayerPrefs); null = let the event pick.</summary>
        static string PickExplain()
        {
            var d = AudioDirector.I ? AudioDirector.I.Def("ann_nuke_explain") : null;
            if (d?.lines == null || d.loaded == null) return null;
            var all = new List<string>();
            for (int i = 0; i < d.lines.Count && i < d.loaded.Count; i++)
                if (d.loaded[i] && !string.IsNullOrEmpty(d.lines[i]) && !all.Contains(d.lines[i])) all.Add(d.lines[i]);
            if (all.Count == 0) return null;
            string last = PlayerPrefs.GetString(ExplainPref, "");
            if (all.Count > 1) all.Remove(last);
            var pick = all[UnityEngine.Random.Range(0, all.Count)];
            PlayerPrefs.SetString(ExplainPref, pick);
            return pick;
        }

        static float MaxClip(string id)
        {
            var d = AudioDirector.I ? AudioDirector.I.Def(id) : null;
            float m = 0f;
            if (d?.loaded != null) foreach (var c in d.loaded) if (c) m = Mathf.Max(m, c.length);
            return m > 0f ? m : 3.5f;
        }

        // ================================================================================================ victory
        /// <summary>Whoops that open the victory lines (the remade joyful "Yeah!" / "Yeee-haa!" and "Woo-hoo! We won!").</summary>
        public static readonly string[] VictoryWhoops = { "Yeah!", "Yeee-haa!", "Woo-hoo! We won!" };
        const string VictoryPref = "oz_v3_victory_last";

        /// <summary>OpenZVictory (won branch): n different lines of vo_victory_cheer for the sequence, a whoop first, random, never a
        /// line of the previous victory (PlayerPrefs, so across matches too); "GG, blue!" only when blue lost. Empty = not imported.</summary>
        public static List<string> VictoryPicks(int n, bool blueLost)
        {
            var res = new List<string>();
            var d = AudioDirector.I ? AudioDirector.I.Def("vo_victory_cheer") : null;
            if (d?.lines == null || d.loaded == null || d.loaded.Count == 0) return res;
            var all = new List<string>();
            foreach (var l in d.lines) if (!string.IsNullOrEmpty(l) && !all.Contains(l) && (blueLost && !TeamColors.Swap || l.IndexOf("blue", StringComparison.OrdinalIgnoreCase) < 0 && !(TeamColors.Swap && TeamColors.NamesColour(l)))) all.Add(l);
            var last = new HashSet<string>(PlayerPrefs.GetString(VictoryPref, "").Split('|'));
            var whoops = all.FindAll(l => Array.IndexOf(VictoryWhoops, l) >= 0);
            var rest = all.FindAll(l => Array.IndexOf(VictoryWhoops, l) < 0);
            string first = PickFresh(whoops, last) ?? PickFresh(rest, last);
            if (first != null) { res.Add(first); rest.Remove(first); whoops.Remove(first); }
            while (res.Count < n)
            {
                var s = PickFresh(rest, last) ?? PickFresh(whoops, last);
                if (s == null) break;
                res.Add(s); rest.Remove(s); whoops.Remove(s);
            }
            if (res.Count < n) return new List<string>();
            PlayerPrefs.SetString(VictoryPref, string.Join("|", res));
            crowdAvoid.Clear();                                                                    // REVIEW: the crowd never echoes the lead's lines
            for (int i = 0; i < d.lines.Count && i < d.loaded.Count; i++)
                if (d.loaded[i] && res.Contains(d.lines[i])) crowdAvoid.Add(ClipKey(d.loaded[i].name));
            return res;
        }

        /// <summary>REVIEW: clip keys (alan_easy_gg for alan_easy_gg_1 / _2) of the three lines the victory sequence speaks. OpenZCelebrate
        /// skips them, so a crowd robot never repeats "Time for rocket fuel!" two seconds after the lead said it.</summary>
        static readonly HashSet<string> crowdAvoid = new HashSet<string>();
        public static bool CrowdAvoids(string key) => key != null && crowdAvoid.Contains(key);
        static string ClipKey(string clip)
        {
            int u = clip.LastIndexOf('_');
            return u > 0 && u < clip.Length - 1 && int.TryParse(clip.Substring(u + 1), out _) ? clip.Substring(0, u) : clip;
        }

        static string PickFresh(List<string> from, HashSet<string> avoid)
        {
            if (from.Count == 0) return null;
            var fresh = from.FindAll(x => !avoid.Contains(x));
            var pool = fresh.Count > 0 ? fresh : from;
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        // ================================================================================================ 2. capture
        /// <summary>AudioDirector.OnEntered (the player's robot took a vehicle that was not his): true = the robot quips now.</summary>
        public static bool CaptureQuip() => Enabled && AudioDirector.Has("vo_vehicle_captured") && UnityEngine.Random.value < CaptureQuipChance;
        public static bool CaptureRachelToo() => UnityEngine.Random.value < CaptureRachelAlso;

        /// <summary>AudioDirector.OnEntered, every boarding (tips: who took which car-park vehicle).</summary>
        public static void NoteEntered(ZObject r, ZObject v, Team before)
        {
            if (!I || r == null || v == null || v.Kind != ZKind.Vehicle) return;
            I.OnEnteredTip(r, v, before);
        }

        // ================================================================================================ 3. fire out
        /// <summary>A fire that was burning went out because of a repair (player's or on screen): a robot says so.</summary>
        public static void FireOut(Vector3 at, bool playerSide)
        {
            if (!Enabled || !playerSide || !AudioDirector.Has("vo_fire_out")) return;
            if (Time.time - lastFireOut < FireOutCooldown || UnityEngine.Random.value > FireOutChance) return;
            lastFireOut = Time.time;
            AudioDirector.Say("vo_fire_out");
        }

        /// <summary>ZF7 damage-fire view: a unit starts / stops burning. repaired = the fire went out because of a repair.</summary>
        public static void SetBurning(ZObject o, bool burning, bool repaired = false)
        {
            if (o == null) return;
            if (burning) { burningUnits.Add(o.Id); return; }
            bool was = burningUnits.Remove(o.Id);
            if (was && repaired && ZGame.I) FireOut(ZGame.I.CenterWorld(o), o.Owner == ZGame.I.player);
        }

        /// <summary>AudioDirector RepairOut: the unit left the repair bay healed; if it was burning, the fire is out.</summary>
        public static void RepairedUnit(ZObject o)
        {
            if (o == null || !burningUnits.Contains(o.Id)) return;
            SetBurning(o, false, true);
        }

        // ================================================================================================ 4. L17 tips
        void Update()
        {
            if (!tipsOn || game == null || game.World == null) return;
            var W = game.World;
            if (!tipsReady)
            {   // the car park = every vehicle that is empty (neutral) when the level starts (L17: the 20 in the bottom rows)
                foreach (var o in W.Objects) if (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral && !o.Destroyed) carPark.Add(o.Id);
                tipsReady = true;
                if (carPark.Count < 3) { tipsOn = false; return; }
            }
            if (handAt < 0f)
            {
                if (ZodBriefing.BriefingActive || !game.enabled || game.paused) return;
                handAt = Time.time;
            }
            if (W.GameOver || game.paused || Automated) return;
            if (AudioDirector.VoiceBusy || ZodBriefing.Speaking) { voiceBusyAt = Time.time; return; }
            if (Time.time - voiceBusyAt < TipQuiet) return;                                              // (25): a breath after any other voice
            float t = Time.time - handAt;
            float gap = Mathf.Max(TipGap, TipGapSlow), first = Mathf.Max(TipFirst, TipFirstSlow);
            if (enemyStole && !tipsDone.Contains("steal") && tipsDone.Contains("car_park") && Time.time - enemyStoleAt > 1.5f && Time.time - lastTip > Mathf.Max(TipStealGap, gap) && t > first + 4f)
            {
                if (NeutralLeft(W) >= 2) Tip("steal", "Warning! The enemy is stealing vehicles from the car park!");
                else tipsDone.Add("steal");
                return;
            }
            if (Time.time - lastTip < gap) return;
            if (t > TipWindow && tipsDone.Contains("car_park")) return;
            int left = NeutralLeft(W);
            if (!tipsDone.Contains("car_park")) { if (t >= first) { if (left >= 3) Tip("car_park", "Tip! The car park down south is full of empty vehicles."); else tipsDone.Add("car_park"); } return; }
            if (!tipsDone.Contains("any_robot") && t >= 35f) { if (playerTook == 0 && left >= 3) Tip("any_robot", "Any robot can drive an empty vehicle. Send them in to capture it!"); else tipsDone.Add("any_robot"); return; }
            if (!tipsDone.Contains("hurry") && t >= 65f) { if (playerTook < 3 && left >= 4) Tip("hurry", "Hurry! Grab those tanks before the enemy does."); else tipsDone.Add("hurry"); return; }
            if (!tipsDone.Contains("cranes") && t >= 20f && PlayerOwns(W, "crane") && AnyBridgeDown(W)) { Tip("cranes", "Cranes from the car park can rebuild the broken bridges."); return; }
            if (!tipsDone.Contains("snipers") && t >= 100f && PlayerOwns(W, "sniper") && EnemyHasVehicle(W)) { Tip("snipers", "Snipers can shoot a driver out of his seat. Then take the vehicle!"); return; }
        }

        void Tip(string key, string line)
        {
            tipsDone.Add(key); lastTip = Time.time;
            AudioDirector.Announce("ann_l17_tip", "=" + line);
            if (log.Count < 12) log.Add($"{key}@{Time.time - handAt:F0}s");
            Debug.Log($"[VoiceV3] L17 tip '{key}' at {Time.time - handAt:F0} s: {line}");
        }

        void OnEnteredTip(ZObject r, ZObject v, Team before)
        {
            if (!tipsOn || !carPark.Contains(v.Id)) return;
            if (r.Owner == game.player) { if (before != game.player) playerTook++; }
            else if (r.Owner != Team.Neutral && before == Team.Neutral && !enemyStole) { enemyStole = true; enemyStoleAt = Time.time; }
        }

        int NeutralLeft(ZWorld W)
        {
            int n = 0;
            foreach (var id in carPark) { var o = W.Get(id); if (o != null && !o.Destroyed && o.Owner == Team.Neutral) n++; }
            return n;
        }

        bool PlayerOwns(ZWorld W, string type)
        {
            foreach (var o in W.Objects) if (o.Owner == game.player && o.Type == type && !o.Destroyed) return true;
            return false;
        }

        static bool AnyBridgeDown(ZWorld W)
        {
            foreach (var o in W.Objects) if (o.Kind == ZKind.Building && o.IsBridge && o.Destroyed) return true;
            return false;
        }

        bool EnemyHasVehicle(ZWorld W)
        {
            foreach (var o in W.Objects) if (o.Kind == ZKind.Vehicle && o.Owner != game.player && o.Owner != Team.Neutral && !o.Destroyed) return true;
            return false;
        }

        // ================================================================================================ checks
        /// <summary>Play-mode check (an editor script): <c>return OpenZ.OpenZVoiceV3.Status();</c></summary>
        public static string Status()
        {
            if (!I) return "OpenZVoiceV3: none in this scene";
            var W = I.game ? I.game.World : null;
            return $"OpenZVoiceV3: nuke {Nuke} (planned {(Time.time - planAt < 60f ? $"{Time.time - planAt:F0} s ago, hold {planHold:F1} s, Rachel {planRachel}, Zod {planZod}, launched {launched}" : "no")}), "
                 + $"tips {(I.tipsOn ? "on" : "off")} car park {I.carPark.Count} (neutral left {(W != null ? I.NeutralLeft(W) : 0)}, player took {I.playerTook}, enemy stole {I.enemyStole}), "
                 + $"done [{string.Join(", ", I.log)}], burning units {burningUnits.Count}";
        }

        /// <summary>Play check: the next tip now (ignores its time and conditions). key: car_park, any_robot, hurry, snipers, cranes, steal.</summary>
        public static string DebugTip(string key)
        {
            if (!I) return "no OpenZVoiceV3";
            var lines = new Dictionary<string, string>
            {
                ["car_park"] = "Tip! The car park down south is full of empty vehicles.",
                ["any_robot"] = "Any robot can drive an empty vehicle. Send them in to capture it!",
                ["hurry"] = "Hurry! Grab those tanks before the enemy does.",
                ["snipers"] = "Snipers can shoot a driver out of his seat. Then take the vehicle!",
                ["cranes"] = "Cranes from the car park can rebuild the broken bridges.",
                ["steal"] = "Warning! The enemy is stealing vehicles from the car park!",
            };
            if (!lines.TryGetValue(key ?? "", out var l)) return "keys: " + string.Join(", ", lines.Keys);
            I.Tip(key, l);
            return "tip " + key;
        }
    }
}
