using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// every world sound is fired through its real gameplay call (same key, gain, bank, pitch) at the screen centre, the screen edge
    /// and 2 screen widths out, and OzLoudnessMeter (after the master) reads its max momentary loudness. Targets are set relative to
    /// the measured Alan order line (voices stay on top): explosions on top by size, weapon shots level with each other, whooshes
    /// under their launch / impact, foley below. adjust = true: before pass (trims off) -> trims -> refine pass -> after pass, then
    /// </summary>
    public static class OzMixProbe
    {
        public static string Status = "idle";
        public static bool Running;
        public static string LastReport;

        class Item
        {
            public string id, group, label, trim; public float target, dur = 1.5f; public System.Action<Vector3> fire; public string loopKey; public bool engine;
        }
        class Res { public float c = float.NaN, e = float.NaN, f = float.NaN; public bool pc, pe, pf, clamp; }

        public static bool Begin(bool adjust)
        {
            var m = OzLoudnessMeter.I;
            if (!m || Running || !ZGame.I || !AudioDirector.I || !CombatAudio.I) { Status = "cannot start (meter / level missing or already running)"; return false; }
            Running = true; Status = "starting";
            m.StartCoroutine(Run(adjust));
            return true;
        }

        static AudioSource engineSrc;
        static int emitterSeq;
        static int E() => -8000000 - (++emitterSeq);   // unique emitters: the gameplay throttles run on Time.time, frozen here

        static List<Item> Items()
        {
            var ca = CombatAudio.I; var ad = AudioDirector.I;
            var L = new List<Item>();
            System.Action<string, string, string, string, float, System.Action<Vector3>> A = (g, id, label, trim, target, f) =>
                L.Add(new Item { group = g, id = id, label = label, trim = trim, target = target, fire = f });
            // ---- explosions: on top, by size (targets = dB relative to the Alan order line)
            A("explosion", "exp_small", "small blast (prop / barrel)", "ca:explosion", -11f, p => ca.PlayPitched("explosion", p, LegacyMatch.GainExplosion * 0.8f, 0f, -1, 1.08f, LegacyMatch.BankExplosion));
            A("explosion", "exp_grenade", "grenade blast", "ca:explosion", -9.5f, p => Spec(ca, LegacyMatch.Grenade, p));
            A("explosion", "exp_rocket", "bazooka / rocket blast", "ca:explosion", -8f, p => Spec(ca, LegacyMatch.Bazooka, p));
            A("explosion", "exp_shell", "light tank shell impact", "ca:tank_impact", -8.5f, p => Spec(ca, LegacyMatch.LightShell, p));
            A("explosion", "exp_heavy_shell", "heavy tank shell impact", "ca:tank_impact", -7f, p => Spec(ca, LegacyMatch.HeavyShell, p));
            A("explosion", "exp_mlrs", "MLRS rocket blast", "ca:heavy", -6f, p => Spec(ca, LegacyMatch.MlrsRocket, p));
            A("explosion", "exp_vehicle", "vehicle death (light tank)", "ca:vdeath", -5.5f, p => Spec(ca, LegacyMatch.DeathLight, p));
            A("explosion", "exp_vehicle_heavy", "vehicle death (heavy tank)", "ca:vdeath", -5f, p => Spec(ca, LegacyMatch.DeathHeavy, p));
            A("explosion", "exp_building", "building collapse", "ca:collapse", -4.5f, p => ca.Play("collapse", p, 1f, 0f, -1, "v3-collapse-large-a", "v3-collapse-large-b", "building-collapse-20260909"));
            A("explosion", "exp_fort", "fort side booms", "ca:fortboom", -3.5f, p => ca.Play("fortboom", p, 0.7f, 0f, -1, "explosion-v1-20260909", "explosion-v2-20260909"));
            A("explosion", "exp_nuke", "nuke (fort death)", "ca:nuke", -2f, p => ca.PlayWide("nuke", p, 1.4f, "legacy-nuke-long"));
            L[L.Count - 1].dur = 2.6f;
            // ---- weapon shots: consistent with each other (cannons a little over, small arms a little under)
            A("shot", "shot_tank_light", "light tank shot", "ca:tankshot", -10f, p => LegacyMatch.LaunchSound("light", p, E(), ca));
            A("shot", "shot_tank_heavy", "heavy tank shot", "ca:heavyshot", -9f, p => LegacyMatch.LaunchSound("heavy", p, E(), ca));
            A("shot", "shot_gun", "gun (cannon) shot", "ca:gunshot", -10f, p => LegacyMatch.LaunchSound("gun", p, E(), ca));
            A("shot", "shot_howitzer", "howitzer shot (gunshot + cannon)", "ca:cannon", -9f, p => LegacyMatch.LaunchSound("howitzer", p, E(), ca));
            A("shot", "shot_fort_gun", "fort gun shot", "ca:fortgun", -8f, p => { ca.PlayPitched("fortgun", p, LegacyMatch.GainHeavyTankShot, 0f, -1, 0.72f, LegacyMatch.BankHeavyTankShot); ca.PlayPitched("gunshot", p, LegacyMatch.GainDefault * 1.6f, 0f, -1, 0.8f, LegacyMatch.BankCannon); });
            A("shot", "shot_rocket", "Tough rocket launch", "ca:rocket", -11f, p => { var rp = WeaponsV2.Pick(WeaponsV2.BankRocket); ca.PlayPitched("rocket", p, WeaponsV2.GainRocket * WeaponsV2.EvenGain(rp, WeaponsV2.BankRocket, "rocket"), 0f, -1, 1f, rp); });
            A("shot", "shot_mech_rocket", "mech rocket pod", "ca:mech_rocket", -11f, p => LegacyMatch.LaunchSound("medium", p, E(), ca));
            A("shot", "shot_mlrs", "MLRS launch", "ca:launch", -11f, p => LegacyMatch.LaunchSound("missile_launcher", p, E(), ca));
            A("shot", "shot_missile_cannon", "missile cannon roar", "ca:mc_rocket", -11f, p => LegacyMatch.LaunchSound("missile_cannon", p, E(), ca));
            A("shot", "shot_laser", "laser fire", "ca:laser", -11f, p => { var lp = WeaponsV2.Pick(WeaponsV2.BankLaserFire); ca.PlayPitched("laser", p, WeaponsV2.GainLaserFire * WeaponsV2.EvenGain(lp, WeaponsV2.BankLaserFire), 0f, -1, 1f, lp); });
            A("shot", "shot_electro", "Electrobot zap", "ad:wpn_electro_fire", -11f, p => AudioDirector.Fire("wpn_electro_fire", p, E()));
            A("shot", "shot_sniper", "sniper crack", "ca:sniper", -11f, p => { var sp = WeaponsV2.Pick(WeaponsV2.BankSniper); ca.PlayPitched("sniper", p, WeaponsV2.GainSniper * WeaponsV2.EvenGain(sp, WeaponsV2.BankSniper), 0f, -1, 1f, sp); });
            A("shot", "shot_pyro", "pyro flame (start + loop)", "ad:wpn_pyro_flame_*", -12f, p => { AudioDirector.Fire("wpn_pyro_flame_start", p, E()); ad.StartLoop("mixprobe:pyro", "wpn_pyro_flame_loop", p); });
            L[L.Count - 1].loopKey = "mixprobe:pyro"; L[L.Count - 1].dur = 2.2f;
            A("shot", "shot_mg", "psycho MG burst", "ca:mg", -12f, p => ca.PlayPitched("mg", p, WeaponsV2.GainMg, 0f, -1, 1f, "wv2_mg_1"));
            A("shot", "shot_burst", "rifle burst", "ca:burst", -12.5f, p => ca.PlayPitched("burst", p, LegacyMatch.GainDefault, 0f, -1, 1f, LegacyMatch.BankRifleBurst));
            A("shot", "shot_rifle", "rifle single", "ca:rifle", -13f, p => ca.PlayPitched("rifle", p, LegacyMatch.GainDefault, 0f, -1, 1f, LegacyMatch.BankRifleSingle));
            A("shot", "shot_bolt", "grunt bolt", "ca:bolt", -13f, p => ca.PlayPitched("bolt", p, LegacyMatch.GainBolt, 0f, -1, 1f, LegacyMatch.BankBolt));
            // ---- whooshes: under their launch and impact
            A("whoosh", "wh_rocket", "Tough rocket in flight", "ca:rocket_flight", -15f, p => ca.PlayPitched("rocket_flight", p, LegacyMatch.GainRocketFlight, 0f, -1, 1f, LegacyMatch.BankRocket));
            A("whoosh", "wh_shell", "shell whoosh", "ca:shell_whoosh", -15f, p => ca.PlayPitched("shell_whoosh", p, LegacyMatch.GainDefault * 1.2f, 0f, -1, 1f, LegacyMatch.BankShellWhoosh));
            A("whoosh", "wh_mech", "mech / MC rocket in flight", "ca:mech_flight", -15f, p => ca.PlayPitched("mech_flight", p, LegacyMatch.GainDefault * 1.1f, 0f, -1, 1f, MlrsSalvo.BankWhoosh));
            A("whoosh", "wh_whistle", "incoming howitzer whistle", "ca:shell_whistle", -14f, p => ca.PlayPitched("shell_whistle", p + Vector3.up * 6f, LegacyMatch.GainHeavy, 0f, -1, 0.87f, LegacyMatch.BankShellWhistle));
            // ---- impacts / debris
            A("impact", "imp_rocket_armor", "rocket on armour", "ca:rocket_armor", -10f, p => ca.PlayPitched("rocket_armor", p, LegacyMatch.GainRocketArmor, 0f, -1, 1f, LegacyMatch.BankRocketArmor));
            A("impact", "imp_laser", "laser zap on target", "ca:laser_hit", -14f, p => { var zp = WeaponsV2.Pick(WeaponsV2.BankLaserZap); ca.PlayPitched("laser_hit", p, WeaponsV2.GainLaserZap * WeaponsV2.EvenGain(zp, WeaponsV2.BankLaserZap), 0f, -1, 1f, zp); });
            A("impact", "imp_electro", "Electrobot hit", "ad:imp_electro_hit", -14f, p => AudioDirector.Fire("imp_electro_hit", p, E()));
            A("impact", "imp_ricochet", "bullet ricochet", "ca:ricochet", -16f, p => ca.PlayPitched("ricochet", p, OpenZFxCatalog.RicochetVolume, 0f, -1, 1f, OpenZFxCatalog.RicochetClips));
            A("impact", "imp_metal_ping", "armour ping", "ca:metal_ping", -16f, p => ca.PlayPitched("metal_ping", p, 0.32f, 0f, -1, 1f, "v3-armor-ricochet-a", "impact-1", "impact-2"));
            A("debris", "deb_heavy", "heavy debris", "ca:debris", -15f, p => ca.PlayPitched("debris", p, 0.75f, 0f, -1, 1f, OzSoundV3.BankHeavyDebris));
            A("debris", "deb_rock", "rock break", "ca:rock", -14f, p => ca.Play("rock", p, 0.8f, 0f, -1, "v3-rock-break-a", "v3-rock-break-b"));
            // ---- engines / world / foley / critters: below the fight
            A("foley", "eng_tank", "tank engine, moving (loop)", "ca:engine_loop", -17f, p => EngineOn(ca, p));
            L[L.Count - 1].engine = true; L[L.Count - 1].dur = 2.2f;
            A("foley", "door", "factory door open", "ad:bld_door_*", -15f, p => AudioDirector.Fire("bld_door_open", p, E()));
            A("foley", "step", "robot footstep, sand", "ad:rob_step_*", -19f, p => AudioDirector.Fire("rob_step_sand", p, E()));
            A("foley", "critter_thud", "bird thud (critter)", "ad:critter_*", -19f, p => AudioDirector.Fire("critter_bird_thud", p, E()));
            A("foley", "critter_gull", "gull take-off (critter)", "ad:critter_*", -20f, p => AudioDirector.Fire("critter_gull_takeoff", p, E()));
            return L;
        }

        static void Spec(CombatAudio ca, LegacyMatch.BlastSpec b, Vector3 p) => ca.PlayPitched(b.key, p, b.gain, 0f, -1, b.pitch, b.clips);

        static void EngineOn(CombatAudio ca, Vector3 p)
        {   // LegacyEngineAudio's own loop, as it plays a crewed tank driving past (GainCrewedMoving)
            var go = new GameObject("MixProbeEngine"); go.transform.position = p + Vector3.up;
            var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.loop = true; a.dopplerLevel = 0; a.spread = 60f;
            var lp = go.AddComponent<AudioLowPassFilter>();
            var clip = ca.Clip(ca.HasClip("tank-loop") ? "tank-loop" : "tank"); if (!clip) { Object.Destroy(go); return; }
            a.clip = clip; ca.Configure(a, lp, CombatAudio.Profile.Field, 85f * ca.RangeScale); a.maxDistance = Mathf.Max(8f, 85f * ca.RangeScale);
            a.volume = LegacyEngineAudio.GainCrewedMoving * ca.LoopBusGain * ca.LevelTrim(clip.name) * OzMixTrim.CaLin("engine_loop");
            a.Play(); engineSrc = a;
        }

        static Vector3 At(Camera cam, ZGame game, Vector3 focus, float vx)
        {
            var ray = cam.ViewportPointToRay(new Vector3(vx, 0.5f, 0f));
            float gy = game.GroundY(focus.x, focus.z);
            float t = Mathf.Abs(ray.direction.y) > 1e-4f ? (gy - ray.origin.y) / ray.direction.y : 100f;
            if (t < 0f) t = 100f;
            var p = ray.origin + ray.direction * t; p.y = game.GroundY(p.x, p.z) + 1f;
            return p;
        }

        static void StopAll(Item it)
        {
            if (it.loopKey != null && AudioDirector.I) AudioDirector.I.StopLoop(it.loopKey, 0.05f);
            if (engineSrc) { Object.Destroy(engineSrc.gameObject); engineSrc = null; }
            foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (s && s.isPlaying && !s.loop && s.spatialBlend > 0.5f) s.Stop();
        }

        static bool AnyClamp()
        {
            foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
                if (s && s.isPlaying && s.spatialBlend > 0.5f && s.volume >= 0.995f) return true;
            return false;
        }

        /// <summary>Fires one item at `at`; returns (max momentary LUFS, played, clamped).</summary>
        static IEnumerator Measure(Item it, Vector3 at, float[] outV, bool[] outB)
        {
            var m = OzLoudnessMeter.I; var ca = CombatAudio.I; var ad = AudioDirector.I;
            int caP = ca.Played, adP = ad.Played;
            m.MarkWindow();
            try { it.fire(at); } catch (System.Exception ex) { Debug.LogWarning("[OzMixProbe] " + it.id + ": " + ex.Message); }
            bool played = ca.Played > caP || ad.Played > adP || (it.engine && engineSrc);
            yield return new WaitForSecondsRealtime(0.06f);
            bool clamp = AnyClamp();
            yield return new WaitForSecondsRealtime(Mathf.Max(0.2f, it.dur - 0.06f));
            outV[0] = m.WindowMaxM; outB[0] = played; outB[1] = clamp;
            StopAll(it);
            yield return new WaitForSecondsRealtime(0.55f);
        }

        static IEnumerator Pass(List<Item> items, Dictionary<string, Res> res, Camera cam, ZGame game, Vector3 focus, bool centre, bool edge, bool far, string name)
        {
            var v = new float[1]; var b = new bool[2]; int n = 0;
            foreach (var it in items)
            {
                n++; Status = $"{name} {n}/{items.Count} {it.id}";
                if (!res.TryGetValue(it.id, out var r)) res[it.id] = r = new Res();
                if (centre) { yield return Measure(it, At(cam, game, focus, 0.5f), v, b); r.c = v[0]; r.pc = b[0]; r.clamp = b[1]; }
                if (edge) { yield return Measure(it, At(cam, game, focus, 1.0f), v, b); r.e = v[0]; r.pe = b[0]; }
                if (far) { yield return Measure(it, At(cam, game, focus, 2.5f), v, b); r.f = v[0]; r.pf = b[0]; }
            }
        }

        static float Median(List<float> x) { if (x.Count == 0) return -120f; var c = new List<float>(x); c.Sort(); return c[c.Count / 2]; }

        static IEnumerator Run(bool adjust)
        {
            var game = ZGame.I; var ad = AudioDirector.I; var ca = CombatAudio.I; var m = OzLoudnessMeter.I;
            var rts = Object.FindFirstObjectByType<RtsCamera>(); var cam = Camera.main;
            if (!rts || !cam) { Status = "no RtsCamera / camera"; Running = false; yield break; }
            float scale = Time.timeScale, h0 = rts.height; bool musicWas = AudioDirector.MusicEnabled;
            var sb = new StringBuilder(); var t0 = System.DateTime.Now;
            try { AudioDirector.MusicEnabled = false; } catch { }
            Time.timeScale = 0f;
            rts.SetHeightImmediate(70f);
            Status = "settling"; yield return new WaitForSecondsRealtime(3f);
            var focus = rts.focus;
            // background bed
            m.MarkWindow(); yield return new WaitForSecondsRealtime(3.2f);
            float bg = m.WindowMedianM, bgMax = m.WindowMaxM;
            // references: 3 Alan order lines, Rachel, UI click (2D, never trimmed)
            var alanM = new List<float>();
            for (int i = 0; i < 3; i++)
            {
                Status = "reference Alan " + (i + 1);
                m.MarkWindow(); AudioDirector.Say("vo_order_ack"); yield return new WaitForSecondsRealtime(2.6f);
                if (m.WindowMaxM > bg + 6f) alanM.Add(m.WindowMaxM);
                yield return new WaitForSecondsRealtime(0.8f);
            }
            m.MarkWindow(); AudioDirector.Announce("ann_robot_manufactured"); yield return new WaitForSecondsRealtime(2.8f); float rachel = m.WindowMaxM;
            yield return new WaitForSecondsRealtime(0.8f);
            m.MarkWindow(); AudioDirector.UI("ui_click"); yield return new WaitForSecondsRealtime(1.0f); float ui = m.WindowMaxM;
            float alan = alanM.Count > 0 ? Median(alanM) : -16f;
            Status = "waiting for the voice duck to release"; yield return new WaitForSecondsRealtime(2.5f);

            var items = Items();
            var before = new Dictionary<string, Res>(); var after = new Dictionary<string, Res>();
            var trims = new Dictionary<string, float>();
            if (adjust)
            {
                OzMixTrim.Off = true;
                yield return Pass(items, before, cam, game, focus, true, false, true, "before");
                OzMixTrim.Off = false;
                // trims from the before pass (absolute: mean deviation of the key's items)
                foreach (var g in Group(items, before, alan, bg)) trims[g.Key] = Mathf.Clamp(Mathf.Round(g.Value * 2f) / 2f, -10f, 10f);
                foreach (var it in items) if (it.trim != null && !trims.ContainsKey(it.trim)) trims[it.trim] = 0f;
                foreach (var kv in trims) SetTrim(kv.Key, kv.Value);
                var refine = new Dictionary<string, Res>();
                yield return Pass(items, refine, cam, game, focus, true, false, false, "refine");
                foreach (var g in Group(items, refine, alan, bg))
                    if (Mathf.Abs(g.Value) > 1f) { trims[g.Key] = Mathf.Clamp(Mathf.Round((trims[g.Key] + g.Value * 0.8f) * 2f) / 2f, -12f, 12f); SetTrim(g.Key, trims[g.Key]); }
            }
            yield return Pass(items, after, cam, game, focus, true, true, true, adjust ? "after" : "measure");

            // ---- report
            sb.AppendLine($"# task_sound_mix_balance: on-screen world mix, {game.level}, {t0:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"OzMixProbe (View/OzMixProbe.cs): sim frozen, music off, default 70 m RTS view, each sound fired through its gameplay call at the screen centre, "
                        + "the right screen edge and 2 screen widths out; value = max momentary loudness (400 ms, LUFS, after the master compressor / limiter). "
                        + "Short-term (3 s) readings are not used: consecutive events 2 s apart would share one window.");
            sb.AppendLine();
            sb.AppendLine($"References (2D, never trimmed): **Alan order line {alan:0.0}** (median of {alanM.Count}), Rachel {rachel:0.0}, UI click {ui:0.0}. "
                        + $"Background bed (ambience, idle units) median {bg:0.0}, max {bgMax:0.0} LUFS. Targets are dB relative to Alan.");
            sb.AppendLine();
            sb.AppendLine("| group | sound | trim key | target vs Alan | before centre | before 2 widths | after centre | after vs Alan | after edge | after 2 widths | trim dB | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            int ok = 0, near = 0, off = 0; float loudest = -120f; string loudestId = "";
            foreach (var it in items)
            {
                before.TryGetValue(it.id, out var rb); var ra = after[it.id];
                float rel = ra.c - alan, dev = rel - it.target;
                string verdict;
                if (!ra.pc) verdict = "NOT PLAYED (gate)";
                else if (Mathf.Abs(dev) <= 2f) { verdict = "OK"; ok++; }
                else if (Mathf.Abs(dev) <= 3.5f) { verdict = $"near ({dev:+0.0;-0.0})"; near++; }
                else { verdict = $"OFF ({dev:+0.0;-0.0})"; off++; }
                if (ra.clamp) verdict += ", source at volume 1 (clamped)";
                if (ra.pc && ra.c > loudest) { loudest = ra.c; loudestId = it.id; }
                string edgeS = ra.pe ? (ra.e >= bg + 1.5f ? $"{ra.e:0.0}" : $"{ra.e:0.0} (at the floor)") : "not played";
                string farS = ra.pf ? $"{ra.f:0.0} ({ra.f - ra.c:+0.0;-0.0})" : "culled (by design)";
                string bfS = rb == null ? "-" : rb.pf ? $"{rb.f:0.0}" : "culled";
                float tr = it.trim != null && trims.TryGetValue(it.trim, out var tv) ? tv : (it.trim != null ? TrimNow(it.trim) : 0f);
                sb.AppendLine($"| {it.group} | {it.label} | {it.trim} | {it.target:0.0} | {(rb == null ? "-" : rb.c.ToString("0.0"))} | {bfS} | {ra.c:0.0} | {rel:+0.0;-0.0} | {edgeS} | {farS} | {tr:+0.0;-0.0} | {verdict} |");
            }
            sb.AppendLine();
            sb.AppendLine($"Result: {ok} OK (within 2 dB), {near} near (2-3.5 dB), {off} OFF. Loudest world sound {loudestId} {loudest:0.0} = Alan {loudest - alan:+0.0;-0.0} dB "
                        + (loudest <= alan + 0.5f ? "(voices stay on top)" : "(OVER the voice: check)") + ".");
            sb.AppendLine("Off screen: only weapons / explosions / destruction play beyond the view margin (quieter + muffled, CombatAudio distant-battle rule); foley, impacts and critters are culled there by design.");
            if (adjust)
            {
                sb.AppendLine();
                sb.AppendLine("Trims written to Assets/OpenZ/Resources/OpenZ/Audio/mix_trims.txt (OzMixTrim, on top of every existing gain):");
                sb.AppendLine("```");
                sb.Append(OzMixTrim.Serialize(""));
                sb.AppendLine("```");
            }
            Time.timeScale = scale; rts.SetHeightImmediate(h0);
            try { AudioDirector.MusicEnabled = musicWas; } catch { }
            string path = Path.Combine(QcDir(), $"mix_balance_{game.level}_{System.DateTime.Now:yyyyMMdd_HHmm}.md");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, sb.ToString());
                if (adjust && Application.isEditor)
                    File.WriteAllText(Path.Combine(Application.dataPath, "OpenZ/Resources/OpenZ/Audio/mix_trims.txt"),
                        OzMixTrim.Serialize($"task_sound_mix_balance trims (OzMixProbe {game.level} {t0:yyyy-MM-dd HH:mm}): dB on top of every existing gain\nca <CombatAudio key> <dB> / ad <AudioDirector id or prefix*> <dB>"));
            }
            catch (System.Exception ex) { Debug.LogWarning("[OzMixProbe] " + ex.Message); }
            LastReport = path;
            Status = $"done: {ok} OK, {near} near, {off} OFF, loudest {loudestId} Alan {loudest - alan:+0.0;-0.0} -> {path}";
            Debug.Log("[OzMixProbe] " + Status);
            Running = false;
        }

        static float TrimNow(string trim) { var p = trim.Split(':'); return p.Length == 2 ? OzMixTrim.Get(p[0], p[1]) : 0f; }
        static void SetTrim(string trim, float db) { var p = trim.Split(':'); if (p.Length == 2) OzMixTrim.Set(p[0], p[1], db); }

        /// <summary>Mean (target - measured) per trim key over its played items.</summary>
        static Dictionary<string, float> Group(List<Item> items, Dictionary<string, Res> res, float alan, float bg)
        {
            var sum = new Dictionary<string, float>(); var n = new Dictionary<string, int>();
            foreach (var it in items)
            {
                if (it.trim == null || !res.TryGetValue(it.id, out var r) || !r.pc || float.IsNaN(r.c)) continue;
                float d = (alan + it.target) - r.c;
                sum[it.trim] = (sum.TryGetValue(it.trim, out var s) ? s : 0f) + d; n[it.trim] = (n.TryGetValue(it.trim, out var k) ? k : 0) + 1;
            }
            var o = new Dictionary<string, float>();
            foreach (var kv in sum) o[kv.Key] = kv.Value / n[kv.Key];
            return o;
        }

        static string QcDir()
        {
            var d = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "work")) && Directory.Exists(Path.Combine(d.FullName, "projects")))
                    return Path.Combine(d.FullName, "work", "records", "qc");
            return Application.persistentDataPath;
        }
    }
}
