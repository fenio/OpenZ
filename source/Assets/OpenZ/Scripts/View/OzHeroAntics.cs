using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads (View/OzHeroSquadView.cs), two view-only jobs that run while a level with a hero squad plays:
    ///  1. The hero squad's own antic rule (<see cref="OzHeroAnticPlan"/>): their standing idle animation stays a grunt's; their ANTIC
    ///     is ONLY the drinking antic (a rocket fuel can), every OzHeroAnticPlan.Period s while a member stands idle with no enemy
    ///     around (AnticDirector.StartThreatM, missiles as the game's antics), each member on his own and staggered (Brad first, Alan a
    ///     few seconds later). The game's antic director gives them nothing else (its scan skips them: <see cref="OwnRule"/>; the
    ///     level-start lounger lie stays as built). A drink starts through the director's own start (AnticDirector.Force: the kit,
    ///     the props, the can throw, the sounds), and the game's own cancel ends it at once on an order, a hit, a selection or an
    ///     enemy coming near. A drink due while the member is off screen is skipped (its 30 s run on), so a camera arriving later never
    ///     finds both starting together.
    ///  2. Play counters for the checks: hero antics started by kind, and the selection after a robot gets into a vehicle, gun, troop
    ///     carrier or fort (PlayerInput keeps the squad's next member selected: a selection entry that has left the map must be gone
    ///     within a frame or two, and the hero left outside must still be selected).
    /// View only: UnityEngine.Random, nothing written to the sim.
    /// </summary>
    [DefaultExecutionOrder(59)]
    public sealed class OzHeroAntics : MonoBehaviour
    {
        /// <summary>The hero squad's own antic rule (off: the heroes get the game's antic list like any grunt).</summary>
        public static bool Enabled = true;
        public static float ScanEvery = 0.5f;
        public static OzHeroAntics I { get; private set; }

        /// <summary>AnticDirector's scan hook: true = this robot's antics are the hero rule's, the director starts none on it.</summary>
        public static bool OwnRule(ZObject o) => Enabled && o != null && o.IsHero;

        // counters (OzHeroSquadView.Report)
        public static int Drinks, DrinksRefused, DueOffScreen, OtherAntics, Threatened;
        public static readonly int[] DrinksBy = new int[4];
        public static readonly List<string> Log = new List<string>();
        public static int GhostEntries, GhostStale, GhostMaxFrames, PartnerKept, PartnerLost;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            I = null; Drinks = DrinksRefused = DueOffScreen = OtherAntics = Threatened = 0; System.Array.Clear(DrinksBy, 0, DrinksBy.Length); Log.Clear();
            GhostEntries = GhostStale = GhostMaxFrames = PartnerKept = PartnerLost = 0;
        }

        public static OzHeroAntics Ensure()
        {
            if (I) return I;
            I = FindFirstObjectByType<OzHeroAntics>();
            if (!I) I = new GameObject("OzHeroAntics").AddComponent<OzHeroAntics>();
            return I;
        }

        void Awake() { if (I && I != this) { Destroy(this); return; } I = this; }
        void OnDestroy() { if (I == this) I = null; }

        static void Say(string s) { if (Log.Count >= 120) Log.RemoveAt(0); Log.Add($"{Time.time:F1} {s}"); Debug.Log("[OpenZ HeroSquad] " + s); }

        sealed class St { public float idleSince = -1f, lastStart = -1f, jit; public AnticRun seen; }
        readonly Dictionary<int, St> state = new Dictionary<int, St>();
        readonly List<(Vector3 at, Team owner)> threats = new List<(Vector3, Team)>();
        readonly List<int> gone = new List<int>();
        float scanT;

        void Update()
        {
            var g = ZGame.I;
            if (!g || g.World == null) return;
            if ((scanT += Time.deltaTime) < ScanEvery) return;
            scanT = 0f;
            float now = Time.time;
            bool run = Enabled && AnticDirector.Enabled && UnitView.AnticsEnabled && !g.paused && g.isActiveAndEnabled && !g.World.GameOver;
            Threats(g);
            gone.Clear(); foreach (var id in state.Keys) gone.Add(id);
            foreach (var v in g.Views.Values)
            {
                if (!v || v.Obj == null || !v.Obj.IsHero || v.Obj.Kind != ZKind.Robot) continue;
                gone.Remove(v.Obj.Id);
                if (!state.TryGetValue(v.Obj.Id, out var s)) state[v.Obj.Id] = s = new St { idleSince = now, jit = Random.value };
                var a = v.Antic;
                if (a != null)
                {
                    if (a != s.seen)
                    {   // a new antic on a hero: ours (a drink we started) or the level-start lie; anything else is counted (must stay 0)
                        s.seen = a;
                        if (a.Kind != AnticKind.Drink && !a.Hold) { OtherAntics++; Say($"#{v.Obj.Id} {v.Obj.HeroMemberName}: a {a.Kind} antic started (the hero rule allows only the drink)"); }
                    }
                    if (a.Kind != AnticKind.Drink) s.idleSince = now;                          // lying on the level-start lounger: not idle time
                    continue;
                }
                if (!v.isActiveAndEnabled || !v.CanAntic || v.AnticBusyReason() != null) { s.idleSince = now; continue; }
                if (Near(v.transform.position, v.Obj.Owner)) { s.idleSince = now; Threatened++; continue; }
                if (!run) continue;
                if (now < OzHeroAnticPlan.NextAt(s.idleSince, s.lastStart, Mathf.Max(0, v.Obj.HeroMember), s.jit)) continue;
                if (!WreckFx.OnScreen(v.transform.position, -0.05f))
                {   // due off screen: this cycle passes unseen, the next one is a full period later (never two catching up together)
                    s.lastStart = now; s.jit = Random.value; DueOffScreen++;
                    continue;
                }
                var r = AnticDirector.Force(v.Obj.Id, "Drink");
                if (r.StartsWith("started"))
                {
                    s.lastStart = now; s.jit = Random.value; s.seen = v.Antic;
                    Drinks++; DrinksBy[Mathf.Clamp(v.Obj.HeroMember, 0, DrinksBy.Length - 1)]++;
                    Say($"#{v.Obj.Id} {v.Obj.HeroMemberName}: drinks rocket fuel ({v.AnticIdleFor:0} s since his last antic or order)");
                }
                else { DrinksRefused++; s.lastStart = now; s.jit = Random.value; Say($"#{v.Obj.Id} {v.Obj.HeroMemberName}: no drink now ({r})"); }
            }
            foreach (var id in gone) state.Remove(id);
        }

        /// <summary>Enemy units that can shoot and landing missiles, as the game's antic start rule sees them (AnticDirector).</summary>
        void Threats(ZGame g)
        {
            threats.Clear(); missiles.Clear();
            foreach (var o in g.World.Objects)
                if (o != null && !o.Destroyed && o.IsUnit && o.Owner != Team.Neutral && o.CanAttack()) threats.Add((ZGame.PxToWorld(o.CenterX, o.CenterY), o.Owner));
            var pm = g.World.PendingMissiles;
            for (int i = 0; i < pm.Count; i++) { var m = pm[i]; if (m != null && !m.debris) missiles.Add(ZGame.PxToWorld(m.x, m.y)); }
        }
        readonly List<Vector3> missiles = new List<Vector3>();

        /// <summary>An enemy of <paramref name="team"/> within AnticDirector.StartThreatM, or a missile landing within MissileNearM.</summary>
        bool Near(Vector3 p, Team team)
        {
            float r = AnticDirector.StartThreatM, rm = AnticDirector.MissileNearM;
            foreach (var (q, owner) in threats) { if (owner == team) continue; float dx = q.x - p.x, dz = q.z - p.z; if (dx * dx + dz * dz < r * r) return true; }
            foreach (var q in missiles) { float dx = q.x - p.x, dz = q.z - p.z; if (dx * dx + dz * dz < rm * rm) return true; }
            return false;
        }

        // ------------------------------------------------------------------ Play counters: the selection after boarding
        PlayerInput input; float inputSearchAt;
        sealed class Ghost { public int frames, removedFrames; }
        readonly Dictionary<ZObject, Ghost> ghosts = new Dictionary<ZObject, Ghost>();
        readonly List<(int partner, string who, float until)> partnerChecks = new List<(int, string, float)>();
        readonly List<ZObject> drop = new List<ZObject>();

        void LateUpdate()
        {
            var g = ZGame.I; if (!g || g.World == null) return;
            if (!input && Time.unscaledTime >= inputSearchAt) { inputSearchAt = Time.unscaledTime + 1f; input = FindFirstObjectByType<PlayerInput>(); }
            if (!input) return;
            var w = g.World; var sel = input.Selection;
            drop.Clear(); foreach (var k in ghosts.Keys) if (!sel.Contains(k)) drop.Add(k);
            foreach (var k in drop) ghosts.Remove(k);
            for (int i = 0; i < sel.Count; i++)
            {
                var o = sel[i]; if (o == null || o.KillAt < 0 || o.Destroyed) continue;                // a selected robot getting into a vehicle, gun, troop carrier or fort
                if (!ghosts.TryGetValue(o, out var gh))
                {
                    ghosts[o] = gh = new Ghost(); GhostEntries++;
                    if (o.IsHero)
                    {   // the hero left outside must stay selected (PlayerInput follows the squad's next member)
                        var lead = o.Leader ?? o;
                        var mates = new List<ZObject>(); if (lead != o) mates.Add(lead); mates.AddRange(lead.Minions);
                        foreach (var m in mates)
                            if (m != null && m != o && m.IsHero && m.Hero == o.Hero && !m.Destroyed && m.KillAt < 0) partnerChecks.Add((m.Id, m.HeroMemberName, Time.unscaledTime + 1f));
                    }
                }
                gh.frames++; GhostMaxFrames = Mathf.Max(GhostMaxFrames, gh.frames);
                if (w.Get(o.Id) != o && ++gh.removedFrames == 2) GhostStale++;                     // gone from the world and still selected after PlayerInput's next pass
            }
            for (int i = partnerChecks.Count - 1; i >= 0; i--)
            {
                var c = partnerChecks[i]; if (Time.unscaledTime < c.until) continue;
                partnerChecks.RemoveAt(i);
                var p = w.Get(c.partner);
                if (p == null || p.Destroyed || p.KillAt >= 0) continue;                                   // he boarded too / died meanwhile: nothing to check
                bool kept = sel.Contains(p) || (p.Leader != null && sel.Contains(p.Leader));
                if (kept) PartnerKept++; else PartnerLost++;
                Say($"after boarding: {c.who} #{c.partner} outside {(kept ? "still selected" : "NOT selected")}");
            }
        }

        /// <summary>Play check lines (OzHeroSquadView.Report).</summary>
        public static string Report() =>
            $"hero antics: drinks {Drinks} (Brad {DrinksBy[0]}, Alan {DrinksBy[1]}), other antics on them {OtherAntics} (wanted 0), drinks due off screen {DueOffScreen}, refused {DrinksRefused}, scans with an enemy near {Threatened}; " +
            $"selection after boarding: entries off the map {GhostEntries}, still selected after removal {GhostStale} (wanted 0), longest {GhostMaxFrames} frame(s); hero left outside still selected {PartnerKept}, lost {PartnerLost} (wanted 0)";
    }
}
