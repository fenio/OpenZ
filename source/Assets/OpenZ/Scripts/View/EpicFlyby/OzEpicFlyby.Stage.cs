#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: the units, the set-up, the scenery and the timed cues of a take.
// Units are placed like the P cheat (CaptureKit.StageBattle: the game's own squads, health x hp_k) and then ordered through the game's
// own waypoints; nothing here changes a rule. Loungers use the hero squad's level-start lie (AnticRun Hold + StartLying: the lying
// robots get up through the antic's own cancel when an enemy comes within lounge_wake_m, an order, a hit). Crocodile catches run on
// the crocodile rules (a cue only starts a hunt on a wading robot: the warning, the stalk, the rise and the bite are the rules').
using System;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        sealed class Placed { public Unit u; public ZObject lead; public readonly List<int> ids = new List<int>(); public int lying; }
        readonly List<Placed> placed = new List<Placed>();
        readonly List<int> stagedIds = new List<int>(); readonly List<Team> stagedTeam = new List<Team>();
        readonly HashSet<int> claimed = new HashSet<int>();
        readonly List<GameObject> madeProps = new List<GameObject>();
        readonly StringBuilder alive = new StringBuilder();
        int crocRead, crocHunts0, crocKills0; bool[] cueDone;

        static Team TeamOf(string s) => s == "r" || s == "red" ? Team.Red : s == "b" || s == "blue" ? Team.Blue : Team.Neutral;
        static int TileX(float x) => Mathf.Clamp(Mathf.FloorToInt(x / 2f), 0, 4095);
        static int TileY(float z) => Mathf.Clamp(Mathf.FloorToInt(-z / 2f), 0, 4095);
        string Where(ZObject o) { var c = g.CenterWorld(o); return c.x.ToString("F0") + "," + c.z.ToString("F0"); }

        void Stage()
        {
            var known = new HashSet<int>(); foreach (var o in W.Objects) if (o != null) known.Add(o.Id);
            var sb = new StringBuilder();
            foreach (var u in sc.units)
            {
                var p = new Placed { u = u }; placed.Add(p);
                int tx = TileX(u.x), ty = TileY(u.z); var team = TeamOf(u.team);
                if (team == Team.Neutral)
                {
                    p.lead = W.CreateObject("vehicle", u.type, tx * 16, ty * 16, Team.Neutral);
                    if (p.lead != null) g.SpawnUnitView(p.lead);
                }
                else CaptureKit.StageBattle((team == Team.Red ? "r " : "b ") + u.type + " " + tx + " " + ty, u.hp_k > 0f ? u.hp_k : 1f);
                foreach (var o in W.Objects)
                {
                    if (o == null || known.Contains(o.Id)) continue;
                    known.Add(o.Id);
                    if (!o.IsUnit) continue;
                    p.ids.Add(o.Id);
                    if (o.Owner != Team.Neutral) { stagedIds.Add(o.Id); stagedTeam.Add(o.Owner); }
                    if (p.lead == null || (o.Leader == null && p.lead.Leader != null) || o.Minions.Count > p.lead.Minions.Count) p.lead = o;
                }
                if (p.lead == null) { Note("WARNING unit " + u.tag + " " + u.team + " " + u.type + " NOT placed (no room at " + u.x + "," + u.z + ")"); continue; }
                string start = string.IsNullOrEmpty(u.start) ? "idle" : u.start, r = start;
                if (start != "idle" && start != "lounge")
                {
                    var to = u.to != null && u.to.Length >= 2 ? new Vector2(u.to[0], u.to[1]) : new Vector2(u.x, u.z);
                    r = OrderIsland(p.lead, start, to.x, to.y, u.via);                                     // via points, hold position (OzEpicFlyby.Island.cs)
                }
                sb.Append(u.tag).Append(" ").Append(u.team).Append(" ").Append(u.type).Append(" x").Append(p.ids.Count).Append(" #").Append(p.lead.Id)
                  .Append(" at ").Append(Where(p.lead)).Append(": ").Append(r).Append("; ");
            }
            Note("placed " + placed.Count + " squads / vehicles: " + sb);
            foreach (var s in sc.setup) Note("set-up " + s.action + ": " + SetupOne(s));
            cueDone = new bool[sc.cues.Length];
        }

        // ------------------------------------------------------------------ orders (the game's own waypoints)
        ZObject NearestEnemy(ZObject u)
        {
            ZObject best = null; long bd = long.MaxValue;
            foreach (var e in W.Objects)
            {
                if (e == null || e.Destroyed || !e.IsUnit || e.Owner == u.Owner || e.Owner == Team.Neutral) continue;
                long dx = e.CenterX - u.CenterX, dy = e.CenterY - u.CenterY, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        ZObject NearestEmpty(float x, float z, float within, bool guns, bool vehicles)
        {
            ZObject best = null; float bd = within;
            foreach (var v in W.Objects)
            {
                if (v == null || v.Destroyed || v.Owner != Team.Neutral || claimed.Contains(v.Id)) continue;
                if (!((guns && v.Kind == ZKind.Cannon) || (vehicles && v.Kind == ZKind.Vehicle))) continue;
                var c = g.CenterWorld(v); float d = new Vector2(c.x - x, c.z - z).magnitude; if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        string Order(ZObject u, string mode, float x, float z)
        {
            if (u == null || u.Destroyed) return "gone";
            var p = ZGame.WorldToPx(new Vector3(x, 0f, z));
            var wl = new List<ZWaypoint>();
            if (mode == "move") wl.Add(ZWaypoint.Move(p.x, p.y));
            else if (mode == "attack_move") wl.Add(ZWaypoint.Move(p.x, p.y, true));
            else if (mode == "attack_nearest")
            {
                var e = NearestEnemy(u); if (e == null) return "attack_nearest: no enemy";
                wl.Add(new ZWaypoint { mode = WpMode.Attack, refId = e.Id, x = e.CenterX, y = e.CenterY });
            }
            else if (mode == "enter")
            {   // board the nearest empty vehicle or gun within 20 m of the point
                var v = NearestEmpty(x, z, 20f, true, true); if (v == null) return "enter: nothing empty within 20 m";
                claimed.Add(v.Id);
                wl.Add(new ZWaypoint { mode = WpMode.Enter, refId = v.Id, x = v.CenterX, y = v.CenterY });
                mode = "enter " + v.Type + " #" + v.Id;
            }
            else return "unknown order " + mode;
            return mode + (W.SendWaypoints(u.Owner, u, wl) ? " ok" : " REFUSED");
        }

        // ------------------------------------------------------------------ set-up
        string SetupOne(Setup s)
        {
            var team = TeamOf(s.team);
            if (s.action == "award_flag")
            {
                ZObject best = null; float bd = 14f;
                foreach (var f in W.Flags) { var c = g.CenterWorld(f); float d = new Vector2(c.x - s.x, c.z - s.z).magnitude; if (d < bd) { bd = d; best = f; } }
                if (best == null) return "no flag within 14 m of " + s.x + "," + s.z;
                if (team == Team.Neutral) return "award_flag needs team r or b";
                W.AwardZone(best, team);
                return "flag #" + best.Id + " at " + Where(best) + " now " + best.Owner;
            }
            if (s.action == "crew_gun")
            {   // a robot of the team boards the gun by the game's own boarding rule (the gun turns to its team, the robot inside)
                var gun = NearestEmpty(s.x, s.z, 8f, true, true); if (gun == null) return "no empty gun or vehicle within 8 m of " + s.x + "," + s.z;
                if (team == Team.Neutral) return "crew_gun needs team r or b";
                int gx = gun.CenterX >> 4, gy = gun.CenterY >> 4;
                if (!OzCheatSpawner.FreeTile(W, gx, gy, 1, true, out int fx, out int fy)) return "no free tile beside the gun";
                var r = W.CreateRobotGroup("grunt", fx * 16, fy * 16, team, 1); if (r == null) return "no robot made";
                g.SpawnUnitView(r);
                var enter = typeof(ZWorld).GetMethod("RobotEnterObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (enter == null) { claimed.Add(gun.Id); return "the boarding rule was not found: " + Order(r, "enter", s.x, s.z); }
                enter.Invoke(W, new object[] { r, gun }); claimed.Add(gun.Id);
                return gun.Type + " #" + gun.Id + " at " + Where(gun) + " crewed, owner " + gun.Owner + ", drivers " + gun.Drivers.Count;
            }
            if (s.action == "lounge_at") return LoungeAt(s);                                           // the scenario's own lounger (OzEpicFlyby.Island.cs)
            return "unknown set-up action";
        }

        // ------------------------------------------------------------------ loungers and scenery (in the pre-roll, the robots stand by then)
        bool propsMade;
        void Loungers()
        {
            var kit = AnticDirector.Kit;
            foreach (var p in placed)
            {
                if (p.u.start != "lounge" || p.lead == null || p.lying >= p.u.lounge) continue;
                var c = g.CenterWorld(p.lead); var fwd = CameraForwardNear(c);
                var parts = new List<string>();
                foreach (int id in p.ids)
                {
                    if (p.lying >= p.u.lounge) break;
                    if (!g.Views.TryGetValue(id, out var v) || !v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.AnticActive) continue;
                    string why = v.CanAntic ? v.AnticBusyReason() : "no antic clips";
                    if (why == null && (!kit || !kit.HasSunbed)) why = "no lounger kit";
                    AnticSpot spot = null;
                    if (why == null)
                    {
                        AnticSpot.GroundForwardOverride = fwd;
                        try { spot = AnticSpot.Sunbed(v); } finally { AnticSpot.GroundForwardOverride = null; }
                        if (spot == null) why = "no free flat spot";
                    }
                    if (why == null)
                    {
                        var run = new AnticRun(v, AnticKind.Sunbed, spot) { Hold = true, StartLying = true };
                        if (v.AnticBegin(run)) { AnticDirector.Ensure().Started(run); p.lying++; why = "lying"; } else why = "refused";
                    }
                    parts.Add("#" + id + " " + why);
                }
                if (parts.Count > 0) Note("loungers " + p.u.tag + ": " + p.lying + " of " + p.u.lounge + " lying (" + string.Join(", ", parts.ToArray()) + ")");
            }
        }

        /// <summary>The ground direction of the camera at the moment it looks closest to this point (the loungers face the lens).</summary>
        Vector3 CameraForwardNear(Vector3 c)
        {
            Vector3 best = Vector3.forward; float bd = float.MaxValue;
            for (float t = 0f; t <= sc.seconds; t += 0.5f)
            {
                Pose(t, out var pos, out var look, out _, out _);
                float d = new Vector2(look.x - c.x, look.z - c.z).sqrMagnitude;
                if (d < bd) { bd = d; best = look - pos; }
            }
            best.y = 0f; return best.sqrMagnitude > 0.01f ? best.normalized : Vector3.forward;
        }

        void Props()
        {
            if (propsMade) return; propsMade = true;
            foreach (var pr in sc.props)
            {
                if (pr.kind != "campfire") { Note("WARNING unknown prop " + pr.kind); continue; }
                var f = OzHeroCampfire.Make(g, new Vector3(pr.x, 0f, pr.z), TileX(pr.x), TileY(pr.z));
                if (f) { madeProps.Add(f.gameObject); Note("campfire at " + pr.x + "," + pr.z); } else Note("WARNING campfire at " + pr.x + "," + pr.z + " not made (no level campfire to copy)");
            }
        }

        // ------------------------------------------------------------------ cues
        void RunCues(float t, int frame)
        {
            for (int c = 0; c < sc.cues.Length; c++)
            {
                var q = sc.cues[c];
                if (cueDone[c] || t < q.t) continue;
                string a = q.action ?? "", res;
                try
                {
                    if (a.StartsWith("order:"))
                    {
                        res = ""; string m = a.Substring(6);
                        foreach (var p in placed) if (p.u.tag == q.tag && p.lead != null) res += OrderIsland(Lead(p), m, q.x, q.z, q.via) + ", ";
                        if (res == "") res = "no squad " + q.tag;
                    }
                    else if (a == "croc_hunt")
                    {
                        res = CrocHunt(q.tag);
                        if (res == null) { if (t < q.t + Mathf.Max(0.5f, q.window_s)) continue; res = "no robot of " + q.tag + " in attack water within " + Mathf.Max(0.5f, q.window_s) + " s"; }
                    }
                    else if (a == "destroy")
                    {
                        res = DestroyLead(q.tag);
                        if (res == null && !string.IsNullOrEmpty(q.alt)) { res = DestroyLead(q.alt); if (res != null) res += " (" + q.tag + " had fallen: " + q.alt + " instead)"; }
                        if (res == null) res = "no living unit " + q.tag + (string.IsNullOrEmpty(q.alt) ? "" : " or " + q.alt);
                    }
                    else if (a == "shake") { shakes.Add(new Shake { t0 = q.t, dur = q.shake_s > 0f ? q.shake_s : 1.2f, posM = q.shake_m > 0f ? q.shake_m : 0.35f, rotDeg = q.shake_deg > 0f ? q.shake_deg : 0.8f }); res = "camera shake"; }
                    else if (a == "sfx") res = PlaySfx(q.file, q.gain > 0f ? q.gain : 0.2f);
                    else if (a == "croc_catch") res = CrocCatchCue(q, t);                                  // OzEpicFlyby.Island.cs
                    else if (a == "croc_show") { res = CrocShowCue(q, t); if (res == null) continue; }   // round 4: the stream's crocodile drawn swimming (OzEpicFlyby.Kf4.cs)
                    else if (a == "wake") res = WakeCue(q.tag);
                    else if (a == "log_landing") res = LandingCue(q);
                    else if (a == "log_units")
                    {
                        var sb = new StringBuilder();
                        foreach (var p in placed) { int al = 0; foreach (int id in p.ids) { var o = W.Get(id); if (o != null && !o.Destroyed) al++; } sb.Append(p.u.tag).Append(" ").Append(al).Append("/").Append(p.ids.Count).Append(p.lead != null && !p.lead.Destroyed ? " at " + Where(p.lead) : "").Append("; "); }
                        res = sb.ToString();
                    }
                    else res = "UNKNOWN ACTION";
                }
                catch (Exception e) { res = "EXCEPTION " + e.Message; }
                cueDone[c] = true;
                Note("cue " + q.t.ToString("F1") + " s (frame " + frame + ") " + a + " " + q.tag + ": " + res);
            }
        }

        /// <summary>The lead of the first living squad / vehicle with this tag dies now (the game's own death chain and blast); null = none alive.</summary>
        string DestroyLead(string tag)
        {
            foreach (var p in placed)
            {
                if (p.u.tag != tag) continue; var o = Lead(p); if (o == null || o.Destroyed || o.KillAt >= 0) continue;
                o.DamageHealth(o.Health + 1); W.UpdateObjectHealth(o, -1); return o.Type + " #" + o.Id + " destroyed at " + Where(o);
            }
            return null;
        }

        /// <summary>The squad's living leader (the game hands a squad on to a minion when its leader falls).</summary>
        ZObject Lead(Placed p)
        {
            if (p.lead != null && !p.lead.Destroyed && p.lead.KillAt < 0) return p.lead;
            foreach (int id in p.ids) { var o = W.Get(id); if (o != null && !o.Destroyed && o.KillAt < 0 && o.Leader == null) return p.lead = o; }
            return p.lead;
        }

        /// <summary>Starts a hunt on the robot of the tag that has waded longest in attack water (null = none there now).</summary>
        string CrocHunt(string tag)
        {
            var list = new List<ZObject>();
            foreach (var p in placed)
                if (p.u.tag == tag) foreach (int id in p.ids) { var r = W.Get(id); if (r != null && !r.Destroyed && r.Kind == ZKind.Robot && r.KillAt < 0 && r.CrocWetSince >= 0 && W.CrocAttackWater(r.CenterX, r.CenterY)) list.Add(r); }
            list.Sort((x, y) => x.CrocWetSince.CompareTo(y.CrocWetSince));
            foreach (var r in list) if (W.CrocStartHuntForTest(r)) return "hunt on " + r.Type + " #" + r.Id + " at " + Where(r) + " (wading " + (W.Time - r.CrocWetSince).ToString("F1") + " s)";
            return null;
        }

        void ReadCrocNotes(int frame)
        {
            if (W.CrocNoteCount <= crocRead) return;
            int first = Mathf.Max(crocRead, W.CrocNoteCount - W.CrocNotes.Count);
            for (int m = first; m < W.CrocNoteCount; m++)
            {
                var e = W.CrocNotes[W.CrocNotes.Count - (W.CrocNoteCount - m)];
                string what = e.what == ZCrocNote.State ? "state " + e.state : e.what == ZCrocNote.Bite ? "BITE" : e.what == ZCrocNote.Moved ? "moved under" : e.what == ZCrocNote.Scared ? "scared off" :
                              e.what == ZCrocNote.Hurry ? "robots hurry" : e.what == ZCrocNote.Flee ? "robot leaves the water" : "robot on the bank";
                Note("crocodile " + e.croc + " frame " + frame + ": " + what + (e.robot >= 0 ? " (robot #" + e.robot + ")" : ""));
            }
            crocRead = W.CrocNoteCount;
        }

        /// <summary>The level's fires (torches, bowls, campfires) against the held clock: how many burn, how many the clock wants lit.</summary>
        void LogFires(string when)
        {
            var lf = L16Fires.I;
            if (!lf) { Note("fires at " + when + ": no fires of level 16 in this level"); return; }
            int want = 0, all = 0;
            foreach (var f in lf.Fires) { if (f == null || f.row == null) continue; all++; if (!f.dead && NightGrade.LightsOn(sc.time_of_day, f.row.onAt, f.row.offAt)) want++; }
            Note("fires at " + when + ": fires lit " + L16Fires.LitNow + " of " + want + " (the held clock " + sc.time_of_day.ToString("F3") + " wants " + want +
                 " of the level's " + all + " lit), burning " + L16Fires.BurningNow + ", real lights " + L16Fires.RealNow + "; the take's own campfires " + madeProps.Count);
        }

        void CountAlive(int frame)
        {
            int ar = 0, ab = 0;
            for (int k = 0; k < stagedIds.Count; k++) { var o = W.Get(stagedIds[k]); if (o == null || o.Destroyed) continue; if (stagedTeam[k] == Team.Red) ar++; else ab++; }
            alive.Append((frame / (float)fps).ToString("F1")).Append(":").Append(ar).Append("/").Append(ab).Append(" ");
        }

        // ------------------------------------------------------------------ a world sound of the level, 2D (the sun birds' calls)
        readonly List<AudioSource> sfx = new List<AudioSource>();
        string PlaySfx(string file, float gain)
        {
            if (string.IsNullOrEmpty(file)) return "no file";
            var clip = Resources.Load<AudioClip>(file.IndexOf('/') >= 0 ? file : "OpenZ/Audio/Sfx/L16/" + file);
            if (!clip) { OzSoundLog.Add("EFB", "epic_flyby_sfx", null, "DROP clip missing (" + file + ")"); return "clip missing: " + file; }
            AudioSource src = null; foreach (var s in sfx) if (s && !s.isPlaying) { src = s; break; }
            if (!src) { src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.dopplerLevel = 0f; src.priority = 60; sfx.Add(src); }
            float vol = gain * (AudioDirector.I ? AudioDirector.I.masterVolume : 1f);
            src.clip = clip; src.loop = false; src.volume = vol; src.Play();
            OzSoundLog.Add("EFB", "epic_flyby_sfx", clip.name, "PLAY 2D flyby", vol);
            return clip.name + " at " + vol.ToString("F2");
        }

        // robot voices that play as world sounds from the level's animal sound pool (the robots' spoken crocodile lines sd_vo_*, the
        // catch's alarm and cut-off cry): stopped before the frame's sound is rendered
        AudioSource[] animalPool; int silenced;

        /// <summary>A clip of the animal pool that is a robot voice: named in silence_clips or starting with one of silence_prefixes.</summary>
        bool IsRobotVoice(string clip)
        {
            if (string.IsNullOrEmpty(clip) || sc == null || sc.audio == null) return false;
            if (sc.audio.silence_clips != null && Array.IndexOf(sc.audio.silence_clips, clip) >= 0) return true;
            if (sc.audio.silence_prefixes != null)
                foreach (var p in sc.audio.silence_prefixes) if (!string.IsNullOrEmpty(p) && clip.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        void Silence()
        {
            if (animalPool == null || animalPool.Length == 0 || !animalPool[0])
            {   // looked up again every frame while it is missing (the level makes it at its first animal sound, again after a reload)
                var go = GameObject.Find("L16Animals_Audio"); animalPool = go ? go.GetComponentsInChildren<AudioSource>(true) : null;
            }
            if (animalPool == null) return;
            foreach (var a in animalPool)
            {
                if (!a || !a.isPlaying || !a.clip || !IsRobotVoice(a.clip.name)) continue;
                a.Stop(); silenced++;
                OzSoundLog.Add("EFB", "epic_flyby_silence", a.clip.name, "STOPPED robot voice (scenario)", 0f);
            }
        }

        void ReleaseSfx()
        {
            foreach (var s in sfx) if (s) s.Stop();
            foreach (var go in madeProps) if (go) Destroy(go);
            madeProps.Clear();
        }
    }
}
#endif
