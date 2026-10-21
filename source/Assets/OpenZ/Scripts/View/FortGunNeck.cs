using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// look had two thin rams (a 0.2 m sleeve and a rod stretched six times: "flimsy 2 ropes" from a low free camera). This builds, in
    /// code, per side a heavy two-stage telescopic ram: a dark body hung from a clevis under the head (two ears, a pin with nuts, the
    /// rod-end eye), a chrome stage and a chrome rod that slide out of it, a steel pressure line with two port blocks along the body, and
    /// a gland on the turntable deck where the ram passes into the turntable (its base pin sits inside the turntable, on the chord of the
    /// eye's path, so the pass-through point stays inside the gland: 7 cm drift). Every part keeps its length and scale: only positions
    /// and turns change (no stretch); the stages keep at least 8 mm of overlap up to a 32 deg head tilt (the head tilts 0..30).
    /// Numbers in FortGunYaw mesh space (FBX units, x10.53 = metres), checked off the engine by the kit's tools/neck_sim.py on
    /// Fort_Split.fbx: ram 1.37 -> 3.28 m pin to pin, clear of the ring top, the head underside, the mantlet and the barrel at every tilt.
    /// (below <see cref="LowCameraDeg"/> over the fort's horizon; the RTS camera at its 55 deg pitch never gets there), else the old two rams
    /// are shown exactly as before. The swap is made only while the head sits down (everything hidden in the 0.2 m slit), never mid-tilt.
    /// View only (no sim, no deviation row). Parts are named FortGunRam_Neck_* so the fort breakup and the wreck tone treat them like the
    /// old rams (ride with the ring, dark metal burnt).</summary>
    public sealed class FortGunNeck
    {
        public enum NeckMode { Auto, Always, Off }
        /// <summary>Auto = the camera rule above; Always = the new neck at every camera (Play checks); Off = the old rams only.</summary>
        public static NeckMode Mode = NeckMode.Auto;
        /// <summary>Camera elevation over the fort's horizon (deg) below which the new neck is shown; it goes back to the old rams above
        /// HighCameraDeg (hysteresis). The RTS camera (pitch 55, any screen row) sees a fort from more than 20 deg.</summary>
        public static float LowCameraDeg = 15f, HighCameraDeg = 18f;

        // ---- design, FortGunYaw mesh space (same numbers as tools/neck_sim.py)
        const float X = 0.105f;                                     // the two rams at x +-X
        const float P1Y = 0.0549f, P1Z = 0.100f;                    // rod-end eye pin under the head (head space, rest)
        const float P0Y = -0.0697f, P0Z = 0.1372f;                  // base pin inside the turntable (yaw space)
        const float LB = 0.120f, LS = 0.110f, LR = 0.110f, OVERLAP = 0.008f, BODY0 = 0.0080f;
        const float R_BODY = 0.0170f, R_CAP = 0.0195f, R_NUT = 0.0185f, R_STAGE = 0.0125f, R_SNUT = 0.0140f, R_ROD = 0.0090f;
        const float EYE_R = 0.0066f, EYE_H = 0.0200f, EAR_IN = 0.0205f, EAR_T = 0.0040f, EAR_Z = 0.0120f, EAR_DROP = 0.0080f;
        const float EAR_TOP_IN = 0.0668f, EAR_TOP_OUT = 0.0812f;    // ear tops 3 mm into the head underside (inner / outer ear)
        const float PIN_R = 0.0040f, PIN_H = 0.0270f, NUT_R = 0.0058f;
        const float GL_Z = 0.1068f, GL_R1 = 0.031f, GL_Y0 = 0.040f, GL_Y1 = 0.0435f, GL_R2 = 0.025f, GL_Y2 = 0.0455f;
        const float LINE_OFF = R_BODY + 0.0045f, LINE_R = 0.0028f;

        sealed class Side { public float sx; public Transform body, cap, nut, stage, snut, rod, line, portA, portB; }
        readonly Side[] sides = new Side[2];
        readonly Transform yaw, head;
        readonly System.Collections.Generic.List<GameObject> all = new System.Collections.Generic.List<GameObject>();
        static Material sHose;
        bool shown;

        /// <summary>Shown now (the new neck) or the old rams; how many swaps since bind; parts built.</summary>
        public bool Shown => shown;
        public int Swaps { get; private set; }
        public int Parts => all.Count;
        /// <summary>Largest pin-to-pin length reached and the smallest stage overlap (FBX units): the Play check of "no stretch".</summary>
        public float MaxLength { get; private set; }
        public float MinOverlap { get; private set; } = 1f;

        FortGunNeck(Transform yaw, Transform head) { this.yaw = yaw; this.head = head; }

        public static FortGunNeck Build(Transform yaw, Transform head, Material dark, Material steel)
        {
            if (!yaw || !head || !dark || !steel) return null;
            if (!sHose)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) return null;
                sHose = new Material(sh) { name = "FortGunRam_Hose" };
                sHose.SetColor("_BaseColor", new Color(0.07f, 0.07f, 0.075f)); sHose.SetFloat("_Metallic", 0.2f); sHose.SetFloat("_Smoothness", 0.35f);
            }
            var n = new FortGunNeck(yaw, head);
            for (int i = 0; i < 2; i++)
            {
                float sx = i == 0 ? -1f : 1f; string k = sx < 0 ? "L" : "R"; var s = new Side { sx = sx };
                // turntable (yaw, fixed): the deck gland, two steps
                n.Fixed(yaw, $"FortGunRam_Neck_{k}_Gland", dark, PrimitiveType.Cylinder, new Vector3(sx * X, (GL_Y0 + GL_Y1) * 0.5f, GL_Z), Quaternion.identity, new Vector3(2 * GL_R1, (GL_Y1 - GL_Y0) * 0.5f, 2 * GL_R1));
                n.Fixed(yaw, $"FortGunRam_Neck_{k}_GlandTop", dark, PrimitiveType.Cylinder, new Vector3(sx * X, (GL_Y1 + GL_Y2) * 0.5f, GL_Z), Quaternion.identity, new Vector3(2 * GL_R2, (GL_Y2 - GL_Y1) * 0.5f, 2 * GL_R2));
                // head (fixed under it): clevis ears, pin, nuts, the rod-end eye (a cylinder about the pin: the same at every swing)
                var xr = Quaternion.FromToRotation(Vector3.up, Vector3.right);
                for (int e = -1; e <= 1; e += 2)
                {
                    bool outer = e * sx > 0; float top = outer ? EAR_TOP_OUT : EAR_TOP_IN, bot = P1Y - EAR_DROP;
                    n.Fixed(head, $"FortGunRam_Neck_{k}_Ear{(outer ? "Out" : "In")}", dark, PrimitiveType.Cube, new Vector3(sx * X + e * (EAR_IN + EAR_T * 0.5f), (top + bot) * 0.5f, P1Z), Quaternion.identity, new Vector3(EAR_T, top - bot, 2 * EAR_Z));
                    n.Fixed(head, $"FortGunRam_Neck_{k}_PinNut{(outer ? "Out" : "In")}", steel, PrimitiveType.Cylinder, new Vector3(sx * X + e * (EAR_IN + EAR_T + 0.0015f), P1Y, P1Z), xr, new Vector3(2 * NUT_R, 0.0015f, 2 * NUT_R));
                }
                n.Fixed(head, $"FortGunRam_Neck_{k}_Pin", steel, PrimitiveType.Cylinder, new Vector3(sx * X, P1Y, P1Z), xr, new Vector3(2 * PIN_R, PIN_H, 2 * PIN_R));
                n.Fixed(head, $"FortGunRam_Neck_{k}_Eye", dark, PrimitiveType.Cylinder, new Vector3(sx * X, P1Y, P1Z), xr, new Vector3(2 * EYE_R, EYE_H, 2 * EYE_R));
                // moving (yaw, posed every frame; fixed scales)
                s.body = n.Tube(yaw, $"FortGunRam_Neck_{k}_Body", dark, R_BODY, LB - BODY0);
                s.cap = n.Tube(yaw, $"FortGunRam_Neck_{k}_BodyCap", dark, R_CAP, 0.003f);
                s.nut = n.Tube(yaw, $"FortGunRam_Neck_{k}_BodyGland", dark, R_NUT, 0.006f);
                s.stage = n.Tube(yaw, $"FortGunRam_Neck_{k}_Stage", steel, R_STAGE, LS);
                s.snut = n.Tube(yaw, $"FortGunRam_Neck_{k}_StageGland", dark, R_SNUT, 0.004f);
                s.rod = n.Tube(yaw, $"FortGunRam_Neck_{k}_Rod", steel, R_ROD, LR);
                s.line = n.Tube(yaw, $"FortGunRam_Neck_{k}_Line", sHose, LINE_R, LB - 0.012f - 0.016f);
                s.portA = n.Part(yaw, $"FortGunRam_Neck_{k}_PortA", sHose, PrimitiveType.Cube, new Vector3(0.010f, 0.010f, 0.008f));
                s.portB = n.Part(yaw, $"FortGunRam_Neck_{k}_PortB", sHose, PrimitiveType.Cube, new Vector3(0.010f, 0.010f, 0.008f));
                n.sides[i] = s;
            }
            n.SetShown(Mode == NeckMode.Always, true);
            n.Pose();
            return n;
        }

        /// <summary>Poses the moving parts from the head's pose (call right after the head is posed); swaps old / new only while the head
        /// is down. Returns true when the new neck is shown (the caller then hides the old two rams).</summary>
        public bool Pose()
        {
            if (!yaw || !head) return false;
            float tilt = Quaternion.Angle(Quaternion.identity, head.localRotation);
            if (tilt < 0.5f)
            {
                bool want = Mode == NeckMode.Always || (Mode == NeckMode.Auto && LowCamera());
                if (want != shown) SetShown(want, false);
            }
            if (!shown) return false;
            for (int i = 0; i < 2; i++)
            {
                var s = sides[i];
                Vector3 p1 = head.localPosition + head.localRotation * new Vector3(s.sx * X, P1Y, P1Z), p0 = new Vector3(s.sx * X, P0Y, P0Z);
                Vector3 d = p0 - p1; float L = d.magnitude; if (L < 1e-5f) continue;
                Vector3 u = d / L;                                                                   // from the eye down to the base pin
                float e = Mathf.Clamp((L - LB) * 0.5f, 0f, Mathf.Min(LS, LR) - OVERLAP);             // each stage's share of the stroke
                MaxLength = Mathf.Max(MaxLength, L); MinOverlap = Mathf.Min(MinOverlap, Mathf.Min(LS, LR) - (L - LB) * 0.5f);
                var xa = Vector3.right - u * Vector3.Dot(Vector3.right, u); xa = xa.sqrMagnitude > 1e-8f ? xa.normalized : Vector3.right;
                var rot = Quaternion.LookRotation(Vector3.Cross(xa, u), u);                          // up = the ram axis, right = the pin axis
                Place(s.body, p1, u, rot, BODY0, LB);
                Place(s.cap, p1, u, rot, BODY0, BODY0 + 0.003f);
                Place(s.nut, p1, u, rot, LB - 0.006f, LB);
                Place(s.stage, p1, u, rot, LB + e - LS, LB + e);
                Place(s.snut, p1, u, rot, LB + e - 0.004f, LB + e);
                Place(s.rod, p1, u, rot, LB + 2 * e - LR, LB + 2 * e);
                Place(s.line, p1 + xa * (s.sx * LINE_OFF), u, rot, 0.016f, LB - 0.012f);
                Vector3 side = xa * (s.sx * (R_BODY + 0.002f));
                s.portA.localPosition = p1 + side + u * 0.016f; s.portA.localRotation = rot;
                s.portB.localPosition = p1 + side + u * (LB - 0.012f); s.portB.localRotation = rot;
            }
            return true;
        }

        bool LowCamera()
        {
            var cam = Camera.main; if (!cam) return false;
            Vector3 d = cam.transform.position - yaw.position; float m = d.magnitude; if (m < 1e-3f) return false;
            float el = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d / m, Vector3.up), -1f, 1f)) * Mathf.Rad2Deg;
            return el < (shown ? HighCameraDeg : LowCameraDeg);
        }

        void SetShown(bool on, bool force)
        {
            if (on == shown && !force) return;
            if (on != shown) Swaps++;
            shown = on;
            foreach (var g in all) if (g) g.SetActive(on);
        }

        static void Place(Transform t, Vector3 p1, Vector3 u, Quaternion rot, float s0, float s1)
        {
            t.localPosition = p1 + u * ((s0 + s1) * 0.5f); t.localRotation = rot;          // the scale was set once at build (never stretched)
        }

        Transform Tube(Transform parent, string name, Material m, float r, float len) =>
            Part(parent, name, m, PrimitiveType.Cylinder, new Vector3(2 * r, len * 0.5f, 2 * r));

        Transform Part(Transform parent, string name, Material m, PrimitiveType type, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>(); if (col) Object.DestroyImmediate(col);
            go.name = name; go.layer = yaw.gameObject.layer;
            go.transform.SetParent(parent, false); go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On;
            all.Add(go);
            return go.transform;
        }

        void Fixed(Transform parent, string name, Material m, PrimitiveType type, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var t = Part(parent, name, m, type, scale); t.localPosition = pos; t.localRotation = rot;
        }
    }
}
