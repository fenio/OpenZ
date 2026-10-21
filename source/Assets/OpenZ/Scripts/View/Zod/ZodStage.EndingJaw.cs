// OpenZ (GPL-3.0) - level 16 endings: the mouth curve of a voice clip for a caller outside the stage
// (ZodBriefing.EndingScene.cs: the two robots speak in the level, not on the commander's stage).
// WRITTEN BY READING ONLY: never compiled, never run. Remove this line once it runs.
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        /// <summary>The jaw value 0..1 at clip time t (s) from Resources/OpenZ/Zod/Boys/alan_mouth_curves.json (one curve per clip,
        /// found by the clip's name); -1 = this clip has no curve (the caller follows the live level instead).</summary>
        public static float CurveJaw(AudioClip clip, float t)
        {
            var curve = AlanCurve(clip);
            if (curve == null) return -1f;
            float fi = t * alanCurveFps; int i0 = Mathf.FloorToInt(fi);
            if (i0 < 0 || i0 >= curve.Length) return 0f;
            int i1 = Mathf.Min(i0 + 1, curve.Length - 1);
            return Mathf.Lerp(curve[i0], curve[i1], fi - i0) / 100f;
        }
    }
}
