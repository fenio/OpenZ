using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// that is playing (its own loops with their key, the engine loops with their vehicle, every other looping source in the scene with its
    /// owner) with the reason it runs, plus any one-shot event that fired 8+ times in the window (a ticking / spamming sound).
    /// A loop without its reason (a turret loop whose turret stopped reporting, a loop whose owner was destroyed) is logged as STALE and
    /// faded out. Editor: runs always, prints only STALE / REPEAT lines unless LoopAudit (PlayerPrefs oz_loopaudit = 1) asks for the full
    /// list; builds: only with oz_loopaudit = 1.
    /// </summary>
    public partial class AudioDirector
    {
        static int loopAuditPref = -1;
        public static bool LoopAudit
        {
            get { if (loopAuditPref < 0) loopAuditPref = PlayerPrefs.GetInt("oz_loopaudit", 0); return loopAuditPref == 1; }
            set { loopAuditPref = value ? 1 : 0; }
        }
        public static float LoopAuditEvery = 10f;
        /// <summary>Loops faded because their follow Transform was destroyed (per frame) / STALE loops the audit found and faded.</summary>
        public int LoopOrphansStopped, LoopAuditStale, LoopAuditRuns;
        public string LastLoopAudit;
        float loopAuditAt;
        readonly Dictionary<string, int> auditPlays = new Dictionary<string, int>();
        readonly HashSet<AudioSource> auditKnown = new HashSet<AudioSource>();

        static string OwnerPath(Transform t)
        {
            if (!t) return "(none)";
            var p = t.parent;
            return p ? (p.parent ? p.parent.name + "/" : "") + p.name + "/" + t.name : t.name;
        }

        /// <summary>fire:<id> loops: a wreck burns for WreckFx.SmoulderSeconds, a burning building for 30 s.</summary>
        string FireLoopReason(string key)
        {
            if (key == null || !key.StartsWith("fire:") || !int.TryParse(key.Substring(5), out int id)) return null;
            float now = Time.time;
            if (wrecks.TryGetValue(id, out var w)) return (now - w.t < WreckFx.SmoulderSeconds + 2f ? "ok" : "STALE") + $" wreck fire, age {now - w.t:0} s";
            if (burning.TryGetValue(id, out var b)) return (now - b.t < 32f ? "ok" : "STALE") + $" building fire, age {now - b.t:0} s";
            return "STALE fire without a wreck / burning building";
        }

        void LoopAuditTick()
        {
            if (!Application.isEditor && !LoopAudit) return;
            float now = Time.unscaledTime;
            if (now < loopAuditAt) return;
            bool first = loopAuditAt <= 0f;
            loopAuditAt = now + Mathf.Max(1f, LoopAuditEvery);
            LoopAuditRuns++;
            bool full = LoopAudit;
            var sb = new StringBuilder(); int n = 0, stale = 0, repeats = 0;
            var lp = ListenerPos;
            auditKnown.Clear();
            List<string> stop = null;
            foreach (var kv in loops)
            {
                var v = kv.Value; if (!v.busy || !v.src) continue;
                auditKnown.Add(v.src);
                if (!v.src.isPlaying || v.fadeTarget <= 0f) continue;
                string reason = OzSoundV3.LoopReason(kv.Key) ?? FireLoopReason(kv.Key);
                if (reason == null) reason = v.followed ? (v.follow ? "ok follows " + OwnerPath(v.follow) : "STALE owner destroyed") : "ok fixed emitter";
                bool bad = reason.StartsWith("STALE");
                if (bad) { stale++; (stop ??= new List<string>()).Add(kv.Key); }
                n++;
                if (full || bad)
                    sb.Append("\n  ").Append(bad ? "STALE " : "").Append("loop ").Append(kv.Key).Append(" | ").Append(v.def != null ? v.def.id : "?")
                      .Append(" | ").Append(v.src.clip ? v.src.clip.name : "-").Append(" | vol ").Append(v.src.volume.ToString("0.000"))
                      .Append(" | ").Append(Vector3.Distance(lp, v.src.transform.position).ToString("0")).Append(" m | age ")
                      .Append((Time.time - v.start).ToString("0")).Append(" s | ").Append(reason);
            }
            if (stop != null) foreach (var k in stop) StopLoop(k, 0.3f);
            var ea = LegacyEngineAudio.I;
            if (ea) { int s = ea.AuditLines(sb, auditKnown, lp, full, ref n); stale += s; }
            foreach (var a in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!a || !a.isPlaying || !a.loop || auditKnown.Contains(a)) continue;
                n++;
                if (full)
                    sb.Append("\n  src ").Append(OwnerPath(a.transform)).Append(" | ").Append(a.clip ? a.clip.name : "-").Append(" | vol ").Append(a.volume.ToString("0.000"))
                      .Append(a.spatialBlend > 0.5f ? " | " + Vector3.Distance(lp, a.transform.position).ToString("0") + " m" : " | 2D").Append(" | owner ").Append(a.gameObject.activeInHierarchy ? "active" : "inactive");
            }
            foreach (var kv in Plays)
            {
                auditPlays.TryGetValue(kv.Key, out int was);
                int d = kv.Value - was;
                if (EngineOwned.ContainsKey(kv.Key)) continue;                                           // coverage bookkeeping (PollCombatLoops), not plays
                var def = Def(kv.Key);
                if (!first && d >= 8) { repeats++; sb.Append(def != null && def.loop ? "\n  REPEAT loop restarted " : "\n  REPEAT one-shot ").Append(kv.Key).Append(" x").Append(d).Append(" in ").Append(LoopAuditEvery.ToString("0")).Append(" s"); }
            }
            foreach (var kv in Plays) auditPlays[kv.Key] = kv.Value;
            LoopAuditStale += stale;
            string head = $"[OzLoops] t {Time.time:0.0} s: {n} looping sources, {stale} STALE (faded), {repeats} repeating one-shots, {LoopOrphansStopped} orphan loops stopped so far";
            LastLoopAudit = head + sb;
            if (full || stale > 0 || repeats > 0) Debug.Log(LastLoopAudit);
        }
    }
}
