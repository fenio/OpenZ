using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    ///  * Ctrl+Alt+W = the enemy fort falls now (win), Ctrl+Alt+L = our fort falls now (loss): OpenZVictory.DebugFortFalls, the real
    ///    chain (RulesView -> strike -> nuke, TeamEliminated, GameOver -> ending -> end screen); only the last kill is sped up;
    ///  * -ozend win|loss &lt;game seconds&gt;: the same at that match time (a real match on autopilot with -level L02 -demo first);
    ///  * -ozfast &lt;x&gt;: game speed x until 25 s before -ozend (the unrecorded lead-in), real Zod speed from then on;
    ///    vehicle (the one nearest the view centre), then it dies through the sim's own death path; Ctrl+Alt+K = the same at once for
    ///    the APC / crane nearest the view centre (the true-60 kill -> settled wreck take of the modelled APC / crane kit);
    ///  * -ozautopilot: a Zod CPU bot also plays OUR side in a normal match (not a bot match: Zod's briefing and the nuke
    ///    transmission with the button smash stay live, which -demo / the capture kit switch off), so a real fight runs unattended.
    /// Session only: nothing is saved.
    /// </summary>
    public class OzDevEnding : MonoBehaviour
    {
        public static OzDevEnding I { get; private set; }
        string endKind; double endAt = -1; float fast = 1f; bool fired, slowed, autopilot; Sim.ZWorld piloted;
        string killKind = "any"; double killAt = -1; int killId = -1; bool killFlown, killDone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I || Application.isEditor || !GameBoot.DevArg("-ozdev")) return;
            var go = new GameObject("OzDevEnding"); DontDestroyOnLoad(go); I = go.AddComponent<OzDevEnding>();
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Equals("-ozend", System.StringComparison.OrdinalIgnoreCase) && i + 2 < a.Length)
                { I.endKind = a[i + 1].ToLowerInvariant(); double.TryParse(a[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out I.endAt); }
                if (a[i].Equals("-ozfast", System.StringComparison.OrdinalIgnoreCase) && i + 1 < a.Length)
                    float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out I.fast);
                if (a[i].Equals("-ozkill", System.StringComparison.OrdinalIgnoreCase) && i + 2 < a.Length)
                { I.killKind = a[i + 1].ToLowerInvariant(); double.TryParse(a[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out I.killAt); }
            }
            I.fast = Mathf.Clamp(I.fast, 1f, 4f);
            I.autopilot = GameBoot.DevArg("-ozautopilot");
            Debug.Log($"[OpenZ] -ozdev ending driver: Ctrl+Alt+W win / Ctrl+Alt+L loss{(I.endAt > 0 ? $", {I.endKind} at {I.endAt:0} s game time" : "")}{(I.fast > 1f ? $", x{I.fast:0.#} until 25 s before" : "")}, cap {GameBoot.CapFps} fps{(I.killAt > 0 ? $", -ozkill {I.killKind} at {I.killAt:0} s game time" : "")}, Ctrl+Alt+K kills the APC / crane nearest the view centre");
        }

        void Update()
        {
            var g = ZGame.I; if (!g || g.World == null) return;
            if (autopilot && piloted != g.World)
            {   // our side on a Zod bot (normal difficulty), added once per world; the match stays a human-vs-CPU match for the view
                piloted = g.World;
                bool has = false; foreach (var b in g.World.Bots) if (b.Team == g.player) has = true;
                if (!has) g.World.Bots.Add(new Sim.ZBot(g.World, g.player, g.seed + 202) { Difficulty = Sim.ZBotDifficulty.Normal });
                Debug.Log($"[OpenZ] -ozautopilot: {g.player} plays on a Zod bot ({(has ? "already there" : "added")}), Zod transmissions stay live");
            }
            var kb = Keyboard.current;
            if (kb != null && kb.ctrlKey.isPressed && kb.altKey.isPressed)
            {
                if (kb.wKey.wasPressedThisFrame) Fall(true, "Ctrl+Alt+W");
                if (kb.lKey.wasPressedThisFrame) Fall(false, "Ctrl+Alt+L");
                if (kb.kKey.wasPressedThisFrame) KillNearest("any", "Ctrl+Alt+K");
            }
            TimedKill(g);
            if (endAt <= 0 || fired) return;
            double t = g.World.Time;
            if (fast > 1f && !slowed)
            {
                if (t < endAt - 25.0) g.gameSpeed = fast;
                else { g.gameSpeed = 1f; slowed = true; Debug.Log($"[OpenZ] -ozdev: real Zod speed from {t:0} s (the take window)"); }
            }
            if (t >= endAt) { fired = true; Fall(endKind != "loss", "-ozend at " + t.ToString("0")); }
        }

        /// <summary>
        /// 4 s before -ozkill the view flies to the APC / crane nearest the view centre
        /// (RtsCamera.FlyTo, Zod's own fly-to), at the time it dies through the sim's death path (DamageHealth + UpdateObjectHealth, as
        /// OpenZVictory.DebugFortFalls), so the real chain runs: Death event -> UnitView.Deaths -> WheeledBreakup.Tear (ApcCraneKit
        /// variant, torn plates, clanks, sink) with every sound of a normal kill. Logged "-ozdev KILL" for the take script.</summary>
        void TimedKill(ZGame g)
        {
            if (killAt <= 0 || killDone) return;
            double t = g.World.Time;
            if (!killFlown && t >= killAt - 4.0)
            {
                killFlown = true;
                var o = Nearest(killKind);
                if (o != null)
                {
                    killId = o.Id;
                    var cam = FindFirstObjectByType<RtsCamera>();
                    if (cam && g.Views.TryGetValue(o.Id, out var v) && v) cam.FlyTo(v.transform.position);
                }
                Debug.Log($"[OpenZ] -ozkill: {(o != null ? $"{o.Owner} {o.Type}#{o.Id} picked, the view flies to it" : "no " + killKind + " on the map")}");
            }
            if (t < killAt) return;
            killDone = true;
            Sim.ZObject target = null;
            foreach (var o in g.World.Objects) if (o != null && o.Id == killId) target = o;
            if (target == null || target.Destroyed || target.KillAt >= 0) target = Nearest(killKind);
            if (target != null) Kill(target, "-ozkill at " + t.ToString("0"));
            else Debug.Log($"[OpenZ] -ozkill at {t:0}: no {killKind} left to kill");
        }

        /// <summary>The living APC / crane ("apc", "crane", or "any" of both) nearest the view centre (RtsCamera.focus).</summary>
        static Sim.ZObject Nearest(string kind)
        {
            var g = ZGame.I; if (!g) return null;
            var cam = FindFirstObjectByType<RtsCamera>(); Vector3 c = cam ? cam.focus : Vector3.zero;
            Sim.ZObject best = null; float bd = float.MaxValue;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!v || v.Dying) continue;
                var o = v.Obj;
                if (o == null || o.Destroyed || o.KillAt >= 0 || o.Kind != Sim.ZKind.Vehicle) continue;
                bool want = kind == "apc" || kind == "crane" ? o.Type == kind : (o.Type == "apc" || o.Type == "crane");
                if (!want) continue;
                var p = v.transform.position; float d = (p.x - c.x) * (p.x - c.x) + (p.z - c.z) * (p.z - c.z);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        static void KillNearest(string kind, string why)
        {
            var o = Nearest(kind);
            if (o != null) Kill(o, why); else Debug.Log($"[OpenZ] -ozdev {why}: no APC / crane on the map");
        }

        static void Kill(Sim.ZObject o, string why)
        {
            var W = ZGame.I.World;
            o.DamageHealth(o.Health + 1); W.UpdateObjectHealth(o, -1);
            Debug.Log($"[OpenZ] -ozdev KILL {o.Owner} {o.Type}#{o.Id} ({why}) at tile ({o.CenterX >> 4},{o.CenterY >> 4}), game time {W.Time:0.0} s");
        }

        static void Fall(bool win, string why)
        {
            int n = OpenZVictory.DebugFortFalls(win);
            Debug.Log($"[OpenZ] -ozdev {(win ? "WIN" : "LOSS")} ({why}): {n} fort part(s) destroyed at game time {(ZGame.I && ZGame.I.World != null ? ZGame.I.World.Time : 0):0} s");
        }
    }
}
