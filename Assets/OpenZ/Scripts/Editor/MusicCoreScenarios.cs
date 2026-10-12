using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace OpenZ
{
    /// <summary>
    /// file with MusicCore.cs; the editor menu OpenZ > Music v3 > Run scenarios runs it inside Unity). A fake audio backend plays
    /// the core's commands (positions advance, clips end, pauses hold their sample) and checks EVERY frame:
    ///  - never more than 2 audible voices, never 2 near full volume (both >= 0.8 of their level), two only while crossfading
    ///    (never longer than the longest crossfade + 1 s), no Play over an audible voice, no pause while audible, no silence gap
    ///    inside Regular / Drama;
    ///  - no gain jump: with the duck divided out, a voice's fade never moves faster than the fastest fade the core may use
    ///    (0.6 s end to end on the equal-power curve, 2.7 / s), and Zod's duck never moves faster than 5 dB/s;
    ///  - never the same track twice in a row, never a restart of a paused (kept) track, every unpause at the exact sample.
    /// Each scenario then asserts its own story (below), including the anti-flap rules (attack waves, switches per 10 min, the
    /// shortest regular interlude), idle units near a fort (never a stuck drama), single cold readings (never a missed drama),
    /// the music toggle (resume, never restart) and the game-over edges. RunAll -> PASS / FAIL lines + the timelines.
    /// </summary>
    public static class MusicCoreScenarios
    {
        // ============================================================================================================ fake backend
        public sealed class FakeOut : IMusicOut
        {
            public sealed class V { public int id; public MusicTrack t; public double pos, pausedPos; public bool on, paused, ended; public float gain, norm, fade; }
            public readonly V[] v = { new V { id = 0 }, new V { id = 1 }, new V { id = 2 }, new V { id = 3 } };
            public readonly MusicCoreConfig cfg;
            public double now; public float dt = 1f / 60f;
            public readonly List<string> events = new List<string>(), violations = new List<string>();
            public readonly List<(double t, MusicTrack track, double at)> plays = new List<(double, MusicTrack, double)>();
            public readonly List<(double t, MusicTrack track, double at, double err)> resumes = new List<(double, MusicTrack, double, double)>();
            public int pauses, stops; public double maxResumeErr; string lastPlayed;
            public bool keepEvents = true;
            /// <summary>Set by Sim: the core's duck this frame (linear). With it the jump check runs on the pure fade (strict).</summary>
            public Func<float> duckLin; public float fadeLimit = 2.7f; public float maxFadeSlope;
            public FakeOut(MusicCoreConfig c) { cfg = c; }

            public float G(MusicTrack t) => (float)Math.Pow(10.0, (t.gainDb + PoolDb(t.pool)) / 20.0);
            float PoolDb(MusicPool p) => p == MusicPool.Victory ? cfg.victoryDb : p == MusicPool.Menu ? cfg.menuDb : p == MusicPool.Drama ? cfg.dramaDb : 0f;
            public void Fail(string s) { if (violations.Count < 60) violations.Add($"{now,8:0.00}  {s}"); }
            readonly Queue<string> ring = new Queue<string>();
            /// <summary>The last events (kept even when keepEvents is off): printed with a violation.</summary>
            public string Recent() => string.Join(" / ", ring);
            void Ev(string s) { string l = $"{now,8:0.00}  {s}"; if (keepEvents) events.Add(l); ring.Enqueue(l.Trim()); if (ring.Count > 6) ring.Dequeue(); }

            public void Play(int i, MusicTrack t, double at)
            {
                var x = v[i];
                if (x.on && !x.ended && !x.paused && x.gain > 0.0005f) Fail($"Play on v{i} while it is audible ({x.t?.name})");
                if (lastPlayed == t.name) Fail($"same track twice in a row: {t.name}");
                foreach (var y in v) if (y.on && y.paused && y.t != null && y.t.name == t.name) Fail($"RESTART of the paused {t.name} (Play instead of unpause)");
                x.t = t; x.pos = at; x.on = true; x.paused = false; x.ended = false; x.gain = 0f; x.norm = 0f; x.fade = 0f;
                plays.Add((now, t, at)); lastPlayed = t.name;
                Ev($"PLAY    v{i} {t.name} @ {at:0.00} s ({t.pool}{(t.zopen ? ", added" : "")})");
            }
            public void Pause(int i)
            {
                var x = v[i];
                if (!x.on || x.paused) Fail($"Pause on v{i} that is not playing");
                if (x.gain > 0.0005f) Fail($"Pause on v{i} while audible (gain {x.gain:0.000})");
                x.paused = true; x.pausedPos = x.pos; pauses++;
                Ev($"PAUSE   v{i} {x.t?.name} @ {x.pos:0.000} s");
            }
            public void Resume(int i)
            {
                var x = v[i];
                if (!x.paused) { Fail($"UnPause on v{i} that is not paused"); return; }
                double err = Math.Abs(x.pos - x.pausedPos);
                maxResumeErr = Math.Max(maxResumeErr, err);
                x.paused = false;
                resumes.Add((now, x.t, x.pos, err));
                Ev($"UNPAUSE v{i} {x.t?.name} @ {x.pos:0.000} s (paused at {x.pausedPos:0.000}, error {err * 44100:0} samples)");
            }
            public void Stop(int i) { var x = v[i]; if (x.on) Ev($"STOP    v{i} {x.t?.name} @ {x.pos:0.00} s"); x.on = false; x.paused = false; x.ended = false; x.t = null; x.gain = 0f; x.norm = 0f; x.fade = 0f; stops++; }
            public void Gain(int i, float g)
            {
                var x = v[i]; if (x.t == null) return;
                float n = g / G(x.t);
                if (duckLin != null)
                {   // strict: the pure fade (the duck divided out) never faster than the fastest fade of the core
                    float dl = Math.Max(1e-4f, duckLin());
                    float f = n / dl;
                    float step = Math.Abs(f - x.fade);
                    if (!x.paused) { maxFadeSlope = Math.Max(maxFadeSlope, step / Math.Max(dt, 1e-3f)); if (step > fadeLimit * Math.Max(dt, 1e-3f) + 2e-4f) Fail($"gain jump v{i} {x.t.name}: fade {x.fade:0.000} -> {f:0.000} in one frame ({step / dt:0.00} / s, limit {fadeLimit:0.00} / s)"); }
                    x.fade = f;
                }
                else if (!x.paused && Math.Abs(n - x.norm) > 5.3f * Math.Max(dt, 1e-3f) + 2e-4f) Fail($"gain jump v{i} {x.t.name}: {x.norm:0.000} -> {n:0.000} in one frame");
                x.gain = g; x.norm = n;
            }
            public double Time(int i) => v[i].pos;
            public bool Ended(int i) => v[i].on && v[i].ended && !v[i].paused;
            public void Log(string line) => Ev("core  " + line.TrimStart());

            public void Advance(double d)
            {
                now += d;
                foreach (var x in v)
                    if (x.on && !x.paused && !x.ended) { x.pos += d; if (x.pos >= x.t.length) { x.pos = x.t.length; x.ended = true; } }
            }
            public bool Audible(V x) => x.on && !x.paused && !x.ended && x.gain > 0.0005f;
        }

        // ============================================================================================================ one simulation
        public sealed class Sim
        {
            public readonly MusicCore core; public readonly FakeOut o; public readonly MusicCoreConfig cfg;
            public MusicInput inp; public readonly float dt; public double t;
            public readonly StringBuilder timeline = new StringBuilder(); public double printEvery; double nextPrint;
            double dualFor, silentFor, lastHitOurs = -1e9, lastHitTheirs = -1e9, gt, quietOkUntil; float prevZod; public float maxZodSlope;
            /// <summary>The director's shared duck target (Rachel -6 / Alan -4): smoothed like AudioDirector.StepDucks (0.15 s / 0.8 s).</summary>
            public float otherDuckTarget;
            public int maxAudible, maxHeld; public double maxDual;
            public (float score, int units, bool hitting) ourAttack, theirAttack;
            /// <summary>Idle / passing hostile strength near a fort that is NOT engaged (the 16-20 tile band, a parked squad): it only
            /// raises the reading's outerScore, which no rule may read.</summary>
            public float ourIdle, theirIdle;
            /// <summary>The game speed: sim seconds per real second (the attack holds run on sim time, the drama minimums on real time).</summary>
            public float speed = 1f;
            public bool ourFortAlive = true, theirFortAlive = true;
            public MusicLayer lastLayer; public readonly List<(double t, MusicLayer l)> layers = new List<(double, MusicLayer)>();
            public Func<Sim, bool> stopWhen;
            /// <summary>Called after every frame (the harness preview export).</summary>
            public Action<Sim> onFrame;

            public Sim(IList<MusicTrack> cat, MusicCoreConfig c, int seed, float dt = 1f / 60f)
            {
                cfg = c; this.dt = dt;
                o = new FakeOut(c) { dt = dt, fadeLimit = (float)(Math.PI * 0.5 / Math.Max(0.05, Math.Min(c.quickOut, c.offFade))) * 1.03f };
                core = new MusicCore(c, o, cat, seed);
                o.duckLin = () => (float)Math.Pow(10.0, core.DuckDb / 20.0);
                inp = new MusicInput { enabled = true, ours = FortReading.Quiet, theirs = FortReading.Quiet };
            }

            public void Ours(float score, int units, bool hitting) => ourAttack = (score, units, hitting);
            public void Theirs(float score, int units, bool hitting) => theirAttack = (score, units, hitting);

            FortReading Read((float score, int units, bool hitting) a, ref double lastHit, bool alive, float idle)
            {
                if (a.hitting) lastHit = gt;
                return new FortReading { alive = alive, score = a.score, units = a.units, outerScore = a.score + idle, sinceHit = lastHit < -1e8 ? 999f : (float)(gt - lastHit) };
            }

            public void Run(double seconds)
            {
                int n = (int)Math.Round(seconds / dt);
                for (int k = 0; k < n; k++)
                {
                    if (!inp.paused)
                    {
                        inp.ours = Read(ourAttack, ref lastHitOurs, ourFortAlive, ourIdle);
                        inp.theirs = Read(theirAttack, ref lastHitTheirs, theirFortAlive, theirIdle);
                    }
                    o.Advance(dt);
                    inp.dt = dt; inp.gameDt = inp.paused ? 0f : dt * speed; inp.playDt = inp.paused ? 0f : dt;
                    if (!inp.paused) gt += dt * speed;                                                  // the sim clock (W.Time) stops while paused
                    float tc = otherDuckTarget < inp.otherDuckDb ? 0.15f : 0.8f;
                    inp.otherDuckDb += (otherDuckTarget - inp.otherDuckDb) * (1f - (float)Math.Exp(-dt / tc));
                    core.Tick(inp);
                    t += dt;
                    Check();
                    if (core.Layer != lastLayer)
                    {   // a start from silence (level start, music switched back on) fades in on purpose: not a gap
                        if (lastLayer == MusicLayer.Off) quietOkUntil = t + cfg.startFade + 0.5;
                        layers.Add((t, core.Layer)); lastLayer = core.Layer;
                    }
                    onFrame?.Invoke(this);
                    if (printEvery > 0 && t + 1e-9 >= nextPrint) { timeline.AppendLine(Line()); nextPrint = t + printEvery; }
                    if (stopWhen != null && stopWhen(this)) { stopWhen = null; return; }
                }
            }

            void Check()
            {
                int aud = 0, held = 0; float power = 0f; int nearFull = 0;
                foreach (var x in o.v)
                {
                    if (x.on) held++;
                    if (!o.Audible(x)) continue;
                    aud++; power += x.norm * x.norm; if (x.norm >= 0.8f) nearFull++;
                }
                maxAudible = Math.Max(maxAudible, aud); maxHeld = Math.Max(maxHeld, held);
                if (aud > 2) o.Fail($"{aud} voices audible");
                if (nearFull >= 2) o.Fail("two voices near full volume at once");
                dualFor = aud == 2 ? dualFor + dt : 0; maxDual = Math.Max(maxDual, dualFor);
                if (dualFor > cfg.maxXfade + 1.0) o.Fail($"two voices together for {dualFor:0.0} s (longer than any crossfade)");
                bool steady = (core.Layer == MusicLayer.Regular || core.Layer == MusicLayer.Drama) && core.Now > cfg.startFade + 1f && t > quietOkUntil;
                silentFor = steady && power < 0.1f ? silentFor + dt : 0;
                if (silentFor > 0.6) { o.Fail($"silence gap in {core.Layer} [recent: {o.Recent()}]"); silentFor = -1e9; }
                int pr = 0, pd = 0;
                foreach (var x in o.v) if (x.on && x.paused) { if (x.t.pool == MusicPool.Drama) pd++; else pr++; }
                if (pr > 1 || pd > 1) o.Fail($"paused bookmarks: {pr} regular, {pd} drama");
                float zd = core.ZodDuck.Db(cfg.zodDuckDb); float zs = Math.Abs(zd - prevZod) / dt; prevZod = zd;
                maxZodSlope = Math.Max(maxZodSlope, zs);
                if (zs > 5.0f) o.Fail($"Zod duck moved {zs:0.0} dB/s (abrupt)");
            }

            public string Line()
            {
                var sb = new StringBuilder();
                sb.Append($"t={t,7:0.00}  {core.Layer,-9}");
                foreach (var x in o.v)
                {
                    if (!x.on) { sb.Append(" | -"); continue; }
                    string st = x.paused ? "PAUSED" : x.ended ? "ended" : "play";
                    sb.Append($" | v{x.id} {st} {x.t.name} {x.pos,6:0.0}/{x.t.length:0} g {x.norm:0.00}");
                }
                float d = core.ZodDuck.Db(cfg.zodDuckDb);
                if (d < -0.01f) sb.Append($"  [zod duck {d:0.00} dB]");
                return sb.ToString();
            }

            public FakeOut.V Voice(Func<FakeOut.V, bool> f) => o.v.FirstOrDefault(x => x.on && f(x));
            public FakeOut.V Regular => Voice(x => x.t.pool == MusicPool.Calm || x.t.pool == MusicPool.Battle);
            public FakeOut.V AudibleOf(MusicPool p) => Voice(x => x.t.pool == p && o.Audible(x));
            public float LevelOf(Func<MusicTrack, bool> f) { float m = 0f; foreach (var x in o.v) if (o.Audible(x) && f(x.t)) m = Math.Max(m, x.norm); return m; }
            public float DramaLevel => LevelOf(tr => tr.pool == MusicPool.Drama);
            /// <summary>The summed power of the audible voices of that kind, their fades only (the duck divided out).</summary>
            public float PowerOf(Func<MusicTrack, bool> f) { float p = 0f; foreach (var x in o.v) if (o.Audible(x) && f(x.t)) p += x.fade * x.fade; return p; }
            public float RegularLevel => LevelOf(tr => tr.pool == MusicPool.Calm || tr.pool == MusicPool.Battle);

            /// <summary>Music switches (entering ToDrama or ToRegular) after `from`.</summary>
            public int Switches(double from = 0) => layers.Count(l => l.t >= from && (l.l == MusicLayer.ToDrama || l.l == MusicLayer.ToRegular));
            /// <summary>Regular interludes: from each fade back (ToRegular) to the next drama (ToDrama), before `until`.</summary>
            public List<double> Interludes(double from = 0, double until = double.MaxValue)
            {
                var r = new List<double>(); double back = -1;
                foreach (var l in layers)
                {
                    if (l.t < from) continue;
                    if (l.t > until) break;
                    if (l.l == MusicLayer.ToRegular) back = l.t;
                    else if (l.l == MusicLayer.ToDrama && back >= 0) { r.Add(l.t - back); back = -1; }
                    else if (l.l == MusicLayer.Off || l.l == MusicLayer.End) back = -1;
                }
                return r;
            }
            /// <summary>The most switches inside any `window` seconds.</summary>
            public int MaxSwitchesIn(double window)
            {
                var ts = layers.Where(l => l.l == MusicLayer.ToDrama || l.l == MusicLayer.ToRegular).Select(l => l.t).ToList();
                int best = 0; for (int a = 0, b = 0; b < ts.Count; b++) { while (ts[b] - ts[a] > window) a++; best = Math.Max(best, b - a + 1); }
                return best;
            }
            /// <summary>Drama lengths (ToDrama -> the next ToRegular).</summary>
            public List<double> Dramas(double from = 0)
            {
                var r = new List<double>(); double on = -1;
                foreach (var l in layers)
                {
                    if (l.t < from) continue;
                    if (l.l == MusicLayer.ToDrama && on < 0) on = l.t;
                    else if (l.l == MusicLayer.ToRegular && on >= 0) { r.Add(l.t - on); on = -1; }
                }
                return r;
            }
        }

        // ============================================================================================================ scenarios
        public sealed class Result { public string name, story; public bool pass; public readonly List<string> notes = new List<string>(); public string detail; }
        static Result R(string n, string story) => new Result { name = n, story = story, pass = true };
        static void Expect(Result r, bool ok, string what) { r.notes.Add((ok ? "  ok   " : "  FAIL ") + what); if (!ok) r.pass = false; }
        static void Finish(Result r, Sim s, bool withEvents)
        {
            Expect(r, s.o.violations.Count == 0, $"frame checks: {s.o.violations.Count} violations (max audible {s.maxAudible}, longest overlap {s.maxDual:0.0} s, max voices held incl. paused {s.maxHeld}, steals {s.core.Steals}, steepest fade {s.o.maxFadeSlope:0.00} / s, steepest Zod duck {s.maxZodSlope:0.0} dB/s)");
            foreach (var v in s.o.violations.Take(12)) r.notes.Add("         " + v);
            Expect(r, s.core.Steals == 0, "no voice stolen");
            Expect(r, s.core.BackToBack == 0, "no track twice in a row");
            var sb = new StringBuilder();
            if (s.timeline.Length > 0) sb.Append(s.timeline);
            if (withEvents) foreach (var e in s.o.events) sb.AppendLine("      " + e);
            r.detail = sb.ToString();
        }
        static void Also(Result r, params Sim[] more)
        {
            foreach (var m in more)
            {
                if (m.o.violations.Count > 0) { r.pass = false; r.notes.Add($"  FAIL frame checks of a second run: {m.o.violations.Count} violations"); r.notes.AddRange(m.o.violations.Take(6).Select(v => "         " + v)); }
                if (m.core.Steals + m.core.BackToBack > 0) { r.pass = false; r.notes.Add($"  FAIL second run: steals {m.core.Steals}, back-to-back {m.core.BackToBack}"); }
            }
        }
        static string F(IEnumerable<double> xs) => string.Join(", ", xs.Select(x => x.ToString("0.0", CultureInfo.InvariantCulture)));

        static MusicCoreConfig Cfg() => new MusicCoreConfig();
        const float BigScore = 9f; const int BigUnits = 5;

        static Result OneUnit(IList<MusicTrack> cat)
        {
            var r = R("1 unit near a fort", "a lone heavy tank (5 pts, 1 unit) shoots our fort for 60 s, then one grunt squad (3 pts, 3 units) for 60 s, then a jeep + a robot (3 pts, 2 units) at their gate for 60 s: never the drama");
            var s = new Sim(cat, Cfg(), 11);
            s.Run(20);
            s.Ours(5f, 1, true); s.Run(60);
            s.Ours(3f, 3, true); s.Run(60);
            s.Ours(0, 0, false); s.Theirs(3f, 2, true); s.Run(60);
            Expect(r, s.core.DramaStarts == 0 && s.o.plays.All(p => p.track.pool != MusicPool.Drama), $"no drama (drama starts {s.core.DramaStarts})");
            Expect(r, s.core.Layer == MusicLayer.Regular, "regular music all along");
            Finish(r, s, false);
            return r;
        }

        static Result BigAttackResume(IList<MusicTrack> cat)
        {
            var r = R("big attack -> drama -> attack stops -> RESUME", "calm music 40 s; a big attack (9 pts, 5 units, hitting) on our fort for 60 s; then nobody: the drama fades in over the paused regular song; ~12 s after the attack stops the same regular song unpauses at its exact sample");
            var s = new Sim(cat, Cfg(), 21) { printEvery = 0.5 };
            s.Run(40);
            var reg0 = s.Regular; string regName = reg0?.t.name; double posAtAttack = reg0?.pos ?? -1;
            s.Ours(BigScore, BigUnits, true);
            s.Run(60);
            var parked = s.Voice(x => x.paused && x.t.pool != MusicPool.Drama);
            var dramaPlay = s.o.plays.FirstOrDefault(p => p.track.pool == MusicPool.Drama);
            Expect(r, dramaPlay.track != null && dramaPlay.t >= 40 + s.cfg.enterHold - 0.05 && dramaPlay.t <= 40 + s.cfg.enterHold + s.cfg.toDramaDelay + 0.1,
                   $"drama starts {dramaPlay.t - 40:0.00} s after the attack (hold {s.cfg.enterHold} s + {s.cfg.toDramaDelay} s crossfade offset): {dramaPlay.track?.name} @ {dramaPlay.at:0.0} s");
            Expect(r, dramaPlay.track != null, $"the drama is a track of the drama pool: {dramaPlay.track?.name} (any of them: the pick is a shuffle bag)");
            Expect(r, parked != null && parked.t.name == regName, $"the regular song {regName} is PAUSED (kept) under the drama at {parked?.pausedPos:0.000} s (it was at {posAtAttack:0.0} s when the attack began)");
            double pausedAt = parked?.pausedPos ?? -1;
            s.Ours(0, 0, false);
            double stopT = s.t;
            s.Run(40);
            var res = s.o.resumes.FirstOrDefault(x => x.track != null && x.track.name == regName);
            Expect(r, res.track != null, $"{regName} UNPAUSES ({s.o.resumes.Count} unpause)");
            Expect(r, res.track != null && Math.Abs(res.at - pausedAt) < 1e-9 && res.err == 0, $"at the same sample: paused {pausedAt:0.000} s, unpaused {res.at:0.000} s, error {res.err * 44100:0} samples");
            Expect(r, res.track != null && res.t - stopT >= s.cfg.exitHold && res.t - stopT <= s.cfg.exitHold + s.cfg.toRegularDelay + 0.2, $"back {res.t - stopT:0.0} s after the attack stopped (calm {s.cfg.exitHold} s + {s.cfg.toRegularDelay} s)");
            Expect(r, !s.o.plays.Any(p => p.t > stopT && p.track.pool != MusicPool.Drama), "no new regular song started (resume, not restart)");
            Expect(r, s.core.Layer == MusicLayer.Regular, "regular again");
            Finish(r, s, true);
            return r;
        }

        static Result Flapping(IList<MusicTrack> cat)
        {
            var r = R("attack flapping and the regular interlude", "hot 2 s / quiet 2 s x 20: no drama; hot 4 s / quiet 4 s x 20: ONE drama that never flaps out; quiet: one fade back; inside the 45 s interlude a 6 s and a 10 s big attack do NOT bring the drama back; a HEAVY attack (14 pts, 6 units, hitting) after 25 s does; after its fade back a big attack only after the 45 s");
            var s = new Sim(cat, Cfg(), 31);
            s.Run(15);
            for (int k = 0; k < 20; k++) { s.Ours(BigScore, BigUnits, true); s.Run(2); s.Ours(0, 0, false); s.Run(2); }
            Expect(r, s.core.DramaStarts == 0, $"2 s bursts: no drama ({s.core.DramaStarts})");
            for (int k = 0; k < 20; k++) { s.Ours(BigScore, BigUnits, true); s.Run(4); s.Ours(0, 0, false); s.Run(4); }
            Expect(r, s.core.DramaStarts == 1 && s.core.DramaEnds == 0 && s.core.Layer == MusicLayer.Drama, $"4 s bursts: one drama, still on (starts {s.core.DramaStarts}, ends {s.core.DramaEnds}, {s.core.Layer})");
            s.stopWhen = x => x.core.Layer == MusicLayer.ToRegular;
            s.Run(60);
            double back = s.t;
            Expect(r, s.core.DramaEnds == 1, $"one fade back after the flapping ({s.core.DramaEnds})");
            s.Run(5); s.Ours(BigScore, BigUnits, true); s.Run(6); s.Ours(0, 0, false); s.Run(4);
            Expect(r, s.core.DramaStarts == 1, $"a 6 s big attack {5:0} s after the fade back: no drama (interlude)");
            s.Ours(BigScore, BigUnits, true); s.Run(8); s.Ours(0, 0, false); s.Run(2);
            Expect(r, s.core.DramaStarts == 1, $"an 8 s big attack {s.t - back - 10:0} s after the fade back: no drama (interlude)");
            s.Ours(14f, 6, true); s.Run(6);
            double heavyAt = s.layers.LastOrDefault(l => l.l == MusicLayer.ToDrama).t - back;
            Expect(r, s.core.DramaStarts == 2 && heavyAt >= s.cfg.heavyInterlude - 0.05, $"a heavy attack (14 pts, 6 units, hitting) re-enters {heavyAt:0.0} s after the fade back (>= {s.cfg.heavyInterlude} s)");
            s.Ours(0, 0, false);
            s.stopWhen = x => x.core.Layer == MusicLayer.ToRegular; s.Run(90);
            double back2 = s.t;
            s.Run(20); s.Ours(BigScore, BigUnits, true); s.Run(30);
            double third = s.layers.LastOrDefault(l => l.l == MusicLayer.ToDrama).t - back2;
            Expect(r, s.core.DramaStarts == 3 && third >= s.cfg.cooldown - 0.05, $"a big attack 20 s after the second fade back waits for the interlude: drama {third:0.0} s after it (>= {s.cfg.cooldown} s)");
            s.Ours(0, 0, false); s.Run(90);
            Expect(r, s.core.DramaStarts == 3 && s.core.DramaEnds == 3, $"exactly 3 dramas, 3 fade backs; interludes {F(s.Interludes())} s");
            Finish(r, s, false);
            return r;
        }

        static Result Waves(IList<MusicTrack> cat, string lv)
        {
            var r = R($"attack waves {lv}", "10 min of attack waves at 20-90 s cadence (big: 8 pts, 4 units, hitting; heavy: 14 pts, 6 units): every regular interlude >= 45 s (>= 25 s heavy), never < 10 s, at most 10 music switches in 10 min, every drama >= 30 s; a siege of waves ends up in ONE long drama");
            var cfg = Cfg();
            var rows = new List<string>();
            foreach (var (w, p, heavy) in new[] { (3.5, 20.0, false), (4.0, 30.0, false), (5.5, 35.0, false), (6.0, 36.0, false), (6.0, 40.0, false), (8.0, 45.0, false), (5.5, 50.0, false),
                                                  (10.0, 60.0, false), (15.0, 75.0, false), (20.0, 90.0, false), (6.0, 36.0, true), (10.0, 45.0, true), (8.0, 60.0, true) })
            {
                var s = new Sim(cat, cfg, 705 + (int)(p * 10) + (heavy ? 1 : 0), 1f / 30f); s.o.keepEvents = false;
                s.Run(30); double t0 = s.t;
                while (s.t - t0 < 600) { if (heavy) s.Ours(14f, 6, true); else s.Ours(8f, 4, true); s.Run(w); s.Ours(0, 0, false); s.Run(p - w); }
                var inter = s.Interludes(t0); var dr = s.Dramas(t0); int sw = s.Switches(t0);
                double min = inter.Count > 0 ? inter.Min() : 999, need = heavy ? cfg.heavyInterlude : cfg.cooldown;
                bool ok = min >= need - 0.05 && sw <= 10 && (dr.Count == 0 || dr.Min() >= cfg.minDrama - 0.05) && s.o.violations.Count == 0 && s.core.Steals == 0 && s.core.BackToBack == 0;
                rows.Add($"{(ok ? "ok  " : "FAIL")} {(heavy ? "HEAVY" : "big  ")} waves {w:0.#} s every {p:0} s: {sw} switches, dramas {F(dr)} s, interludes {(inter.Count > 0 ? F(inter) : "none")} s{(s.o.violations.Count > 0 ? $", {s.o.violations.Count} frame violations: {s.o.violations[0]}" : "")}");
                if (!ok) r.pass = false;
            }
            foreach (var x in rows) r.notes.Add("    " + x);
            return r;
        }

        static Result IdleNearFort(IList<MusicTrack> cat)
        {
            var r = R("idle units near a fort never hold the drama", "(a) a big attack on THEIR fort ends; one light tank of ours idles 16-20 tiles from it (3 pts, not engaged) for 10 min; (b) the same with an enemy squad (3 robots) idling 18 tiles from OUR fort; (c) 4 idle enemy tanks parked near our fort (12 pts, not engaged) for 10 min, no attack: never a drama");
            var a = new Sim(cat, Cfg(), 802); a.Run(30);
            a.Theirs(12f, 7, true); a.Run(60); a.Theirs(0, 0, false); a.theirIdle = 3f; double stopA = a.t; a.Run(600);
            var backA = a.layers.FirstOrDefault(l => l.l == MusicLayer.ToRegular).t - stopA;
            Expect(r, a.core.DramaEnds == 1 && a.core.DramaStarts == 1 && backA > 0 && backA <= a.cfg.exitHold + 0.5, $"(a) one fade back {backA:0.0} s after the attack stopped, no drama after it ({a.core.DramaStarts} / {a.core.DramaEnds})");
            var b = new Sim(cat, Cfg(), 803); b.Run(30);
            b.Theirs(12f, 7, true); b.Run(60); b.Theirs(0, 0, false); b.ourIdle = 3f; double stopB = b.t; b.Run(600);
            var backB = b.layers.FirstOrDefault(l => l.l == MusicLayer.ToRegular).t - stopB;
            Expect(r, b.core.DramaEnds == 1 && backB > 0 && backB <= b.cfg.exitHold + 0.5, $"(b) one fade back {backB:0.0} s after the attack stopped");
            var c = new Sim(cat, Cfg(), 804); c.ourIdle = 12f; c.Run(600);
            Expect(r, c.core.DramaStarts == 0, $"(c) parked tanks alone: no drama ({c.core.DramaStarts})");
            Finish(r, a, false); Also(r, b, c);
            return r;
        }

        static Result LoneShelling(IList<MusicTrack> cat)
        {
            var r = R("a lone unit left shelling never holds the drama", "a big attack on our fort 60 s, then ONE light tank (3 pts, 1 unit) keeps hitting the fort for 10 min: the drama fades back 60 s after the last big-attack reading (hotLapse), never returns, the regular song resumes at its sample");
            var s = new Sim(cat, Cfg(), 811); s.Run(30); string reg = s.Regular?.t.name;
            s.Ours(BigScore, BigUnits, true); s.Run(60); s.Ours(3f, 1, true); double stop = s.t; s.Run(600);
            var back = s.layers.FirstOrDefault(l => l.l == MusicLayer.ToRegular).t - stop;
            Expect(r, s.core.DramaEnds == 1 && s.core.DramaStarts == 1 && Math.Abs(back - s.cfg.hotLapse) <= 0.5, $"fade back {back:0.0} s after the attack became one unit (hotLapse {s.cfg.hotLapse} s); dramas {s.core.DramaStarts}");
            Expect(r, s.o.resumes.Any(x => x.track.name == reg && x.err == 0), $"{reg} resumed at its sample");
            Finish(r, s, false);
            return r;
        }

        static Result Dropouts(IList<MusicTrack> cat)
        {
            var r = R("single cold readings never miss an attack", "a 66 s big attack (9 pts, 5 units, hitting) with ONE cold 0.25 s reading every 3 s: the drama within 4.5 s; with a cold 0.25 s reading every 1 s (75 % hot): within 6 s; 2 s on / 2 s off: never");
            var a = new Sim(cat, Cfg(), 821); a.Run(30); double t0 = a.t;
            for (int k = 0; k < 22; k++) { a.Ours(BigScore, BigUnits, true); a.Run(2.75); a.Ours(0, 0, false); a.Run(0.25); }
            var inA = a.layers.FirstOrDefault(l => l.l == MusicLayer.ToDrama).t - t0;
            Expect(r, a.core.DramaStarts == 1 && inA > 0 && inA <= 4.5, $"cold every 3 s: drama {inA:0.00} s after the attack began ({a.core.DramaStarts} drama)");
            var b = new Sim(cat, Cfg(), 822); b.Run(30); double t1 = b.t;
            for (int k = 0; k < 66; k++) { b.Ours(BigScore, BigUnits, true); b.Run(0.75); b.Ours(0, 0, false); b.Run(0.25); }
            var inB = b.layers.FirstOrDefault(l => l.l == MusicLayer.ToDrama).t - t1;
            Expect(r, b.core.DramaStarts == 1 && inB > 0 && inB <= 6.0, $"cold every 1 s: drama {inB:0.00} s after the attack began");
            var c = new Sim(cat, Cfg(), 823); c.Run(30);
            for (int k = 0; k < 30; k++) { c.Ours(BigScore, BigUnits, true); c.Run(2); c.Ours(0, 0, false); c.Run(2); }
            Expect(r, c.core.DramaStarts == 0, "2 s on / 2 s off: no drama");
            Finish(r, a, false); Also(r, b, c);
            return r;
        }

        static Result DramaChain(IList<MusicTrack> cat)
        {
            var r = R("a drama track ends mid-attack", "a 20 min siege of their fort: each drama track crossfades at its tail into a DIFFERENT drama track, never silence, never the same one twice in a row; then the regular song resumes");
            var s = new Sim(cat, Cfg(), 41);
            s.Run(20);
            string regName = s.Regular?.t.name;
            s.Theirs(12f, 7, true); s.Run(20 * 60);
            var dr = s.o.plays.Where(p => p.track.pool == MusicPool.Drama).Select(p => p.track.name).ToList();
            bool diff = true; for (int k = 1; k < dr.Count; k++) if (dr[k] == dr[k - 1]) diff = false;
            Expect(r, dr.Count >= 5, $"{dr.Count} drama tracks in 20 min: {string.Join(" > ", dr)}");
            Expect(r, diff, "each one different from the previous");
            bool spaced = true; for (int k = 2; k < dr.Count; k++) if (dr[k] == dr[k - 2]) spaced = false;
            Expect(r, spaced, "and never the one before that either (no A B A)");
            int n = cat.Count(t => t.pool == MusicPool.Drama);
            Expect(r, dr.Take(n).Distinct().Count() == Math.Min(n, dr.Count), $"the first {n} are all different (every drama track once per cycle)");
            s.Theirs(0, 0, false); s.Run(40);
            Expect(r, s.o.resumes.Any(x => x.track.name == regName && x.err == 0), $"{regName} resumed after 20 min of drama");
            Finish(r, s, false);
            return r;
        }

        static Result DramaPick(IList<MusicTrack> cat, string lv)
        {
            var r = R($"drama picks {lv}", "the drama pool is a shuffle bag that outlives the match: 60 matches in a row, each a new MusicCore that gets the bag of the match before (as the host does); a drama is never the dramatic track played last, also across matches, and every track plays once before one repeats");
            var drama = cat.Where(t => t.pool == MusicPool.Drama).ToList();
            int runs = 0, weaker = 0, picks = 0; var firsts = new Dictionary<string, int>();
            string last = "", bag = ""; var seq = new List<string>();
            for (int seed = 0; seed < 60; seed++)
            {
                var s = new Sim(cat, Cfg(), 3000 + seed, 1f / 30f); s.o.keepEvents = false;
                s.core.DramaLast = last; s.core.DramaBag = bag;                                      // what the host reads from the saved settings
                s.inp.battle = seed % 2 == 1;
                s.Run(20 + seed % 7 * 13);
                for (int k = 0; k < 4; k++)
                {
                    var regT = s.Regular?.t;
                    int before = s.o.plays.Count;
                    s.Ours(BigScore, BigUnits, true); s.Run(40); s.Ours(0, 0, false); s.Run(200);
                    var play = s.o.plays.Skip(before).FirstOrDefault(p => p.track.pool == MusicPool.Drama);
                    if (play.track == null) continue;
                    if (k == 0) { runs++; firsts[play.track.name] = firsts.TryGetValue(play.track.name, out var q) ? q + 1 : 1; }
                    if (regT != null && play.track.intensity < regT.intensity - s.cfg.dramaIntensityTol) weaker++;
                }
                seq.AddRange(s.o.plays.Where(p => p.track.pool == MusicPool.Drama).Select(p => p.track.name));
                picks += s.core.DramaPicks; last = s.core.DramaLast; bag = s.core.DramaBag;         // what the host saves
                if (s.o.violations.Count > 0 || s.core.Steals + s.core.BackToBack > 0) { r.pass = false; r.notes.Add($"  FAIL seed {seed}: {s.o.violations.FirstOrDefault()}"); }
            }
            r.notes.Add($"         drama intensities: {string.Join(", ", drama.OrderByDescending(t => t.intensity).Select(t => $"{t.name} {t.intensity:+0.00;-0.00}"))}");
            int n = drama.Count, repeats = 0, badRounds = 0;
            for (int k = 1; k < seq.Count; k++) if (seq[k] == seq[k - 1]) repeats++;
            for (int k = 0; k + n <= seq.Count; k += n) if (seq.Skip(k).Take(n).Distinct().Count() != n) badRounds++;
            Expect(r, runs > 0 && picks == seq.Count && seq.Count >= n, $"{seq.Count} dramas started in {runs} matches with a drama ({picks} picks, every pick was played)");
            Expect(r, repeats == 0, $"the same drama track twice in a row: {repeats} times (within a match and from one match to the next)");
            Expect(r, badRounds == 0, $"every round of {n} picks holds each of the {n} drama tracks once ({seq.Count / Math.Max(1, n)} full rounds, {badRounds} with a repeat)");
            Expect(r, n < 2 || firsts.Count >= 2, $"the first drama of a match changes from match to match: {string.Join(", ", firsts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " x" + kv.Value))}");
            r.notes.Add($"         dramas weaker than the song they replace: {weaker} (the pick is random; the intensity no longer steers it)");
            return r;
        }

        static Result WinDuringDrama(IList<MusicTrack> cat)
        {
            var r = R("win during drama", "a big attack on their fort; the fort falls (game over, win) 60 s in; the drama plays on through the nuke and the celebration; the end screen shows 18 s later: the drama fades out 2.5 s under the 7.3 s victory sting, the victory lap fades in 1 s before the sting ends, then the menu songs");
            var s = new Sim(cat, Cfg(), 51) { printEvery = 1.0 };
            s.Run(20);
            s.Theirs(BigScore, BigUnits, true); s.Run(60);
            string d0 = s.AudibleOf(MusicPool.Drama)?.t.name;
            s.inp.gameOver = true; s.inp.result = 1; s.theirFortAlive = false; s.Theirs(0, 0, false);
            double over = s.t;
            float minNorm = 9f;
            for (int k = 0; k < 18 * 60; k++) { s.Run(1f / 60f); var d = s.AudibleOf(MusicPool.Drama); minNorm = Math.Min(minNorm, d != null ? d.norm : 0f); }
            Expect(r, minNorm > 0.9f || s.o.plays.Count(p => p.track.pool == MusicPool.Drama && p.t > over) > 0, $"the drama plays on from the fall to the end screen (lowest drama level {minNorm:0.00}; {d0} ...)");
            s.inp.endScreen = true; s.inp.stingSeconds = 7.3f;
            double shown = s.t;
            s.Run(200);
            var end = s.o.plays.Where(p => p.t > shown).ToList();
            Expect(r, end.Count > 0 && end[0].track.pool == MusicPool.Victory, $"end music = victory lap: {end.FirstOrDefault().track?.name}");
            Expect(r, end.Count > 0 && Math.Abs(end[0].t - shown - (7.3 - s.cfg.endLead)) < 0.05, $"it starts {end.FirstOrDefault().t - shown:0.00} s after the end screen (sting 7.3 s - {s.cfg.endLead} s)");
            Expect(r, end.Count > 1 && end[1].track.pool == MusicPool.Menu, $"then the menu songs: {end.Skip(1).FirstOrDefault().track?.name}");
            Expect(r, !s.o.plays.Any(p => p.t > over && p.t < shown && p.track.pool != MusicPool.Drama), "no regular / end music between the fall and the end screen");
            Finish(r, s, true);
            return r;
        }

        static Result LossDuringDrama(IList<MusicTrack> cat)
        {
            var r = R("loss during drama", "a big attack on our fort, it falls (loss) 50 s in; the drama plays on 15 s to the end screen; the drama fades 2.5 s under the 4.4 s defeat sting; the menu songs (-6 dB) come in 1 s before the sting ends");
            var s = new Sim(cat, Cfg(), 61);
            s.Run(20);
            s.Ours(BigScore, BigUnits, true); s.Run(50);
            s.inp.gameOver = true; s.inp.result = -1; s.ourFortAlive = false; s.Ours(0, 0, false);
            double over = s.t;
            s.Run(15);
            Expect(r, s.AudibleOf(MusicPool.Drama) != null && s.AudibleOf(MusicPool.Drama).norm > 0.9f, "drama at full level 15 s after the fall");
            s.inp.endScreen = true; s.inp.stingSeconds = 4.4f; double shown = s.t;
            s.Run(2.6);
            Expect(r, s.AudibleOf(MusicPool.Drama) == null, "the drama is gone 2.6 s after the end screen");
            s.Run(60);
            var end = s.o.plays.Where(p => p.t > shown).ToList();
            Expect(r, end.Count > 0 && end[0].track.pool == MusicPool.Menu && Math.Abs(end[0].t - shown - 3.4) < 0.05, $"menu music {end.FirstOrDefault().track?.name} at +{end.FirstOrDefault().t - shown:0.00} s (4.4 - 1)");
            Expect(r, !s.o.plays.Any(p => p.t > over && p.t < shown && p.track.pool != MusicPool.Drama), "only the drama between the fall and the end screen");
            Finish(r, s, false);
            return r;
        }

        static Result GameOverEdges(IList<MusicTrack> cat)
        {
            var r = R("game over edges", "(a) calm music, a sneaky win: the regular song plays on to the end screen (no drama swell); (b) the drama's fade back began 0.5 s before the win: the drama comes back and plays to the end screen; 3 s / 6 s before: the regular song plays on (no drama -> regular -> drama); (c) a big attack hot 1.5 s at the fall: the drama from the fall; (d) dramaOnGameOver on: the drama 2.5 s after the fall, none when the end screen comes first (skip at 1.3 s / 2.0 s)");
            var cfg = Cfg();
            var a = new Sim(cat, cfg, 71); a.Run(100); string regA = a.Regular?.t.name;
            a.inp.gameOver = true; a.inp.result = 1; a.Run(1.3);
            a.inp.endScreen = true; a.inp.stingSeconds = 7.3f; double sa = a.t; a.Run(30);
            Expect(r, !a.o.plays.Any(p => p.track.pool == MusicPool.Drama) && a.o.plays.Any(p => p.t > sa && p.track.pool == MusicPool.Victory), $"(a) no drama at all ({regA} to the end screen, skip at 1.3 s), then the victory lap");
            var runs = new List<Sim> { a };
            foreach (var before in new[] { 0.5, 3.0, 6.0 })
            {
                var s = new Sim(cat, cfg, 511); runs.Add(s);
                s.Run(30); string reg = s.Regular?.t.name; s.Ours(BigScore, BigUnits, true); s.Run(40); s.Ours(0, 0, false);
                s.stopWhen = q => q.core.Layer == MusicLayer.ToRegular; s.Run(60);
                s.Run(before);
                float dAt = s.DramaLevel;
                s.inp.gameOver = true; s.inp.result = 1; s.theirFortAlive = false; double over = s.t;
                float minD = 9f, maxD = 0f, maxR = 0f; int regPlaysAfter = s.o.plays.Count;
                for (int k = 0; k < 15 * 60; k++)
                {
                    s.Run(1f / 60f);
                    if (s.t - over >= 3.0) { minD = Math.Min(minD, s.DramaLevel); maxR = Math.Max(maxR, s.RegularLevel); }
                    maxD = Math.Max(maxD, s.t - over >= 0.1 ? s.DramaLevel : 0f);
                }
                float regAtShow = s.RegularLevel;
                s.inp.endScreen = true; s.inp.stingSeconds = 7.3f; s.Run(20);
                bool newDrama = s.o.plays.Skip(regPlaysAfter).Any(p => p.track.pool == MusicPool.Drama) || s.o.resumes.Any(x => x.t > over && x.track.pool == MusicPool.Drama);
                if (before < 1.0)
                    Expect(r, s.core.EndDrama && minD >= 0.99f && maxR <= 0.001f, $"(b) fade back {before:0.0} s before the win (drama at {dAt:0.00}): the drama comes back: from +3 s drama {minD:0.00}, regular {maxR:0.00}");
                else
                    Expect(r, !s.core.EndDrama && !newDrama && maxD <= dAt + 0.001f && regAtShow >= 0.99f, $"(b) fade back {before:0.0} s before the win (drama at {dAt:0.00}): no drama again (drama never above {maxD:0.00} after the fall; the regular at {regAtShow:0.00} when the end screen shows)");
            }
            var c = new Sim(cat, cfg, 73); runs.Add(c);
            c.Run(60); c.Ours(BigScore, BigUnits, true); c.Run(1.5); c.inp.gameOver = true; c.inp.result = -1; c.ourFortAlive = false; c.Run(16);
            Expect(r, c.AudibleOf(MusicPool.Drama) != null && c.core.EndDrama, "(c) the attack hot 1.5 s at the fall: the drama plays from the fall");
            c.inp.endScreen = true; c.inp.stingSeconds = 4.4f; c.Run(30);
            Expect(r, c.o.plays.Last().track.pool == MusicPool.Menu, "(c) menu music at the end");
            var dc = Cfg(); dc.dramaOnGameOver = true;
            foreach (var skip in new[] { 1.3, 2.0, 6.0 })
            {
                var d = new Sim(cat, dc, 74); runs.Add(d);
                d.Run(60); d.inp.gameOver = true; d.inp.result = 1; double over = d.t; d.Run(skip);
                d.inp.endScreen = true; d.inp.stingSeconds = 7.3f; d.Run(20);
                var dp = d.o.plays.FirstOrDefault(p => p.track.pool == MusicPool.Drama);
                if (skip < dc.gameOverDramaDelay) Expect(r, dp.track == null, $"(d) dramaOnGameOver, end screen {skip:0.0} s after the fall: no drama started");
                else Expect(r, dp.track != null && dp.t - over >= dc.gameOverDramaDelay - 0.02 && dp.t - over <= dc.gameOverDramaDelay + dc.toDramaDelay + 0.1, $"(d) dramaOnGameOver, end screen {skip:0.0} s after the fall: the drama started {dp.t - over:0.00} s after the fall ({dc.gameOverDramaDelay} s + the {dc.toDramaDelay} s crossfade offset)");
            }
            Finish(r, a, false); Also(r, runs.Skip(1).ToArray());
            return r;
        }

        static Result ZodDuck(IList<MusicTrack> cat, bool inDrama)
        {
            var r = R(inDrama ? "Zod speaks during drama" : "Zod speaks during regular music",
                      (inDrama ? "during a drama, " : "during calm music, ") + "Zod's window is up for 8 s: the music dips very subtly (-2.5 dB), eased in over 0.8 s and back over 1.5 s; a Rachel line (-6 dB) inside it wins, never summed" + (inDrama ? "; then Zod speaks through the fade back (no jumps)" : ""));
            var s = new Sim(cat, Cfg(), inDrama ? 82 : 81) { printEvery = 0.25 };
            s.Run(inDrama ? 10 : 30);
            if (inDrama) { s.Ours(BigScore, BigUnits, true); s.Run(30); }
            Func<FakeOut.V> cur = () => inDrama ? s.AudibleOf(MusicPool.Drama) : s.Regular;
            float before = cur().norm;
            s.inp.zodSpeaking = true;
            var db = new List<(double t, float db)>();
            for (int k = 0; k < 8 * 60; k++) { s.Run(1f / 60f); db.Add((s.t, (float)(20 * Math.Log10(cur().norm / before)))); }
            float at04 = db[(int)(0.4 * 60)].db, deep = db.Skip(60).Min(x => x.db);
            s.otherDuckTarget = -6f; s.Run(2); float withRachel = (float)(20 * Math.Log10(cur().norm / before)); s.otherDuckTarget = 0f;
            s.Run(4);
            s.inp.zodSpeaking = false;
            var rel = new List<float>();
            for (int k = 0; k < 3 * 60; k++) { s.Run(1f / 60f); rel.Add((float)(20 * Math.Log10(cur().norm / before))); }
            float maxSlope = 0f; for (int k = 1; k < db.Count; k++) maxSlope = Math.Max(maxSlope, Math.Abs(db[k].db - db[k - 1].db) * 60f);
            for (int k = 1; k < rel.Count; k++) maxSlope = Math.Max(maxSlope, Math.Abs(rel[k] - rel[k - 1]) * 60f);
            Expect(r, Math.Abs(deep - s.cfg.zodDuckDb) < 0.05f, $"depth {deep:0.00} dB (target {s.cfg.zodDuckDb} dB)");
            Expect(r, at04 > -1.6f && at04 < -0.6f, $"eased: {at04:0.00} dB after 0.4 s, full after 0.8 s");
            Expect(r, Math.Abs(withRachel + 6f) < 0.1f, $"Rachel inside Zod: {withRachel:0.00} dB (the deepest wins, not -8.5)");
            Expect(r, rel[(int)(0.75 * 60)] > -1.6f && rel[(int)(0.75 * 60)] < -0.8f && Math.Abs(rel[(int)(1.6 * 60)]) < 0.05f, $"release: {rel[(int)(0.75 * 60)]:0.00} dB after 0.75 s, {rel[(int)(1.6 * 60)]:0.00} dB after 1.6 s");
            Expect(r, maxSlope <= 5.0f, $"never abrupt: steepest {maxSlope:0.0} dB/s");
            if (inDrama)
            {
                s.inp.zodSpeaking = true; s.Ours(0, 0, false);
                s.stopWhen = x => x.core.Layer == MusicLayer.Regular; s.Run(60);
                s.inp.zodSpeaking = false; s.Run(5);
                Expect(r, s.o.resumes.Count >= 1, "the fade back under Zod's line still resumes the regular song");
            }
            Finish(r, s, false);
            return r;
        }

        static Result ZodPause(IList<MusicTrack> cat)
        {
            var r = R("Zod's duck through the pause menu", "Zod talks 3 s, the pause menu opens (ZodBriefing reads not-speaking while paused) for 30 s: the duck releases during the pause (0 dB within 1.6 s), never stays ducked; he talks again after the pause: eased in again");
            var s = new Sim(cat, Cfg(), 612); s.Run(30);
            s.inp.zodSpeaking = true; s.Run(3);
            float dStart = s.core.ZodDuck.Db(s.cfg.zodDuckDb);
            s.inp.paused = true; s.inp.zodSpeaking = false; s.Run(1.6);
            float d16 = s.core.ZodDuck.Db(s.cfg.zodDuckDb); s.Run(28.4);
            float d30 = s.core.ZodDuck.Db(s.cfg.zodDuckDb);
            s.inp.paused = false; s.inp.zodSpeaking = true; s.Run(0.4); float dIn = s.core.ZodDuck.Db(s.cfg.zodDuckDb); s.Run(3); s.inp.zodSpeaking = false; s.Run(2);
            Expect(r, Math.Abs(dStart - s.cfg.zodDuckDb) < 0.01f && d16 > -0.01f && d30 > -0.01f, $"ducked {dStart:0.00} dB when the pause opened, {d16:0.00} dB 1.6 s into it, {d30:0.00} dB after 30 s");
            Expect(r, dIn < -0.5f && dIn > -1.8f && s.core.ZodDuck.amount == 0f, $"after the pause: eased in again ({dIn:0.00} dB after 0.4 s), released after");
            Finish(r, s, false);
            return r;
        }

        static Result RapidFortSwitch(IList<MusicTrack> cat)
        {
            var r = R("rapid fort switching", "a big attack on our fort for 40 s, then at once on THEIR fort for 40 s (ours quiet), then on ours again for 20 s: ONE drama that carries on (no restart, no second drama; a drama track that ends crossfades into the next one), then one fade back and the resume");
            var s = new Sim(cat, Cfg(), 91);
            s.Run(20); string regName = s.Regular?.t.name;
            s.Ours(BigScore, BigUnits, true); s.Run(40);
            string d1 = s.AudibleOf(MusicPool.Drama)?.t.name;
            s.Ours(0, 0, false); s.Theirs(10f, 6, true); s.Run(40);
            s.Theirs(0, 0, false); s.Ours(8f, 4, true); s.Run(20);
            string d3 = s.AudibleOf(MusicPool.Drama)?.t.name;
            Expect(r, s.core.DramaStarts == 1 && s.core.DramaEnds == 0, $"one drama through both forts (starts {s.core.DramaStarts}, ends {s.core.DramaEnds})");
            int dPlays = s.o.plays.Count(p => p.track.pool == MusicPool.Drama);
            Expect(r, dPlays == 1 || d1 == d3 || s.o.events.Count(e => e.Contains("(drama (next))")) == dPlays - 1, $"the drama carries on: {d1} -> {d3} ({dPlays} drama tracks, every change at a track's end)");
            s.Ours(0, 0, false); s.Run(40);
            Expect(r, s.core.DramaEnds == 1 && s.o.resumes.Any(x => x.track.name == regName), $"one fade back, {regName} resumed");
            Finish(r, s, false);
            return r;
        }

        static Result PauseUnpause(IList<MusicTrack> cat)
        {
            var r = R("pause / unpause", "the pause menu mid-crossfade (to drama) and during the drama with nobody left: the fades finish on real time (music plays low-passed in the pause menu), the detector is frozen (the drama never ends while paused); after unpausing the calm 12 s count starts");
            var s = new Sim(cat, Cfg(), 101);
            s.Run(20); string regName = s.Regular?.t.name;
            s.Ours(BigScore, BigUnits, true);
            s.stopWhen = x => x.core.Layer == MusicLayer.ToDrama; s.Run(10);
            s.Run(1.0);
            s.inp.paused = true; s.Run(10); s.inp.paused = false;
            Expect(r, s.core.Layer == MusicLayer.Drama && s.AudibleOf(MusicPool.Drama) != null, "the crossfade finished during the pause, one voice");
            s.Run(30);
            s.Ours(0, 0, false); s.Run(0.5);
            s.inp.paused = true; s.Run(90);
            Expect(r, s.core.Layer == MusicLayer.Drama && s.core.DramaEnds == 0, "90 s paused with nobody at the fort: still drama (detector frozen)");
            s.inp.paused = false; double un = s.t;
            s.stopWhen = x => x.core.Layer == MusicLayer.ToRegular; s.Run(60);
            Expect(r, s.t - un >= s.cfg.exitHold - 0.6 && s.t - un <= s.cfg.exitHold + 0.1, $"the fade back {s.t - un:0.0} s after unpausing");
            s.Run(10);
            Expect(r, s.o.resumes.Any(x => x.track.name == regName && x.err == 0), $"{regName} resumed at its sample");
            Finish(r, s, false);
            return r;
        }

        static Result GameSpeed(IList<MusicTrack> cat)
        {
            var r = R("game speed 2x", "the sim runs at 2x: the enter hold and the hit windows count SIM seconds (enter 3 sim s = 1.5 s, like the fort's hit record), the calm hold counts on both clocks (never sooner than 12 s in either: 12 s real at 2x), the musical minimums count real seconds (drama >= 30 s, interlude >= 45 s)");
            var s = new Sim(cat, Cfg(), 131) { speed = 2f };
            s.Run(30); double a = s.t;
            s.Ours(BigScore, BigUnits, true); s.Run(40); s.Ours(0, 0, false); double stop = s.t; s.Run(40);
            var into = s.layers.FirstOrDefault(l => l.l == MusicLayer.ToDrama).t - a;
            var back = s.layers.FirstOrDefault(l => l.l == MusicLayer.ToRegular).t - stop;
            Expect(r, Math.Abs(into - 1.5) < 0.1, $"drama {into:0.00} s (real) after the attack began (3 sim s)");
            Expect(r, Math.Abs(back - 12.0) < 0.2, $"fade back {back:0.00} s (real) after the attack stopped (12 s on both clocks)");
            s.Ours(BigScore, BigUnits, true); s.Run(30); double second = s.layers.LastOrDefault(l => l.l == MusicLayer.ToDrama).t - (stop + back);
            Expect(r, s.core.DramaStarts == 2 && second >= s.cfg.cooldown - 0.05, $"the next drama {second:0.0} s (real) after the fade back (interlude {s.cfg.cooldown} s real)");
            Finish(r, s, false);
            return r;
        }

        static Result MusicOff(IList<MusicTrack> cat)
        {
            var r = R("music off (recording takes) and toggled mid-drama", "oz_music off for a whole match with a big attack, a win and the end screen: not one Play; toggled off during a drama (all silent within 0.6 s) and on again: the SAME drama unpauses where it was, and after the attack the regular song that was playing before the drama resumes; off / on during the regular song and on the end screen: the same song / end music resumes (nothing restarts)");
            var s = new Sim(cat, Cfg(), 111); s.inp.enabled = false;
            s.Run(30); s.Ours(BigScore, BigUnits, true); s.Run(60); s.inp.gameOver = true; s.inp.result = 1; s.Run(15); s.inp.endScreen = true; s.inp.stingSeconds = 7.3f; s.Run(30);
            Expect(r, s.o.plays.Count == 0 && s.o.resumes.Count == 0, $"music off: {s.o.plays.Count} Play, {s.o.resumes.Count} unpause");
            var t = new Sim(cat, Cfg(), 112);
            t.Run(20); string reg = t.Regular?.t.name; t.Ours(BigScore, BigUnits, true); t.Run(30);
            string dr = t.AudibleOf(MusicPool.Drama)?.t.name; int playsBefore = t.o.plays.Count;
            t.inp.enabled = false; t.Run(0.7);
            Expect(r, t.o.v.All(x => !t.o.Audible(x)), "toggle off mid-drama: silent after 0.7 s");
            t.Run(10); t.inp.enabled = true; t.Run(5);
            Expect(r, t.AudibleOf(MusicPool.Drama)?.t.name == dr && t.o.plays.Count == playsBefore && t.o.resumes.Any(x => x.track.name == dr && x.err == 0), $"toggle on during the attack: {dr} unpaused where it was (no new Play)");
            t.Ours(0, 0, false); t.Run(40);
            Expect(r, t.core.Layer == MusicLayer.Regular && t.Regular?.t.name == reg && t.o.resumes.Any(x => x.track.name == reg && x.err == 0), $"then {reg} (the song before the drama) resumed, not a new song");
            var u = new Sim(cat, Cfg(), 113);
            u.Run(40); string reg2 = u.Regular?.t.name; int p2 = u.o.plays.Count;
            u.inp.enabled = false; u.Run(8); u.inp.enabled = true; u.Run(5);
            Expect(r, u.Regular?.t.name == reg2 && u.o.plays.Count == p2 && u.o.resumes.Any(x => x.track.name == reg2), $"off / on during {reg2}: it resumed where it was");
            var e = new Sim(cat, Cfg(), 114);
            e.Run(30); e.inp.gameOver = true; e.inp.result = 1; e.Run(18); e.inp.endScreen = true; e.inp.stingSeconds = 7.3f; e.Run(40);
            string lap = e.o.plays.Last().track.name; int p3 = e.o.plays.Count;
            e.inp.enabled = false; e.Run(3); e.inp.enabled = true; e.Run(10);
            Expect(r, e.o.plays.Count == p3 && e.o.resumes.Any(x => x.track.name == lap) && e.core.Layer == MusicLayer.End, $"off / on on the end screen: {lap} resumed (no new end sequence)");
            Finish(r, t, false); Also(r, s, u, e);
            return r;
        }

        static Result RegularPlaylist(IList<MusicTrack> cat)
        {
            var r = R("regular playlist 40 min + mood switches", "40 min of play with Zod's calm / battle mood flipping every 1-3 min: songs crossfade at their tails, own and added songs alternate, never one twice in a row, a mood left within 3 min comes back where it was, no drama");
            var s = new Sim(cat, Cfg(), 121);
            var rnd = new Random(5);
            double end = 40 * 60;
            while (s.t < end) { s.Run(60 + rnd.Next(120)); s.inp.battle = !s.inp.battle; }
            var seq = s.o.plays.Where(p => p.track.pool == MusicPool.Calm || p.track.pool == MusicPool.Battle).ToList();
            int alt = 0, same = 0;
            for (int k = 1; k < seq.Count; k++) if (seq[k].track.pool == seq[k - 1].track.pool) { if (seq[k].track.zopen != seq[k - 1].track.zopen) alt++; else same++; }
            var names = seq.Select(p => p.track.name).ToList();
            int calmHeard = names.Distinct().Count(n => cat.Any(t => t.name == n && t.pool == MusicPool.Calm)), calmAll = cat.Count(t => t.pool == MusicPool.Calm);
            double zShare = seq.Sum(p => p.track.zopen ? 1.0 : 0.0) / Math.Max(1, seq.Count);
            Expect(r, seq.Count >= 15, $"{seq.Count} songs started, {calmHeard}/{calmAll} calm songs heard");
            Expect(r, same <= alt / 4 + 2, $"sources alternate within a mood: {alt} alternations, {same} same-source neighbours (mood resumes / guests)");
            Expect(r, s.o.events.Count(e => e.Contains("back where it was") || e.Contains("continues at")) > 0, "a mood switch back continued a song where it was left");
            Expect(r, s.core.DramaStarts == 0, "no drama");
            r.notes.Add($"         added songs = {zShare * 100:0} % of the starts");
            Finish(r, s, false);
            return r;
        }

        static Result MoodFrozenDuringDrama(IList<MusicTrack> cat)
        {
            var r = R("mood frozen during drama, 20 s grace after", "calm song; a big attack; Zod's mood turns to battle during the drama; after the attack the CALM song resumes (what was playing), and the switch to battle waits 20 s");
            var s = new Sim(cat, Cfg(), 131);
            s.Run(30); string calm = s.Regular?.t.name;
            s.Ours(BigScore, BigUnits, true); s.Run(10); s.inp.battle = true; s.Run(30);
            s.Ours(0, 0, false);
            s.stopWhen = x => x.core.Layer == MusicLayer.Regular; s.Run(60);
            double back = s.t;
            Expect(r, s.o.resumes.Any(x => x.track.name == calm), $"{calm} resumed although the mood is battle");
            s.stopWhen = x => x.Voice(v => v.t.pool == MusicPool.Battle) != null; s.Run(60);
            Expect(r, s.t - back >= s.cfg.moodGrace - 0.1 && s.t - back <= s.cfg.moodGrace + 0.5, $"battle music {s.t - back:0.0} s after the return (grace {s.cfg.moodGrace} s)");
            Finish(r, s, false);
            return r;
        }

        static Result TailThenDrama(IList<MusicTrack> cat)
        {
            var r = R("drama during a playlist crossfade", "the attack lands while two regular songs crossfade at a tail: the old tail is sped out (0.6 s) before the drama comes in, the NEW song is the one paused and resumed");
            var s = new Sim(cat, Cfg(), 141);
            s.Run(5);
            var first = s.Regular.t; float x0 = Math.Max(2.5f, Math.Min(8f, Math.Min(first.tail, first.length * 0.25f)));
            s.stopWhen = q => q.Regular != null && q.Regular.t == first && first.length - q.Regular.pos <= x0 + s.cfg.enterHold - 1.0; s.Run(900);
            s.Ours(BigScore, BigUnits, true);
            s.stopWhen = q => q.core.Layer == MusicLayer.ToDrama; s.Run(10);
            int regAud = s.o.v.Count(v => s.o.Audible(v) && v.t.pool != MusicPool.Drama);
            Expect(r, regAud == 2, $"the attack hit mid-crossfade ({regAud} regular voices sounding: {first.name} out, the next song in)");
            s.Run(40);
            var parkedV = s.Voice(v => v.paused && v.t.pool != MusicPool.Drama);
            var kept = parkedV?.t;
            Expect(r, kept != null && kept != first, $"the new song {kept?.name} is kept (the old {first.name} faded out)");
            s.Ours(0, 0, false); s.Run(40);
            Expect(r, kept != null && s.o.resumes.Any(q => q.track == kept && q.err == 0), $"and {kept?.name} resumes at its sample");
            Finish(r, s, false);
            return r;
        }

        static Result DramaResume(IList<MusicTrack> cat)
        {
            var r = R("a second attack within 3 min", "attack 20 s, 60 s of peace, attack again: the paused drama track continues where it was (drama resume window 180 s); after 4 min of peace a new drama track starts instead");
            var s = new Sim(cat, Cfg(), 151);
            s.Run(20); s.Ours(BigScore, BigUnits, true); s.Run(20);
            string d1 = s.AudibleOf(MusicPool.Drama)?.t.name; s.Ours(0, 0, false); s.Run(60);
            s.Ours(BigScore, BigUnits, true); s.Run(20);
            Expect(r, s.o.resumes.Any(x => x.track.name == d1 && x.err == 0), $"{d1} continued at its sample");
            s.Ours(0, 0, false); s.Run(240);
            string last = s.o.plays.LastOrDefault(p => p.track.pool == MusicPool.Drama).track?.name; int before = s.o.plays.Count, res0 = s.o.resumes.Count;
            s.Ours(BigScore, BigUnits, true); s.Run(20);
            var np = s.o.plays.Skip(before).FirstOrDefault(p => p.track.pool == MusicPool.Drama);
            Expect(r, np.track != null && np.track.name != last && !s.o.resumes.Skip(res0).Any(x => x.track.pool == MusicPool.Drama), $"after 4 min a fresh drama track: {np.track?.name} (not a resume, not {last} again)");
            Finish(r, s, false);
            return r;
        }

        /// <summary>Debug: the random match with this index keeps its event log and prints it (harness --debug-random).</summary>
        public static int DebugMatch = -1; public static Action<string> DebugLog;

        static Result RandomMatches(IList<MusicTrack> cat, string level, int matches, double minutes)
        {
            var r = R($"random matches {level}", $"{matches} matches x {minutes:0} min at 30 fps: random moods, raids of every size on both forts (lone units, squads, sieges, attack WAVES at 20-70 s cadence, dropouts of single readings, idle units parked near a fort), Zod lines, Rachel / Alan ducks, pauses, music toggles, game speed 1x / 2x, then a win / loss / draw and the end screen 15-24 s later: every frame check holds, every interlude >= 25 s, at most 12 switches in any 10 min");
            var rnd = new Random(level == "L02" ? 2 : 17);
            int viol = 0, starts = 0, b2b = 0, steals = 0, maxSw = 0; double resumeErr = 0; var dramasPer = new List<int>(); double dramaSecs = 0; int maxAud = 0, maxHeld = 0; double maxDual = 0, maxFade = 0;
            var inter = new List<double>(); var firstViol = new List<string>();
            var cfg = Cfg();
            for (int m = 0; m < matches; m++)
            {
                var s = new Sim(cat, cfg, 1000 + m, 1f / 30f); s.o.keepEvents = m == DebugMatch;
                if (rnd.NextDouble() < 0.15) s.speed = 2f;
                double end = minutes * 60;
                double nextMood = 30 + rnd.Next(150), nextRaid = 60 + rnd.Next(300), raidEnd = -1, nextZod = 20 + rnd.Next(200), zodEnd = -1, nextPause = 200 + rnd.Next(900), pauseEnd = -1, nextDuck = 10 + rnd.Next(60), duckEnd = -1;
                double nextIdle = 100 + rnd.Next(600), idleEnd = -1;
                bool toggles = rnd.NextDouble() < 0.1; double nextToggle = 300 + rnd.Next(1200);
                int raidFort = 1; float raidScore = 0; int raidUnits = 0; double flicker = 0; bool waves = false; double wavePeriod = 0, waveOn = 0, waveT0 = 0;
                while (s.t < end)
                {
                    if (s.t >= nextMood) { s.inp.battle = !s.inp.battle; nextMood = s.t + 30 + rnd.Next(150); }
                    if (s.t >= nextRaid && raidEnd < 0)
                    {
                        raidFort = rnd.Next(2) + 1; double kind = rnd.NextDouble(); waves = false;
                        if (kind < 0.30) { raidScore = rnd.Next(1, 6); raidUnits = 1; }                 // one unit (a lone tank / a scout)
                        else if (kind < 0.48) { raidScore = rnd.Next(2, 5); raidUnits = rnd.Next(2, 4); } // a squad
                        else if (kind < 0.75) { raidScore = rnd.Next(6, 16); raidUnits = rnd.Next(2, 9); } // a real attack
                        else { raidScore = rnd.Next(6, 16); raidUnits = rnd.Next(3, 8); waves = true; wavePeriod = 20 + rnd.Next(50); waveOn = 3 + rnd.Next(Math.Max(1, (int)(wavePeriod * 0.5))); waveT0 = s.t; }
                        raidEnd = s.t + (waves ? 120 + rnd.Next(360) : 10 + rnd.Next(kind < 0.48 ? 40 : 180));
                    }
                    if (raidEnd >= 0)
                    {
                        bool dip = rnd.NextDouble() < 0.03; if (dip) flicker = s.t + 1 + rnd.Next(4);
                        float sc = s.t < flicker ? raidScore * 0.4f : raidScore;
                        bool on = !waves || (s.t - waveT0) % wavePeriod < waveOn;
                        bool drop = rnd.NextDouble() < 0.08;                                              // a single cold reading (a unit re-targets)
                        bool hit = rnd.NextDouble() < 0.7;
                        if (!on || drop) { if (raidFort == 1) s.Ours(0, 0, false); else s.Theirs(0, 0, false); }
                        else if (raidFort == 1) s.Ours(sc, raidUnits, hit); else s.Theirs(sc, raidUnits, hit);
                        if (s.t >= raidEnd) { s.Ours(0, 0, false); s.Theirs(0, 0, false); raidEnd = -1; nextRaid = s.t + 30 + rnd.Next(400); }
                    }
                    if (s.t >= nextIdle && idleEnd < 0) { if (rnd.Next(2) == 0) s.ourIdle = rnd.Next(2, 13); else s.theirIdle = rnd.Next(2, 13); idleEnd = s.t + 60 + rnd.Next(300); }
                    if (idleEnd >= 0 && s.t >= idleEnd) { s.ourIdle = s.theirIdle = 0f; idleEnd = -1; nextIdle = s.t + 100 + rnd.Next(600); }
                    if (s.t >= nextZod && zodEnd < 0) { s.inp.zodSpeaking = true; zodEnd = s.t + 4 + rnd.Next(12); }
                    if (zodEnd >= 0 && s.t >= zodEnd) { s.inp.zodSpeaking = false; zodEnd = -1; nextZod = s.t + 60 + rnd.Next(300); }
                    if (s.t >= nextDuck && duckEnd < 0) { s.otherDuckTarget = rnd.NextDouble() < 0.5 ? -6f : -4f; duckEnd = s.t + 1 + rnd.Next(3); }
                    if (duckEnd >= 0 && s.t >= duckEnd) { s.otherDuckTarget = 0f; duckEnd = -1; nextDuck = s.t + 5 + rnd.Next(60); }
                    if (s.t >= nextPause && pauseEnd < 0) { s.inp.paused = true; pauseEnd = s.t + 3 + rnd.Next(60); }
                    if (pauseEnd >= 0 && s.t >= pauseEnd) { s.inp.paused = false; pauseEnd = -1; nextPause = s.t + 200 + rnd.Next(900); }
                    if (toggles && s.t >= nextToggle) { s.inp.enabled = !s.inp.enabled; nextToggle = s.t + (s.inp.enabled ? 300 + rnd.Next(900) : 5 + rnd.Next(40)); }
                    s.Run(0.25);
                }
                s.inp.paused = false; s.inp.enabled = true;
                s.inp.gameOver = true; s.inp.result = rnd.Next(3) - 1; if (s.inp.result == 1) s.theirFortAlive = false; else if (s.inp.result == -1) s.ourFortAlive = false;
                double overAt = s.t;                                                                // (a drama whose fade back had just begun comes back at the game over: not an interlude)
                s.Run(15 + rnd.Next(10));
                float dp = s.PowerOf(tr => tr.pool == MusicPool.Drama), rp = s.PowerOf(tr => tr.pool == MusicPool.Calm || tr.pool == MusicPool.Battle);
                bool dramaAtShow = dp >= 0.8f;                                                        // (two drama tracks may be crossfading: their power)
                if (dramaAtShow != s.core.EndDrama) { viol++; if (firstViol.Count < 10) firstViol.Add($"match {m}: at the end screen drama power {dp:0.00} but the game-over latch said {s.core.EndDrama}"); }
                if (!s.core.EndDrama && rp < 0.8f) { viol++; if (firstViol.Count < 10) firstViol.Add($"match {m}: no drama and the regular music at power {rp:0.00} at the end screen"); }
                s.inp.endScreen = true; s.inp.stingSeconds = s.inp.result == 1 ? 7.3f : s.inp.result == -1 ? 4.4f : 0f;
                s.Run(60);
                if (!s.o.v.Any(v => s.o.Audible(v) && (v.t.pool == MusicPool.Menu || v.t.pool == MusicPool.Victory))) { viol++; if (firstViol.Count < 10) firstViol.Add($"match {m}: no end music"); }
                var mi = s.Interludes(0, overAt); inter.AddRange(mi);
                if (mi.Count > 0 && mi.Min() < cfg.heavyInterlude - 0.05) { viol++; if (firstViol.Count < 10) firstViol.Add($"match {m}: regular interlude {mi.Min():0.0} s"); }
                int sw = s.MaxSwitchesIn(600); maxSw = Math.Max(maxSw, sw);
                if (sw > 12) { viol++; if (firstViol.Count < 10) firstViol.Add($"match {m}: {sw} music switches within 10 min"); }
                viol += s.o.violations.Count; foreach (var v in s.o.violations) if (firstViol.Count < 10) firstViol.Add($"match {m}: {v}");
                starts += s.core.Starts; b2b += s.core.BackToBack; steals += s.core.Steals; resumeErr = Math.Max(resumeErr, s.o.maxResumeErr);
                dramasPer.Add(s.core.DramaStarts); maxAud = Math.Max(maxAud, s.maxAudible); maxHeld = Math.Max(maxHeld, s.maxHeld); maxDual = Math.Max(maxDual, s.maxDual); maxFade = Math.Max(maxFade, s.o.maxFadeSlope);
                dramaSecs += s.Dramas().Sum();
                if (m == DebugMatch && DebugLog != null) { DebugLog($"== {level} match {m}: layers " + string.Join(" ", s.layers.Select(l => $"{l.l}@{l.t:0.0}"))); foreach (var e in s.o.events) DebugLog(e); }
            }
            Expect(r, viol == 0, $"{viol} violations over {matches * minutes / 60:0} h of play (max audible {maxAud}, longest overlap {maxDual:0.0} s, max voices held incl. paused {maxHeld}, steepest fade {maxFade:0.00} / s)");
            foreach (var v in firstViol) r.notes.Add("         " + v);
            Expect(r, b2b == 0, $"{starts} track starts, {b2b} back-to-back repeats");
            Expect(r, steals == 0, $"{steals} voice steals");
            Expect(r, resumeErr == 0, $"worst unpause position error {resumeErr * 44100:0} samples");
            inter.Sort();
            Expect(r, inter.Count == 0 || inter[0] >= cfg.heavyInterlude - 0.05, $"{inter.Count} regular interludes between two dramas: shortest {(inter.Count > 0 ? inter[0] : 0):0.0} s, under 45 s {inter.Count(x => x < cfg.cooldown - 0.05)} (heavy attacks), under 10 s {inter.Count(x => x < 10)}, median {(inter.Count > 0 ? inter[inter.Count / 2] : 0):0} s");
            Expect(r, maxSw <= 12, $"most music switches in any 10 min: {maxSw}");
            double avg = dramasPer.Average();
            r.notes.Add($"         dramas per {minutes:0} min match: avg {avg:0.0}, min {dramasPer.Min()}, max {dramasPer.Max()} (random raid mix, not a balance measurement); avg drama {dramaSecs / Math.Max(1, dramasPer.Sum()):0} s");
            return r;
        }

        static Result NoDramaClips(IList<MusicTrack> cat)
        {
            var r = R("no drama clips (a table without mus_fort_danger clips)", "a 10 min siege and a win with an empty drama pool: the regular playlist simply goes on (tails, no silence), then the end music");
            var s = new Sim(cat.Where(t => t.pool != MusicPool.Drama).ToList(), Cfg(), 161);
            s.Run(20); s.Ours(12f, 6, true); s.Run(600); s.inp.gameOver = true; s.inp.result = 1; s.Run(15); s.inp.endScreen = true; s.inp.stingSeconds = 7.3f; s.Run(20);
            Expect(r, s.core.DramaStarts == 0 && s.o.plays.Count(p => p.track.pool != MusicPool.Victory && p.track.pool != MusicPool.Menu) >= 3, $"{s.o.plays.Count} songs, no drama");
            Expect(r, s.o.plays.Last().track.pool == MusicPool.Victory, "victory lap at the end");
            Finish(r, s, false);
            return r;
        }

        // ============================================================================================================ run
        public static bool RunAll(IList<MusicTrack> l02, IList<MusicTrack> l17, Action<string> log, int randomMatches = 200, double minutes = 40)
        {
            var results = new List<Result>();
            void Do(Func<Result> f)
            {
                Result res;
                try { res = f(); } catch (Exception ex) { res = R(f.Method.Name, "threw"); res.pass = false; res.notes.Add("  FAIL exception " + ex); }
                results.Add(res);
            }
            var cat = l02;
            Do(() => OneUnit(cat));
            Do(() => BigAttackResume(cat));
            Do(() => Flapping(cat));
            Do(() => Waves(l02, "L02"));
            Do(() => Waves(l17, "L17"));
            Do(() => IdleNearFort(l17));
            Do(() => LoneShelling(l02));
            Do(() => Dropouts(l02));
            Do(() => DramaChain(cat));
            Do(() => DramaPick(l02, "L02"));
            Do(() => DramaPick(l17, "L17"));
            Do(() => WinDuringDrama(cat));
            Do(() => LossDuringDrama(cat));
            Do(() => GameOverEdges(cat));
            Do(() => ZodDuck(cat, false));
            Do(() => ZodDuck(cat, true));
            Do(() => ZodPause(l17));
            Do(() => RapidFortSwitch(l17));
            Do(() => PauseUnpause(l17));
            Do(() => GameSpeed(l17));
            Do(() => MusicOff(l17));
            Do(() => RegularPlaylist(l17));
            Do(() => MoodFrozenDuringDrama(l17));
            Do(() => TailThenDrama(l02));
            Do(() => DramaResume(l17));
            Do(() => NoDramaClips(l02));
            if (randomMatches > 0) { Do(() => RandomMatches(l02, "L02", randomMatches, minutes)); Do(() => RandomMatches(l17, "L17", randomMatches, minutes)); }
            var sb = new StringBuilder();
            sb.AppendLine("music_v3 MusicCore scenarios");
            foreach (var res in results)
            {
                sb.AppendLine($"{(res.pass ? "PASS" : "FAIL")}  {res.name}");
                sb.AppendLine($"      {res.story}");
                foreach (var n in res.notes) sb.AppendLine("    " + n);
            }
            int fails = results.Count(x => !x.pass);
            sb.AppendLine(fails == 0 ? $"MUSIC V3 SCENARIOS OK ({results.Count}/{results.Count} PASS)" : $"MUSIC V3 SCENARIOS FAIL ({fails} of {results.Count})");
            log(sb.ToString());
            var det = new StringBuilder();
            foreach (var res in results) if (!string.IsNullOrEmpty(res.detail)) { det.AppendLine("===== " + res.name + " (timeline / events)"); det.Append(res.detail); }
            log("DETAIL\n" + det);
            return fails == 0;
        }
    }
}
