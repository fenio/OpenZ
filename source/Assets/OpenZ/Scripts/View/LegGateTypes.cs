using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OpenZ
{
    /// <summary>
    /// legs_gate_v2 (work item H2): the legs gate for EVERY robot type. LegJitterGate only walks the player squads that happen to
    /// be on screen (on L02 / L17 that is grunts), so the v3 types (psycho, tough, sniper, pyro, laser, electrobot) were never
    /// gated. This harness finds open vehicle ground near the camera (no water: wading lowers the root) with no unit or building
    /// within 12 (then 8) tiles, no empty vehicle / gun, foreign flag or grenade crate the squads could auto-grab, and no enemy
    /// unit whose attack + agro radius reaches the lanes. It spawns one player squad per type in parallel lanes 5 tiles (80 px)
    /// apart (Lanes; the widest V, pyro / laser: 4 robots x BodyScale 1.15, spans 45 px), orders every leader to stand where it
    /// spawned (a player order: PlayerHold, DV38, so no idle auto-grab), lets them stand SettleSeconds (RobotLegIK captures each
    /// avatar's flat idle boots), runs LegJitterGate.Begin(8, drive false) and walks every leader 5 tiles along its lane with the
    /// SAME 1-tile side step and back (idle -> walk -> turn -> stop, like the gate's own drive): the lanes stay parallel and
    /// never meet. The camera is held at CameraHeight over the lanes (every lane in view is checked per frame). The gate's
    /// thresholds are untouched.
    /// gate table is "ok", at least one has >= LegJitterGate.MinGaitFrames steady gait frames, the whole squad was in the camera
    /// view on >= MinInView of the gate frames, and its walking sound played: squads of 3+ = their own rob_squad_march loop
    /// (AudioDirector loop "march:[leader id]"; loops do not write OzSoundLog), squads of 1-2 = their OWN single steps: a new PLAY of
    /// mech_tough_step (tough) / rob_step_* (others) with one of the squad's robot ids as the emitter (AudioDirector.lastEmit, read by
    /// reflection; the OzSoundLog "AD ... PLAY" lines have no emitter and are printed level-wide for reference only). Per batch it
    /// also prints RobotLegIK's stride probe (ProbeFoot / ProbeRoot /
    /// ProbeN), the reach ratio per foot and, per type, the drawn boots' slide over the ground, lock share, leg lengths and hips
    /// (the right boot is never planted: README section 7).
    /// BeginFaces (RP1): one grunt, day and night, front + three-quarter face close-ups at 2560x1600 into
    /// BeginEyes (README 5.4): one grunt squad, RTS camera at 22 m following its leader, one 2560x1600 frame at mid-stride and one
    /// standing: legs_walk_midstride_[HHmm].png / legs_stand_[HHmm].png in the same folder.
    /// Test harness only (like RobotTypesShow / LegJitterGate): spawns, orders, CPU bots paused while it runs; units removed without
    /// wrecks afterwards. Start: menu OpenZ/Checks/Legs gate: all robot types (Play), or LegGateTypes.Begin() / BeginFaces() / BeginEyes().
    /// </summary>
    public sealed class LegGateTypes : MonoBehaviour
    {
        public static string[][] Batches =
        {
            new[] { "grunt", "psycho", "tough", "sniper" },
            new[] { "pyro", "laser", "electrobot" },
        };
        /// <summary>Lane rows (tiles from the field centre), 5 tiles apart; spawn column SpawnDx; every leader walks WalkPx forward
        /// and SidePx to the side (the same for every lane, so the lanes stay parallel) and back.</summary>
        public static int[] Lanes = { -8, -3, 2, 7 };
        public static int SpawnDx = -4, WalkPx = 80, SidePx = 16;
        /// <summary>CameraHeight 60 m (pitch 55, FOV 32): the lanes span 20 tiles (40 m) across; with the focus FocusNearPx (1.5 tiles)
        /// to the near side they sit inside the 0.1-0.9 viewport band (near 17 of 17.6 m, far 23 of 24.4 m). Single steps play up
        /// to AudioDirector.footstepMaxHeight (95 m).</summary>
        public static float GateSeconds = 8f, SettleSeconds = 2.5f, CameraHeight = 60f, FocusNearPx = 24f, MinInView = 0.8f;
        public static string Status = "idle", Result = "";
        static LegGateTypes running;
        const int FieldX0 = -7, FieldX1 = 4, FieldY0 = -10, FieldY1 = 10;
        /// <summary>Largest distance of a lane robot from the field centre (px): the corner of the lane field + 0.5 tile.</summary>
        const int HalfExtentPx = 13 * 16;

        /// <summary>
        /// last PLAY (UpdateRobots plays every single step with the robot's own o.Id). OzSoundLog entries carry no emitter.</summary>
        static readonly FieldInfo LastEmitField = typeof(AudioDirector).GetField("lastEmit", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly string[] ToughStepKeys = { "mech_tough_step" };
        static readonly string[] RobStepKeys = { "rob_step_sand", "rob_step_concrete", "rob_step_metal", "rob_step_wood", "rob_step_water" };
        readonly Dictionary<(string, int), float> emitSeen = new Dictionary<(string, int), float>();
        readonly Dictionary<string, int> ownSteps = new Dictionary<string, int>();
        float emitT0; bool emitReadable;

        static readonly string[] SoundKeys = { "rob_squad_march", "rob_step_sand", "rob_step_concrete", "rob_step_metal", "rob_step_wood", "rob_step_water", "mech_tough_step", "rob_servo", "rob_run_start" };

        int mode;   // 0 = all-types gate, 1 = RP1 face close-ups, 2 = 22 m eye frames (walk mid-stride + stand)
        ZGame g; ZWorld W;
        readonly List<ZObject> stage = new List<ZObject>();
        readonly StringBuilder sum = new StringBuilder();
        string fieldNote = "";

        public static LegGateTypes Begin() => Launch(0);
        /// <summary>RP1: one grunt on open ground, day then night, front + three-quarter face close-ups (2560x1600).</summary>
        public static LegGateTypes BeginFaces() => Launch(1);
        /// <summary>
        /// 2560x1600 frame at mid-stride (the leader's walk phase at the middle of a boot's swing window) and one of the squad standing
        /// </summary>
        public static LegGateTypes BeginEyes() => Launch(2);

        static LegGateTypes Launch(int mode)
        {
            if (!ZGame.I || ZGame.I.World == null) { Status = "no running ZGame (enter Play on L02 or L17)"; Debug.LogWarning("[LegGateTypes] " + Status); return null; }
            if (running) { Debug.LogWarning("[LegGateTypes] already running"); return running; }
            if (RobotTypesShow.Running) { Status = "RobotTypesShow is running: wait for it"; Debug.LogWarning("[LegGateTypes] " + Status); return null; }
            Status = "running"; Result = "";
            var s = ZGame.I.gameObject.AddComponent<LegGateTypes>();
            s.mode = mode;
            return s;
        }

        void Start() { running = this; StartCoroutine(mode == 1 ? RunFaces() : mode == 2 ? RunEyes() : Run()); }
        void OnDestroy() { if (running == this) running = null; }

        static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));

        // ------------------------------------------------------------------------------------------------ field
        struct Near { public int x, y, r; public bool flag; }
        List<(int tx, int ty)> owned; List<Near> grab, enemy;

        /// <summary>Everything that can pull a staged squad off its lane, collected once per field search.</summary>
        void CollectHazards()
        {
            owned = new List<(int, int)>(); grab = new List<Near>(); enemy = new List<Near>();
            int ours = 0;
            foreach (var t in Batches.SelectMany(b => b)) ours = Mathf.Max(ours, W.S.Unit(t)?.attack_radius ?? 0);
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.KillAt >= 0) continue;
                if (o.Owner != Team.Neutral) owned.Add(((o.LocX + o.WidthPix / 2) / 16, (o.LocY + o.HeightPix / 2) / 16));
                if (o.IsUnit && o.CanBeEntered()) grab.Add(new Near { x = o.CenterX, y = o.CenterY, r = W.S.auto_grab_vehicle_distance });   // ZObject.cs auto-enter
                else if (o.Type == "grenades") grab.Add(new Near { x = o.CenterX, y = o.CenterY, r = W.S.auto_grab_vehicle_distance });      // grenade crates
                if (o.IsUnit && o.Owner != Team.Neutral && o.Owner != g.player)
                    enemy.Add(new Near { x = o.CenterX, y = o.CenterY, r = Mathf.Max(o.AttackRadius, ours) + W.S.agro_distance });            // WithinAgroRadius, either way
            }
            foreach (var f in W.Flags)
                if (f != null && !f.Destroyed && f.Owner != g.player) grab.Add(new Near { x = f.CenterX, y = f.CenterY, r = W.S.auto_grab_flag_distance, flag = true });
        }

        /// <summary>The lanes need tiles x cx-7..cx+4, y cy-10..cy+10: every tile robot ground and no water (wading lowers the root),
        /// at least 90 % vehicle ground (open ground, no rock / cliff field), no red / blue unit or building within `clear` tiles, no
        /// grab target (empty vehicle / gun, foreign flag, grenade crate) within its auto-grab distance (`held`: only the very close
        /// ZWorld.CloseGrabPx a held unit still takes) of any lane robot, no enemy unit whose attack + agro radius reaches a lane.</summary>
        bool FieldOk(int cx, int cy, int clear, bool held)
        {
            int px = cx * 16 + 8, py = cy * 16 + 8;
            foreach (var (tx, ty) in owned) if (Mathf.Abs(tx - cx) <= clear && Mathf.Abs(ty - cy) <= clear) return false;
            foreach (var n in grab)
            {
                int r = held ? (ZWorld.CloseGrabDeviation ? Mathf.Min(n.r, n.flag ? ZWorld.CloseGrabFlagPx : ZWorld.CloseGrabPx) : 0) : n.r;
                if (Dist(n.x, n.y, px, py) <= r + HalfExtentPx) return false;
            }
            foreach (var n in enemy) if (Dist(n.x, n.y, px, py) <= n.r + HalfExtentPx) return false;
            int all = 0, veh = 0;
            for (int x = cx + FieldX0; x <= cx + FieldX1; x++)
                for (int y = cy + FieldY0; y <= cy + FieldY1; y++)
                {
                    if (!W.Path.TilePassable(x, y, true)) return false;
                    if (W.Map != null) { var c = W.Map.ClassAt(x, y); if (c == ZClass.Water || c == ZClass.DeepWater) return false; }
                    all++; if (W.Path.TilePassable(x, y, false)) veh++;
                }
            return veh >= all * 0.9f;
        }

        static float Dist(int ax, int ay, int bx, int by) => Mathf.Sqrt((float)(ax - bx) * (ax - bx) + (float)(ay - by) * (ay - by));

        /// <summary>
        /// and owned / grab / enemy hazards cover the rest, so the all-types gate, the 22 m eye frames and the face close-ups never ran).
        /// The rule is now per LANE: the tiles any lane robot can stand on (leader path WalkPx / SidePx + the widest V, VHalfPx, + body
        /// BodyPx) must be robot-walkable (no building / prop / rock: TilePassable) and dry (no water or deep water: no wading); no red /
        /// blue unit within LaneClear tiles of that band; no grab target whose grab distance and no enemy whose attack + agro radius
        /// reaches the band. Tiles OFF the lanes may be anything (rock, buildings, water). Each lane may shift up to LaneShiftMax tiles
        /// along the walk (still parallel: the rows and the walk are the same for every lane; the camera's width holds them). The run is
        /// test mode with the CPU bots paused, as before; the gate limits are untouched.</summary>
        public static int LaneShiftMax = 5, LaneClear = 2;
        const int VHalfPx = 23, BodyPx = 6;
        readonly int[] laneDx = new int[8]; int firstLane;

        static void LaneRect(int tx, int ty, out int x0, out int x1, out int y0, out int y1)
        {
            x0 = tx * 16 + 8 - VHalfPx - BodyPx; x1 = tx * 16 + 8 + WalkPx + VHalfPx + BodyPx;
            y0 = ty * 16 + 8 - VHalfPx - BodyPx; y1 = ty * 16 + 8 + SidePx + VHalfPx + BodyPx;
        }

        static float RectDist(int x, int y, int x0, int x1, int y0, int y1)
        {
            float dx = Mathf.Max(0, Mathf.Max(x0 - x, x - x1)), dy = Mathf.Max(0, Mathf.Max(y0 - y, y - y1));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        bool LaneOk(int tx, int ty, int clear, bool held)
        {
            LaneRect(tx, ty, out int x0, out int x1, out int y0, out int y1);
            int t0 = Mathf.FloorToInt(x0 / 16f), t1 = Mathf.FloorToInt(x1 / 16f), u0 = Mathf.FloorToInt(y0 / 16f), u1 = Mathf.FloorToInt(y1 / 16f);
            for (int x = t0; x <= t1; x++)
                for (int y = u0; y <= u1; y++)
                {
                    if (!W.Path.TilePassable(x, y, true)) return false;                                  // lane tiles walkable (no building / prop / rock)
                    if (W.Map != null) { var c = W.Map.ClassAt(x, y); if (c == ZClass.Water || c == ZClass.DeepWater) return false; }   // and dry
                }
            foreach (var (otx, oty) in owned) if (otx >= t0 - clear && otx <= t1 + clear && oty >= u0 - clear && oty <= u1 + clear) return false;
            foreach (var n in grab)
            {
                int r = held ? (ZWorld.CloseGrabDeviation ? Mathf.Min(n.r, n.flag ? ZWorld.CloseGrabFlagPx : ZWorld.CloseGrabPx) : 0) : n.r;
                if (RectDist(n.x, n.y, x0, x1, y0, y1) <= r) return false;
            }
            foreach (var n in enemy) if (RectDist(n.x, n.y, x0, x1, y0, y1) <= n.r) return false;
            return true;
        }

        /// <summary>Lanes at field centre (cx, cy): every lane (need = Lanes.Length) or the first one (need = 1) gets a column shift
        /// (laneDx, nearest 0 first) whose band passes LaneOk.</summary>
        bool LanesOk(int cx, int cy, int clear, bool held, int need)
        {
            int got = 0; firstLane = -1;
            for (int k = 0; k < Lanes.Length && k < laneDx.Length; k++)
            {
                bool ok = false;
                for (int m = 0; m <= 2 * LaneShiftMax && !ok; m++)
                {
                    int dx = (m & 1) == 1 ? (m + 1) / 2 : -(m / 2);
                    if (LaneOk(cx + SpawnDx + dx, cy + Lanes[k], clear, held)) { laneDx[k] = dx; ok = true; }
                }
                if (ok) { got++; if (firstLane < 0) firstLane = k; if (got >= need) return true; }
                else if (need >= Lanes.Length) return false;
            }
            return got >= need;
        }

        (int, int, bool) Field(int fx, int fy) => Field(fx, fy, Lanes.Length);

        (int, int, bool) Field(int fx, int fy, int need)
        {
            CollectHazards();
            // strict first: no grab target within its full auto-grab distance; then the held radius (the squads are held by then)
            var tiers = ZWorld.GrabPolicyDeviation ? new[] { false, true } : new[] { false };
            foreach (bool held in tiers)
                foreach (int clear in new[] { 4, LaneClear })
                    for (int r = 0; r <= 45; r++)
                        for (int dx = -r; dx <= r; dx++)
                            for (int dy = -r; dy <= r; dy++)
                            {
                                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                                if (!LanesOk(fx + dx, fy + dy, clear, held, need)) continue;
                                int gr = held ? (ZWorld.CloseGrabDeviation ? Mathf.Max(ZWorld.CloseGrabPx, ZWorld.CloseGrabFlagPx) : 0) : Mathf.Max(W.S.auto_grab_vehicle_distance, W.S.auto_grab_flag_distance);
                                fieldNote = $"lane tiles walkable and dry (off-lane tiles may be blocked), lane column shifts {string.Join(" ", laneDx.Take(Lanes.Length))}, no red / blue unit within {clear} tiles of a lane, no empty vehicle / gun, foreign flag or grenade crate within {(gr + HalfExtentPx) / 16f:0.#} tiles " +
                                            $"({(held ? "held-unit close grab " : "full auto-grab ")}{gr} px + lane extent {HalfExtentPx} px), no enemy unit whose attack + agro radius reaches a lane";
                                return (fx + dx, fy + dy, true);
                            }
            return (fx, fy, false);
        }

        void ClearStage()
        {
            foreach (var o in stage)
            {
                if (o == null) continue;
                bool alive = W.Get(o.Id) != null && !o.Destroyed;
                if (alive) o.KillAt = W.Time;
                if (alive && g.Views.TryGetValue(o.Id, out var v) && v) v.Hide();
            }
            stage.Clear();
        }

        static Dictionary<string, int> Snap()
        {
            var ad = AudioDirector.I; var d = new Dictionary<string, int>();
            if (ad) foreach (var k in SoundKeys) d[k] = ad.Plays.TryGetValue(k, out var n) ? n : 0;
            return d;
        }

        // ------------------------------------------------------------------------------------------------ per-frame watch
        sealed class Rec
        {
            public ZObject o; public string type; public UnitView v; public Animator a; public RobotLegIK ik;
            public readonly Transform[] foot = new Transform[2]; public readonly Vector3[] last = new Vector3[2]; public bool lastOk; public float prevDt;
            public readonly List<float>[] h = { new List<float>(), new List<float>() }, spd = { new List<float>(), new List<float>() };
            public readonly int[] lockN = new int[2]; public int walkN;
        }
        readonly List<Rec> recs = new List<Rec>();
        readonly Dictionary<string, int> typeFrames = new Dictionary<string, int>(), typeInView = new Dictionary<string, int>(), marchFrames = new Dictionary<string, int>();

        void Sample(List<(ZObject o, int hx, int hy, string type, int squad)> leaders)
        {
            var cam = Camera.main; var ad = AudioDirector.I;
            var allIn = new Dictionary<string, bool>();
            foreach (var r in recs)
            {
                if (r.o == null || r.o.Destroyed || !r.v || !r.v.isActiveAndEnabled) { allIn[r.type] = false; continue; }
                bool vis = true;
                if (cam) { var vp = cam.WorldToViewportPoint(r.v.transform.position); vis = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f; }
                allIn[r.type] = (allIn.TryGetValue(r.type, out var b) ? b : true) && vis;
                if (!r.a || !r.foot[0] || !r.foot[1]) continue;
                // coroutines resume before the Animator: the bones hold the pose drawn last frame, which took last frame's dt
                bool walk = r.a.GetCurrentAnimatorStateInfo(0).IsName("walking") && !r.a.IsInTransition(0);
                for (int i = 0; i < 2; i++)
                {
                    var p = r.foot[i].position;
                    if (walk && r.lastOk && r.prevDt > 0f && r.prevDt < 0.05f)
                    {
                        var d = p - r.last[i]; d.y = 0f;
                        r.h[i].Add(p.y - g.GroundY(p.x, p.z)); r.spd[i].Add(d.magnitude / r.prevDt);
                        if (r.ik && r.ik.LockWeight(i) > 0.5f) r.lockN[i]++;
                    }
                    r.last[i] = p;
                }
                if (walk) r.walkN++;
                r.lastOk = true; r.prevDt = Time.deltaTime;
            }
            foreach (var kv in allIn) { typeFrames[kv.Key] = (typeFrames.TryGetValue(kv.Key, out var n) ? n : 0) + 1; if (kv.Value) typeInView[kv.Key] = (typeInView.TryGetValue(kv.Key, out var m) ? m : 0) + 1; }
            if (ad) foreach (var l in leaders) if (l.squad >= 3 && !l.o.Destroyed && ad.LoopActive("march:" + l.o.Id)) marchFrames[l.type] = (marchFrames.TryGetValue(l.type, out var k) ? k : 0) + 1;
            // squads of 1-2: count each new PLAY of a step sound whose emitter is one of THIS squad's robots
            var le = ad && LastEmitField != null ? LastEmitField.GetValue(ad) as Dictionary<(string, int), float> : null;
            if (le == null) return;
            emitReadable = true;
            foreach (var l in leaders)
            {
                if (l.squad >= 3) continue;
                var keys = l.type == "tough" ? ToughStepKeys : RobStepKeys;
                foreach (var u in new[] { l.o }.Concat(l.o.Minions))
                    foreach (var key in keys)
                        if (le.TryGetValue((key, u.Id), out var t) && t >= emitT0 && (!emitSeen.TryGetValue((key, u.Id), out var pt) || pt != t))
                        { emitSeen[(key, u.Id)] = t; ownSteps[l.type] = (ownSteps.TryGetValue(l.type, out var c) ? c : 0) + 1; }
            }
        }

        /// <summary>Drawn boot slide per foot over the ground: walking frames whose ankle is within 3 cm of its lowest over the batch
        /// count as "on the ground"; mean horizontal speed of those frames (a planted boot = 0).</summary>
        static (float slide, float share, int n) Slide(IEnumerable<Rec> rs, int i)
        {
            float s = 0f; int n = 0, all = 0;
            foreach (var r in rs)
            {
                if (r.h[i].Count == 0) continue;
                float lo = r.h[i].Min(); all += r.h[i].Count;
                for (int k = 0; k < r.h[i].Count; k++) if (r.h[i][k] < lo + 0.03f) { s += r.spd[i][k]; n++; }
            }
            return (n > 0 ? s / n : 0f, all > 0 ? (float)n / all : 0f, n);
        }

        static readonly FieldInfo LegLenField = typeof(RobotLegIK).GetField("legLen", BindingFlags.NonPublic | BindingFlags.Instance);

        // ------------------------------------------------------------------------------------------------ the gate
        IEnumerator Run()
        {
            g = ZGame.I; W = g.World;
            var rts = FindFirstObjectByType<RtsCamera>();
            var focus = rts ? rts.focus : Vector3.zero;
            int fx = Mathf.RoundToInt(focus.x * 8f / 16f), fy = Mathf.RoundToInt(-focus.z * 8f / 16f);
            var (cx, cy, found) = Field(fx, fy);
            string level = g.level;
            sum.AppendLine($"# Legs gate, all robot types (Play) {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}, level {level}");
            sum.AppendLine($"RobotLegIK On {RobotLegIK.On}, KneeHints {RobotLegIK.KneeHints}, StanceLock {RobotLegIK.StanceLock}, EaseBlends {RobotLegIK.EaseBlends}, StopHold {RobotLegIK.StopHold}, " +
                           $"ProcSwing {RobotLegIK.ProcSwing} (lift {RobotLegIK.SwingLift:0.00} m, skip [{string.Join(", ", RobotLegIK.ProcSwingSkip)}]), GroundFlat {RobotLegIK.GroundFlat}, RetimeFades {RobotLegIK.RetimeFades}, GaitFadeStep {RobotLegIK.GaitFadeStep}, UnitView.IdleFade {UnitView.IdleFade:0.00} s x anim speed, Time.timeScale {Time.timeScale}");
            sum.AppendLine($"Field: tile ({cx},{cy}) {(found ? "lanes on walkable, dry tiles; " + fieldNote : "NOT FOUND near the camera: move the camera over open ground (no parked vehicles, flags or crates within 18 tiles) and re-run")}");
            sum.AppendLine($"Lanes: rows {string.Join(", ", Lanes)} tiles from the centre, spawn column {SpawnDx}, walk +{WalkPx} px / side +{SidePx} px for every lane, camera {CameraHeight:0} m; thresholds = LegJitterGate (unchanged)\n");
            if (!found) { Finish(level, false, new List<string> { "no open field near the camera" }); yield break; }
            var bots = W.Bots.ToList(); W.Bots.Clear();                                   // CPU bots paused while the harness runs (restored below)
            if (rts) { rts.FlyTo(ZGame.PxToWorld(cx * 16 + 8, cy * 16 + 8 + FocusNearPx)); rts.ZoomTo(CameraHeight); }
            var typeRows = new Dictionary<string, List<(int id, bool minion, int gait, string verdict)>>();
            var typeSound = new Dictionary<string, (bool ok, string what)>();
            var typeView = new Dictionary<string, float>();
            var fails = new List<string>(); bool allGates = true;
            for (int b = 0; b < Batches.Length; b++)
            {
                var types = Batches[b]; var leaders = new List<(ZObject o, int hx, int hy, string type, int squad)>();
                var ids = new Dictionary<int, string>();
                recs.Clear(); typeFrames.Clear(); typeInView.Clear(); marchFrames.Clear(); emitSeen.Clear(); ownSteps.Clear(); emitReadable = false;
                for (int k = 0; k < types.Length && k < Lanes.Length; k++)
                {
                    int tx = cx + SpawnDx + laneDx[k], ty = cy + Lanes[k];
                    var o = W.CreateRobotGroup(types[k], tx * 16, ty * 16, g.player);
                    if (o == null) { fails.Add($"{types[k]}: could not spawn"); continue; }
                    g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
                    stage.Add(o); stage.AddRange(o.Minions);
                    ids[o.Id] = types[k]; foreach (var m in o.Minions) ids[m.Id] = types[k];
                    int hx = o.LocX + o.WidthPix / 2, hy = o.LocY + o.HeightPix / 2;
                    // a player order: PlayerHold (DV38), so the idle squad never walks off to board a vehicle or take a flag / crate
                    W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(hx, hy) });
                    leaders.Add((o, hx, hy, types[k], 1 + o.Minions.Count));
                    foreach (var u in new[] { o }.Concat(o.Minions))
                    {
                        var rec = new Rec { o = u, type = types[k] };
                        if (g.Views.TryGetValue(u.Id, out var uv) && uv)
                        {
                            rec.v = uv; rec.a = uv.GetComponentInChildren<Animator>(); rec.ik = rec.a ? rec.a.GetComponent<RobotLegIK>() : null;
                            if (rec.a && rec.a.isHuman) { rec.foot[0] = rec.a.GetBoneTransform(HumanBodyBones.LeftFoot); rec.foot[1] = rec.a.GetBoneTransform(HumanBodyBones.RightFoot); }
                        }
                        recs.Add(rec);
                    }
                }
                yield return new WaitForSeconds(SettleSeconds);
                foreach (var (o, hx, hy, type, squad) in leaders)
                {
                    bool gone = o.Destroyed || W.Get(o.Id) == null;
                    if (gone || Dist(o.LocX + o.WidthPix / 2, o.LocY + o.HeightPix / 2, hx, hy) > 32f)
                        fails.Add($"{type}: left its lane during the settle ({(gone ? "gone / boarded" : "moved")}, hold {o.PlayerHold})");
                }
                var snd0 = Snap(); float t0 = Time.unscaledTime; emitT0 = t0;
                LegJitterGate.Begin(GateSeconds, false);
                yield return null; yield return null;                                        // the gate collects the robots in its Start
                double pf0 = RobotLegIK.ProbeFoot, pr0 = RobotLegIK.ProbeRoot, pa0 = RobotLegIK.ProbeAnim; int pn0 = RobotLegIK.ProbeN;
                var rs0 = (double[])RobotLegIK.ReachRatioSum.Clone(); var rn0 = (int[])RobotLegIK.ReachRatioN.Clone();
                foreach (var (o, hx, hy, type, squad) in leaders)
                    if (!o.Destroyed) W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(hx + WalkPx, hy + SidePx) });   // the same side step on every lane: parallel
                float tTurn = Time.time + GateSeconds * 0.55f;
                while (Time.time < tTurn) { Sample(leaders); yield return null; }
                foreach (var (o, hx, hy, type, squad) in leaders)
                    if (!o.Destroyed) W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(hx, hy) });
                float tEnd = Time.unscaledTime + GateSeconds + 4f;
                while (!LegJitterGate.Status.StartsWith("done") && Time.unscaledTime < tEnd) { Sample(leaders); yield return null; }
                var snd1 = Snap();
                string gateLine = LegJitterGate.Status;
                string md = Path.Combine(Root, "work", "records", "leg_gate.md"), tr = Path.Combine(Root, "work", "records", "leg_gate_trace.csv");
                string bmd = Path.Combine(Root, "work", "records", $"leg_gate_types_{level}_b{b + 1}.md");
                var table = new List<string>();
                try
                {
                    if (File.Exists(md)) { File.Copy(md, bmd, true); table = File.ReadAllLines(md).Where(l => l.StartsWith("| ") && l.Length > 2 && char.IsDigit(l[2])).ToList(); }
                    if (File.Exists(tr)) File.Copy(tr, Path.Combine(Root, "work", "records", $"leg_gate_types_{level}_b{b + 1}_trace.csv"), true);
                }
                catch (System.Exception ex) { fails.Add("batch " + (b + 1) + " report copy failed: " + ex.Message); }
                if (!gateLine.StartsWith("done")) { allGates = false; fails.Add($"batch {b + 1}: LegJitterGate did not finish ({gateLine})"); }
                foreach (var row in table)
                {
                    var c = row.Split('|').Select(x => x.Trim()).ToArray();
                    if (c.Length < 17 || !int.TryParse(c[1], out int id) || !ids.TryGetValue(id, out var type)) continue;
                    var wg = c[5].Split('/'); int gait = wg.Length > 1 && int.TryParse(wg[1].Trim(), out var gn) ? gn : 0;
                    if (!typeRows.TryGetValue(type, out var lst)) typeRows[type] = lst = new List<(int, bool, int, string)>();
                    lst.Add((id, c[2].EndsWith(" m"), gait, c[16]));
                }
                // ---- sounds: batch counts, then the proof per type (squads of 3+: their own march loop; 1-2: their single steps in OzSoundLog)
                var steps = new StringBuilder();
                foreach (var k in SoundKeys) { int n = (snd1.TryGetValue(k, out var a) ? a : 0) - (snd0.TryGetValue(k, out var z) ? z : 0); if (n > 0) steps.Append($"{k} x{n}, "); }
                int march = (snd1.TryGetValue("rob_squad_march", out var m1) ? m1 : 0) - (snd0.TryGetValue("rob_squad_march", out var m0) ? m0 : 0);
                var slog = OzSoundLog.Since(t0).Where(e => e.src == "AD" && e.key != null && e.outcome != null && e.outcome.StartsWith("PLAY")).ToList();
                int toughSteps = slog.Count(e => e.key == "mech_tough_step"), robSteps = slog.Count(e => e.key.StartsWith("rob_step_"));
                int smallRob = leaders.Count(l => l.squad < 3 && l.type != "tough");
                foreach (var (o, hx, hy, type, squad) in leaders)
                {
                    bool ok; string what;
                    if (squad >= 3) { int f = marchFrames.TryGetValue(type, out var mf) ? mf : 0; ok = f > 0 && march > 0; what = $"rob_squad_march loop on {f} frames (batch loop starts x{march})"; }
                    else
                    {   // per robot: this squad's own ids as the emitter (level-wide OzSoundLog count printed for reference only)
                        int own = ownSteps.TryGetValue(type, out var os) ? os : 0;
                        string emitters = string.Join(" / ", new[] { o }.Concat(o.Minions).Select(u => u.Id));
                        string key = type == "tough" ? "mech_tough_step" : "rob_step_*";
                        ok = emitReadable && own > 0;
                        what = emitReadable ? $"own {key} PLAY x{own} (emitter {emitters}; level-wide OzSoundLog x{(type == "tough" ? toughSteps : robSteps)}{(type != "tough" && smallRob > 1 ? $", {smallRob} small squads" : "")})"
                                            : "AudioDirector.lastEmit not readable: no per-robot proof";
                    }
                    typeSound[type] = (ok, what);
                    int tf = typeFrames.TryGetValue(type, out var n1) ? n1 : 0, ti = typeInView.TryGetValue(type, out var n2) ? n2 : 0;
                    typeView[type] = tf > 0 ? (float)ti / tf : 0f;
                }
                // ---- stride probe + reach ratio (RobotLegIK statics: every walking BS-01 robot in the level; only the staged squads are ordered)
                int pn = RobotLegIK.ProbeN - pn0;
                string probe = pn > 0 ? $"{pn} contact frames, clip-goal boot speed along the motion {(RobotLegIK.ProbeFoot - pf0) / pn:0.00} m/s vs root {(RobotLegIK.ProbeRoot - pr0) / pn:0.00} m/s (anim speed {(RobotLegIK.ProbeAnim - pa0) / pn:0.00}; 0 m/s = no slide)" : "no contact frames";
                string Reach(int i) { int n = RobotLegIK.ReachRatioN[i] - rn0[i]; return n > 0 ? $"{(RobotLegIK.ReachRatioSum[i] - rs0[i]) / n:0.00} ({n} locked frames)" : "- (never locked)"; }
                sum.AppendLine($"## Batch {b + 1}: {string.Join(", ", types)} -> {Path.GetFileName(bmd)}");
                sum.AppendLine($"- LegJitterGate: {gateLine}");
                sum.AppendLine($"- walking sounds this batch (AudioDirector.Plays): {(steps.Length > 0 ? steps.ToString().TrimEnd(',', ' ') : "NONE")}; OzSoundLog AD PLAY: mech_tough_step x{toughSteps}, rob_step_* x{robSteps}");
                sum.AppendLine($"- RobotLegIK stride probe (ProbeFoot / ProbeRoot / ProbeN, walking): {probe}");
                sum.AppendLine($"- reach ratio hip-pin / leg length while locked: L {Reach(0)}, R {Reach(1)}");
                sum.AppendLine("- drawn boots per type (walking frames; slide = mean ground speed of a boot within 3 cm of its lowest, a planted boot = 0; locked = lock weight > 0.5):");
                foreach (var type in types)
                {
                    var rs = recs.Where(q => q.type == type).ToList(); if (rs.Count == 0) continue;
                    var (sl, shl, nl) = Slide(rs, 0); var (sr, shr, nr) = Slide(rs, 1);
                    int wn = rs.Sum(q => q.h[0].Count);
                    var ik = rs.Select(q => q.ik).FirstOrDefault(q => q); var len = ik && LegLenField != null ? LegLenField.GetValue(ik) as float[] : null;
                    var a0 = rs.Select(q => q.a).FirstOrDefault(q => q && q.isHuman); string hips = "-";
                    var v0 = rs.Select(q => q.v).FirstOrDefault(q => q);
                    if (a0 && v0)
                    {
                        var inv = Quaternion.Inverse(v0.transform.rotation); var hl = a0.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var hr = a0.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                        if (hl && hr) { var ll = inv * (hl.position - a0.transform.position); var lr = inv * (hr.position - a0.transform.position); hips = $"L ({ll.x:0.000}, {ll.y:0.000}, {ll.z:0.000}) R ({lr.x:0.000}, {lr.y:0.000}, {lr.z:0.000}) m"; }
                    }
                    int lk0 = rs.Sum(q => q.lockN[0]), lk1 = rs.Sum(q => q.lockN[1]);
                    sum.AppendLine($"  - {type}: {wn} frames; slide L {sl:0.00} m/s ({shl * 100f:0} % on the ground) / R {sr:0.00} m/s ({shr * 100f:0} %); locked L {(wn > 0 ? 100f * lk0 / wn : 0f):0} % / R {(wn > 0 ? 100f * lk1 / wn : 0f):0} %; " +
                                   $"legLen L {(len != null && len.Length > 1 ? len[0].ToString("0.000") : "-")} / R {(len != null && len.Length > 1 ? len[1].ToString("0.000") : "-")} m; hips (model root frame) {hips}");
                }
                sum.AppendLine();
                ClearStage();
                yield return new WaitForSeconds(0.5f);
            }
            foreach (var b in bots) if (!W.Bots.Contains(b)) W.Bots.Add(b);
            sum.AppendLine("| type | robots in the gate | minions | max steady gait frames | squad in view | walking sound | failing robots | verdict |");
            sum.AppendLine("|---|---|---|---|---|---|---|---|");
            bool pass = allGates;
            foreach (var type in Batches.SelectMany(x => x))
            {
                typeRows.TryGetValue(type, out var lst); lst = lst ?? new List<(int, bool, int, string)>();
                int maxGait = lst.Count > 0 ? lst.Max(r => r.gait) : 0;
                var bad = lst.Where(r => r.verdict != "ok").Select(r => $"{r.id}{(r.minion ? " m" : "")}: {r.verdict}").ToList();
                (bool ok, string what) snd = typeSound.TryGetValue(type, out var sv) ? sv : (false, "not measured (not spawned)");
                float view = typeView.TryGetValue(type, out var vv) ? vv : 0f;
                var why = new List<string>();
                if (lst.Count == 0) why.Add("not in the gate table (off screen / not spawned)");
                if (bad.Count > 0) why.Add(string.Join("; ", bad));
                if (lst.Count > 0 && maxGait < LegJitterGate.MinGaitFrames) why.Add($"no robot with {LegJitterGate.MinGaitFrames}+ steady gait frames");
                if (view < MinInView) why.Add($"whole squad in the camera view on only {view * 100f:0} % of the gate frames (need {MinInView * 100f:0} %)");
                if (!snd.ok) why.Add($"walking sound missing ({snd.what})");
                string verdict = why.Count == 0 ? "PASS" : "FAIL";
                if (why.Count > 0) { pass = false; fails.Add($"{type}: {string.Join(", ", why)}"); }
                sum.AppendLine($"| {type} | {lst.Count} | {lst.Count(r => r.minion)} | {maxGait} | {view * 100f:0} % | {snd.what}{(snd.ok ? "" : " **MISSING**")} | {(bad.Count > 0 ? string.Join("<br>", bad) : "-")} | {verdict} |");
            }
            if (fails.Count > 0 && pass) pass = false;
            Finish(level, pass, fails);
        }

        void Finish(string level, bool pass, List<string> fails)
        {
            Result = (pass ? "PASS" : "FAIL") + (fails.Count > 0 ? ": " + string.Join("; ", fails.Take(10)) : "");
            sum.Insert(0, $"RESULT: {Result}\n\n");
#if UNITY_EDITOR
            try { File.WriteAllText(Path.Combine(Root, "work", "records", $"leg_gate_types_{level}.md"), sum.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning("[LegGateTypes] write failed: " + ex.Message); }
#endif
            Status = "done: " + Result;
            Debug.Log($"[LegGateTypes] {level} RESULT: {Result} -> work/records/leg_gate_types_{level}.md");
            Destroy(this);
        }

        // ------------------------------------------------------------------------------------------------ RP1 grunt face close-ups
        IEnumerator RunFaces()
        {
            g = ZGame.I; W = g.World;
            var rts = FindFirstObjectByType<RtsCamera>();
            var focus = rts ? rts.focus : Vector3.zero;
            var (cx, cy, found) = Field(Mathf.RoundToInt(focus.x * 8f / 16f), Mathf.RoundToInt(-focus.z * 8f / 16f), 1);
            if (!found) { Status = "done: FAIL: no open field near the camera"; Debug.LogWarning("[LegGateTypes] faces: " + Status); Destroy(this); yield break; }
            cx += SpawnDx + laneDx[firstLane] + 2; cy += Lanes[firstLane];
            var bots = W.Bots.ToList(); W.Bots.Clear();
            var o = W.CreateRobotGroup("grunt", cx * 16, cy * 16, g.player, 1);
            var shots = new List<string>(); string why = "";
            var dn = FindFirstObjectByType<DayNight>(); float tod = dn ? dn.timeOfDay : 0.08f; bool dnRun = dn && dn.running;
            if (o != null)
            {
                g.SpawnUnitView(o); stage.Add(o);
                W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(o.LocX + o.WidthPix / 2, o.LocY + o.HeightPix / 2) });   // hold (DV38)
                if (rts) { rts.FlyTo(ZGame.PxToWorld(cx * 16 + 8, cy * 16 + 8)); rts.ZoomTo(22f); }
                yield return new WaitForSeconds(SettleSeconds);
                foreach (var (name, t) in new[] { ("day", 0.08f), ("night", 0.62f) })
                {
                    if (dn) { dn.running = false; NightLights.JumpTo(t); }
                    yield return new WaitForSeconds(1f);                                     // lights / grade settle
                    yield return new WaitForEndOfFrame();
                    foreach (var (view, yaw) in new[] { ("front", 0f), ("three_quarter", 35f) })
                    {
                        string p = null;
                        try { p = FaceShot(o, name + "_" + view, yaw); } catch (System.Exception ex) { why = ex.Message; }
                        if (p != null) shots.Add(p);
                    }
                }
                if (dn) { dn.timeOfDay = tod; dn.Apply(tod); dn.running = dnRun; }
            }
            else why = "could not spawn a grunt";
            ClearStage();
            foreach (var b in bots) if (!W.Bots.Contains(b)) W.Bots.Add(b);
            Status = shots.Count == 4 ? "done: PASS: " + string.Join(", ", shots.Select(Path.GetFileName)) : $"done: FAIL: {shots.Count}/4 shots {why}";
            Debug.Log($"[LegGateTypes] faces {g.level}: {Status} -> work/records/milestones/legs_gate_v2/");
            Destroy(this);
        }

        string FaceShot(ZObject o, string tag, float yaw)
        {
            if (!g.Views.TryGetValue(o.Id, out var v) || !v) return null;
            var a = v.GetComponentInChildren<Animator>(); var head = a && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            var smr = v.GetComponentInChildren<SkinnedMeshRenderer>();
            float hgt = smr ? smr.bounds.size.y : 1.8f;
            Vector3 target = head ? head.position : smr ? new Vector3(smr.bounds.center.x, smr.bounds.max.y - hgt * 0.12f, smr.bounds.center.z) : v.transform.position + Vector3.up * 1.6f;
            var face = -v.transform.forward; face.y = 0f;                                   // robots face their -z (UnitView: modelYaw 180)
            if (face.sqrMagnitude < 1e-6f) face = Vector3.back;
            face = Quaternion.Euler(0f, yaw, 0f) * face.normalized;
            var cam = ShotCamera("LegGateFaceCam");
            cam.fieldOfView = 30f; cam.nearClipPlane = 0.03f; cam.farClipPlane = 400f;
            cam.transform.position = target + face * Mathf.Max(0.6f, hgt * 0.75f) + Vector3.up * hgt * 0.05f; cam.transform.LookAt(target);
            return RenderShot(cam, $"grunt_face_{g.level}_{tag}_{System.DateTime.Now:HHmm}.png");
        }

        const int ShotW = 2560, ShotH = 1600;

        /// <summary>A disabled copy of the main camera (transform, FOV, URP post / AA / shadows) with the 2560x1600 aspect.</summary>
        static Camera ShotCamera(string name)
        {
            var go = new GameObject(name); var cam = go.AddComponent<Camera>(); var main = Camera.main;
            if (main) cam.CopyFrom(main);
            cam.enabled = false; cam.aspect = (float)ShotW / ShotH;                          // the frame's own aspect, not the Game view's
            var md = main ? main.GetComponent<UniversalAdditionalCameraData>() : null;
            if (md) { var cd = cam.GetUniversalAdditionalCameraData(); cd.renderPostProcessing = md.renderPostProcessing; cd.antialiasing = md.antialiasing; cd.renderShadows = md.renderShadows; }
            return cam;
        }

        /// <summary></summary>
        static string RenderShot(Camera cam, string file)
        {
            var rt = new RenderTexture(ShotW, ShotH, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                var prev = RenderTexture.active; RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, ShotW, ShotH), 0, 0); tex.Apply(); RenderTexture.active = prev;
                string dir = Path.Combine(Root, "work", "records", "milestones", "legs_gate_v2"); Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, file);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                return path;
            }
            finally { cam.targetTexture = null; rt.Release(); Destroy(rt); Destroy(tex); Destroy(cam.gameObject); }
        }

        // ------------------------------------------------------------------------------------------------ README 5.4: 22 m eye frames
        /// <summary>Mid-swing phase per foot = the middle of RobotLegIK's walking swing window (L 0.680 -> 0.150, R 0.180 -> 0.650).</summary>
        static readonly float[] MidSwing = { 0.915f, 0.415f };
        public static float EyeHeight = 22f;

        static bool Crossed(float prev, float cur, float x) => cur >= prev ? prev < x && x <= cur : x > prev || x <= cur;

        string Ankles(IEnumerable<Animator> anims)
        {
            var sb = new StringBuilder();
            foreach (var a in anims)
            {
                if (!a || !a.isHuman) continue;
                var l = a.GetBoneTransform(HumanBodyBones.LeftFoot); var r = a.GetBoneTransform(HumanBodyBones.RightFoot);
                if (!l || !r) continue;
                sb.Append(sb.Length > 0 ? ", " : "").Append($"L {l.position.y - g.GroundY(l.position.x, l.position.z):0.00} / R {r.position.y - g.GroundY(r.position.x, r.position.z):0.00}");
            }
            return "ankles over ground m: " + (sb.Length > 0 ? sb.ToString() : "-");
        }

        IEnumerator RunEyes()
        {
            g = ZGame.I; W = g.World;
            var rts = FindFirstObjectByType<RtsCamera>();
            var focus = rts ? rts.focus : Vector3.zero;
            var (cx, cy, found) = Field(Mathf.RoundToInt(focus.x * 8f / 16f), Mathf.RoundToInt(-focus.z * 8f / 16f), 1);
            if (!found) { Status = "done: FAIL: no open field near the camera"; Debug.LogWarning("[LegGateTypes] eyes: " + Status); Destroy(this); yield break; }
            cx += laneDx[firstLane]; cy += Lanes[firstLane];
            var bots = W.Bots.ToList(); W.Bots.Clear();
            var notes = new List<string>(); string why = "";
            float h0 = rts ? rts.height : 70f; var follow0 = rts ? rts.follow : null;
            var o = W.CreateRobotGroup("grunt", (cx + SpawnDx) * 16, cy * 16, g.player);
            if (o == null) why = "could not spawn a grunt squad";
            else
            {
                g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
                stage.Add(o); stage.AddRange(o.Minions);
                int hx = o.LocX + o.WidthPix / 2, hy = o.LocY + o.HeightPix / 2;
                W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(hx, hy) });              // hold (DV38)
                var anims = new[] { o }.Concat(o.Minions).Select(u => g.Views.TryGetValue(u.Id, out var uv) && uv ? uv.GetComponentInChildren<Animator>() : null).Where(x => x).ToList();
                var lv = g.Views.TryGetValue(o.Id, out var v0) ? v0 : null; var la = lv ? lv.GetComponentInChildren<Animator>() : null;
                if (rts && lv) { rts.follow = lv.transform; rts.ZoomTo(EyeHeight); }
                yield return new WaitForSeconds(SettleSeconds + 0.5f);
                // 1) mid-stride: the leader's steady walk (> 1 s, no cross-fade) at the middle of a boot's swing window
                W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(hx + WalkPx, hy) });
                float steady = -1f, prevPh = -1f, tOut = Time.time + 8f; bool shot = false;
                while (la && Time.time < tOut && !shot)
                {
                    var st = la.GetCurrentAnimatorStateInfo(0);
                    if (!st.IsName("walking") || la.IsInTransition(0)) { steady = -1f; prevPh = -1f; yield return null; continue; }
                    if (steady < 0f) steady = Time.time;
                    float ph = Mathf.Repeat(st.normalizedTime, 1f);
                    if (Time.time - steady > 1f && prevPh >= 0f && (Crossed(prevPh, ph, MidSwing[0]) || Crossed(prevPh, ph, MidSwing[1])))
                    {
                        string note = $"phase {ph:0.000} ({(Crossed(prevPh, ph, MidSwing[0]) ? "left" : "right")} boot mid-swing), {Ankles(anims)}";
                        try { var p = RenderShot(ShotCamera("LegGateEyeCam"), $"legs_walk_midstride_{System.DateTime.Now:HHmm}.png"); notes.Add($"{Path.GetFileName(p)} ({note})"); }
                        catch (System.Exception ex) { why = ex.Message; }
                        shot = true;
                    }
                    prevPh = ph; yield return null;
                }
                if (!shot) why = "no steady walk at mid-stride within 8 s";
                // 2) standing: back to the hold point, every robot of the squad in idle (no cross-fade) for 1.5 s
                W.SendWaypoints(g.player, o, new[] { ZWaypoint.Move(hx, hy) });
                float still = -1f; tOut = Time.time + 15f; shot = false;
                while (Time.time < tOut && !shot && anims.Count > 0)
                {
                    bool idle = anims.All(x => x && (x.GetCurrentAnimatorStateInfo(0).IsName("idle") || x.GetCurrentAnimatorStateInfo(0).IsName("bazooka_idle")) && !x.IsInTransition(0));
                    if (!idle) still = -1f;
                    else if (still < 0f) still = Time.time;
                    else if (Time.time - still >= 1.5f)
                    {
                        try { var p = RenderShot(ShotCamera("LegGateEyeCam"), $"legs_stand_{System.DateTime.Now:HHmm}.png"); notes.Add($"{Path.GetFileName(p)} ({Ankles(anims)})"); }
                        catch (System.Exception ex) { why = ex.Message; }
                        shot = true;
                    }
                    yield return null;
                }
                if (!shot && why == "") why = "the squad did not stand still within 15 s";
            }
            if (rts) { rts.follow = follow0; rts.ZoomTo(h0); }
            ClearStage();
            foreach (var b in bots) if (!W.Bots.Contains(b)) W.Bots.Add(b);
            Status = notes.Count == 2 ? "done: PASS: " + string.Join("; ", notes) : $"done: FAIL: {notes.Count}/2 frames {why} {string.Join("; ", notes)}";
            Debug.Log($"[LegGateTypes] eyes {g.level} ({EyeHeight:0} m): {Status} -> work/records/milestones/legs_gate_v2/");
            Destroy(this);
        }
    }
}
