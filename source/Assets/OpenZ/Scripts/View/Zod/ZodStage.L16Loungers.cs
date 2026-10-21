// OpenZ (GPL-3.0) - Level 16: the transmission window shows the two robots on their sun loungers (OzL16LoungerSet).
//  - LoungerFeed(camera): the window's picture (Feed) is rendered by that camera, a second view of the LEVEL, instead of the stage's own
//    camera; the commander and his room are not rendered while it is on (nothing of him is in the picture). LoungerFeed(null) gives the
//    picture back. SetLive, the next line and the stage's own shots are untouched.
//  - LoungerJaw(clip, t): the mouth curve of a voice clip (Resources/OpenZ/Zod/Boys/alan_mouth_curves.json, one curve per clip, found by
//    the clip's name) for a speaker outside the stage; -1 = this clip has no curve.
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        Camera loungerCam;
        /// <summary>True while the window's picture comes from the lounger set's camera.</summary>
        public bool LoungerFeedOn => loungerCam;

        public void LoungerFeed(Camera c)
        {
            if (c)
            {
                if (!Feed) EnsureFeed(640, 480);
                loungerCam = c;
                if (cam) cam.enabled = false;                                                             // the stage's own camera rests: one picture, one camera
                if (vol) vol.enabled = false;
                if (model && model.gameObject.activeSelf) model.gameObject.SetActive(false);             // the commander is not in this picture
                if (smoke && !(model && smoke.transform.IsChildOf(model)) && smoke.gameObject.activeSelf) smoke.gameObject.SetActive(false);
                c.targetTexture = Feed; c.aspect = Feed.width / (float)Mathf.Max(1, Feed.height); c.enabled = true;
            }
            else if (loungerCam)
            {
                loungerCam.enabled = false; loungerCam.targetTexture = null; loungerCam = null;
            }
        }

        /// <summary>The jaw value 0..1 at clip time t (s) from the voice clip's mouth curve; -1 = the clip has no curve.</summary>
        public static float LoungerJaw(AudioClip clip, float t)
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
