// OpenZ (GPL-3.0)
// The red-button insert uses a SEPARATE bronze hand-and-forearm prop, not his
// rigged / retargeted arm. A properly closed armoured fist modelled in the hammer-fist pose (rounded bevelled plates: four knuckle
// caps, curled finger plates, the thumb wrapped over the index and middle finger, a dorsal plate, a wrist cuff, a vambrace with a
// ridge and rivets), wrist bent toward the pinky so the forearm rises steeply (about 50 deg) behind it. In the insert it swings
// down into frame from above around its elbow, the pinky side (the bottom of the fist) lands flat on the cap at SlamHit, bounces a
// little, rests on the pressed cap, then lifts and leaves the frame up and back before the cut to the wide shot. His own right arm
// stays in its animation pose (lowered, out of the insert) while the prop is on. PropFist = false restores the IK arm press.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        /// <summary></summary>
        public static bool PropFist = true;
        /// <summary>Prop pose knobs: wrist bend toward the pinky, fist tilt, forearm length to the swing pivot (m), raise angles (deg).</summary>
        public static float FistUlnar = 35f, FistTilt = -15f, FistElbow = 0.40f, FistEnter = 80f, FistWind = 62f, FistLeave = 72f, FistBounce = 4.5f;

        Transform fistRoot, fistArm; Vector3 fistPos0, fistElbow, fistAxis; Quaternion fistRot0; bool fistWhoosh;
        public float FistAngle { get; private set; }
        public float FistGap { get; private set; }

        static Material BronzeLit(Color32 c, float smooth, float metal)
        {
            var m = Lit(c, smooth, metal);
            if (m && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            return m;
        }

        void EnsureFist()
        {
            if (fistRoot) return;
            // right forearm / hand vertices: plates ~113-121, mid 87,72,57, grooves 55,46,37), no longer a shiny orange-red metal tube
            var bronze = BronzeLit(new Color32(114, 89, 64, 255), 0.45f, 0.3f);
            var bronzeHi = BronzeLit(new Color32(134, 108, 80, 255), 0.5f, 0.32f);
            var bronzeDk = BronzeLit(new Color32(70, 57, 45, 255), 0.4f, 0.28f);
            var glove = BronzeLit(new Color32(56, 47, 38, 255), 0.36f, 0.25f);                // the dark joints / grooves between his plates
            // pale olive lips / rivets, dark joint sleeve, grey elbow ring (Zod2_Body_BaseColor clusters: olive 101,99,67, pale 139,129,93, joints 37,30,25)
            Material hand = bronze, handHi = bronzeHi, handDk = bronzeDk, cuff = bronzeDk, plate = bronze, plateHi = bronzeHi, ring = bronzeDk;
            if (ModelV2)
            {
                hand = BronzeLit(new Color32(66, 66, 63, 255), 0.5f, 0.45f); handHi = BronzeLit(new Color32(92, 92, 88, 255), 0.55f, 0.45f); handDk = BronzeLit(new Color32(46, 45, 43, 255), 0.45f, 0.4f);
                cuff = BronzeLit(new Color32(101, 99, 67, 255), 0.45f, 0.3f); plate = cuff; plateHi = BronzeLit(new Color32(128, 122, 86, 255), 0.5f, 0.3f);
                glove = BronzeLit(new Color32(37, 32, 27, 255), 0.3f, 0.2f); ring = handHi;
            }
            var root = new GameObject("HammerFistProp") { layer = Layer };
            root.transform.SetParent(transform, false);
            fistRoot = root.transform;
            var arm = new GameObject("Arm") { layer = Layer }; arm.transform.SetParent(fistRoot, false); fistArm = arm.transform;
            // ---- the fist, hand frame: +X knuckles (front), +Y thumb side, +Z palm side, -Z the plated back of the hand
            FistPart("Core", fistArm, new Vector3(0.034f, -0.002f, 0.004f), new Vector3(0.098f, 0.112f, 0.066f), 0.022f, handDk);
            FistPart("Dorsal", fistArm, new Vector3(0.030f, 0f, -0.034f), new Vector3(0.088f, 0.106f, 0.020f), 0.008f, hand);
            FistPart("DorsalRib", fistArm, new Vector3(0.026f, 0f, -0.045f), new Vector3(0.060f, 0.022f, 0.010f), 0.004f, handHi);
            float[] ys = { 0.040f, 0.0135f, -0.0135f, -0.040f };
            for (int i = 0; i < 4; i++)
            {
                float k = i == 3 ? 0.88f : 1f, back = i == 3 ? 0.007f : i == 0 ? 0.002f : 0f, y = ys[i];
                FistPart("Knuckle" + i, fistArm, new Vector3(0.082f - back, y, -0.024f), new Vector3(0.034f, 0.026f, 0.030f) * k, 0.010f, handHi);
                FistPart("Proximal" + i, fistArm, new Vector3(0.096f - back, y, 0.008f), new Vector3(0.030f, 0.025f, 0.050f) * k, 0.011f, hand);
                FistPart("Middle" + i, fistArm, new Vector3(0.072f - back, y, 0.038f), new Vector3(0.044f, 0.024f, 0.026f) * k, 0.010f, hand);
            }
            FistSeg("Thumb0", fistArm, new Vector3(0.002f, 0.050f, 0.010f), new Vector3(0.042f, 0.046f, 0.034f), 0.032f, 0.030f, handDk);
            FistSeg("Thumb1", fistArm, new Vector3(0.040f, 0.046f, 0.032f), new Vector3(0.078f, 0.026f, 0.050f), 0.028f, 0.027f, hand);
            FistPart("ThumbNailPlate", fistArm, new Vector3(0.070f, 0.031f, 0.055f), new Vector3(0.026f, 0.020f, 0.010f), 0.005f, handHi);
            FistPart("Heel", fistArm, new Vector3(0.018f, -0.046f, 0.016f), new Vector3(0.064f, 0.030f, 0.050f), 0.013f, hand);
            // ---- wrist cuff + forearm (bent toward the thumb side = the wrist's pinky-side bend)
            FistPart("Cuff", fistArm, new Vector3(-0.022f, 0f, 0f), new Vector3(0.042f, 0.132f, 0.112f), 0.014f, cuff);
            FistPart("CuffBand", fistArm, new Vector3(-0.022f, 0f, 0f), new Vector3(0.016f, 0.140f, 0.120f), 0.006f, plateHi);
            var fore = new GameObject("Forearm") { layer = Layer }; fore.transform.SetParent(fistArm, false);
            fore.transform.localPosition = new Vector3(-0.030f, 0f, 0f); fore.transform.localRotation = Quaternion.Euler(0f, 0f, -FistUlnar);
            // (each with a raised lip toward the wrist, a centre ridge and two rivets on the camera side), the elbow ring
            FistPart("Sleeve", fore.transform, new Vector3(-0.190f, 0f, 0f), new Vector3(0.330f, 0.090f, 0.080f), 0.026f, glove);
            for (int p = 0; p < 3; p++)
            {
                float cx = -0.078f - p * 0.104f, sw = 1f + p * 0.035f;                                  // the plates widen a little toward the elbow
                FistPart("Plate" + p, fore.transform, new Vector3(cx, 0f, 0f), new Vector3(0.094f, 0.104f * sw, 0.094f * sw), 0.024f, plate);
                FistPart("PlateLip" + p, fore.transform, new Vector3(cx + 0.044f, 0f, 0f), new Vector3(0.014f, 0.110f * sw, 0.100f * sw), 0.006f, plateHi);
                FistPart("PlateRidge" + p, fore.transform, new Vector3(cx - 0.004f, 0f, -0.047f * sw), new Vector3(0.070f, 0.020f, 0.010f), 0.004f, plateHi);
                foreach (var y in new[] { -0.036f, 0.036f })
                    FistPart("Rivet" + p, fore.transform, new Vector3(cx - 0.032f, y * sw, -0.047f * sw), new Vector3(0.012f, 0.012f, 0.008f), 0.004f, plateHi);
            }
            FistPart("ElbowRing", fore.transform, new Vector3(-0.355f, 0f, 0f), new Vector3(0.036f, 0.124f, 0.112f), 0.012f, ring);
            // ---- the impact pose in the root frame (root: +Z toward the main camera, +X his right, +Y up)
            var xh = new Vector3(-0.756f, 0f, 0.655f).normalized;                       // knuckles toward the camera and his left
            var zh = Vector3.Cross(xh, Vector3.up);                                      // palm side: his left and away (the back of the hand faces the camera)
            fistRot0 = Quaternion.LookRotation(zh, Vector3.up) * Quaternion.Euler(0f, 0f, FistTilt);
            fistArm.localPosition = Vector3.zero; fistArm.localRotation = fistRot0;
            fistRoot.position = Vector3.zero; fistRoot.rotation = Quaternion.identity;
            // the lowest point of the fist (the pinky side) lands on the cap centre
            Vector3 low = Vector3.zero; float ly = float.PositiveInfinity;
            foreach (var mf in fistArm.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue; var vs = mf.sharedMesh.vertices; var tr = mf.transform;
                for (int i = 0; i < vs.Length; i++) { var p = fistRoot.InverseTransformPoint(tr.TransformPoint(vs[i])); if (p.y < ly) { ly = p.y; low = p; } }
            }
            fistPos0 = -low + Vector3.up * 0.002f;
            var foreDir = fistRot0 * (Quaternion.Euler(0f, 0f, -FistUlnar) * Vector3.left);
            fistElbow = fistPos0 + fistRot0 * new Vector3(-0.030f, 0f, 0f) + foreDir * FistElbow;
            fistAxis = Vector3.Cross(foreDir, Vector3.up).normalized;
            // the raise must lift the fist: flip the hinge if +10 deg moves it down
            var test = Quaternion.AngleAxis(10f, fistAxis) * (fistPos0 - fistElbow) + fistElbow;
            if (test.y < fistPos0.y) fistAxis = -fistAxis;
            fistArm.localPosition = fistPos0;
            root.SetActive(false);
            Debug.Log("[ZodCameo] hammer-fist prop built: " + fistArm.GetComponentsInChildren<MeshRenderer>(true).Length + " bronze parts, forearm elevation " +
                      (90f - Vector3.Angle(foreDir, Vector3.up)).ToString("F0") + " deg, pinky-side contact 2 mm over the cap");
        }

        Renderer FistPart(string name, Transform parent, Vector3 pos, Vector3 size, float r, Material m)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(parent, false); go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = RoundedBox(size, r);
            go.AddComponent<MeshRenderer>();
            return Dress(go, m, true);
        }

        /// <summary>A rounded segment from a to b (hand frame), thickness w x d.</summary>
        void FistSeg(string name, Transform parent, Vector3 a, Vector3 b, float w, float d, Material m)
        {
            var r = FistPart(name, parent, (a + b) * 0.5f, new Vector3((b - a).magnitude + w * 0.6f, w, d), Mathf.Min(w, d) * 0.45f, m);
            r.transform.localRotation = Quaternion.FromToRotation(Vector3.right, b - a);
        }

        static readonly Dictionary<long, Mesh> roundedBoxes = new Dictionary<long, Mesh>();

        /// <summary>A closed rounded box (bevelled edges: two rows per edge, analytic smooth normals), size in m, centred.</summary>
        public static Mesh RoundedBox(Vector3 size, float r)
        {
            long key = (long)Mathf.RoundToInt(size.x * 2000f) * 73856093L ^ (long)Mathf.RoundToInt(size.y * 2000f) * 19349663L ^ (long)Mathf.RoundToInt(size.z * 2000f) * 83492791L ^ (long)Mathf.RoundToInt(r * 2000f);
            if (roundedBoxes.TryGetValue(key, out var cached) && cached) return cached;
            Vector3 h = size * 0.5f; r = Mathf.Min(r, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f);
            Vector3 hi = h - Vector3.one * r;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            for (int f = 0; f < 6; f++)
            {
                int ax = f / 2, a1 = (ax + 1) % 3, a2 = (ax + 2) % 3; float sgn = f % 2 == 0 ? 1f : -1f;
                float[] c1 = { -h[a1], -h[a1] + r * 0.29f, -h[a1] + r, h[a1] - r, h[a1] - r * 0.29f, h[a1] };
                float[] c2 = { -h[a2], -h[a2] + r * 0.29f, -h[a2] + r, h[a2] - r, h[a2] - r * 0.29f, h[a2] };
                int b = verts.Count;
                for (int i = 0; i < 6; i++)
                    for (int j = 0; j < 6; j++)
                    {
                        var p = Vector3.zero; p[ax] = sgn * h[ax]; p[a1] = c1[i]; p[a2] = c2[j];
                        var pin = new Vector3(Mathf.Clamp(p.x, -hi.x, hi.x), Mathf.Clamp(p.y, -hi.y, hi.y), Mathf.Clamp(p.z, -hi.z, hi.z));
                        var n = p - pin;
                        if (n.sqrMagnitude < 1e-12f) { n = Vector3.zero; n[ax] = sgn; } else n.Normalize();
                        verts.Add(pin + n * r); norms.Add(n);
                    }
                for (int i = 0; i < 5; i++)
                    for (int j = 0; j < 5; j++)
                    {
                        int v0 = b + i * 6 + j, v1 = v0 + 1, v2 = v0 + 6, v3 = v2 + 1;
                        if (sgn > 0f) { tris.Add(v0); tris.Add(v3); tris.Add(v1); tris.Add(v0); tris.Add(v2); tris.Add(v3); }
                        else { tris.Add(v0); tris.Add(v1); tris.Add(v3); tris.Add(v0); tris.Add(v3); tris.Add(v2); }
                    }
            }
            var m = new Mesh { name = "OZ_RoundedBox" };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0); m.RecalculateBounds(); m.RecalculateTangents();
            roundedBoxes[key] = m;
            return m;
        }

        /// <summary>ApplySlam (prop mode): places and swings the prop on the cap; u = stage seconds since Slam, press = cap travel 0..1.</summary>
        void ApplyFist(float u, float press, float w)
        {
            EnsureFist();
            if (!fistRoot) return;
            bool on = buttonPlaced && w > 0f && u >= 0f && u < slamDuration + 0.05f;
            if (fistRoot.gameObject.activeSelf != on) fistRoot.gameObject.SetActive(on);
            if (!on) return;
            Vector3 up = transform.up;
            fistRoot.position = buttonTop + up * (CapTop - CapTravel * Mathf.Max(0f, press));
            fistRoot.rotation = Quaternion.LookRotation(slamFwd, up);
            float a, lift = 0f;
            if (u < FistStrikeFrom) a = Mathf.Lerp(FistEnter, FistWind, Mathf.SmoothStep(0f, 1f, u / FistStrikeFrom));
            else if (u < SlamHit) { float k = (u - FistStrikeFrom) / (SlamHit - FistStrikeFrom); a = FistWind * (1f - k * k); }
            else if (u < SlamHit + 0.16f) a = FistBounce * Mathf.Sin(Mathf.PI * (u - SlamHit) / 0.16f);
            else if (u < SlamRelease) a = 0f;
            else { float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - SlamRelease) / FistLiftSeconds)); a = FistLeave * k; lift = 0.25f * k; }
            var q = Quaternion.AngleAxis(a, fistAxis);
            fistArm.localPosition = fistElbow + q * (fistPos0 - fistElbow) + Vector3.up * lift;
            fistArm.localRotation = q * fistRot0;
            FistAngle = a;
            FistGap = a <= 0.01f && lift <= 0f ? 0.002f : (fistArm.localPosition - fistPos0).y;
            if (!fistWhoosh && u >= 0.08f && u < SlamHit) { fistWhoosh = true; FistSwing?.Invoke(); }
        }

        /// <summary>Raised when the prop fist starts its swing down (ZodBriefing plays the whoosh).</summary>
        public System.Action FistSwing;
        public static float FistStrikeFrom = 0.17f, FistLiftSeconds = 0.13f;

        /// <summary>Console offset toward the camera in prop mode (m): his body and his lowered arm stay behind it.</summary>
        public static float PropConsoleForward = 0.25f;

        /// <summary>Prop mode: his own right arm hangs at his side (a little out and back) for the whole insert, so only the prop
        /// fist is seen on the button (first Play check: his animated hand crossed the insert after the hit).</summary>
        void LowerArm(float w)
        {
            if (!uArm || !fArm || !hand || w <= 0f) return;
            Vector3 up = transform.up, A = uArm.position, B = fArm.position, C = hand.position;
            float la = (B - A).magnitude, lb = (C - B).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Vector3 T = A - up * (la + lb) * 0.9f + slamRight * 0.12f - slamFwd * 0.10f;
            Quaternion u0 = uArm.rotation, f0 = fArm.rotation, rUp, r1;
            SolveArm(T, la, lb, out rUp, out r1);
            uArm.rotation = Quaternion.Slerp(u0, rUp * u0, w);
            fArm.rotation = Quaternion.Slerp(f0, rUp * r1 * f0, w);
        }

        void HideFist() { if (fistRoot) fistRoot.gameObject.SetActive(false); fistWhoosh = false; }

        /// <summary>
        /// in the insert (shadows only, so the console keeps his shade), so the prop arm never reads as a third arm.</summary>
        public static bool HideBodyInInsert = true;
        Renderer[] bodyRs; UnityEngine.Rendering.ShadowCastingMode[] bodyModes; bool bodyHidden;

        void HideBody(bool hide)
        {
            if (hide == bodyHidden || !model) return;
            if (bodyRs == null)
            {
                bodyRs = model.GetComponentsInChildren<Renderer>(true);
                bodyModes = new UnityEngine.Rendering.ShadowCastingMode[bodyRs.Length];
                for (int i = 0; i < bodyRs.Length; i++) if (bodyRs[i]) bodyModes[i] = bodyRs[i].shadowCastingMode;
            }
            for (int i = 0; i < bodyRs.Length; i++)
            {
                var r = bodyRs[i]; if (!r || r is ParticleSystemRenderer) continue;
                r.shadowCastingMode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : bodyModes[i];
            }
            bodyHidden = hide;
        }

        /// <summary>Prop-mode insert camera from the cap (toward the main camera, up, to his left; m), aim height over the cap, FOV:
        /// tighter and lower than the IK-arm insert, the console + button fill the frame.</summary>
        public static Vector3 PropInsert = new Vector3(0.50f, 0.27f, 0.07f);
        public static float PropInsertAim = 0.10f, PropInsertFov = 40f;

        /// <summary>Proof: the prop state (swing angle, gap over the cap top in mm, active).</summary>
        public string FistStatus() => "fist prop " + (PropFist ? "ON" : "off") + (fistRoot && fistRoot.gameObject.activeSelf ? " shown" : " hidden") +
                                       ", swing " + FistAngle.ToString("F1") + " deg, gap over cap " + (FistGap * 1000f).ToString("F0") + " mm, body " + (bodyHidden ? "hidden (shadows only)" : "drawn");
    }
}
