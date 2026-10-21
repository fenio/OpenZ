// OpenZ (GPL-3.0) - Level 16 briefing: the far flock that crosses the sun's disc in the sunrise shot.
// Seven to eleven birds in a loose V, dark against the disc and its halo, slow wing beats, far enough away that each bird is small
// against the sun; each takes SunBirdCross seconds over the disc and the flock's middle is on the disc at SunBirdsMid of the shot.
// They are drawn only (one small mesh of the stage's own, no animal of the level, no rule reads them) and are gone with the shot.
// Counted for the check: SunBirdsOverDisc = seconds with five or more birds over the disc, SunBirdsMost = most birds over it at once.
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16BriefingStage
    {
        public static string SunShot = "dawn_swamp";
        public static int SunBirds = 9;
        /// <summary>Metres from the lens, wing span (m), seconds a bird takes over the disc, wing beats a second, where in the shot the
        /// flock's middle is on the disc (0..1), the disc's radius as the lens sees it (deg), metres between two birds, the calls' gain.</summary>
        public static float SunBirdsFar = 170f, SunBirdSpan = 1.6f, SunBirdCross = 3.4f, SunBirdBeat = 2.1f, SunBirdsMid = 0.52f, SunDiscDeg = 1.2f, SunBirdGap = 1.7f, GainSunBirds = 0.14f;
        public static Color SunBirdColor = new Color(0.07f, 0.05f, 0.05f, 1f);
        public static float SunBirdsOverDisc; public static int SunBirdsMost; public static bool SunBirdsDrawn;

        GameObject sunFlockGo; Mesh sunFlockMesh; Material sunFlockMat; Vector3[] sbVerts, sbOff; float[] sbPhase;
        Vector3 sbCam0, sbDir, sbRight; bool sbSet, sbLogged; float sbPrevInto = -1f;

        void BuildSunFlock()
        {
            int n = Mathf.Clamp(SunBirds, 7, 11);
            var sh = Shader.Find("Sprites/Default"); if (!sh) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) { Warn("sun birds", "no plain shader for the far flock: no birds cross the sun"); return; }
            sunFlockMat = new Material(sh) { name = "L16SunBirds (runtime)", color = SunBirdColor };
            if (sunFlockMat.HasProperty("_BaseColor")) sunFlockMat.SetColor("_BaseColor", SunBirdColor);
            if (sunFlockMat.HasProperty("_Cull")) sunFlockMat.SetFloat("_Cull", 0f);
            sunFlockGo = new GameObject("L16SunBirds"); sunFlockGo.transform.SetParent(transform, false);
            sunFlockMesh = new Mesh { name = "L16SunBirds" }; sunFlockMesh.MarkDynamic();
            sunFlockGo.AddComponent<MeshFilter>().sharedMesh = sunFlockMesh;
            var mr = sunFlockGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = sunFlockMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            sbVerts = new Vector3[n * 10]; sbOff = new Vector3[n]; sbPhase = new float[n];
            var tris = new int[n * 18]; var rnd = new System.Random(16);
            for (int i = 0; i < n; i++)
            {   // a loose V: the leader in front, the others behind it left and right, each a little off its place
                int rank = (i + 1) / 2; float side = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f);
                sbOff[i] = new Vector3(-rank * SunBirdGap + (float)(rnd.NextDouble() - 0.5) * 0.8f, side * rank * 0.40f + (float)(rnd.NextDouble() - 0.5) * 0.4f, side * rank * 1.4f);
                sbPhase[i] = (float)rnd.NextDouble();
                int v = i * 10, t = i * 18;
                for (int s = 0; s < 2; s++)
                {   // one wing: root top, root bottom, elbow top, elbow bottom, tip
                    int b = v + s * 5, k = t + s * 9;
                    tris[k] = b; tris[k + 1] = b + 1; tris[k + 2] = b + 3; tris[k + 3] = b; tris[k + 4] = b + 3; tris[k + 5] = b + 2; tris[k + 6] = b + 2; tris[k + 7] = b + 3; tris[k + 8] = b + 4;
                }
            }
            sunFlockMesh.vertices = sbVerts; sunFlockMesh.triangles = tris;
            sunFlockGo.SetActive(false);
        }

        void LogSunBirds()
        {
            if (sbLogged) return; sbLogged = true;
            Debug.Log($"[OpenZ L16 briefing] the far flock over the sun: {(sbOff != null ? sbOff.Length : 0)} birds, five or more over the disc for {SunBirdsOverDisc:F2} s, most at once {SunBirdsMost}");
        }

        void TickSunBirds(string shot, float into, float dur, float duck)
        {
            if (shot != SunShot || !cam)
            {
                if (sunFlockGo && sunFlockGo.activeSelf) { sunFlockGo.SetActive(false); LogSunBirds(); }
                sbSet = false; SunBirdsDrawn = false; return;
            }
            if (!sunFlockGo) BuildSunFlock();
            if (!sunFlockGo) return;
            if (!sbSet)
            {   // the line of sight to the drawn sun, taken on the shot's first frame (the camera cranes a few metres: nothing at this distance)
                var sun = RenderSettings.sun;
                sbDir = FlybySky.VisualSun(sun ? -sun.transform.forward : new Vector3(0.94f, 0.16f, -0.3f).normalized).normalized;
                sbCam0 = cam.transform.position; sbRight = Vector3.Cross(Vector3.up, sbDir).normalized; sbSet = true; sbLogged = false;
                SunBirdsOverDisc = 0f; SunBirdsMost = 0; sbPrevInto = into;
            }
            if (!sunFlockGo.activeSelf) sunFlockGo.SetActive(true);
            SunBirdsDrawn = true;
            float discW = 2f * SunBirdsFar * Mathf.Tan(SunDiscDeg * Mathf.Deg2Rad), speed = discW / Mathf.Max(0.5f, SunBirdCross);
            float tMid = Mathf.Max(1f, dur) * SunBirdsMid, along = (into - tMid) * speed + (sbOff.Length / 2) * SunBirdGap * 0.5f;
            Vector3 mid = sbCam0 + sbDir * SunBirdsFar + sbRight * along + Vector3.up * (0.015f * along);
            Vector3 camPos = cam.transform.position; int over = 0; float half = SunBirdSpan * 0.5f, th = SunBirdSpan * 0.055f;
            for (int i = 0; i < sbOff.Length; i++)
            {
                Vector3 c = mid + sbRight * sbOff[i].x + Vector3.up * (sbOff[i].y + Mathf.Sin(into * 0.9f + sbPhase[i] * 6f) * 0.22f) + sbDir * sbOff[i].z;
                float flap = Mathf.Sin((into * SunBirdBeat + sbPhase[i]) * Mathf.PI * 2f);              // -1 wings down .. 1 wings up
                Vector3 up = Vector3.up;
                for (int s = 0; s < 2; s++)
                {
                    float sg = s == 0 ? -1f : 1f; int b = i * 10 + s * 5;
                    Vector3 elbow = c + sbRight * (sg * half * 0.48f) + up * (half * (0.16f + 0.22f * flap));
                    Vector3 tip = c + sbRight * (sg * half) + up * (half * (0.02f + 0.55f * flap));
                    sbVerts[b] = c + up * th; sbVerts[b + 1] = c - up * th; sbVerts[b + 2] = elbow + up * (th * 0.75f); sbVerts[b + 3] = elbow - up * (th * 0.75f); sbVerts[b + 4] = tip;
                }
                if (Vector3.Angle(c - camPos, sbDir) <= SunDiscDeg) over++;
            }
            sunFlockMesh.vertices = sbVerts; sunFlockMesh.RecalculateBounds();
            float dt = into - sbPrevInto; sbPrevInto = into;
            if (over > SunBirdsMost) SunBirdsMost = over;
            if (over >= 5 && dt > 0f && dt < 0.5f) SunBirdsOverDisc += dt;
            // their far calls, soft, as they come to the disc and as they leave it
            if (Crossed(tMid - 1.1f, into)) Play("brief_l16_far_flock", "sd_jungle_bird_d", GainSunBirds, duck, shot);
            if (Crossed(tMid + 0.9f, into)) Play("brief_l16_far_flock", "sd_jungle_bird_e", GainSunBirds * 0.8f, duck, shot);
        }

        void ReleaseSunBirds()
        {
            if (sunFlockGo && sunFlockGo.activeSelf) { sunFlockGo.SetActive(false); LogSunBirds(); }
            sbSet = false; SunBirdsDrawn = false;
        }
    }
}
