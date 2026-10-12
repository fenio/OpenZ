using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Runs several Play harnesses one after the other (each waits for the previous one's status to leave "running"), so one bridge
    /// call starts a whole proof batch. Names: mc, robotfix, impact, look, acne, tower:[tag], shorebeach. Log in Status.
    /// </summary>
    public class CheckQueue : MonoBehaviour
    {
        public static string Status = "idle";
        static readonly Dictionary<string, (Action<string> start, Func<string> status)> Known = new Dictionary<string, (Action<string>, Func<string>)>
        {
            ["mc"] = (a => MissileCannonCheck.Run(), () => MissileCannonCheck.Report),
            ["robotfix"] = (a => RobotFixCheck.Run(), () => RobotFixCheck.Report),
            ["impact"] = (a => ImpactCheck.Run(), () => ImpactCheck.Report),
            ["look"] = (a => LookCheck.Run(), () => LookCheck.Report),
            ["acne"] = (a => ShadowAcneShots.Run(), () => ShadowAcneShots.Status),
            ["tower"] = (a => TowerCrewShot.Run(a), () => TowerCrewShot.Report),
            ["shorebeach"] = (a => ShoreBeachCapture.Run(), () => ShoreBeachCapture.Status),
            ["fortdef"] = (a => FortDefenderShow.Run(string.IsNullOrEmpty(a) ? "after" : a), () => FortDefenderShow.Report),
            ["spacing"] = (a => SpacingTrace.Run(string.IsNullOrEmpty(a) ? "grunt" : a), () => SpacingTrace.Report),
        };

        public static void Run(params string[] names) { Status = "running"; var q = new GameObject("CheckQueue").AddComponent<CheckQueue>(); q.StartCoroutine(q.Go(names)); }

        IEnumerator Go(string[] names)
        {
            var log = new System.Text.StringBuilder();
            foreach (var n in names)
            {
                var parts = n.Split(':'); string key = parts[0], arg = parts.Length > 1 ? parts[1] : "";
                if (!Known.TryGetValue(key, out var k)) { log.Append($"{n}: unknown; "); continue; }
                k.start(arg); yield return null; yield return null;
                float t0 = Time.realtimeSinceStartup;
                while (k.status() != null && k.status().StartsWith("running") && Time.realtimeSinceStartup - t0 < 240f) yield return null;
                log.Append($"{n}: {k.status()}; "); Status = "running: " + log;
                yield return new WaitForSeconds(1f);
            }
            Status = "done: " + log; Destroy(gameObject);
        }
    }
}
