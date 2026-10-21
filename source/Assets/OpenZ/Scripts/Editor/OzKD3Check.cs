using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Item 212, round 3 (menu OpenZ/V1.1/KD3 ...): the broken fort cannon's pieces at rest.
    /// <list type="bullet">
    /// <item><b>Cannon Rest Darkness A/B (Play)</b>: a level with an enemy fort, the camera on it at the standard zoom. The enemy fort falls;
    /// the ending runs as in the game (its camera orbit held off and the camera held 24 s after the blast for this session so the frames
    /// line up). OzKD3Probe measures the thrown cannon at +2 s and the resting pieces at +8 s, one real frame per probe (the pieces' own
    /// pixels against the concrete chunks beside them), prints every probe, the earlier checks' sampling error, the cause and the rest
    /// look's numbers to set.</item>
    /// <item><b>Fort Blast + Pauses For Pictures at +6, +10, +14 s (Play)</b>: the ending as in the game (the orbit on), the cut to the
    /// loungers held until +18 s, the camera at 70 m; the editor pauses at each time: take the picture, un-pause.</item>
    /// </list>
    /// Test tools only: nothing is saved; every setting changed is put back.
    /// </summary>
    public static class OzKD3Check
    {
        const string M = "OpenZ/V1.1/KD3 ";
        static readonly float[] At = { 6f, 10f, 14f };
        static int step; static float t0, until, pid0, mh0, fz0, pz0; static bool armed;

        [MenuItem(M + "Cannon Rest Darkness A/B (Play)")]
        static void RunAB()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KD3] enter Play first (a level with an enemy fort), the camera on it"); return; }
            if (OzKD3Probe.Running) { Debug.LogWarning("[OpenZ KD3] the A/B is already running"); return; }
            if (!OzKD3Probe.Begin()) { Debug.LogWarning("[OpenZ KD3] could not start"); return; }
            Debug.Log($"[OpenZ KD3] the enemy fort falls; the thrown cannon at +{OzKD3Probe.FlightAt:0} s, the resting pieces at +{OzKD3Probe.ProbeAt:0} s (about 25 frames of probes), then the verdict in the console");
        }

        [MenuItem(M + "Fort Blast + Pauses For Pictures at +6, +10, +14 s (Play)")]
        static void RunPictures()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KD3] enter Play first (a level with an enemy fort), the camera on it"); return; }
            pid0 = OpenZVictory.PostImpactDelay; mh0 = OpenZVictory.MaxHold; fz0 = OpenZEnding.FortZoom; pz0 = OpenZEnding.PushInZoom;
            // the ending's safety release counts from the game over (+ the nuke intro's hold), 4-6 s before the impact: moved out so it cannot cut the +18 s hold
            OpenZVictory.PostImpactDelay = 18f; OpenZVictory.MaxHold = Mathf.Max(mh0, 34f); OpenZEnding.FortZoom = 70f; OpenZEnding.PushInZoom = 70f; armed = true;
            int n = OpenZVictory.DebugFortFalls(true);
            if (n == 0) { Debug.LogWarning("[OpenZ KD3] no enemy fort standing"); PutBack(); return; }
            step = 0; t0 = -1f; until = Time.realtimeSinceStartup + 120f;
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ KD3] the enemy fort falls; the editor pauses at +6, +10 and +14 s: take the picture (RTS camera, standard zoom, full screen), then un-pause");
        }

        static void PutBack()
        {
            if (!armed) return;
            OpenZVictory.PostImpactDelay = pid0; OpenZVictory.MaxHold = mh0; OpenZEnding.FortZoom = fz0; OpenZEnding.PushInZoom = pz0; armed = false;
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; PutBack(); return; }
            if (EditorApplication.isPaused) return;
            if (t0 < 0f)
            {
                if (OpenZNukeMushroom.Current && OpenZNukeMushroom.Current.Age > 0f) t0 = Time.time - OpenZNukeMushroom.Current.Age;
                else if (Time.realtimeSinceStartup > until) { Debug.LogWarning("[OpenZ KD3] no mushroom appeared"); EditorApplication.update -= Tick; PutBack(); }
                return;
            }
            float since = Time.time - t0;
            if (step < At.Length && since >= At[step])
            {
                int rested = 0, count = 0;
                foreach (var p in OzFortGunBreakup.LastPieces) if (p) { count++; if (p.Rested) rested++; }
                step++;
                EditorApplication.isPaused = true;
                Debug.Log($"[OpenZ KD3] paused at blast +{since:0.00} s ({count} cannon pieces, {rested} at rest; {OzFortGunRestLook.State}): take the picture, then un-pause");
                if (step == At.Length) { EditorApplication.update -= Tick; PutBack(); }
            }
        }
    }
}
