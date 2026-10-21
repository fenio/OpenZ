using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit KS checks (menu OpenZ/V1.1/KS ...), items 214, 215: the fort blast throws the trees, its wave runs over the ground.
    /// <list type="bullet">
    /// <item>Fort Blast (Play, Level 16, the camera on the fort that will fall, at the standard zoom): the enemy fort falls
    /// (OpenZVictory.DebugFortFalls); for this check only the cut to the loungers waits 12 s and the ending camera keeps 70 m. At the
    /// blast +0.5, +1, +2, +4 s it prints the wave over the ground and the thrown trees (at +0.5 s it keeps the share of the front drawn
    /// and the most directions on raised ground); at +14.5 s the blast's frame count, the trees (all gone, thrown / snapped and thrown /
    /// broken over, every thrown foot out from where it stood, landed, where they came to rest against the set and the endings' camera
    /// spots), the wave over the raised ground against the kit's model, and the grouped sounds.</item>
    /// <item>... the OWN fort: the same at the other fort (the fort blast happens at either fort).</item>
    /// <item>... BEFORE (enemy fort / OWN fort): the run-19 tip-over and the flat ring (BlastThrow, SurfaceRing, BlastSurfaceTiming off)
    /// for the before / after frame count; its numbers are kept for this editor session per fort, and the next run at THAT fort compares
    /// with them. Use a fresh Play session each.</item>
    /// <item>... + Pauses For Pictures (and the BEFORE + Pauses For Pictures): the editor pauses at +0.5, +1, +2 and +4 s (take the
    /// picture, un-pause).</item>
    /// <item>On another level (Level 2, Level 17) the same menus run; the wave's line against the Level 16 model is then an info line
    /// (the pictures beside the BEFORE's are the check there).</item>
    /// </list>
    /// Test tools only: nothing is saved; every setting it changes is put back.
    /// </summary>
    public static class OzKSCheck
    {
        const string M = "OpenZ/V1.1/KS ", Key = "OzKSCheck.before.";
        static readonly float[] At = { 0.5f, 1f, 2f, 4f, 14.5f };
        static int step, up05; static bool pics, before, passAll, enemyRun; static float t0, until, fire0, shown05;
        static readonly StringBuilder sb = new StringBuilder();
        static float pid0, fz0, pz0; static bool throw0, ring0, timing0;

        [MenuItem(M + "Fort Blast: Trees Thrown, Wave Over The Ground (Play)")]
        static void Run() => Start(false, false, true);

        [MenuItem(M + "Fort Blast: the OWN fort, the other side (Play)")]
        static void RunOwn() => Start(false, false, false);

        [MenuItem(M + "Fort Blast + Pauses For Pictures (Play)")]
        static void RunPictures() => Start(true, false, true);

        [MenuItem(M + "Fort Blast, BEFORE: the tip-over and the flat ring (Play)")]
        static void RunBefore() => Start(false, true, true);

        [MenuItem(M + "Fort Blast, BEFORE: the OWN fort (Play)")]
        static void RunBeforeOwn() => Start(false, true, false);

        [MenuItem(M + "Fort Blast, BEFORE + Pauses For Pictures (Play)")]
        static void RunBeforePictures() => Start(true, true, true);

        static void Start(bool pictures, bool old, bool enemy)
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KS] enter Play first (Level 16), the camera on the fort that will fall"); return; }
            pid0 = OpenZVictory.PostImpactDelay; fz0 = OpenZEnding.FortZoom; pz0 = OpenZEnding.PushInZoom;
            throw0 = OzTreeFall.BlastThrow; ring0 = OpenZFortShockwave.SurfaceRing; timing0 = OzTreeFall.BlastSurfaceTiming;
            OpenZVictory.PostImpactDelay = 12f; OpenZEnding.FortZoom = 70f; OpenZEnding.PushInZoom = 70f;
            if (old) { OzTreeFall.BlastThrow = false; OpenZFortShockwave.SurfaceRing = false; OzTreeFall.BlastSurfaceTiming = false; }
            pics = pictures; before = old; enemyRun = enemy; step = 0; passAll = true; sb.Length = 0; fire0 = OpenZFortShockwave.LastFireTime; shown05 = -1f; up05 = -1;
            int n = OpenZVictory.DebugFortFalls(enemy);
            if (n == 0) { Debug.LogWarning("[OpenZ KS] no such fort standing"); PutBack(); return; }
            t0 = -1f; until = Time.realtimeSinceStartup + 120f;
            sb.AppendLine($"[OpenZ KS] fort blast check ({(old ? "BEFORE: the run-19 tip-over and the flat ring" : "trees thrown, the wave over the ground")}), {n} {(enemy ? "enemy" : "own")} fort part(s) destroyed, frame cap {Application.targetFrameRate}");
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ KS] the fort falls; numbers at +0.5, +1, +2, +4 s and the frame count at +14.5 s" + (pictures ? "; the editor pauses at +0.5, +1, +2 and +4 s for the pictures" : ""));
        }

        static void PutBack()
        {
            OpenZVictory.PostImpactDelay = pid0; OpenZEnding.FortZoom = fz0; OpenZEnding.PushInZoom = pz0;
            OzTreeFall.BlastThrow = throw0; OpenZFortShockwave.SurfaceRing = ring0; OzTreeFall.BlastSurfaceTiming = timing0;
        }

        static void Line(string what, bool ok, string info = "")
        {
            if (!ok) passAll = false;
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {what}{(info.Length > 0 ? ": " + info : "")}");
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; PutBack(); return; }
            if (EditorApplication.isPaused) return;
            if (t0 < 0f)
            {
                if (OpenZFortShockwave.LastFireTime > fire0 + 0.01f) t0 = OpenZFortShockwave.LastFireTime;
                else if (Time.realtimeSinceStartup > until) { Debug.LogWarning("[OpenZ KS] no fort blast came within 120 s"); EditorApplication.update -= Tick; PutBack(); }
                return;
            }
            float since = Time.time - t0;
            if (step >= At.Length || since < At[step]) return;
            Sample(At[step], since);
            step++;
            if (pics && step < At.Length) { EditorApplication.isPaused = true; Debug.Log($"[OpenZ KS] paused at blast +{since:0.00} s: take the picture (game camera, standard zoom, full screen), then un-pause"); }
            if (step == At.Length) { Debug.Log(sb.ToString() + (before ? "" : passAll ? "  ALL PASS" : "  NOT ALL PASS")); EditorApplication.update -= Tick; PutBack(); }
        }

        static void Sample(float at, float since)
        {
            if (at < 10f)
            {
                sb.AppendLine($"  +{at:0.0} s ({since:0.00}): {OpenZFortShockwave.SurfaceStatus()}");
                if (at < 0.75f) { shown05 = OpenZFortShockwave.SurfaceShownShare; up05 = OpenZFortShockwave.SurfaceUpMost; }
                sb.AppendLine($"  +{at:0.0} s: {OzTreeFall.ThrowStatus()}");
                return;
            }
            sb.AppendLine("  " + OzTreeFall.BlastStatus());
            sb.AppendLine("  " + OzTreeFall.BlastFrameStatus());
            foreach (var s in OzTreeFall.BlastSlowNotes) sb.AppendLine("    " + s);
            sb.AppendLine("  " + OzTreeFall.ThrowStatus());
            sb.AppendLine("  " + OpenZFortShockwave.SurfaceStatus());
            bool frames = OzTreeFall.BlastFrameNumbers(out int n, out float median, out int over50);
            string fort = enemyRun ? "enemy" : "own";
            if (before)
            {
                if (frames) SessionState.SetString(Key + fort, median.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) + $";{over50};{n};{Application.targetFrameRate}");
                sb.AppendLine($"  info  BEFORE at the {fort} fort kept for this editor session: median {median:0.00} ms, over 50 ms {over50}, {n} frames (run 19's clip take: median 22.59 ms, over 50 ms 2, at the 72 cap)");
                return;
            }
            // the kit's model (tools/ks_model.py on the Level 16 data) for the fort that fell: the north fort is the blue one
            bool north = OpenZFortShockwave.LastCentre.z > -130f;
            float wantShown = north ? 0.8f : 0.75f; int wantUp = north ? 30 : 20;
            string model = north ? "blue fort: 85 % drawn at +0.5 s, 73 directions on raised ground by then" : "red fort: 81 % drawn at +0.5 s, 80 directions on raised ground by then";
            string modelSet = !(ZGame.I && ZGame.I.level == "L16") ? "no model for this level" : north ? "blue fort: 7.4 m" : "red fort: 74.6 m";
            int felled = OzTreeFall.BlastFelled, gone = OzTreeFall.ThrownNear + OzTreeFall.ThrownMid + OzTreeFall.BrokenOver;
            Line("every tree the wave reached went (thrown, snapped and thrown, or broken over), none waits", felled > 0 && gone == felled && OzTreeFall.BlastWaiting == 0,
                 $"felled {felled}, thrown {OzTreeFall.ThrownNear}, snapped and thrown {OzTreeFall.ThrownMid}, broken over {OzTreeFall.BrokenOver}, waiting {OzTreeFall.BlastWaiting}");
            Line("the near ring was THROWN and the middle ring SNAPPED AND THROWN (not a tip-over in place)", OzTreeFall.ThrownNear > 0 && OzTreeFall.ThrownMid > 0,
                 $"short throws: no ground under the tree {OzTreeFall.NoGroundShort}, the end of the ground {OzTreeFall.EdgeHeld}; broken over for the set {OzTreeFall.CampHeld}");
            float least = OzTreeFall.ThrowTravelLeast;
            Line("every thrown tree's foot came to rest out from where it stood (at least 0.8 x the broken foot's shove, none fell over in place)",
                 !float.IsPositiveInfinity(least) && least >= 0.8f * OzTreeFall.FarSkid.x, $"the shortest {(float.IsPositiveInfinity(least) ? "-" : least.ToString("0.0"))} m (the kit's model on Level 16: 2.1 m blue, 2.2 m red; the broken foot's shove {OzTreeFall.FarSkid.x:0.0} m)");
            float sc = OzTreeFall.ThrowSetClosest;
            Line("no thrown tree rests in the set or on the endings' camera spots (the resting trunk, foot to top, more than BlastSceneClear from them and the lines between them)",
                 sc > OzTreeFall.BlastSceneClear, $"{(float.IsPositiveInfinity(sc) ? "no set on this level" : "closest " + sc.ToString("0.0") + " m")} (the kit's model: {modelSet}), BlastSceneClear {OzTreeFall.BlastSceneClear:0.#} m");
            Line("the thrown trees came down and lie (or sink) as logs", OzTreeFall.ThrowLanded >= gone - 2, $"landed {OzTreeFall.ThrowLanded} of {gone}");
            int notes = 0, plays = 0; for (int k = 0; k < OzTreeFall.SoundNotes.Length; k++) { notes += OzTreeFall.SoundNotes[k]; plays += OzTreeFall.SoundPlays[k]; }
            Line("every visible action has its sound, grouped (crack, whoosh, thud, slide played; far fewer sounds than notes)",
                 OzTreeFall.SoundPlays[0] > 0 && OzTreeFall.SoundPlays[2] > 0 && OzTreeFall.SoundPlays[3] > 0 && OzTreeFall.SoundPlays[6] > 0 && plays <= OzTreeFall.ThrowSoundsMost && plays * 3 <= notes,
                 $"{notes} notes -> {plays} sounds (slides {OzTreeFall.ThrowSlides}: {OzTreeFall.SoundNotes[6]} notes -> {OzTreeFall.SoundPlays[6]} played)");
            string wave = $"at +0.5 s drawn {(shown05 < 0f ? "-" : (shown05 * 100f).ToString("0"))} %, on raised ground {up05} (the most in the blast {OpenZFortShockwave.SurfaceUpMost}, the highest it climbed {OpenZFortShockwave.SurfaceTopMost:0.0} m)";
            if (ZGame.I && ZGame.I.level == "L16")
                Line($"the wave ran over the raised ground as the model has it (at +0.5 s drawn on at least {wantShown * 100f:0} % of its directions, at least {wantUp} directions on raised ground)",
                     shown05 >= wantShown && up05 >= wantUp, wave + $"; the kit's model, {model}");
            else sb.AppendLine($"  info  the wave over the ground on {(ZGame.I ? ZGame.I.level : "?")} (no model for this level: compare the pictures with the BEFORE's): {wave}");
            var b = SessionState.GetString(Key + fort, "");
            if (frames && b.Length > 0)
            {
                var p = b.Split(';');
                float bm = float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture); int bo = int.Parse(p[1]);
                Line($"the frame count holds the BEFORE's at the {fort} fort (median at most 1.5 ms over it, at most 1 more frame over 50 ms)", median <= bm + 1.5f && over50 <= bo + 1,
                     $"median {median:0.00} ms against {bm:0.00}, over 50 ms {over50} against {bo} (BEFORE at cap {p[3]}, now {Application.targetFrameRate})");
            }
            else sb.AppendLine($"  info  no BEFORE at the {fort} fort in this editor session: median {median:0.00} ms, over 50 ms {over50} (run 19's clip take: median 22.59 ms, over 50 ms 2, at the 72 cap)");
        }
    }
}
