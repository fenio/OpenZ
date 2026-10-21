using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The mushroom's smoke keeps its motion blur for its whole life, and its ground puffs are fog.
    /// <list type="bullet">
    /// <item><b>Motion blur</b>: the puff shader blurs each puff along the way it travels during the exposure (MOTION BLUR option,
    /// OzFxSmooth). A puff that hardly moves is drawn sharp. The crown is what the RTS camera sees from above, and its puffs only
    /// turned slowly about the axis: once the cloud stopped rising fast (from about 4 s) they stood nearly still and were drawn as crisp
    /// round puffs. Now the crown <b>rolls</b> like a real mushroom cap: every crown puff travels outward from the top over the dome
    /// to the rim (one pass in <see cref="CrownRollSeconds"/> s), fades out there into the rolling ring and comes back in at the top.
    /// The roll carries most of the blur; every crown, inner and ring puff also <b>churns</b> gently on a small loop of its own
    /// (<see cref="ChurnShare"/> of its size, <see cref="ChurnRate"/> to ChurnRate + <see cref="ChurnRateSpread"/> rad/s: 0.24-0.48 turns
    /// a second, near the cap's own boil, so it reads as billowing, not as circling). The exposure for the mushroom is
    /// <see cref="Smear"/> x the shutter (0.42 s, as the other blast puffs; it was 0.28 s). The blur still follows only real motion:
    /// nothing is smeared that does not move. Option off = no blur, as before.</item>
    /// <item><b>Base surge</b>: the 64 ground puffs are drawn by the fog shader (OzBlastFog): flipbook dust, soft radial edge, thinning
    /// smoothly where the ground cuts them, a slower fade in (<see cref="FogIn"/> s), sorted back to front like the mushroom's own
    /// lists. Their place, size and timing are unchanged.</item>
    /// </list>
    /// </summary>
    public sealed partial class OpenZNukeMushroom
    {
        /// <summary>Exposure of the mushroom's puffs in shutters (0.07 s each): 6 = 0.42 s (was OzFxSmooth.SmearBigPuff 4 = 0.28 s).</summary>
        public static float Smear = 6f;
        /// <summary>The crown's roll: seconds for a puff to travel from the top of the dome to the rim (0 = no roll, the old crown).</summary>
        public static float CrownRollSeconds = 6.5f;
        /// <summary>Each puff's own churn loop, as a share of its size (0 = none), and its pace (rad/s: rate + spread x the puff's own random).</summary>
        public static float ChurnShare = 0.1f, ChurnRate = 1.5f, ChurnRateSpread = 1.5f;
        /// <summary>Base surge as fog: on (the fog shader there) / off (the old lit dust puffs).</summary>
        public static bool BaseSurgeFog = true;
        /// <summary>Fade-in of the fog puffs of the base surge (s) and their opacity x.</summary>
        public static float FogIn = 1.6f, FogAlpha = 0.85f;

        ParticleSystem kdFog; Material kdFogMat; ParticleSystem.Particle[] kdArr; float[] kdKey; int kdN; Vector3 kdEye;
        float[] kdSize, kdVis; Vector3[] kdChurnVel; float kdRollA = 1f;
        static readonly int KdSmearGlobal = Shader.PropertyToID("_OZFxSmear"), KdSmearMat = Shader.PropertyToID("_Smear");
        /// <summary>Proof: the base-surge puffs drawn as fog this frame.</summary>
        public int FogLive { get; private set; }

        /// <summary>Build: the base surge's fog system (when the fog shader is there).</summary>
        void KdBuild()
        {
            if (!BaseSurgeFog || !OzBlastFog.Enabled) return;
            kdFogMat = OzBlastFog.NewMaterial("NukeMushroom fog");
            if (!kdFogMat) return;
            kdFog = OzBlastFog.MakeSystem("NukeMushroom_Fog", transform, kdFogMat, false, out _);
            kdArr = new ParticleSystem.Particle[DustCount + 8]; kdKey = new float[DustCount + 8];
        }

        /// <summary>The crown / inner puff's place on the dome (0 top .. 1 rim, as q.b before) while the cap rolls; fades it at the
        /// rim and at the top where it starts over. The old fixed place when the roll is off.</summary>
        float KdCrownB(in Puff q, ref float alpha)
        {
            kdRollA = 1f;
            if (CrownRollSeconds <= 0f) return q.b;
            float bmax = q.k == Kind.Inner ? 0.8f : 0.92f;
            float f0 = Mathf.Clamp01(q.b / bmax); f0 *= f0;                        // area share (the golden spiral is even by area)
            float f = Mathf.Repeat(f0 + t / CrownRollSeconds * (0.85f + 0.3f * q.rnd), 1f);
            kdRollA = Mathf.SmoothStep(0f, 1f, f / 0.07f) * (1f - Mathf.SmoothStep(0f, 1f, (f - 0.86f) / 0.14f));
            alpha *= kdRollA;
            return Mathf.Sqrt(f) * bmax;
        }

        /// <summary>The puff's own churn: a small loop at its own pace (m). Also keeps the puff's size, how visible the roll leaves it and
        /// the churn's own velocity, for the blur proof.</summary>
        Vector3 KdChurn(in Puff q, float size, int i)
        {
            if (kdSize == null || kdSize.Length != puffs.Length) { kdSize = new float[puffs.Length]; kdVis = new float[puffs.Length]; kdChurnVel = new Vector3[puffs.Length]; }
            kdSize[i] = size; kdVis[i] = q.k == Kind.Torus ? 1f : kdRollA; kdRollA = 1f;
            if (ChurnShare <= 0f) { kdChurnVel[i] = Vector3.zero; return Vector3.zero; }
            float w = ChurnRate + ChurnRateSpread * q.c, ph = q.rnd * 6.2831853f + t * w, ph2 = ph * 0.7f + q.c * 3f;
            float r = size * ChurnShare;
            kdChurnVel[i] = new Vector3(-Mathf.Sin(ph) * r * w, Mathf.Cos(ph2) * r * 0.6f * 0.7f * w, Mathf.Cos(ph) * r * w);
            return new Vector3(Mathf.Cos(ph) * r, Mathf.Sin(ph2) * r * 0.6f, Mathf.Sin(ph) * r);
        }

        /// <summary>A base-surge puff goes to the fog system (true) or stays a lit puff (false: no fog).</summary>
        bool KdToFog(ParticleSystem.Particle pt)
        {
            if (!kdFog || kdArr == null || kdN >= kdArr.Length) return false;
            if (kdN == 0) { var cam = Camera.main; kdEye = cam ? cam.transform.position : transform.position; }
            var c = pt.startColor; float a = c.a / 255f * Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.05f, FogIn)) * FogAlpha;
            pt.startColor = new Color32(c.r, c.g, c.b, (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255));
            pt.startSize *= 1.25f;                                                  // a fog ball, wider than the lit puff's body
            kdKey[kdN] = -(pt.position - kdEye).sqrMagnitude;                       // back to front (premultiplied fog balls overlap)
            kdArr[kdN++] = pt;
            return true;
        }

        /// <summary>End of the frame's puffs: the fog system gets this frame's base surge, far puffs first.</summary>
        void KdFlush()
        {
            if (kdFog)
            {
                if (kdN > 1) System.Array.Sort(kdKey, kdArr, 0, kdN);
                kdFog.SetParticles(kdArr, kdN); FogLive = kdN;
            }
            kdN = 0;
        }

        void KdDestroy() { if (kdFogMat) Destroy(kdFogMat); }

        /// <summary>Proof: the exposure the front and back puff materials really use (_Smear, in shutters).</summary>
        public void KdSmear(out float frontSmear, out float backSmear)
        {
            frontSmear = frontMat ? frontMat.GetFloat(KdSmearMat) : -1f;
            backSmear = backMat ? backMat.GetFloat(KdSmearMat) : -1f;
        }

        /// <summary>Proof: the cap's puffs (crown, inner, ring) drawn now and how many are blurred, as the puff shader draws it: the
        /// velocity the renderer gets (with the part toward / away from the camera taken out, as the shader does) x the global shutter
        /// (_OZFxSmear.x, 0 = option off) x the material's _Smear, capped at its longest stretch, as a share of the puff's size;
        /// `rollBlurred` = the same without the churn (the roll and the cloud's own motion alone).</summary>
        public void KdBlurStats(float minShare, Camera cam, out int n, out int blurred, out float meanShare, out int rollBlurred)
        {
            n = 0; blurred = 0; meanShare = 0f; rollBlurred = 0;
            if (puffs == null || lastVel == null || prevPos == null || kdSize == null || kdVis == null || kdChurnVel == null) return;
            var g = Shader.GetGlobalVector(KdSmearGlobal);
            float expo = g.x * (frontMat ? frontMat.GetFloat(KdSmearMat) : Smear);
            float most = Mathf.Min(g.y > 0f ? g.y : 0.3f, 0.3f);
            Vector3 eye = cam ? cam.transform.position : Vector3.zero;
            for (int i = 0; i < puffs.Length && i < kdSize.Length; i++)
            {
                var k = puffs[i].k; if (k != Kind.Crown && k != Kind.Inner && k != Kind.Torus) continue;
                if (kdSize[i] <= 0.05f || kdVis[i] < 0.05f) continue;                                   // faded out at the rim / the top of its roll
                var ray = cam ? (prevPos[i] - eye).normalized : Vector3.zero;
                var v = lastVel[i]; v -= ray * Vector3.Dot(v, ray);
                var vr = lastVel[i] - kdChurnVel[i]; vr -= ray * Vector3.Dot(vr, ray);
                float share = Mathf.Min(most, v.magnitude * expo / kdSize[i]);
                float shareR = Mathf.Min(most, vr.magnitude * expo / kdSize[i]);
                n++; meanShare += share; if (share >= minShare) blurred++; if (shareR >= minShare) rollBlurred++;
            }
            if (n > 0) meanShare /= n;
        }
    }
}
