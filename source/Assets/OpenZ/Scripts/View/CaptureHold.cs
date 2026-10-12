// OpenZ (GPL-3.0)
// View only, not a deviation: the sim keeps Zod's timing (RobotEnterObject, zserver.cpp:1375-1421: the robot is gone and the vehicle
// changes team in the tick its centre touches the vehicle; Entered + OwnerChanged events). BoardingView then plays the climb-in
// (ladder / hatch / seat / door, 0.7-1.3 s + up to 1 s round the hull, zobject budgets in BoardingView), and until now CombatView
// repainted the vehicle, started its engine and played the capture cue at once, AudioDirector had Rachel / the robot say "Vehicle
// captured" 0.35-0.8 s after the event, VehicleLights / UnitBlinkers lit it and LegacyEngineAudio started its engine loop: all before
// the robot sat down.
// CaptureHold keeps the vehicle looking and sounding like it did before the capture (grey, dark, silent) until its boarding
// animation ends (BoardingView.EndGhost: every robot of that boarding inside, or the animation aborted: the vehicle moved / fired,
// was destroyed or its view went away), then runs the queued actions (capture cue, engine start, voice lines) in order.
// Safety: a boarding with no animation (no BoardingView, no vehicle view) releases on the next frame; nothing waits longer than
// MaxWait seconds of sim time (the animation runs on sim time too: 4x bot match, pause).
using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public static class CaptureHold
    {
        /// <summary>Off = the old behaviour (everything at the sim's Entered event). PlayerPrefs oz_capture_after_board (ZGame).</summary>
        public static bool Enabled = true;
        /// <summary>Sim seconds a capture may be held at most (BoardingView's longest enter: 1.3 s + 1 s round the hull + APC stagger).</summary>
        public static float MaxWait = 4f;

        sealed class Hold { public ZObject v; public Team shown; public double since; public int frame; public bool ghost; public readonly List<Action> actions = new List<Action>(); }
        static readonly Dictionary<int, Hold> holds = new Dictionary<int, Hold>();
        static readonly Dictionary<int, Team> lastShown = new Dictionary<int, Team>();
        static int pumpFrame = -1;

        public static int Held, ReleasedByAnimation, ReleasedNoAnimation, ReleasedTimeout, ReleasedGone, ActionsRun;
        public static double LastHoldSeconds, MaxHoldSeconds;
        public static string Status() => $"CaptureHold {(Enabled ? "on" : "off")}: held {Held}, released by the animation {ReleasedByAnimation}, no animation {ReleasedNoAnimation}, " +
                                         $"timeout {ReleasedTimeout}, vehicle gone {ReleasedGone}; actions run {ActionsRun}; last hold {LastHoldSeconds:F2} s, max {MaxHoldSeconds:F2} s; open {holds.Count}";

        static ZWorld World => ZGame.I ? ZGame.I.World : null;

        /// <summary>A capture of vehicle / cannon v starts (the Entered event of a non-passenger boarding): it keeps showing `shown`
        /// (the owner before: neutral) until Boarded. Idempotent.</summary>
        public static void Begin(ZObject v, Team shown)
        {
            if (!Enabled || v == null || holds.ContainsKey(v.Id)) return;
            var w = World;
            holds[v.Id] = new Hold { v = v, shown = shown, since = w != null ? w.Time : 0, frame = Time.frameCount };
            lastShown[v.Id] = shown;
            Held++;
        }

        /// <summary>BoardingView: an enter animation for this vehicle exists (the hold waits for it).</summary>
        public static void GhostStarted(int vehicleId) { if (holds.TryGetValue(vehicleId, out var h)) h.ghost = true; }

        /// <summary>BoardingView: the last enter animation of this vehicle ended (the crew sits inside, or it was aborted).</summary>
        public static void Boarded(int vehicleId) => Release(vehicleId, 0);

        /// <summary>The vehicle's capture is still being animated.</summary>
        public static bool IsHeld(int vehicleId) { Pump(); return holds.ContainsKey(vehicleId); }

        /// <summary>Run `a` once the robot sits inside (at once when nothing is held for this vehicle).</summary>
        public static void When(int vehicleId, Action a)
        {
            if (a == null) return;
            Pump();
            if (holds.TryGetValue(vehicleId, out var h)) h.actions.Add(a);
            else Run(a);
        }

        /// <summary>Owner to paint / light / hear for this object: the owner before the capture while its climb-in plays, else the
        /// sim's. Recognises a capture on its own (an Entered event this frame) when it runs before BoardingView has seen it.</summary>
        public static Team Shown(ZObject o)
        {
            if (o == null) return Team.Neutral;
            if (!Enabled || (o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon)) return o.Owner;
            Pump();
            if (holds.TryGetValue(o.Id, out var h)) return h.shown;
            if (lastShown.TryGetValue(o.Id, out var was) && was != o.Owner && was == Team.Neutral && EnteredThisFrame(o.Id)) { Begin(o, was); return was; }
            lastShown[o.Id] = o.Owner;
            return o.Owner;
        }

        static bool EnteredThisFrame(int vehicleId)
        {
            var w = World; if (w == null) return false;
            var ev = w.Events;
            for (int i = 0; i < ev.Count; i++) if (ev[i].type == ZEventType.Entered && ev[i].otherId == vehicleId && !ev[i].flag) return true;
            return false;
        }

        static void Release(int id, int why)
        {
            if (!holds.TryGetValue(id, out var h)) return;
            holds.Remove(id);
            lastShown.Remove(id);
            var w = World;
            LastHoldSeconds = w != null ? w.Time - h.since : 0; if (LastHoldSeconds > MaxHoldSeconds) MaxHoldSeconds = LastHoldSeconds;
            switch (why) { case 0: ReleasedByAnimation++; break; case 1: ReleasedNoAnimation++; break; case 2: ReleasedTimeout++; break; default: ReleasedGone++; break; }
            foreach (var a in h.actions) Run(a);
        }

        static void Run(Action a) { try { a(); ActionsRun++; } catch (Exception e) { Debug.LogException(e); } }

        /// <summary>Once per frame: release holds that have nothing to wait for (no animation was made for them after a frame), whose
        /// vehicle is gone, or that waited MaxWait sim seconds.</summary>
        static void Pump()
        {
            if (holds.Count == 0 || pumpFrame == Time.frameCount) return;
            pumpFrame = Time.frameCount;
            var w = World; double now = w != null ? w.Time : 0;
            List<(int id, int why)> done = null;
            foreach (var kv in holds)
            {
                var h = kv.Value;
                int why = -1;
                if (h.v.Destroyed || h.v.KillAt >= 0 || (w != null && w.Get(h.v.Id) != h.v)) why = 3;
                else if (!h.ghost && Time.frameCount > h.frame + 1) why = 1;
                else if (now - h.since > MaxWait) why = 2;
                if (why >= 0) (done ??= new List<(int, int)>()).Add((kv.Key, why));
            }
            if (done != null) foreach (var (id, why) in done) Release(id, why);
        }

        /// <summary>A new level: nothing carries over.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() { holds.Clear(); lastShown.Clear(); pumpFrame = -1; Held = ReleasedByAnimation = ReleasedNoAnimation = ReleasedTimeout = ReleasedGone = ActionsRun = 0; LastHoldSeconds = MaxHoldSeconds = 0; }
    }
}
