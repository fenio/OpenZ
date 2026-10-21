using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// every .12 s the 4 crewed vehicles nearest the camera's ground focus get a looping engine (the player's selected vehicles
    /// has no loop and plays metal footsteps instead (mech_step, final-metal-land-a/b/c .48, rate .78-1.10, every ~.98 m,
    /// any other crewed vehicle .20. Loops start at a random offset, native pitch, fade out over .12 s
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class LegacyEngineAudio : MonoBehaviour
    {
        public static LegacyEngineAudio I { get; private set; }
        public int maxAudible = 3;
        public float reselectEvery = 0.12f;
        public const float GainSelectedMoving = 0.48f, GainSelectedIdle = 0.16f, GainCrewed = 0.20f;
        /// <summary>audio_v2: a crewed vehicle driving past the camera no longer sounds like one idling (T19: .20 for both); a short
        /// stop idles at .13, and after ParkAfter s the session's parked idle (GainParked .04) takes over.</summary>
        public const float GainCrewedMoving = 0.30f, GainCrewedIdle = 0.13f;
        /// <summary>
        /// shooter vehicle but a constant drone from a row of parked RTS vehicles. After ParkAfter s standing still an engine eases
        /// down to a low idle (GainParked, selected GainParkedSelected); it comes straight back when the vehicle moves.</summary>
        public const float ParkAfter = 2f, GainParked = 0.04f, GainParkedSelected = 0.08f;
        readonly Dictionary<int, float> movedAt = new Dictionary<int, float>();

        class Loop { public AudioSource s; public AudioLowPassFilter lp; public int id = -1; public float target, cur; public string bank; }
        readonly List<Loop> loops = new List<Loop>();
        readonly List<UnitView> cand = new List<UnitView>();
        readonly HashSet<int> audible = new HashSet<int>();
        readonly Dictionary<int, (Vector3 last, float travel, int phase)> mech = new Dictionary<int, (Vector3, float, int)>();
        float nextSel;
        PlayerInput input; RtsCamera rts;
        public int ActiveLoops { get; private set; }

        void Awake()
        {
            I = this;
            for (int i = 0; i < maxAudible; i++)
            {
                var go = new GameObject("Engine" + i); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>(); a.playOnAwake = false; a.loop = true; a.dopplerLevel = 0; a.spread = 60f; a.priority = 150;
                loops.Add(new Loop { s = a, lp = go.AddComponent<AudioLowPassFilter>() });
            }
        }

        static string Clip(CombatAudio au, string bank) => au.HasClip(bank + "-loop") ? bank + "-loop" : bank;   // gapless WAV first

        /// <summary>
        /// loops.Find(x => x.id == id) ?? loops.Find(x => x.id < 0 || (x.cur < 0.002f && !audible.Contains(x.id))), without allocating
        /// two closures per audible engine every frame.</summary>
        Loop FindLoop(int id)
        {
            for (int i = 0; i < loops.Count; i++) if (loops[i].id == id) return loops[i];
            for (int i = 0; i < loops.Count; i++) { var x = loops[i]; if (x.id < 0 || (x.cur < 0.002f && !audible.Contains(x.id))) return x; }
            return null;
        }

        void LateUpdate()
        {
            var game = ZGame.I; var au = CombatAudio.I;
            if (!game || game.World == null || !au) return;
            if (!input) input = FindFirstObjectByType<PlayerInput>();
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            Vector3 focus = rts ? rts.focus : (Camera.main ? Camera.main.transform.position : Vector3.zero);
            float dt = Time.deltaTime;

            if (Time.time >= nextSel)
            {   // nearest 4 to the camera ground focus, the player's own first
                nextSel = Time.time + reselectEvery;
                cand.Clear();
                foreach (var v in game.Views.Values)
                {
                    if (!v || !v.isActiveAndEnabled || v.Dying || v.Obj == null || v.Obj.Destroyed) continue;
                    if (v.Obj.Kind != ZKind.Vehicle || CaptureHold.Shown(v.Obj) == Team.Neutral) continue;   // boarding_nb_v1 NB17
                    if (LegacyMatch.EngineLoop(v.Obj) == null) continue;
                    cand.Add(v);
                }
                cand.Sort((a, b) => Score(a, focus).CompareTo(Score(b, focus)));
                audible.Clear();
                for (int i = 0; i < cand.Count && i < maxAudible; i++) audible.Add(cand[i].Obj.Id);
            }

            foreach (var l in loops) l.target = 0f;
            foreach (var id in audible)
            {
                if (!game.Views.TryGetValue(id, out var v) || !v || v.Obj == null) continue;
                if (v.Dying || v.Obj.Destroyed || !v.isActiveAndEnabled || CaptureHold.Shown(v.Obj) == Team.Neutral) continue;
                var bank = LegacyMatch.EngineLoop(v.Obj); if (bank == null) continue;
                var l = FindLoop(id);
                if (l == null) continue;
                if (l.id != id || l.bank != bank || !l.s.isPlaying)
                {
                    var clip = au.Clip(Clip(au, bank)); if (!clip) continue;
                    l.id = id; l.bank = bank; l.cur = 0f;
                    l.s.clip = clip; l.s.pitch = OzSoundV3.Enabled ? OzSoundV3.EnginePitch(v.Obj) : 1f;   // sound_v3: heavy .82, MLRS .92, APC / crane 1.08
                    l.s.time = (Mathf.Abs(id * 7919) % 997) / 997f * clip.length;
                    au.Configure(l.s, l.lp, CombatAudio.Profile.Field, 85f * au.RangeScale);
                    l.s.Play();
                }
                l.s.transform.position = v.transform.position + Vector3.up;
                bool sel = input && input.Selection.Contains(v.Obj);
                bool moving = v.Obj.IsMoving;
                if (moving || !movedAt.ContainsKey(id)) movedAt[id] = Time.time;
                bool parked = !moving && Time.time - movedAt[id] > ParkAfter;
                l.target = moving ? (sel ? GainSelectedMoving : GainCrewedMoving)                                        // audio_v2: moving .30
                                  : parked ? (sel ? GainParkedSelected : GainParked) : (sel ? GainSelectedIdle : GainCrewedIdle);   // audio_v2: short stop .13
                l.s.maxDistance = Mathf.Max(8f, 85f * au.RangeScale);
            }
            int active = 0;
            foreach (var l in loops)
            {
                l.cur = Mathf.Lerp(l.cur, l.target, 1f - Mathf.Exp(-dt / (l.target > 0f ? 0.08f : 0.12f)));   // setTargetAtTime .08 / stop .12
                if (l.target <= 0f && l.cur <= 0.002f) { if (l.s.isPlaying) l.s.Stop(); l.id = -1; continue; }
                l.s.volume = l.cur * au.LoopBusGain * au.LevelTrim(l.s.clip ? l.s.clip.name : null) * OzMixTrim.CaLin("engine_loop");   // tank-loop -12 LUFS -> leveller
                active++;
            }
            ActiveLoops = active;
            MechSteps(game, au);
        }

        /// <summary>
        /// the vehicle is gone, dying, despawned or neutral. Returns the STALE count.</summary>
        public int AuditLines(System.Text.StringBuilder sb, HashSet<AudioSource> known, Vector3 listener, bool full, ref int n)
        {
            int stale = 0; var game = ZGame.I;
            foreach (var l in loops)
            {
                if (!l.s) continue;
                known.Add(l.s);
                if (!l.s.isPlaying || l.target <= 0f && l.cur < 0.01f) continue;
                n++;
                UnitView v = null; if (game != null && l.id >= 0) game.Views.TryGetValue(l.id, out v);
                bool bad = !v || v.Obj == null || v.Dying || v.Obj.Destroyed || !v.isActiveAndEnabled || CaptureHold.Shown(v.Obj) == Team.Neutral;
                if (bad) { stale++; l.target = 0f; audible.Remove(l.id); }
                if (!(full || bad)) continue;
                string state = "vehicle gone #" + l.id;
                if (v && v.Obj != null)
                {
                    float t; bool parked = movedAt.TryGetValue(l.id, out t) && Time.time - t > ParkAfter;
                    state = (bad ? "" : "ok ") + v.Obj.Owner + " " + v.Obj.Type + " #" + v.Obj.Id + " " + (v.Obj.IsMoving ? "moving" : parked ? "parked idle" : "idle");
                }
                sb.Append("\n  ").Append(bad ? "STALE " : "").Append("engine ").Append(l.s.name).Append(" | ").Append(l.s.clip ? l.s.clip.name : "-")
                  .Append(" | vol ").Append(l.s.volume.ToString("0.000")).Append(" | ").Append(Vector3.Distance(listener, l.s.transform.position).ToString("0")).Append(" m | ").Append(state);
            }
            return stale;
        }

        float Score(UnitView v, Vector3 focus)
        {
            float d = (new Vector2(v.transform.position.x - focus.x, v.transform.position.z - focus.z)).sqrMagnitude;
            return input && input.Selection.Contains(v.Obj) ? d - 1e6f : d;
        }

        readonly Dictionary<int, int> plantIdx = new Dictionary<int, int>();
        /// <summary>
        /// gain and rate) at the planted foot, then MechFoley's servo / stomp now and then. No extra thud: the existing banks, now on
        /// the real contact frame instead of a Travel phase (steps in place when turning included).</summary>
        public void MechPlant(UnitView v, int leg, Vector3 at)
        {
            var au = CombatAudio.I;
            if (!au || !v || v.Obj == null || v.Obj.Type != "medium" || v.Dying || !v.isActiveAndEnabled) return;
            plantIdx.TryGetValue(v.Obj.Id, out int n); n++; plantIdx[v.Obj.Id] = n;
            // takes by the surface under the planted foot (sand / concrete / L17 steel deck; L02 wooden deck -> concrete)
            au.PlayPitched("mech_step", at, LegacyMatch.GainMechStep * Random.Range(0.90f, 1.02f), 0.3f, v.Obj.Id, Random.Range(OzFoleyV1.PlantPitchMin, OzFoleyV1.PlantPitchMax), PlantBank(at));
            MechFoley.Plant(v, n, at, au);
        }

        /// <summary>The OzFoleyV1 mech plant bank for the ground under a planted foot (the robot-step surface rule).</summary>
        public static string[] PlantBank(Vector3 at)
        {
            var g = ZGame.I; bool city = g && g.level == "L17", deck = false, road = false;
            var tm = TrackMarks.I; if (tm && tm.Surface != null) tm.Surface.Sample(at.x, at.z, out float gy, out deck);
            var map = g && g.World != null ? g.World.Map : null;
            if (map != null) { var px = ZGame.WorldToPx(at); road = map.ClassAt(px.x >> 4, px.y >> 4) == ZClass.Road; }
            return OzFoleyV1.BankMechPlant(OzFoleyV1.SurfaceFor(deck, city, road, g && g.level == "L16"));
        }

        /// <summary>Mech (medium tank) footfalls: a step every half stride (phase = floor(travel x 3.2 / pi)).</summary>
        void MechSteps(ZGame game, CombatAudio au)
        {
            foreach (var v in game.Views.Values)
            {
                if (!v || v.Obj == null || v.Obj.Type != "medium" || v.Dying || !v.isActiveAndEnabled) continue;
                var mwd = MechFoley.Walk(v); if (mwd && mwd.Drives) { mech.Remove(v.Obj.Id); continue; }
                var p = v.transform.position;
                if (!mech.TryGetValue(v.Obj.Id, out var m)) { mech[v.Obj.Id] = (p, 0f, 0); continue; }
                float step = new Vector2(p.x - m.last.x, p.z - m.last.z).magnitude;
                if (step > 5f) step = 0f;                                                                        // teleport / respawn
                m.travel += step; m.last = p;
                var mw = MechFoley.Walk(v); if (mw) m.travel = mw.Travel;                                   // vehicles_nb_v1 NB26: the legs' own stride (footfalls on the foot plants)
                int phase = Mathf.FloorToInt(m.travel * OpenZFxCatalog.MechStepPhase / Mathf.PI);
                if (phase != m.phase)
                {
                    m.phase = phase;
                    au.PlayPitched("mech_step", p, LegacyMatch.GainMechStep * Random.Range(0.78f, 1.02f), 0.3f, v.Obj.Id,
                        Random.Range(0.78f, 1.10f), PlantBank(p));
                    MechFoley.Footfall(v, phase, p, au);                                                  // vehicles_nb_v1 NB26: servo whirs / heavy stomps now and then, on screen
                }
                mech[v.Obj.Id] = m;
            }
        }
    }
}
