using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// a square lid, an open shaft through the turret roof down to the hull deck, whose faces flickered) with a round hatch
    /// built at run time on the turret: an armoured base plate that closes the old square cut-out, a round collar, a dark
    /// closed well (inward walls + floor, nothing co-planar with the turret) and a round lid on the vehicle's own
    /// *_HatchHinge, so BoardingView keeps opening it with Zod's lid timing (ZVehicle::ProcessLid) and the commander pops out
    /// View only, no sim state. BoardingView.RigOf calls Ensure before it reads the hinge; idempotent.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoundHatch : MonoBehaviour
    {
        class Spec
        {
            public string parent, prefix;
            public Vector3 center;          // parent-local, rest pose: the roof point under the hatch centre
            public float tilt;              // roof slope, deg (+ = the rear is higher)
            public float rOut, rIn, bottom, top, floor;          // collar radii; heights in the hatch frame (0 = the roof point)
            public bool plate; public float plateHx, plateHz, plateRc, plateTop, plateBottom;
            public float lidR, lidT = 0.06f, lidDome = 0.022f;
            public bool useFrac; public Vector3 frac;   // vehicles_nb_v1 NB24: centre = a fraction of the parent's mesh bounds, sizes in metres
            public float yaw;                           // DV87: the hatch frame turned about the roof's up axis, deg (the hinge sits on the frame's +z side)
        }

        // Measured on the GLBs (Unity axes, the models' own metres, rest pose). Light: square cut-out x +-0.565, z -0.08..1.08
        // (model), roof 3.28 front / 3.22 rear; mech: cut-out x -0.48..0.68, z -0.48..0.72, roof plane 3.93 + 0.145 z;
        // heavy: hatch x +-0.2, z 0.62..0.82 inside a raised ring (r ~0.35 around z 0.95, 2.36) with a hinge block to 2.505,
        // roof 2.24, rear beacon at (-0.46, 0.75), 0.49 from the centre.
        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            ["light"] = new Spec { parent = "TankTurretYaw", prefix = "tank_", center = new Vector3(0f, 1.5563f, 0.199f), tilt = 0f,
                                   rOut = 0.50f, rIn = 0.40f, bottom = 0.005f, top = 0.11f, floor = -0.26f,
                                   plate = true, plateHx = 0.625f, plateHz = 0.64f, plateRc = 0.18f, plateTop = 0.03f, plateBottom = -0.26f, lidR = 0.485f },
            ["medium"] = new Spec { parent = "MechWaistYaw", prefix = "mech_", center = new Vector3(0.10f, 1.563f, -0.256f), tilt = 8.3f,
                                   rOut = 0.50f, rIn = 0.40f, bottom = 0.015f, top = 0.115f, floor = -0.26f,
                                   plate = true, plateHx = 0.64f, plateHz = 0.66f, plateRc = 0.18f, plateTop = 0.035f, plateBottom = -0.26f, lidR = 0.485f },
            ["heavy"] = new Spec { parent = "HeavyTankTurretYaw", prefix = "heavy_", center = new Vector3(0f, 0.758f, 0.92f), tilt = 0f,
                                   rOut = 0.44f, rIn = 0.35f, bottom = -0.04f, top = 0.36f, floor = 0.29f, plate = false, lidR = 0.425f },
            // vehicles_nb_v1 NB24: the crane's cab roof hatch (imported m07_crane: roof y 0.045-0.06 GLB at x -0.4..-0.3, beside the boom
            // |z| <= 0.11 whose underside is 0.23 up): centre 0.689 / 0.537 / 0.702 of the Model mesh bounds (+z side, away from the door)
            ["crane"] = new Spec { parent = "Model", prefix = "crane_", useFrac = true, frac = new Vector3(0.689f, 0.537f, 0.702f), tilt = 0f,
                                   rOut = 0.32f, rIn = 0.25f, bottom = -0.03f, top = 0.09f, floor = -0.25f, plate = false, lidR = 0.305f, lidT = 0.05f },
            // DV87: the APC driver's roof hatch on the front roof plate, behind the windscreen (measured on the kit body OZ_APC_Live.glb, GLB units:
            // the plate x -0.43..-0.13, z -0.20..+0.20, top y 0.4596, flat within 1.3 cm under the collar): centre 0.647 / 0.967 / 0.500 of
            // the Model mesh bounds = 0.71 m in front of the middle, 2.16 m in front of the rear dome; the collar (0.68 m) stays on the plate
            // (0.76 m long, 1.01 m wide); the dark floor sits 2 cm above the plate; yaw -90: the hinge at the rear (the Model's -x)
            ["apc"] = new Spec { parent = "Model", prefix = "apc_", useFrac = true, frac = new Vector3(0.647f, 0.967f, 0.5f), tilt = 0f, yaw = -90f,
                                 rOut = 0.34f, rIn = 0.27f, bottom = -0.03f, top = 0.10f, floor = 0.02f, plate = false, lidR = 0.325f, lidT = 0.05f },
        };

        const int Seg = 48;
        const float Gap = 0.004f;                                   // lid bottom above the collar top (faces opposite, never co-planar)

        public Transform Frame { get; private set; }
        public Transform Hinge { get; private set; }
        public Vector3 RimWorld => Frame ? Frame.TransformPoint(new Vector3(0f, spec.top, 0f)) : transform.position;
        Spec spec; Quaternion hingeRest;

        /// <summary>Builds the round hatch once (light / medium / heavy); returns null for other types. On a rebuilt rig the
        /// hinge is put back to rest (BoardingView reads its rest rotation right after).</summary>
        public static RoundHatch Ensure(Transform root, string type)
        {
            if (!root || type == null || !Specs.TryGetValue(type, out var s)) return null;
            var rh = root.GetComponent<RoundHatch>();
            if (rh) { if (rh.Hinge) rh.Hinge.localRotation = rh.hingeRest; return rh.Frame ? rh : null; }
            rh = root.gameObject.AddComponent<RoundHatch>();
            rh.spec = s;
            if (!rh.Build(root)) { return null; }
            var tp = root.GetComponent<TeamPaint>(); if (tp) tp.Apply();                                  // team / charred blocks on the new parts
            return rh;
        }

        /// <summary>DV87 (WheeledWreckBreakup.Tear, the APC): the roof hatch leaves the wreck with the blast: its lid flies off as a
        /// wreck piece of its own, the collar goes. Two of the three modelled APC wrecks are torn open right under it
        /// (apc_crane_rebuild_v1: the roof hole of V2 and the front roof hole of V3); a whole collar would hang over the hole.</summary>
        public static bool BreakOff(UnitView v, Vector3 centre, float power, int id)
        {
            var rh = v ? v.GetComponent<RoundHatch>() : null;
            if (!rh || !rh.Frame) return false;
            var lid = rh.Hinge; rh.Hinge = null;
            if (lid && !WreckPieces.Single(lid, centre, power * 0.8f, id + 4409)) Destroy(lid.gameObject);
            Destroy(rh.Frame.gameObject); rh.Frame = null;
            return true;
        }

        bool Build(Transform root)
        {
            Transform parent = null, hinge = null, hatchTop = null;
            var old = new List<GameObject>();
            Material metalSrc = null, anySrc = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (!parent && n == spec.parent) parent = t;
                else if (!hinge && n == spec.prefix + "HatchHinge") hinge = t;
                else if (!hatchTop && n == spec.prefix + "Board_HatchTop") hatchTop = t;
                if (n.StartsWith(spec.prefix + "HatchRim") || n == spec.prefix + "HatchLid" || n == spec.prefix + "HatchHandle" || n == "HeavyTank_hatch_lid")
                    old.Add(t.gameObject);
                var mr = t.GetComponent<MeshRenderer>();
                if (mr && mr.sharedMaterial && mr.sharedMaterial.shader && mr.sharedMaterial.shader.name.Contains("TeamLit"))
                {
                    var m = mr.sharedMaterial; bool plain = !m.HasProperty(BaseMapId) || !m.GetTexture(BaseMapId);
                    if (plain && !metalSrc && (m.name.Contains("metal") || m.name.Contains("detail"))) metalSrc = m;
                    if (!anySrc) anySrc = m;
                }
            }
            if (!parent) { Debug.LogWarning($"[OpenZ] RoundHatch: {root.name} has no {spec.parent}"); return false; }
            var mats = Mats(metalSrc ? metalSrc : anySrc);

            // the old square hatch: rim bars, square lid + handle (under the hinge: their meshes go first, BoardingView's
            // PickHingeSign measures the hinge children in this frame)
            foreach (var g in old)
            {
                var mf = g.GetComponent<MeshFilter>(); if (mf) mf.sharedMesh = null;
                g.SetActive(false); Destroy(g);
            }

            var frame = new GameObject("OZ_RoundHatch").transform;
            frame.SetParent(parent, false);
            var center = spec.center; float unit = 1f;                                                 // vehicles_nb_v1 NB24: fraction of the parent's mesh, metres
            if (spec.useFrac)
            {
                var pmf = parent.GetComponent<MeshFilter>(); var pb = pmf && pmf.sharedMesh ? pmf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                center = pb.min + Vector3.Scale(spec.frac, pb.size);
                unit = Mathf.Abs(root.lossyScale.x) / Mathf.Max(1e-4f, Mathf.Abs(parent.lossyScale.x));
            }
            frame.localPosition = center; frame.localScale = Vector3.one * unit;
            frame.localRotation = Quaternion.AngleAxis(spec.yaw, Vector3.up) * Quaternion.AngleAxis(-spec.tilt, Vector3.right);   // -x rotation lifts +z (the rear); DV87: yaw first turns the hinge side (0 for every tank and the crane)
            Frame = frame;

            Part(frame, "OZ_Hatch_Collar", CollarMesh(), mats[0], mats[2]);
            if (spec.plate) Part(frame, "OZ_Hatch_Plate", PlateMesh(), mats[0]);

            // the lid on the model's own hinge: the axis just behind the collar at mid lid thickness (it stands up behind the
            // commander without cutting the collar), rest = the hatch frame's orientation
            var axis = new Vector3(0f, spec.top + Gap + spec.lidT * 0.5f, spec.rOut + 0.02f);
            if (!hinge) { hinge = new GameObject(spec.prefix + "HatchHinge").transform; hinge.SetParent(parent, false); }
            hinge.localPosition = center + frame.localRotation * (axis * unit); if (spec.useFrac) hinge.localScale = Vector3.one * unit;   // vehicles_nb_v1 NB24
            hinge.localRotation = frame.localRotation; hingeRest = hinge.localRotation;
            Hinge = hinge;
            Part(hinge, "OZ_Hatch_Lid", LidMesh(), mats[1], mats[0]);

            // boarding / commander anchor over the hatch centre (heavy: its hull anchor sat 0.4 m in front of the cupola)
            if (hatchTop && hatchTop.parent)
            {
                var l = hatchTop.parent.InverseTransformPoint(frame.position);
                hatchTop.localPosition = new Vector3(l.x, hatchTop.localPosition.y, l.z);
            }
            return true;
        }

        static void Part(Transform parent, string name, Mesh mesh, params Material[] m)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
        }

        // ------------------------------------------------------------------ materials (clones of the vehicle's plain TeamLit metal)
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"),
                            SmoothId = Shader.PropertyToID("_Smoothness"), TeamModeId = Shader.PropertyToID("_TeamMode");
        static readonly Dictionary<Material, Material[]> matCache = new Dictionary<Material, Material[]>();

        /// <summary>[0] gunmetal collar / plate / handle (the old rim's DV3_metal colour), [1] light warm-grey lid (neutral, never
        /// team: low saturation stays out of TeamLit's paint band), [2] near-black well (clean, no texture, low gloss).</summary>
        static Material[] Mats(Material src)
        {
            if (!src) src = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (matCache.TryGetValue(src, out var m) && m[0]) return m;
            m = new[] { Clone(src, "OZ_HatchMetal", new Color(0.4196f, 0.4392f, 0.4745f), 0.35f),
                        Clone(src, "OZ_HatchLid", new Color(0.64f, 0.62f, 0.58f), 0.32f),
                        Clone(src, "OZ_HatchWell", new Color(0.035f, 0.037f, 0.042f), 0.08f) };
            matCache[src] = m;
            return m;
        }

        static Material Clone(Material src, string name, Color c, float smooth)
        {
            var m = new Material(src) { name = name };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c); else m.color = c;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, smooth);
            if (m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);                                   // hatch parts are never team paint
            return m;
        }

        // ------------------------------------------------------------------ meshes (hatch frame: y up, rear +z, metres)
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            public readonly List<int>[] sub;
            public MB(int subs) { sub = new List<int>[subs]; for (int i = 0; i < subs; i++) sub[i] = new List<int>(); }
            int V(Vector3 p, Vector3 nn) { v.Add(p); n.Add(nn); return v.Count - 1; }
            /// <summary>Quad a-b-c-d with per-corner normals; the winding is picked so the front face looks along the normals.</summary>
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd)
            {
                var want = na + nb + nc + nd;
                int ia = V(a, na), ib = V(b, nb), ic = V(c, nc), id = V(d, nd);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) >= 0f) sub[s].AddRange(new[] { ia, ib, ic, ia, ic, id });
                else sub[s].AddRange(new[] { ia, ic, ib, ia, id, ic });
            }
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn) => Quad(s, a, b, c, d, nn, nn, nn, nn);
            public void Tri(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc)
            {
                int ia = V(a, na), ib = V(b, nb), ic = V(c, nc);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), na + nb + nc) >= 0f) sub[s].AddRange(new[] { ia, ib, ic });
                else sub[s].AddRange(new[] { ia, ic, ib });
            }
            public void Box(int s, Vector3 c, Vector3 h)
            {
                for (int ax = 0; ax < 3; ax++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        var nn = Vector3.zero; nn[ax] = sg;
                        int u = (ax + 1) % 3, w = (ax + 2) % 3;
                        Vector3 P(float a, float b) { var p = Vector3.zero; p[ax] = sg * h[ax]; p[u] = a * h[u]; p[w] = b * h[w]; return c + p; }
                        Quad(s, P(-1, -1), P(1, -1), P(1, 1), P(-1, 1), nn);
                    }
            }
            public void Cylinder(int s, Vector3 c, float r, float halfLen, int seg)       // along x, capped
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                    var d0 = new Vector3(0, Mathf.Cos(a0), Mathf.Sin(a0)); var d1 = new Vector3(0, Mathf.Cos(a1), Mathf.Sin(a1));
                    Quad(s, c + d0 * r - Vector3.right * halfLen, c + d0 * r + Vector3.right * halfLen, c + d1 * r + Vector3.right * halfLen, c + d1 * r - Vector3.right * halfLen, d0, d0, d1, d1);
                    Tri(s, c + Vector3.right * halfLen, c + d0 * r + Vector3.right * halfLen, c + d1 * r + Vector3.right * halfLen, Vector3.right, Vector3.right, Vector3.right);
                    Tri(s, c - Vector3.right * halfLen, c + d1 * r - Vector3.right * halfLen, c + d0 * r - Vector3.right * halfLen, Vector3.left, Vector3.left, Vector3.left);
                }
            }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(v); m.SetNormals(n); m.subMeshCount = sub.Length;
                for (int i = 0; i < sub.Length; i++) m.SetTriangles(sub[i], i);
                m.RecalculateBounds();
                return m;
            }
        }

        static Vector3 Dir(int i) { float a = i * Mathf.PI * 2f / Seg; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }

        /// <summary>Collar: outer wall + top chamfer + flat top (metal, submesh 0); the well: inward wall down to a floor disc
        /// (near-black, submesh 1); a hinge bracket on the rear wall (metal).</summary>
        Mesh CollarMesh()
        {
            var b = new MB(2);
            float ro = spec.rOut, ri = spec.rIn, y0 = spec.bottom, y1 = spec.top, ch = 0.025f, fl = spec.floor;
            for (int i = 0; i < Seg; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1), up = Vector3.up;
                var nc0 = (d0 + up).normalized; var nc1 = (d1 + up).normalized;
                b.Quad(0, d0 * ro + up * y0, d1 * ro + up * y0, d1 * ro + up * (y1 - ch), d0 * ro + up * (y1 - ch), d0, d1, d1, d0);        // outer wall
                b.Quad(0, d0 * ro + up * (y1 - ch), d1 * ro + up * (y1 - ch), d1 * (ro - ch) + up * y1, d0 * (ro - ch) + up * y1, nc0, nc1, nc1, nc0);   // chamfer
                b.Quad(0, d0 * (ro - ch) + up * y1, d1 * (ro - ch) + up * y1, d1 * ri + up * y1, d0 * ri + up * y1, up);                    // top
                b.Quad(1, d0 * ri + up * y1, d1 * ri + up * y1, d1 * ri + up * fl, d0 * ri + up * fl, -d0, -d1, -d1, -d0);                 // well wall (inward)
                b.Tri(1, up * fl, d0 * ri + up * fl, d1 * ri + up * fl, up, up, up);                                                        // well floor
            }
            b.Box(0, new Vector3(0f, y1 - 0.035f, ro + 0.012f), new Vector3(0.17f, 0.03f, 0.03f));                                         // hinge bracket
            return b.Mesh("OZ_Hatch_Collar");
        }

        /// <summary>Base plate: a rounded-square armour plate over the old square cut-out, a flat top from its outline in to
        /// under the collar, skirts down into the turret (the roof slopes 0-15 cm under it).</summary>
        Mesh PlateMesh()
        {
            var b = new MB(1);
            float yt = spec.plateTop, yb = spec.plateBottom, rin = spec.rOut - 0.03f, ch = 0.02f;
            for (int i = 0; i < Seg; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1), up = Vector3.up;
                Vector3 o0 = Outline(d0), o1 = Outline(d1);
                Vector3 n0 = OutlineNormal(o0), n1 = OutlineNormal(o1);
                Vector3 i0 = o0 - n0 * ch, i1 = o1 - n1 * ch;
                var nc0 = (n0 + up).normalized; var nc1 = (n1 + up).normalized;
                b.Quad(0, d0 * rin + up * yt, i0 + up * yt, i1 + up * yt, d1 * rin + up * yt, up);                                           // top
                b.Quad(0, i0 + up * yt, o0 + up * (yt - ch), o1 + up * (yt - ch), i1 + up * yt, nc0, nc0, nc1, nc1);                       // chamfer
                b.Quad(0, o0 + up * (yt - ch), o0 + up * yb, o1 + up * yb, o1 + up * (yt - ch), n0, n0, n1, n1);                            // skirt
            }
            return b.Mesh("OZ_Hatch_Plate");
        }

        float SdRoundRect(Vector3 p)
        {
            float qx = Mathf.Abs(p.x) - (spec.plateHx - spec.plateRc), qz = Mathf.Abs(p.z) - (spec.plateHz - spec.plateRc);
            float ox = Mathf.Max(qx, 0f), oz = Mathf.Max(qz, 0f);
            return Mathf.Sqrt(ox * ox + oz * oz) + Mathf.Min(Mathf.Max(qx, qz), 0f) - spec.plateRc;
        }
        Vector3 Outline(Vector3 d)
        {
            float lo = 0f, hi = spec.plateHx + spec.plateHz;
            for (int k = 0; k < 40; k++) { float m = 0.5f * (lo + hi); if (SdRoundRect(d * m) < 0f) lo = m; else hi = m; }
            return d * lo;
        }
        Vector3 OutlineNormal(Vector3 p)
        {
            const float e = 1e-3f;
            var g = new Vector3(SdRoundRect(p + Vector3.right * e) - SdRoundRect(p - Vector3.right * e), 0f,
                                SdRoundRect(p + Vector3.forward * e) - SdRoundRect(p - Vector3.forward * e));
            return g.sqrMagnitude > 1e-12f ? g.normalized : p.normalized;
        }

        /// <summary>Round lid in hinge space (axis = x through the origin; the closed lid lies toward -z): a slightly domed disc
        /// with a chamfered edge and a bottom face (light grey, submesh 0), a knuckle on the axis and a grab handle (metal).</summary>
        Mesh LidMesh()
        {
            var b = new MB(2);
            float R = spec.lidR, T = spec.lidT, ch = 0.018f, dome = spec.lidDome;
            var c = new Vector3(0f, 0f, -(spec.rOut + 0.02f));                                  // lid centre (axis sits at mid thickness)
            float yb = -T * 0.5f, yt = T * 0.5f;
            var top = c + Vector3.up * (yt + dome);
            for (int i = 0; i < Seg; i++)
            {
                Vector3 d0 = Dir(i), d1 = Dir(i + 1), up = Vector3.up;
                var nd0 = (up + d0 * (dome / (R - ch))).normalized; var nd1 = (up + d1 * (dome / (R - ch))).normalized;
                b.Tri(0, top, c + d0 * (R - ch) + up * yt, c + d1 * (R - ch) + up * yt, up, nd0, nd1);                                         // dome
                var nc0 = (d0 + up).normalized; var nc1 = (d1 + up).normalized;
                b.Quad(0, c + d0 * (R - ch) + up * yt, c + d1 * (R - ch) + up * yt, c + d1 * R + up * (yt - ch), c + d0 * R + up * (yt - ch), nc0, nc1, nc1, nc0);   // chamfer
                b.Quad(0, c + d0 * R + up * (yt - ch), c + d1 * R + up * (yt - ch), c + d1 * R + up * yb, c + d0 * R + up * yb, d0, d1, d1, d0);                      // rim
                b.Tri(0, c + up * yb, c + d1 * R + up * yb, c + d0 * R + up * yb, -up, -up, -up);                                                                     // underside
            }
            b.Cylinder(1, Vector3.zero, 0.034f, 0.13f, 10);                                                                       // knuckle on the axis
            b.Box(1, new Vector3(0f, 0f, -0.035f), new Vector3(0.12f, 0.012f, 0.035f));                                          // strap to the lid
            // grab handle toward the front edge: a bar on two posts
            float hz = c.z - R * 0.5f, hy = yt + dome * 0.4f;
            b.Box(1, new Vector3(0f, hy + 0.05f, hz), new Vector3(0.12f, 0.014f, 0.016f));
            b.Box(1, new Vector3(-0.1f, hy + 0.02f, hz), new Vector3(0.014f, 0.03f, 0.014f));
            b.Box(1, new Vector3(0.1f, hy + 0.02f, hz), new Vector3(0.014f, 0.03f, 0.014f));
            return b.Mesh("OZ_Hatch_Lid");
        }
    }
}
