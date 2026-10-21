using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Idle antics: now and then an idle robot does a small thing on its own (for example a can of rocket fuel: drink, throw
    /// the can away, the can lands with physics and pops out a little later).
    ///
    /// Who: a living BS-01 humanoid robot (grunt and the five V3 types; the laser's Generic rig has no antic clips and is excluded)
    /// that has stood idle for IdleMin + its own 0..IdleJitter s: no order or waypoint (its squad leader's neither), not moving, not
    /// engaged, not selected, not hit, no enemy unit within StartThreatM and no missile landing near, its cooldown over, on screen.
    /// How many: at most MaxPerTeam per team and MaxOnScreen on screen at once, MinStartGap s between any two starts, then a low
    /// chance per scan (mean MeanWait s), so a quiet army does one small thing now and then, never all at once.
    /// What: one antic from a weighted list. Fishing only where open water lies within FishReachM of the robot (never from a bridge
    /// deck, facing the water, the bobber on the water surface with AnticSpot.FishMarginM of open water all round it: never on water
    /// so narrow that the cast could reach the far bank); the sunbed only when its team holds more zones than any other team,
    /// within SunbedFortM of its own fort, on a free flat spot; the prop antics only when the kit is imported.
    /// Cancel: UnitView.AnticAnim checks every frame and cancels on the same frame (props pop off, the weapon pops back).
    /// View only: UnityEngine.Random, no sim writes, so replays and the sim hash are untouched.
    /// </summary>
    [DefaultExecutionOrder(58)]
    public sealed class AnticDirector : MonoBehaviour
    {
        public static bool Enabled = true, Verbose = true;
        public static float IdleMin = 12f, IdleJitter = 8f, MeanWait = 22f, ScanEvery = 0.5f, MinStartGap = 5f;
        public static float CooldownMin = 70f, CooldownMax = 110f;
        public static int MaxPerTeam = 2, MaxOnScreen = 3;
        /// <summary>Who gets the antic when one is due: with PreferSeen the idle robot the player sees best (his own team first, in
        /// the part of the picture the fixed panels leave free, near the middle) instead of whichever robot rolled it. The chance
        /// that an antic starts in a scan is the same either way. The free part of the picture as viewport fractions.</summary>
        public static bool PreferSeen = true;
        public static float SeenXMin = 0.06f, SeenXMax = 0.94f, SeenYMin = 0.24f, SeenYMax = 0.90f;
        public static int StartsSeenArea, StartsPlayer;
        readonly List<UnitView> cands = new List<UnitView>();
        readonly List<float> candScore = new List<float>();
        public static float StartThreatM = 36f, CancelThreatM = 28f, MissileNearM = 12f;
        public static float FishReachM = 3f, SunbedFortM = 70f;
        /// <summary>Resting antics (fishing, the sunbed) only from RestMinGameSec s of play (sim time: the sim starts when the player gets
        /// the camera) and only while the robot's team leads the unit bar by RestLeadRatio (own units >= that many times the enemy's); a running
        /// rest ends (props pop out) when the lead stays under the ratio for RestEndGrace s. Per team: the CPU's robots use the CPU's lead.
        /// Drink / phone / the small moves keep their own rules. ForceIgnoresRest lets a test harness force a rest antic anyway.</summary>
        public static float RestMinGameSec = 300f, RestLeadRatio = 1.4f, RestEndGrace = 3f;
        public static bool ForceIgnoresRest;
        public static int RestRefusedEarly, RestRefusedLead, RestAllowed, RestEndedLead;
        /// <summary>Why team t may not start a resting antic now (null = it may).</summary>
        public static string RestBlock(ZWorld w, Team t)
        {
            if (w == null || t == Team.Neutral) return "no world";
            if (w.Time < RestMinGameSec) return $"before {RestMinGameSec / 60f:0}:00 of play (now {(int)(w.Time / 60)}:{(int)(w.Time % 60):00})";
            int own = w.UnitCount(t), enemy = 0;
            foreach (Team e in System.Enum.GetValues(typeof(Team))) if (e != t && e != Team.Neutral) enemy = Mathf.Max(enemy, w.UnitCount(e));
            if (own < RestLeadRatio * Mathf.Max(1, enemy)) return $"lead below {RestLeadRatio:0.0}x ({own} vs {enemy} units)";
            return null;
        }
        readonly Dictionary<AnticRun, float> restLow = new Dictionary<AnticRun, float>();
        /// <summary>No antic in the first MinLevelTime s of level play (counted while the game runs unpaused, so the briefing never uses it up), or while the game object is off.</summary>
        public static float MinLevelTime = 40f;
        /// <summary>Weights of the antic list (0 = never). Order: Turn, Scan, Neck, Phone, Drink, Fish, Sunbed.</summary>
        public static float[] Weights = { 1.5f, 1f, 1.5f, 3f, 4f, 4f, 5f };

        public static AnticDirector I { get; private set; }
        public static AnticKit Kit => AnticKit.Get();

        ZGame game; PlayerInput input; float scanT, lastStart = -99f, summaryT, playT, inputSearchAt;
        readonly List<AnticRun> runs = new List<AnticRun>();
        readonly Dictionary<Team, List<Vector3>> enemies = new Dictionary<Team, List<Vector3>>();
        readonly List<Vector3> missiles = new List<Vector3>();
        int enemiesFrame = -1;

        // stats (Report / the Play check)
        public static int Starts, Cancels, Ends, MaxTeamSeen, MaxScreenSeen;
        public static readonly int[] StartsBy = new int[7], CancelsBy = new int[7];
        public static readonly List<string> Log = new List<string>();
        public static float FishBobberErrMaxCm;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            I = null; Starts = Cancels = Ends = MaxTeamSeen = MaxScreenSeen = 0; FishBobberErrMaxCm = 0f; StartsSeenArea = StartsPlayer = 0;
            System.Array.Clear(StartsBy, 0, 7); System.Array.Clear(CancelsBy, 0, 7); Log.Clear();
        }

        public static AnticDirector Ensure()
        {
            if (I) return I;
            I = FindFirstObjectByType<AnticDirector>();
            if (!I) I = new GameObject("AnticDirector").AddComponent<AnticDirector>();
            return I;
        }

        void Awake() { if (I && I != this) { Destroy(this); return; } I = this; }
        void OnDestroy() { if (I == this) I = null; }

        static void Say(string s) { if (Log.Count >= 300) Log.RemoveAt(0); Log.Add($"{Time.time:F2} {s}"); if (Verbose) Debug.Log("[OpenZ Antics] " + s); }

        // ------------------------------------------------------------------ queries used by UnitView every frame
        public static bool IsSelected(ZObject o)
        {
            var d = I; if (!d || o == null) return false;
            if (!d.input && Time.unscaledTime >= d.inputSearchAt) { d.inputSearchAt = Time.unscaledTime + 1f; d.input = FindFirstObjectByType<PlayerInput>(); }   // no scene search per robot per frame
            if (!d.input) return false;
            var sel = d.input.Selection;
            for (int i = 0; i < sel.Count; i++) { var s = sel[i]; if (s == o || (o.Leader != null && s == o.Leader)) return true; }
            return false;
        }

        /// <summary>A threat for a robot doing an antic: an enemy unit within CancelThreatM or a missile landing within MissileNearM.</summary>
        public static string ThreatReason(UnitView v)
        {
            var d = I; if (!d || v == null || v.Obj == null) return null;
            d.RefreshThreats();
            var p = v.transform.position;
            if (d.enemies.TryGetValue(v.Obj.Owner, out var list))
                foreach (var e in list) if ((e - p).sqrMagnitude < CancelThreatM * CancelThreatM) return "enemy near";
            foreach (var m in d.missiles) if ((m - p).sqrMagnitude < MissileNearM * MissileNearM) return "missile near";
            return null;
        }

        void RefreshThreats()
        {
            if (enemiesFrame == Time.frameCount) return;
            enemiesFrame = Time.frameCount;
            foreach (var l in enemies.Values) l.Clear();
            missiles.Clear();
            var g = ZGame.I; if (!g || g.World == null) return;
            if (!enemies.ContainsKey(Team.Red)) { enemies[Team.Red] = new List<Vector3>(); enemies[Team.Blue] = new List<Vector3>(); }
            foreach (var o in g.World.Objects)
            {
                if (o == null || o.Destroyed || !o.IsUnit || o.Owner == Team.Neutral || !o.CanAttack()) continue;
                var w = ZGame.PxToWorld(o.CenterX, o.CenterY);
                if (o.Owner != Team.Red) enemies[Team.Red].Add(w);
                if (o.Owner != Team.Blue) enemies[Team.Blue].Add(w);
            }
            var pm = g.World.PendingMissiles;                                                        // IReadOnlyList: an index loop (a foreach would box its enumerator every frame)
            for (int i = 0; i < pm.Count; i++) { var m = pm[i]; if (m != null && !m.debris) missiles.Add(ZGame.PxToWorld(m.x, m.y)); }
        }

        public static void NoteFish(float errM) { FishBobberErrMaxCm = Mathf.Max(FishBobberErrMaxCm, errM * 100f); }

        // ------------------------------------------------------------------ runs
        internal void Started(AnticRun r)
        {
            runs.Add(r); Starts++; StartsBy[(int)r.Kind]++; lastStart = Time.time;
            int team = 0, screen = 0;
            foreach (var x in runs) { if (x.View && x.View.Obj != null && r.View.Obj != null && x.View.Obj.Owner == r.View.Obj.Owner) team++; if (x.View && WreckFx.OnScreen(x.View.transform.position)) screen++; }
            MaxTeamSeen = Mathf.Max(MaxTeamSeen, team); MaxScreenSeen = Mathf.Max(MaxScreenSeen, screen);
            var o = r.View.Obj;
            Say($"start {r.Kind} on {o.Type} #{o.Id} ({o.Owner}) idle {r.View.AnticIdleFor:F1} s; active team {team}/{MaxPerTeam}, on screen {screen}/{MaxOnScreen}");
        }

        internal void Stopped(AnticRun r, bool cancelled, string reason)
        {
            runs.Remove(r);
            if (cancelled) { Cancels++; CancelsBy[(int)r.Kind]++; } else Ends++;
            var o = r.View ? r.View.Obj : null;
            Say($"{(cancelled ? "cancel" : "end")} {r.Kind} on {(o != null ? o.Type + " #" + o.Id : "?")} at {r.Age:F2} s in {r.Step}: {reason}" + (cancelled ? " (props popped off, weapon back, same frame)" : ""));
        }

        void Update()
        {
            game = ZGame.I;
            if (!Enabled || !game || game.World == null || !UnitView.AnticsEnabled) return;
            runs.RemoveAll(r => r == null || r.Finished || !r.View);
            if ((summaryT += Time.deltaTime) >= 60f) { summaryT = 0f; Say(Report()); }
            for (int i = runs.Count - 1; i >= 0; i--)
            {   // a resting antic ends gracefully when its team's lead stays under the ratio (or the game turns)
                var r = runs[i]; if ((r.Kind != AnticKind.Fish && r.Kind != AnticKind.Sunbed) || r.Hold) continue;   // DV108: the level-start lie of a hero squad ends only on a click, an order or a threat
                if (ForceIgnoresRest || r.View.Obj == null) continue;
                var block = RestBlock(game.World, r.View.Obj.Owner);
                if (block == null || block.StartsWith("before")) { restLow.Remove(r); continue; }
                if (!restLow.TryGetValue(r, out var since)) { restLow[r] = Time.time; continue; }
                if (Time.time - since < RestEndGrace) continue;
                restLow.Remove(r); RestEndedLead++;
                Say($"rest ends: {r.Kind} on {r.View.Obj.Type} #{r.View.Obj.Id} ({block})");
                r.View.AnticCancel("team lead lost: " + block, false);
            }
            if (game.isActiveAndEnabled && !game.paused) playT += Time.deltaTime;                  // level PLAY time: a briefing (game disabled) or a pause does not count
            if ((scanT += Time.deltaTime) < ScanEvery) return;
            float dt = scanT; scanT = 0f;
            if (game.paused || !game.isActiveAndEnabled || game.World.GameOver || playT < MinLevelTime || Time.time - lastStart < MinStartGap) return;
            var kit = Kit;
            RefreshThreats();
            float p = 1f - Mathf.Exp(-dt / Mathf.Max(1f, MeanWait));
            int screen = 0; foreach (var r in runs) if (r.View && !r.Hold && WreckFx.OnScreen(r.View.transform.position)) screen++;   // DV108: nor of those on screen
            if (screen >= MaxOnScreen) return;
            cands.Clear(); candScore.Clear(); int rolled = 0;
            var cam = Camera.main;
            foreach (var v in game.Views.Values)
            {
                if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.AnticActive || OzHeroAntics.OwnRule(v.Obj)) continue;   // KC4b: the hero squad's antics are their own rule (the drink only, View/OzHeroAntics.cs)
                if (v.AnticIdleFor < IdleMin + IdleJitter * Hash01(v.Obj.Id) || Time.time < v.AnticCooldownUntil) continue;
                if (!v.CanAntic || !WreckFx.OnScreen(v.transform.position, -0.05f)) continue;
                if (TeamActive(v.Obj.Owner) >= MaxPerTeam) continue;
                if (Threatened(v, StartThreatM)) { v.AnticResetIdle(); continue; }
                bool roll = Random.value <= p;
                if (!PreferSeen)
                {
                    if (roll && TryStart(v, null, kit, false, out _)) { NoteSeen(v, cam); return; }    // one start per scan (MinStartGap)
                    continue;
                }
                if (roll) rolled++;
                cands.Add(v); candScore.Add(SeenScore(v, cam));
            }
            if (!PreferSeen || rolled == 0) return;
            for (int n = 0; n < 3 && cands.Count > 0; n++)
            {   // the best seen first; a robot that cannot start gives way to the next
                int best = 0; for (int i = 1; i < cands.Count; i++) if (candScore[i] > candScore[best]) best = i;
                var v = cands[best]; cands.RemoveAt(best); candScore.RemoveAt(best);
                if (TryStart(v, null, kit, false, out _)) { NoteSeen(v, cam); return; }
            }
        }

        /// <summary>How well the player sees this robot now: his own team first, then the free part of the picture, then nearness to
        /// its middle, with a small random part so the same robot is not always the one.</summary>
        float SeenScore(UnitView v, Camera cam)
        {
            float s = v.Obj.Owner == game.player ? 2f : 0f;
            if (!cam) return s;
            var vp = cam.WorldToViewportPoint(v.transform.position);
            if (InSeenArea(vp)) s += 1.5f;
            float dx = vp.x - 0.5f, dy = vp.y - 0.5f * (SeenYMin + SeenYMax);
            return s - Mathf.Sqrt(dx * dx + dy * dy) + Random.value * 0.25f;
        }

        static bool InSeenArea(Vector3 vp) => vp.z > 0f && vp.x >= SeenXMin && vp.x <= SeenXMax && vp.y >= SeenYMin && vp.y <= SeenYMax;

        void NoteSeen(UnitView v, Camera cam)
        {
            if (v.Obj.Owner == game.player) StartsPlayer++;
            if (cam && InSeenArea(cam.WorldToViewportPoint(v.transform.position))) StartsSeenArea++;
        }

        void LateUpdate()
        {
            for (int i = runs.Count - 1; i >= 0; i--) { var r = runs[i]; if (r != null && !r.Finished) r.Late(Time.deltaTime); }
            AnticFx.Step();
        }

        int TeamActive(Team t) { int n = 0; foreach (var r in runs) if (r.View && r.View.Obj != null && r.View.Obj.Owner == t && !r.Hold) n++; return n; }   // DV108: a held level-start lie is not one of the team's antics

        bool Threatened(UnitView v, float radius)
        {
            var p = v.transform.position;
            if (enemies.TryGetValue(v.Obj.Owner, out var list)) foreach (var e in list) if ((e - p).sqrMagnitude < radius * radius) return true;
            foreach (var m in missiles) if ((m - p).sqrMagnitude < MissileNearM * MissileNearM) return true;
            return false;
        }

        static float Hash01(int id) { uint h = (uint)id * 2654435761u; h ^= h >> 15; return (h & 0xFFFF) / 65535f; }

        /// <summary>Picks (or forces) an antic for `v` and starts it. `force` skips the team-lead rule of the sunbed (Play check).</summary>
        bool TryStart(UnitView v, AnticKind? want, AnticKit kit, bool force, out string why)
        {
            why = null;
            var options = new List<(AnticKind k, float w, AnticSpot s)>();
            for (int i = 0; i < 7; i++)
            {
                var kind = (AnticKind)i;
                if (want.HasValue && want.Value != kind) continue;
                float w = want.HasValue ? 1f : Weights[i];
                if (w <= 0f) continue;
                if (!want.HasValue && v.AnticLast == kind) w *= 0.25f;                                      // rarely the same thing twice
                AnticSpot s = null;
                switch (kind)
                {
                    case AnticKind.Phone: case AnticKind.Drink:
                        if (!kit || !kit.HasHandProps || !kit.socketOk) { why = "kit: hand props missing"; continue; }
                        break;
                    case AnticKind.Fish:
                        if (!kit || !kit.HasFishing) { why = "kit: fishing props missing"; continue; }
                        if (!RestOk(v, kind, out why)) continue;
                        s = AnticSpot.Fishing(v, FishReachM, out var fwhy); AnticSpot.NoteFishPick(s);
                        if (s == null) { why = fwhy; if (AnticSpot.LastWasNarrow) Say($"no fishing for {v.Obj.Type} #{v.Obj.Id}: {fwhy}"); continue; }
                        break;
                    case AnticKind.Sunbed:
                        if (!kit || !kit.HasSunbed) { why = "kit: sunbeds missing"; continue; }
                        if (!RestOk(v, kind, out why)) continue;
                        if (!force && !TeamLeads(v.Obj.Owner)) { why = "team not leading"; continue; }
                        if (!force && !NearOwnFort(v, SunbedFortM)) { why = "not near its own fort"; continue; }
                        s = AnticSpot.Sunbed(v); if (s == null) { why = "no free flat spot for the lounger"; continue; }
                        break;
                }
                options.Add((kind, w, s));
            }
            if (options.Count == 0) return false;
            float sum = 0f; foreach (var o in options) sum += o.w;
            float r = Random.value * sum; var pick = options[options.Count - 1];
            foreach (var o in options) { if ((r -= o.w) <= 0f) { pick = o; break; } }
            var run = new AnticRun(v, pick.k, pick.s);
            if (!v.AnticBegin(run)) { why = "view refused (busy or no clip states)"; return false; }
            Started(run);
            if (pick.k == AnticKind.Fish && pick.s != null) Say($"fish spot #{v.Obj.Id}: {pick.s.FishText()}");
            return true;
        }

        bool RestOk(UnitView v, AnticKind kind, out string why)
        {
            why = null;
            if (ForceIgnoresRest) return true;
            var block = RestBlock(game ? game.World : null, v.Obj.Owner);
            if (block == null) { RestAllowed++; return true; }
            if (block.StartsWith("before")) RestRefusedEarly++; else RestRefusedLead++;
            why = "rest refused: " + block;
            if (Verbose && Time.time - lastRestSay > 10f) { lastRestSay = Time.time; Say($"{kind} refused for {v.Obj.Type} #{v.Obj.Id}: {block}"); }
            return false;
        }
        float lastRestSay = -99f;

        bool TeamLeads(Team t)
        {
            var w = game ? game.World : null; if (w == null || t == Team.Neutral) return false;
            int mine = 0, best = 0; var other = new Dictionary<Team, int>();
            foreach (var z in w.Zones)
            {
                if (z.Owner == t) mine++;
                else if (z.Owner != Team.Neutral) { other.TryGetValue(z.Owner, out int n); other[z.Owner] = n + 1; }
            }
            foreach (var n in other.Values) best = Mathf.Max(best, n);
            return mine > best;
        }

        bool NearOwnFort(UnitView v, float radius)
        {
            var w = game.World; var p = v.transform.position;
            foreach (var o in w.Objects)
            {
                if (o == null || o.Destroyed || o.Kind != ZKind.Building || o.Owner != v.Obj.Owner || o.Type == null || !o.Type.StartsWith("fort")) continue;
                var c = game.CenterWorld(o, false); c.y = p.y;
                if ((c - p).sqrMagnitude < radius * radius) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Play check helpers (an editor menu or a code call in Play)
        /// <summary>Starts antic `kind` (Turn, Scan, Neck, Phone, Drink, Fish, Sunbed) on robot `id` now: skips the idle timer, the chance,
        /// the caps and the sunbed's team-lead / fort rule; keeps every other rule (idle state, water, flat spot, kit).</summary>
        public static string Force(int id, string kind)
        {
            var d = Ensure(); var g = ZGame.I; if (!g || g.World == null) return "no game";
            if (!System.Enum.TryParse(kind, true, out AnticKind k)) return "unknown kind " + kind;
            if (!g.Views.TryGetValue(id, out var v) || !v) return "no view #" + id;
            if (v.AnticActive) return "#" + id + " already runs " + v.Antic.Kind;
            if (!v.CanAntic) return "#" + id + " cannot play antics (no humanoid antic states)";
            var busy = v.AnticBusyReason(); if (busy != null) return "#" + id + " busy: " + busy;
            d.game = g;
            return d.TryStart(v, k, Kit, true, out var why) ? $"started {k} on #{id}" : $"cannot start {k} on #{id}: {why}";
        }

        /// <summary>Forces `kind` on the idle robot nearest the screen centre (a team-`team` robot, 0 = any team).</summary>
        public static string ForceNearCentre(string kind, int team = 0)
        {
            var g = ZGame.I; var cam = Camera.main; if (!g || !cam) return "no game / camera";
            UnitView best = null; float bd = float.MaxValue;
            foreach (var v in g.Views.Values)
            {
                if (!v || !v.isActiveAndEnabled || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.AnticActive || !v.CanAntic) continue;
                if (team != 0 && (int)v.Obj.Owner != team) continue;
                if (v.AnticBusyReason() != null) continue;
                var vp = cam.WorldToViewportPoint(v.transform.position); if (vp.z <= 0f) continue;
                float dd = (vp.x - 0.5f) * (vp.x - 0.5f) + (vp.y - 0.5f) * (vp.y - 0.5f);
                if (System.Enum.TryParse(kind, true, out AnticKind kk) && kk == AnticKind.Fish && AnticSpot.Fishing(v, FishReachM) == null) continue;
                if (dd < bd) { bd = dd; best = v; }
            }
            return best ? Force(best.Obj.Id, kind) : "no idle antic-capable robot on screen" + (kind.ToLower() == "fish" ? " within reach of open water" : "");
        }

        /// <summary>Counts, caps, cancels, the can log and the last runs.</summary>
        public static string Report()
        {
            var sb = new StringBuilder();
            sb.Append($"antics: starts {Starts} (");
            for (int i = 0; i < 7; i++) sb.Append($"{(AnticKind)i} {StartsBy[i]}{(i < 6 ? ", " : "")}");
            sb.Append($"), ends {Ends}, cancels {Cancels}; max active per team {MaxTeamSeen}/{MaxPerTeam}, max on screen {MaxScreenSeen}/{MaxOnScreen}; ");
            sb.Append($"cans thrown {AnticCan.Thrown}, clatters {AnticCan.Clatters}, disposed {AnticCan.Disposed}, sunk {AnticCan.Sunk}; bobber max off the water {FishBobberErrMaxCm:F1} cm; fishing spots found {AnticSpot.FishSpotsOk}, refused for narrow water {AnticSpot.FishSpotsNarrow}, for a foot over the water {AnticSpot.FishSpotsWetFoot}{(AnticSpot.FishSpotsOk > 0 ? $", min far-bank clearance {AnticSpot.FishMinFarClear:F1} m" : "")}; prop pops {AnticFx.Pops}");
            if (I) foreach (var r in I.runs) sb.Append($" | running {r.Kind} #{(r.View && r.View.Obj != null ? r.View.Obj.Id : -1)} {r.Step} {r.Age:F1} s{(r.Kind == AnticKind.Fish ? $", feet over water {AnticSpot.FeetOverWater(r.View)}" : "")}");
            return sb.ToString();
        }

        public static string LastLog(int n = 30)
        {
            var sb = new StringBuilder();
            for (int i = Mathf.Max(0, Log.Count - n); i < Log.Count; i++) sb.AppendLine(Log[i]);
            foreach (var c in AnticCan.Log) sb.AppendLine(c);
            return sb.ToString();
        }
    }

    /// <summary>Robot antics: where a fishing robot casts / a lounger stands (world space, found once at the start).</summary>
    public sealed class AnticSpot
    {
        public float yaw, waterY;
        public Vector3 edge, land, bed, parasol;
        public Quaternion bedRot = Quaternion.identity, parasolRot = Quaternion.identity;
        /// <summary>The lounger's width factor for this robot's body (AnticSockets.BedWidthFor).</summary>
        public float bedWidth = 1f;

        /// <summary>
        /// shore, so such places are never fishing spots). The bobber's landing point needs FishMarginM of open water all round it
        /// (the far bank included): the cast out from the water's edge must be shorter than the water width along the cast line minus
        /// FishMarginM. Open water = see OpenWater (never a bridge deck, a quay top, a sand bar, the far bank or the wet shore band).</summary>
        public static float FishMarginM = 1.5f, FishDepthM = 0.05f, FishSampleM = 0.2f;
        /// <summary>The angler's footprint: a disc round the robot that holds both feet at every facing (the farthest foot / toe bone
        /// from its centre, at least FishFootM, plus FishFootMarginM for the sole round the bone; authored m) must be dry: nowhere on
        /// it may the drawn water show (WaterShows: a water surface over ground lower than the water level + FishFootDryM).</summary>
        public static float FishFootM = 0.30f, FishFootMarginM = 0.20f, FishFootDryM = 0.03f;
        public static int FishBearings = 36;
        /// <summary>Casts out from the water's edge, longest first (authored m, x robot scale).</summary>
        static readonly float[] CastOut = { 3.2f, 2.6f, 2.0f };
        /// <summary>Fishing spot stats (AnticDirector candidates, Report): spots found, robots by the water refused for narrow water, the
        /// smallest clearances of the spots found. LastWasNarrow: the last FishingAt call saw water but no cast fitted.</summary>
        public static int FishSpotsOk, FishSpotsNarrow, FishSpotsWetFoot; public static bool LastWasNarrow, LastWasWetFoot;
        public static float FishMinFarClear = float.PositiveInfinity, FishMinRingClear = float.PositiveInfinity;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFishStats() { FishSpotsOk = FishSpotsNarrow = FishSpotsWetFoot = 0; LastWasNarrow = LastWasWetFoot = false; FishMinFarClear = FishMinRingClear = float.PositiveInfinity; }
        public static void NoteFishPick(AnticSpot s)
        {
            if (s == null) { if (LastWasNarrow) FishSpotsNarrow++; if (LastWasWetFoot) FishSpotsWetFoot++; return; }
            FishSpotsOk++; FishMinFarClear = Mathf.Min(FishMinFarClear, s.farClear); FishMinRingClear = Mathf.Min(FishMinRingClear, s.ringClear);
        }

        /// <summary>The chosen cast: water width along the cast line from the edge (a lower bound when openBeyond), the bobber's distance
        /// out from the edge, its clearance to the first dry point beyond it and the radius of open water checked all round it.</summary>
        public float castWidth, castOut, farClear, ringClear; public bool openBeyond;
        public string FishText() =>
            $"water {(openBeyond ? ">" : "")}{castWidth:F1} m wide on the cast line, bobber {castOut:F1} m out from the edge, {(openBeyond ? ">" : "")}{farClear:F1} m clear to the far bank, open water {ringClear:F1} m all round";

        /// <summary>Open water at q = the water surface the player sees: under a drawn water surface (the level's WAT_* meshes) and the
        /// walkable surface there (river bed, bank, quay or a bridge deck) at least FishDepthM under it. Measured on L02: the Zod water
        /// tiles are not the drawn river (a one-tile stream is drawn about 5.5 m wide, drawn water lies up to 14 m from the nearest
        /// water tile, some tiles carry dry bank) and the terrain also dips under the water level in dry pits the water mesh does not
        /// cover (153 of 1811 sample points), so neither the tiles nor the terrain height alone is the surface.</summary>
        public static bool OpenWater(OpenZFxPlayer fxp, Vector3 q) => fxp.GroundAt(q) < fxp.WaterYAt(q) - FishDepthM && WaterMeshAt(fxp, q);

        // the drawn water surface, rasterised once per level into an XZ grid (no colliders: nothing else may hit it)
        const float CovCell = 0.25f;
        static bool[] cov; static int covW, covH, covKey; static float covX0, covZ0; static bool covMesh;
        static float[] covY;                                                              // b27: the height of the drawn surface per cell
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCoverage() { cov = null; covY = null; covKey = 0; covMesh = false; }

        /// <summary>q lies under a drawn water surface (falls back to the Zod water tile when the level has no readable WAT_ mesh).</summary>
        public static bool WaterMeshAt(OpenZFxPlayer fxp, Vector3 q)
        {
            if (covKey != fxp.GetInstanceID()) BuildCoverage(fxp);
            if (!covMesh) return OpenZFxPlayer.IsWaterTile(q);
            int ix = Mathf.FloorToInt((q.x - covX0) / CovCell), iz = Mathf.FloorToInt((q.z - covZ0) / CovCell);
            return ix >= 0 && iz >= 0 && ix < covW && iz < covH && cov[iz * covW + ix];
        }

        /// <summary>b27: the same, with the height of the drawn surface there (the highest water triangle over the middle of q's
        /// quarter-metre cell: every pool, stream and step at its own height). y = NaN when the level has no readable WAT_ mesh and
        /// the water tiles answer: the caller then takes the level's water table or flat height.</summary>
        public static bool WaterMeshAt(OpenZFxPlayer fxp, Vector3 q, out float y)
        {
            y = float.NaN;
            if (covKey != fxp.GetInstanceID()) BuildCoverage(fxp);
            if (!covMesh) return OpenZFxPlayer.IsWaterTile(q);
            int ix = Mathf.FloorToInt((q.x - covX0) / CovCell), iz = Mathf.FloorToInt((q.z - covZ0) / CovCell);
            if (ix < 0 || iz < 0 || ix >= covW || iz >= covH || !cov[iz * covW + ix]) return false;
            y = covY[iz * covW + ix];
            return true;
        }

        /// <summary>b27, counted check: the middle of q's cell of the drawn-water grid (the point that cell was rasterised at); q
        /// itself when the level has no readable WAT_ mesh.</summary>
        public static Vector3 WaterCellMiddle(OpenZFxPlayer fxp, Vector3 q)
        {
            if (covKey != fxp.GetInstanceID()) BuildCoverage(fxp);
            if (!covMesh) return q;
            return new Vector3(covX0 + (Mathf.FloorToInt((q.x - covX0) / CovCell) + 0.5f) * CovCell, q.y, covZ0 + (Mathf.FloorToInt((q.z - covZ0) / CovCell) + 0.5f) * CovCell);
        }

        static void BuildCoverage(OpenZFxPlayer fxp)
        {
            covKey = fxp.GetInstanceID(); covMesh = false; cov = null; covY = null;
            var tris = new List<Vector3>(); bool unreadable = false;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.name.StartsWith("WAT_") || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); var m = mf ? mf.sharedMesh : null;
                if (!m) continue;
                if (!m.isReadable) { unreadable = true; continue; }
                var v = m.vertices; var t = m.triangles; var M = r.transform.localToWorldMatrix;
                for (int i = 0; i + 2 < t.Length; i += 3) { tris.Add(M.MultiplyPoint3x4(v[t[i]])); tris.Add(M.MultiplyPoint3x4(v[t[i + 1]])); tris.Add(M.MultiplyPoint3x4(v[t[i + 2]])); }
            }
            if (tris.Count == 0 || unreadable) { Debug.Log("[OpenZ Antics] fishing water surface: " + (unreadable ? "a water mesh is not readable" : "no water mesh") + ", the water tiles are used"); return; }
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var p in tris) { x0 = Mathf.Min(x0, p.x); z0 = Mathf.Min(z0, p.z); x1 = Mathf.Max(x1, p.x); z1 = Mathf.Max(z1, p.z); }
            covX0 = x0 - 1f; covZ0 = z0 - 1f;
            covW = Mathf.CeilToInt((x1 - x0 + 2f) / CovCell); covH = Mathf.CeilToInt((z1 - z0 + 2f) / CovCell);
            cov = new bool[covW * covH]; covY = new float[covW * covH];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 a = new Vector2(tris[i].x, tris[i].z), b = new Vector2(tris[i + 1].x, tris[i + 1].z), c = new Vector2(tris[i + 2].x, tris[i + 2].z);
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-6f) continue;
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - covX0) / CovCell)), ix1 = Mathf.Min(covW - 1, Mathf.FloorToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - covX0) / CovCell));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, Mathf.Min(b.y, c.y)) - covZ0) / CovCell)), iz1 = Mathf.Min(covH - 1, Mathf.FloorToInt((Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - covZ0) / CovCell));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float px = covX0 + (ix + 0.5f) * CovCell, pz = covZ0 + (iz + 0.5f) * CovCell;
                        float w0 = (b.x - px) * (c.y - pz) - (b.y - pz) * (c.x - px), w1 = (c.x - px) * (a.y - pz) - (c.y - pz) * (a.x - px), w2 = (a.x - px) * (b.y - pz) - (a.y - pz) * (b.x - px);
                        if (area > 0f ? (w0 >= 0f && w1 >= 0f && w2 >= 0f) : (w0 <= 0f && w1 <= 0f && w2 <= 0f))
                        {   // b27: the height of this triangle at the cell's middle (w0, w1, w2 over area = the weights of its three corners); the highest one wins
                            int k = iz * covW + ix; float y = (w0 * tris[i].y + w1 * tris[i + 1].y + w2 * tris[i + 2].y) / area;
                            if (!cov[k] || y > covY[k]) covY[k] = y;
                            cov[k] = true;
                        }
                    }
            }
            covMesh = true;
        }

        /// <summary>Open water on a disc of radius r round c: the centre, 32 rays at 1.05 r, 16 at 0.67 r, 8 at 0.33 r (L02 survey, 2394
        /// bank points: 16 rays at r let a bank bump slip between them, 1.30 m clear; this pattern measured >= 1.55 m everywhere).</summary>
        static bool OpenDisc(OpenZFxPlayer fxp, Vector3 c, float r)
        {
            if (!OpenWater(fxp, c)) return false;
            for (int i = 0; i < 32; i++)
            {
                var d = Quaternion.Euler(0f, i * 11.25f, 0f) * Vector3.forward;
                if (!OpenWater(fxp, c + d * (r * 1.05f))) return false;
                if ((i & 1) == 0 && !OpenWater(fxp, c + d * (r * 0.67f))) return false;
                if ((i & 3) == 0 && !OpenWater(fxp, c + d * (r * 0.33f))) return false;
            }
            return true;
        }

        /// <summary>Never fish from a bridge: the L02 bridges stand on Road-class tiles (not water tiles), so the deck is found by the
        /// walk-layer collider under the robot (ZodObjects/bridge_*/WalkDeck); nor from the water (a ford, a wading robot).</summary>
        static bool OnDeckOrInWater(OpenZFxPlayer fxp, Vector3 p, out string why)
        {
            why = null;
            if (OpenZFxPlayer.IsWaterTile(p)) { why = "standing on a deck or in a ford"; return true; }
            if (Physics.Raycast(new Vector3(p.x, 200f, p.z), Vector3.down, out var hit, 400f, 1 << ZGame.WalkLayer, QueryTriggerInteraction.Ignore))
                for (var t = hit.collider.transform; t; t = t.parent)
                    if (t.name.IndexOf("bridge", System.StringComparison.OrdinalIgnoreCase) >= 0 || t.name.IndexOf("deck", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    { why = "standing on a bridge deck"; return true; }
            if (OpenWater(fxp, p)) { why = "standing in the water"; return true; }
            return false;
        }

        static readonly HumanBodyBones[] FootBones = { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes };

        /// <summary>The drawn water shows at q (deep water or the thin film on the shore): under a water surface, the ground there not
        /// higher than FishFootDryM over the water level.</summary>
        public static bool WaterShows(OpenZFxPlayer fxp, Vector3 q) => fxp.GroundAt(q) < fxp.WaterYAt(q) + FishFootDryM && WaterMeshAt(fxp, q);

        /// <summary>No water shows on a disc of radius r round c: the centre, 16 rays at r, 8 at r / 2.</summary>
        static bool DryDisc(OpenZFxPlayer fxp, Vector3 c, float r)
        {
            if (WaterShows(fxp, c)) return false;
            for (int i = 0; i < 16; i++)
            {
                var d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                if (WaterShows(fxp, c + d * r)) return false;
                if ((i & 1) == 0 && WaterShows(fxp, c + d * (r * 0.5f))) return false;
            }
            return true;
        }

        /// <summary>The footprint radius (world m) of robot v at world scale k: see FishFootM.</summary>
        public static float FootRadius(UnitView v, float k)
        {
            float r = FishFootM * k;
            var an = v ? v.GetComponentInChildren<Animator>() : null;
            if (an && an.isHuman)
            {
                var c = v.transform.position;
                foreach (var b in FootBones) { var t = an.GetBoneTransform(b); if (!t) continue; var d = t.position - c; d.y = 0f; r = Mathf.Max(r, d.magnitude); }
            }
            return r + FishFootMarginM * k;
        }

        /// <summary>How many of robot v's foot and toe bones stand over shown water now (the fishing check: 0).</summary>
        public static int FeetOverWater(UnitView v)
        {
            var fxp = OpenZFxPlayer.I; if (!v || !fxp || float.IsNegativeInfinity(fxp.WaterY)) return 0;
            var an = v.GetComponentInChildren<Animator>(); if (!an || !an.isHuman) return 0;
            int n = 0;
            foreach (var b in FootBones) { var t = an.GetBoneTransform(b); if (t && WaterShows(fxp, t.position)) n++; }
            return n;
        }

        public static AnticSpot Fishing(UnitView v, float reach) => Fishing(v, reach, out _);
        public static AnticSpot Fishing(UnitView v, float reach, out string why) =>
            FishingAt(v ? v.transform.position : Vector3.zero, AnticSockets.WorldScale(v), reach, out why, FootRadius(v, AnticSockets.WorldScale(v)));

        /// <summary>Where a robot standing at p (world scale k) casts: FishBearings bearings; on each, the water's edge within
        /// reach x k, then the open-water run along the line; a cast out of 3.2 / 2.6 / 2.0 m (x k) counts only when the run reaches
        /// FishMarginM past the landing point and the disc of FishMarginM round it is open water. The nearest edge wins (the angler faces
        /// the nearest water), then the longest cast. Never from a bridge deck or the water (OnDeckOrInWater). Null = no fishing here.</summary>
        public static AnticSpot FishingAt(Vector3 p, float k, float reach, out string why, float foot = -1f)
        {
            why = null; LastWasNarrow = LastWasWetFoot = false;
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled || float.IsNegativeInfinity(fxp.WaterY)) { why = "no water on this map"; return null; }
            if (OnDeckOrInWater(fxp, p, out why)) return null;
            if (foot < 0f) foot = (FishFootM + FishFootMarginM) * k;
            if (!DryDisc(fxp, p, foot)) { why = "a foot would stand over the water"; LastWasWetFoot = true; return null; }   // both feet on dry ground
            float r = reach * k, m = FishMarginM, st = FishSampleM;
            AnticSpot best = null; float bestScore = float.MaxValue; bool sawWater = false;
            for (int i = 0; i < FishBearings; i++)
            {
                var dir = Quaternion.Euler(0f, i * 360f / FishBearings, 0f) * Vector3.forward;
                float d0 = -1f;
                for (float d = 0.5f; d <= r; d += 0.25f) if (OpenWater(fxp, p + dir * d)) { d0 = d; break; }
                if (d0 < 0f) continue;
                sawWater = true;
                if (best != null && d0 - 0.05f * CastOut[0] * k >= bestScore) continue;                  // cannot beat the spot already found
                float need = d0 + CastOut[0] * k + m + st, dFar = float.PositiveInfinity;
                for (float d = d0 + st; d <= need; d += st) if (!OpenWater(fxp, p + dir * d)) { dFar = d; break; }   // the far bank, a deck, a sand bar
                foreach (float extra in CastOut)
                {
                    float land = d0 + extra * k;
                    if (dFar - land < m) continue;                                                         // too close to (or past) the far bank
                    var lp = p + dir * land;
                    if (!OpenDisc(fxp, lp, m)) continue;                                                   // a shore, a deck or a sand bar beside it
                    float score = d0 - 0.05f * extra * k;
                    if (score >= bestScore) break;
                    bool open = float.IsPositiveInfinity(dFar);
                    float run = open ? need - d0 : dFar - d0;
                    best = new AnticSpot
                    {
                        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, waterY = fxp.WaterYAt(p),
                        edge = p + dir * (d0 + 0.35f * k), land = lp,
                        castWidth = run, castOut = land - d0, farClear = open ? need - land : dFar - land, ringClear = m, openBeyond = open,
                    };
                    bestScore = score;
                    break;                                                                                 // the longest cast that fits on this bearing
                }
            }
            LastWasNarrow = best == null && sawWater;
            if (best == null) why = sawWater ? $"water too narrow on every cast line (the bobber needs {m:F1} m of open water all round)" : "no open water within reach";
            return best;
        }

        /// <summary>The parasol's lean away from the camera (deg), the clear gap (authored m) between the far edge of the bed and the
        /// pole along the camera's view, the canopy centre height (authored m, ANT_Parasol: 2.93 m tall, canopy 2.76 m across) and the
        /// largest sideways shift (authored m) that puts the canopy's shadow on the beds.</summary>
        public static float ParasolTilt = 28f, ParasolGap = 1.75f, ParasolCanopyY = 2.6f, ParasolShadeShift = 1.2f;

        /// <summary>The view direction of the game camera on the ground plane (screen-up on the map); +Z when there is none.</summary>
        /// <summary>DV108 (View/OzHeroSquadView.cs): while set, the lounger layout uses this ground direction instead of the camera's
        /// (the level-start loungers are laid out for the standard game view while the briefing camera is still on).</summary>
        public static Vector3? GroundForwardOverride;
        public static Vector3 CameraGroundForward()
        {
            if (GroundForwardOverride.HasValue) return GroundForwardOverride.Value;
            var cam = Camera.main;
            if (cam) { var f = cam.transform.forward; f.y = 0f; if (f.sqrMagnitude > 0.01f) return f.normalized; }
            return Vector3.forward;
        }

        /// <summary>A free flat spot behind the robot for the lounger (BedBack m back) and one for the parasol, laid out for the RTS
        /// camera: the robot faces the camera (it lies with its head away from it, toward the top of the screen), then +-30, +-60, +-90
        /// deg; the parasol stands past the far (screen-top) edge of the bed by ParasolGap, beside the head end, leaning ParasolTilt deg
        /// away from the camera, so from the game camera (pitch 55-70) the canopy sits above the heads on screen and never covers the
        /// beds or the robots on them; shifted sideways (at most ParasolShadeShift) so the canopy's shadow falls on the bed's line.</summary>
        public static AnticSpot Sunbed(UnitView v)
        {
            var g = ZGame.I; if (!g) return null;
            var p = v.transform.position; float k = AnticSockets.WorldScale(v), wf = AnticSockets.BedWidthFor(v);
            var camF = CameraGroundForward(); var camR = Vector3.Cross(Vector3.up, camF);
            float camYaw = Mathf.Atan2(-camF.x, -camF.z) * Mathf.Rad2Deg;                                  // facing the camera
            float ta = ParasolTilt * Mathf.Deg2Rad;
            var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up * Mathf.Cos(ta) + camF * Mathf.Sin(ta)).normalized);
            float hc = ParasolCanopyY * k * Mathf.Cos(ta), lean = ParasolCanopyY * k * Mathf.Sin(ta);
            float shade = 0f;                                                                                    // canopy shadow onto the bed line
            var sun = RenderSettings.sun;
            if (sun && sun.isActiveAndEnabled && sun.transform.forward.y < -0.2f)
            {
                var L = sun.transform.forward; var off = new Vector3(L.x, 0f, L.z) * (hc / -L.y);
                shade = Mathf.Clamp(-Vector3.Dot(off, camR), -ParasolShadeShift * k, ParasolShadeShift * k);
            }
            foreach (float dy in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f })
            {
                float yaw = camYaw + dy;
                var front = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; var right = Vector3.Cross(Vector3.up, front);
                var c = p - front * (AnticSockets.BedBack * k);
                if (!Flat(g, c, front, right, 0.55f * k * wf, 1.3f * k, 0.12f * k, out float gy)) continue;
                var head = c - front * (1.2f * k);                                                               // the bed's head end
                float far = Vector3.Dot(c, camF) + 1.2f * k * Mathf.Abs(Vector3.Dot(front, camF)) + 0.55f * k * wf * Mathf.Abs(Vector3.Dot(right, camF));
                var baseP = head + camF * (far + ParasolGap * k - Vector3.Dot(head, camF));
                foreach (float s in new[] { shade, 0f, 0.9f * k, -0.9f * k })
                {
                    var pc = baseP + camR * s;
                    if (!Flat(g, pc, camF, camR, 0.35f * k, 0.35f * k, 0.25f * k, out float py)) continue;
                    var under = pc + camF * lean;                                                                 // the ground under the canopy
                    if (!Flat(g, under, camF, camR, 0.9f * k, 0.9f * k, 1.5f * k, out float _)) continue;
                    if (Crowded(g, v, c, 1.9f * k) || Crowded(g, v, pc, 0.9f * k)) continue;
                    var bedRot = Quaternion.LookRotation(front, Vector3.up);
                    return new AnticSpot { yaw = yaw, bed = new Vector3(c.x, gy, c.z), parasol = new Vector3(pc.x, py, pc.z), bedRot = bedRot, parasolRot = tilt * bedRot, bedWidth = wf };
                }
            }
            return null;
        }

        static bool Flat(ZGame g, Vector3 c, Vector3 f, Vector3 r, float hw, float hl, float tol, out float y)
        {
            y = g.GroundY(c.x, c.z); float lo = y, hi = y;
            for (int i = 0; i < 9; i++)
            {
                var q = c + r * hw * ((i % 3) - 1) + f * hl * ((i / 3) - 1);
                if (OpenZFxPlayer.IsWaterTile(q)) return false;
                var cl = OpenZFxPlayer.ClassAt(q); if (cl == ZClass.Impassable) return false;
                float h = g.GroundY(q.x, q.z); lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                if (Physics.Raycast(new Vector3(q.x, h + 6f, q.z), Vector3.down, out var hit, 5.85f, ~OzRockSolid.Mask, QueryTriggerInteraction.Ignore) && hit.point.y > h + 0.15f
                    && !hit.collider.GetComponentInParent<UnitView>()) return false;                    // something stands there (hut, wall, prop)
            }
            y = Mathf.Min(y, lo + 0.02f);
            return hi - lo <= tol;
        }

        static bool Crowded(ZGame g, UnitView self, Vector3 c, float r)
        {
            foreach (var u in g.Views.Values)
            {
                if (!u || u == self || !u.isActiveAndEnabled) continue;
                var d = u.transform.position - c; d.y = 0f;
                if (d.sqrMagnitude < r * r) return true;
            }
            return false;
        }
    }

    /// <summary>Robot antics: the sockets, the stowed weapon and the scale rule.</summary>
    public static class AnticSockets
    {
        /// <summary>
        /// Measured in Play on PropSocket_LeftHand: the fist's thumb
        /// axis (up while the can is held at the chest, the can top tipped into the mouth while drinking) is the socket's +Z, not +Y as
        /// the clips README assumed, and the head lies on the socket's -X side while the robot looks at the phone. Both props come in
        /// with their top on +Y (IC_FuelCan; ANT_Phone also with its screen on +Z), so both lay sideways across the fist (from the game
        /// camera only a thin sliver under the helmet). Now: the phone's top on +Z, its screen to -X (toward the eyes and the ear);
        /// the can's top on +Z (upright in the fist, tipped into the mouth on the drink).</summary>
        public static Quaternion PhoneRot = Quaternion.LookRotation(Vector3.left, Vector3.forward);
        public static Quaternion CanRot = Quaternion.Euler(90f, 0f, 0f);
        /// <summary>The can: the game's rocket-fuel can (IC_FuelCan, 0.32 m: Alan's and Brad's can is the same model, 13.6 % of Alan's
        /// height) x 1.2 so it reads at the RTS camera (was 0.85: a 27 cm can lying across the fist, hidden under the helmet). The phone
        /// x 1.15 for the same reason.</summary>
        public static float CanScale = 1.2f, PhoneScale = 1.15f, LineWidth = 0.035f, CastFlight = 0.9f, ReelSpeed = 1.6f;
        /// <summary>The modelled smartphone is built at its in-game size (about 3x a real phone for the robot hand: 0.215 x 0.44 m), so
        /// it sits on the socket at this scale.</summary>
        public static float PhoneV2Scale = 1f;
        /// <summary>The wide types (tough, pyro, electrobot, ...) hide a grunt-size phone behind their round shoulders: the phone grows
        /// with the shoulder span like the lounger, x (1 + PhoneBodyGain (span / BedRefSpan - 1)), clamped to 1..PhoneBodyMax.</summary>
        public static float PhoneBodyGain = 0.6f, PhoneBodyMax = 1.5f;
        public static float PhoneBodyFor(UnitView v)
        {
            var an = v ? v.GetComponentInChildren<Animator>() : null; if (!an || !an.isHuman) return 1f;
            var l = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = an.GetBoneTransform(HumanBodyBones.RightUpperArm); if (!l || !r) return 1f;
            float span = Vector3.Distance(l.position, r.position) / Mathf.Max(0.01f, WorldScale(v));
            return Mathf.Clamp(1f + PhoneBodyGain * (span / BedRefSpan - 1f), 1f, PhoneBodyMax);
        }
        /// <summary>The caught toy fish x 1.5 so the catch reads at the RTS camera.</summary>
        public static float FishScale = 1.5f;
        /// <summary>The lounger is built for the grunt (shoulder span 0.62 m between the upper-arm bones, measured in Play); the wider
        /// types lie on a wider bed: width x (1 + BedWidthGain (span / BedRefSpan - 1)), clamped to 1..BedWidthMax (measured spans: sniper 0.78,
        /// pyro 0.99, electrobot 1.11, laser 1.13, tough 1.24 m: their arms and round shoulders hung far over the 1.02 m frame).</summary>
        public static float BedRefSpan = 0.62f, BedWidthGain = 0.9f, BedWidthMax = 1.9f;
        public static float BedWidthFor(UnitView v)
        {
            var an = v ? v.GetComponentInChildren<Animator>() : null; if (!an || !an.isHuman) return 1f;
            var l = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var r = an.GetBoneTransform(HumanBodyBones.RightUpperArm); if (!l || !r) return 1f;
            float span = Vector3.Distance(l.position, r.position) / Mathf.Max(0.01f, WorldScale(v));
            return Mathf.Clamp(1f + BedWidthGain * (span / BedRefSpan - 1f), 1f, BedWidthMax);
        }
        /// <summary>The empty can's throw whoosh (the grenade throw swish, softer).</summary>
        public static float TossWhooshGain = 0.6f;
        /// <summary>The fishing rod's tip may point this far (deg, on the ground plane) off the angler's facing before it is turned out.</summary>
        public static float RodFrontTolerance = 30f;
        /// <summary>
        /// Measured in Play: on the weapon convention (identity, the rod along the rifle's barrel axis, socket -Z) the rod pointed 62 deg
        /// DOWN from the fist through the whole hold (tip 0.8 m under the ground, into the water). The clips hold the rod along the
        /// socket's +Y (the same axis swap as the can): +90 deg about X puts the tip forward and 30 deg up in the hold (tip 2.0 m up,
        /// 2.3 m out, as the clips preview), over the shoulder in the wind-up and level at the release; the reel hangs under it.</summary>
        public static Quaternion RodRot = Quaternion.Euler(90f, 0f, 0f);
        /// <summary>Lounger centre behind the robot (authored m). The clip lies FLAT on a 0.40 m seat (contract: a 2.6 m box centred 1.0 m back);
        /// the delivered loungers raise a 38 deg backrest from the hinge (SEAT_HIPS + 0.05 m) to the head end, which would run through the
        /// lying torso at 1.0 m. At 1.8 m the hinge sits just past the lying robot's head top (2.16 m back): the backrest stands behind the
        /// head like a headboard, the hips / back lie on the flat bed, the raised boots stay off its near end (0.6 m back).</summary>
        public static float BedBack = 1.8f;
        /// <summary>The lounger build whose SEAT_HIPS height is nearest this (the clip's seat top) is used: the sling build (0.38 m), not
        /// the 0.50 m cushion one, so the lying body neither floats nor sinks.</summary>
        public static float SeatTop = 0.40f;

        static readonly Dictionary<int, float> scale = new Dictionary<int, float>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => scale.Clear();

        public static Transform RightWeapon(UnitView v) => Named(v, "RightHandWeapon");

        /// <summary>World metres per authored metre at this robot: the weapon socket's world scale (weapons and props share the same
        /// import rule and sit on the socket with a unit scale).</summary>
        public static float WorldScale(UnitView v)
        {
            if (!v) return 1f;
            int key = v.GetInstanceID();
            if (scale.TryGetValue(key, out var s)) return s;
            var rw = RightWeapon(v);
            s = rw ? (Mathf.Abs(rw.lossyScale.x) + Mathf.Abs(rw.lossyScale.y) + Mathf.Abs(rw.lossyScale.z)) / 3f : Mathf.Abs(v.transform.lossyScale.y);
            if (s < 0.05f || s > 20f) s = 1f;
            scale[key] = s; return s;
        }

        /// <summary>PropSocket_LeftHand under LeftHandGrip (made once from the kit's pose).</summary>
        public static Transform LeftProp(UnitView v, AnticKit kit)
        {
            var ps = Named(v, "PropSocket_LeftHand"); if (ps) return ps;
            var lg = Named(v, "LeftHandGrip"); if (!lg || !kit || !kit.socketOk) return null;
            var go = new GameObject("PropSocket_LeftHand") { layer = lg.gameObject.layer };
            go.transform.SetParent(lg, false);
            go.transform.localPosition = kit.socketPos; go.transform.localRotation = kit.socketRot; go.transform.localScale = kit.socketScale;
            return go.transform;
        }

        /// <summary>The robot's held weapon root (the model under RightHandWeapon, or the named rifle / bazooka).</summary>
        public static Transform Weapon(UnitView v)
        {
            var rw = RightWeapon(v);
            if (rw) for (int i = 0; i < rw.childCount; i++) { var c = rw.GetChild(i); if (!c.name.StartsWith("Antic")) return c; }
            foreach (var t in v.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("BS01_Weapon_") || t.name == "Rifle_Heavy" || t.name == "Bazooka") return t;
            return null;
        }

        static Transform Named(UnitView v, string n)
        {
            if (!v) return null;
            foreach (var t in v.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }
    }
}
