#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: the far flock that crosses the rising sun's disc, the look of the level-start sunrise shot (a loose
// V of dark birds, slow wing beats, far enough that each bird is small against the disc). Drawn only (one small mesh of this take,
// no animal of the level, no rule reads it), gone with the take. Two soft calls as the flock comes to the disc and as it leaves it.
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        GameObject flockGo; Mesh flockMesh; Material flockMat; Vector3[] fbVerts, fbOff; float[] fbPhase;
        Vector3 fbCam0, fbRight; bool fbSet, fbCall1, fbCall2; int fbMost; float fbOverSec, fbPrevT = -1f;
        static readonly Color FlockColor = new Color(0.07f, 0.05f, 0.05f, 1f);

        void BuildFlock(int n)
        {
            var sh = Shader.Find("Sprites/Default"); if (!sh) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) { Note("WARNING no plain shader for the far flock: no birds over the sun"); return; }
            flockMat = new Material(sh) { name = "EpicFlybyBirds (runtime)", color = FlockColor };
            if (flockMat.HasProperty("_BaseColor")) flockMat.SetColor("_BaseColor", FlockColor);
            if (flockMat.HasProperty("_Cull")) flockMat.SetFloat("_Cull", 0f);
            flockGo = new GameObject("EpicFlybyBirds"); flockGo.transform.SetParent(transform, false);
            flockMesh = new Mesh { name = "EpicFlybyBirds" }; flockMesh.MarkDynamic();
            flockGo.AddComponent<MeshFilter>().sharedMesh = flockMesh;
            var mr = flockGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = flockMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            fbVerts = new Vector3[n * 10]; fbOff = new Vector3[n]; fbPhase = new float[n];
            var tris = new int[n * 18]; var rnd = new System.Random(sc.seed + 16);
            float gap = 1.7f;
            for (int i = 0; i < n; i++)
            {   // the leader in front, the others behind it left and right, each a little off its place
                int rank = (i + 1) / 2; float side = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f);
                fbOff[i] = new Vector3(-rank * gap + (float)(rnd.NextDouble() - 0.5) * 0.8f, side * rank * 0.40f + (float)(rnd.NextDouble() - 0.5) * 0.4f, side * rank * 1.4f);
                fbPhase[i] = (float)rnd.NextDouble();
                int v = i * 10, t = i * 18;
                for (int s = 0; s < 2; s++)
                {
                    int b = v + s * 5, k = t + s * 9;
                    tris[k] = b; tris[k + 1] = b + 1; tris[k + 2] = b + 3; tris[k + 3] = b; tris[k + 4] = b + 3; tris[k + 5] = b + 2; tris[k + 6] = b + 2; tris[k + 7] = b + 3; tris[k + 8] = b + 4;
                }
            }
            flockMesh.vertices = fbVerts; flockMesh.triangles = tris;
        }

        void TickBirds(float t, Vector3 camPos)
        {
            var b = sc.sun_birds; if (b == null || !b.on) return;
            int n = Mathf.Clamp(b.count > 0 ? b.count : 9, 5, 15);
            float far = b.far_m > 0f ? b.far_m : 170f, span = b.span_m > 0f ? b.span_m : 1.6f, cross = b.cross_s > 0f ? b.cross_s : 3.4f, mid = b.mid_s > 0f ? b.mid_s : 2.5f;
            if (!flockGo) BuildFlock(n);
            if (!flockGo) return;
            if (!fbSet)
            {   // the line of sight to the drawn sun from the camera's first place (the camera moves a few metres: nothing at this distance)
                Pose(0f, out fbCam0, out _, out _, out _);
                fbRight = Vector3.Cross(Vector3.up, sunDir); if (fbRight.sqrMagnitude < 1e-6f) fbRight = Vector3.right; fbRight.Normalize();
                fbSet = true;
            }
            const float discDeg = 1.2f;
            float discW = 2f * far * Mathf.Tan(discDeg * Mathf.Deg2Rad), speed = discW / Mathf.Max(0.5f, cross);
            float along = (t - mid) * speed + (n / 2) * 1.7f * 0.5f;
            if (Mathf.Abs(along) > far * 0.9f) { if (flockGo.activeSelf) flockGo.SetActive(false); return; }
            if (!flockGo.activeSelf) flockGo.SetActive(true);
            Vector3 c0 = fbCam0 + sunDir * far + fbRight * along + Vector3.up * (0.015f * along);
            int over = 0; float half = span * 0.5f, th = span * 0.055f, beat = 2.1f;
            for (int i = 0; i < n; i++)
            {
                Vector3 c = c0 + fbRight * fbOff[i].x + Vector3.up * (fbOff[i].y + Mathf.Sin(t * 0.9f + fbPhase[i] * 6f) * 0.22f) + sunDir * fbOff[i].z;
                float flap = Mathf.Sin((t * beat + fbPhase[i]) * Mathf.PI * 2f);
                for (int s = 0; s < 2; s++)
                {
                    float sg = s == 0 ? -1f : 1f; int k = i * 10 + s * 5;
                    Vector3 elbow = c + fbRight * (sg * half * 0.48f) + Vector3.up * (half * (0.16f + 0.22f * flap));
                    Vector3 tip = c + fbRight * (sg * half) + Vector3.up * (half * (0.02f + 0.55f * flap));
                    fbVerts[k] = c + Vector3.up * th; fbVerts[k + 1] = c - Vector3.up * th; fbVerts[k + 2] = elbow + Vector3.up * (th * 0.75f); fbVerts[k + 3] = elbow - Vector3.up * (th * 0.75f); fbVerts[k + 4] = tip;
                }
                if (Vector3.Angle(c - camPos, sunDir) <= discDeg) over++;
            }
            flockMesh.vertices = fbVerts; flockMesh.RecalculateBounds();
            float dt = t - fbPrevT; fbPrevT = t;
            if (over > fbMost) fbMost = over;
            BirdsFrame(t, over);                                                                   // round 4: frame 0 into the take log (OzEpicFlyby.Kf4.cs)
            if (over >= 5 && dt > 0f && dt < 0.5f) fbOverSec += dt;
            float gain = b.gain > 0f ? b.gain : 0.14f;
            if (!fbCall1 && t >= mid - 1.1f) { fbCall1 = true; PlaySfx("sd_jungle_bird_d", gain); }
            if (!fbCall2 && t >= mid + 0.9f) { fbCall2 = true; PlaySfx("sd_jungle_bird_e", gain * 0.8f); }
        }

        void ReleaseBirds()
        {
            ReleaseLensBirds();                                                                    // OzEpicFlyby.LensBirds.cs
            if (sc != null && sc.sun_birds != null && sc.sun_birds.on && fbSet) Note("the far flock over the sun: five or more birds over the disc for " + fbOverSec.ToString("F2") + " s, most at once " + fbMost);
            if (flockGo) Destroy(flockGo);
            if (flockMesh) Destroy(flockMesh);
            if (flockMat) Destroy(flockMat);
            flockGo = null; fbSet = false;
        }
    }
}
#endif
