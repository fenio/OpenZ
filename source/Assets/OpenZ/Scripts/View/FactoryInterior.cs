using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// Behind every FactoryView door (robot factory, vehicle factory, fort gate) the baked
    /// door patch of the building mesh is cut out (triangles fully inside the door opening that face out of the wall, once per source
    /// mesh: robot factory ~480, vehicle factory ~700, fort ~310 small imported triangles) and a real bay is built behind it: inward
    /// walls with gunmetal panels and ribs, a ribbed steel floor plate with an amber hazard line at the sill, a ceiling with a warm
    /// work-light strip and two lamp boxes, a crate stack and a tool rack in the back corners, and a warm point light that comes up
    /// with the door (FactoryView.Tick: intensity follows the door's openness). A gunmetal jamb frame inside the opening hides the
    /// ragged edge of the cut. Closed, the door leaves cover all of it (they sit 4.5 cm proud and 1.5 % wider than the opening).
    /// The old flat dark opening quad is switched off when the bay is built. View only.
    /// </summary>
    public static class FactoryInterior
    {
        public const float Depth = 2.6f, SideRoom = 0.35f, HeadRoom = 0.45f, Jamb = 0.09f;
        /// <summary>Proof counters.</summary>
        public static int Built, CutTris;
        static readonly Dictionary<Mesh, (Mesh m, int cut)> cutCache = new Dictionary<Mesh, (Mesh, int)>();

        /// <summary>Cuts the door patch and builds the bay under `rig` (door rectangle in rig-local metres). Returns the bay root and
        /// its lamp, or null when the building mesh cannot be cut (then the old dark opening stays).</summary>
        public static Transform Build(Transform rig, Transform building, Vector3 doorMin, Vector3 doorMax, Material baseMat, out Light lamp)
        {
            lamp = null;
            if (!rig || !building || !baseMat) return null;
            MeshFilter best = null; int bestCut = 0; Mesh bestMesh = null;
            foreach (var mf in building.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var toRig = rig.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var cut = Cut(mf.sharedMesh, toRig, doorMin, doorMax, out int n);
                if (n > bestCut) { bestCut = n; best = mf; bestMesh = cut; }
            }
            if (!best || bestCut < 8) return null;
            best.sharedMesh = bestMesh; CutTris += bestCut;
            var mats = Mats(baseMat);
            var bay = new GameObject("OZ_FactoryBay").transform; bay.SetParent(rig, false);
            float x0 = doorMin.x - SideRoom, x1 = doorMax.x + SideRoom, y0 = doorMin.y, y1 = doorMax.y + HeadRoom, zf = doorMin.z - 0.005f, zb = doorMin.z - Depth;
            float cx = (x0 + x1) * 0.5f, cz = (zf + zb) * 0.5f, hx = (x1 - x0) * 0.5f, hz = (zf - zb) * 0.5f;
            // shell: walls + ceiling (inward), dark painted steel
            Part(bay, "OZ_Bay_Shell", InwardBox(new Vector3(x0, y0 - 0.06f, zb), new Vector3(x1, y1, zf)), mats[0], false);
            // floor plate + ribs + amber sill line
            var fl = new List<(Vector3, Vector3)> { (new Vector3(cx, y0 - 0.03f, cz), new Vector3(hx, 0.03f, hz)) };
            for (float z = zb + 0.3f; z < zf - 0.2f; z += 0.38f) fl.Add((new Vector3(cx, y0 + 0.006f, z), new Vector3(hx - 0.12f, 0.006f, 0.025f)));
            Part(bay, "OZ_Bay_Floor", OzLathe.Boxes("OZ_Bay_Floor", fl), mats[1], false);
            Part(bay, "OZ_Bay_Sill", OzLathe.Boxes("OZ_Bay_Sill", new List<(Vector3, Vector3)> { (new Vector3(cx, y0 + 0.004f, zf - 0.16f), new Vector3((doorMax.x - doorMin.x) * 0.5f, 0.004f, 0.05f)) }), mats[3], false);
            // wall panels with ribs: back wall + both sides
            var pn = new List<(Vector3, Vector3)>();
            float wh = (y1 - y0) * 0.5f, wy = (y0 + y1) * 0.5f;
            for (float x = x0 + 0.45f; x < x1 - 0.3f; x += 0.7f) pn.Add((new Vector3(x, wy, zb + 0.03f), new Vector3(0.04f, wh - 0.1f, 0.03f)));
            for (float z = zb + 0.45f; z < zf - 0.35f; z += 0.7f) { pn.Add((new Vector3(x0 + 0.03f, wy, z), new Vector3(0.03f, wh - 0.1f, 0.04f))); pn.Add((new Vector3(x1 - 0.03f, wy, z), new Vector3(0.03f, wh - 0.1f, 0.04f))); }
            pn.Add((new Vector3(cx, y0 + 0.55f, zb + 0.02f), new Vector3(hx - 0.1f, 0.03f, 0.02f)));                       // a waist rail on the back wall
            Part(bay, "OZ_Bay_Panels", OzLathe.Boxes("OZ_Bay_Panels", pn), mats[1], false);
            // ceiling work light strip + two lamp boxes (emissive)
            Part(bay, "OZ_Bay_Lamps", OzLathe.Boxes("OZ_Bay_Lamps", new List<(Vector3, Vector3)>
            {
                (new Vector3(cx, y1 - 0.02f, cz), new Vector3(0.06f, 0.015f, hz - 0.35f)),
                (new Vector3(x0 + 0.25f, y1 - 0.25f, zb + 0.05f), new Vector3(0.09f, 0.06f, 0.03f)),
                (new Vector3(x1 - 0.25f, y1 - 0.25f, zb + 0.05f), new Vector3(0.09f, 0.06f, 0.03f)),
            }), mats[2], false);
            // props: a crate stack in the back left corner, a tool rack on the back right wall
            float k = Mathf.Clamp((doorMax.y - doorMin.y) / 1.6f, 0.7f, 1.6f);
            Part(bay, "OZ_Bay_Crates", OzLathe.Boxes("OZ_Bay_Crates", new List<(Vector3, Vector3)>
            {
                (new Vector3(x0 + 0.42f * k, y0 + 0.28f * k, zb + 0.40f * k), new Vector3(0.28f * k, 0.28f * k, 0.28f * k)),
                (new Vector3(x0 + 0.40f * k, y0 + 0.74f * k, zb + 0.36f * k), new Vector3(0.20f * k, 0.18f * k, 0.20f * k)),
                (new Vector3(x0 + 0.95f * k, y0 + 0.20f * k, zb + 0.30f * k), new Vector3(0.22f * k, 0.20f * k, 0.18f * k)),
            }), mats[4], true);
            var rack = new List<(Vector3, Vector3)>();
            float rx = x1 - 0.55f * k;
            for (int i = 0; i < 3; i++) rack.Add((new Vector3(rx, y0 + (0.45f + 0.42f * i) * k, zb + 0.12f), new Vector3(0.38f * k, 0.02f, 0.10f)));
            rack.Add((new Vector3(rx - 0.38f * k, y0 + 0.75f * k, zb + 0.12f), new Vector3(0.02f, 0.75f * k, 0.02f)));
            rack.Add((new Vector3(rx + 0.38f * k, y0 + 0.75f * k, zb + 0.12f), new Vector3(0.02f, 0.75f * k, 0.02f)));
            Part(bay, "OZ_Bay_Rack", OzLathe.Boxes("OZ_Bay_Rack", rack), mats[1], true);
            // jamb frame inside the opening (hides the ragged cut edge)
            float jx0 = doorMin.x, jx1 = doorMax.x, jy0 = doorMin.y, jy1 = doorMax.y, jz = doorMin.z + 0.03f, jd = 0.32f;
            Part(bay, "OZ_Bay_Jamb", OzLathe.Boxes("OZ_Bay_Jamb", new List<(Vector3, Vector3)>
            {
                (new Vector3(jx0 + Jamb * 0.5f, (jy0 + jy1) * 0.5f, jz - jd * 0.5f), new Vector3(Jamb * 0.5f, (jy1 - jy0) * 0.5f, jd * 0.5f)),
                (new Vector3(jx1 - Jamb * 0.5f, (jy0 + jy1) * 0.5f, jz - jd * 0.5f), new Vector3(Jamb * 0.5f, (jy1 - jy0) * 0.5f, jd * 0.5f)),
                (new Vector3((jx0 + jx1) * 0.5f, jy1 - Jamb * 0.5f, jz - jd * 0.5f), new Vector3((jx1 - jx0) * 0.5f, Jamb * 0.5f, jd * 0.5f)),
            }), mats[1], true);
            var lg = new GameObject("OZ_Bay_Light"); lg.transform.SetParent(bay, false); lg.transform.localPosition = new Vector3(cx, y1 - 0.35f, cz - 0.2f);
            lamp = lg.AddComponent<Light>(); lamp.type = LightType.Point; lamp.color = new Color(1f, 0.86f, 0.66f);
            lamp.range = Mathf.Max(4f, (x1 - x0) * 1.6f); lamp.intensity = 0f; lamp.shadows = LightShadows.None; lamp.renderMode = LightRenderMode.ForcePixel; lamp.enabled = false;
            Built++;
            return bay;
        }

        static Mesh Cut(Mesh src, Matrix4x4 toRig, Vector3 dmin, Vector3 dmax, out int cut)
        {
            if (cutCache.TryGetValue(src, out var c) && c.m) { cut = c.cut; return c.m; }
            var v = src.vertices; cut = 0;
            var lists = new List<int>[src.subMeshCount];
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var t = src.GetTriangles(s); var keep = new List<int>(t.Length);
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = toRig.MultiplyPoint3x4(v[t[i]]), b = toRig.MultiplyPoint3x4(v[t[i + 1]]), cc = toRig.MultiplyPoint3x4(v[t[i + 2]]);
                    if (In(a, dmin, dmax) && In(b, dmin, dmax) && In(cc, dmin, dmax) && Vector3.Cross(b - a, cc - a).normalized.z > 0.5f) { cut++; continue; }
                    keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                }
                lists[s] = keep;
            }
            if (cut < 8) { cutCache[src] = (null, cut); return null; }
            var m = Object.Instantiate(src); m.name = src.name + "_bay";
            for (int s = 0; s < lists.Length; s++) m.SetTriangles(lists[s], s);
            m.RecalculateBounds();
            cutCache[src] = (m, cut);
            return m;
        }
        static bool In(Vector3 q, Vector3 a, Vector3 b) => q.x > a.x && q.x < b.x && q.y > a.y - 0.03f && q.y < b.y && Mathf.Abs(q.z - a.z) < 0.45f;

        static void Part(Transform parent, string name, Mesh mesh, Material m, bool shadows)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
        }

        /// <summary>A box with its faces turned inward; the +z (front) face left open.</summary>
        static Mesh InwardBox(Vector3 min, Vector3 max)
        {
            var b = OzLathe.Boxes("OZ_Bay_Shell", new List<(Vector3, Vector3)> { ((min + max) * 0.5f, (max - min) * 0.5f) });
            var n = b.normals; var t = b.triangles; var keep = new List<int>();
            for (int i = 0; i < t.Length; i += 3)
            {
                if (n[t[i]].z > 0.5f) continue;
                keep.Add(t[i]); keep.Add(t[i + 2]); keep.Add(t[i + 1]);
            }
            for (int i = 0; i < n.Length; i++) n[i] = -n[i];
            b.normals = n; b.SetTriangles(keep, 0); b.RecalculateBounds();
            return b;
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
                            SmoothId = Shader.PropertyToID("_Smoothness"), TeamModeId = Shader.PropertyToID("_TeamMode"), VcId = Shader.PropertyToID("_UseVertexColor"), PfaId = Shader.PropertyToID("_PaintFromAlpha");
        static readonly Dictionary<Material, Material[]> matCache = new Dictionary<Material, Material[]>();
        /// <summary>[0] dark wall steel, [1] gunmetal, [2] work light (emissive), [3] amber sill line, [4] khaki crates.</summary>
        static Material[] Mats(Material src)
        {
            if (matCache.TryGetValue(src, out var m) && m[0]) return m;
            m = new[] { Clone(src, "OZ_BayWall", new Color(0.20f, 0.21f, 0.22f), 0.2f), Clone(src, "OZ_BayMetal", new Color(0.40f, 0.42f, 0.44f), 0.45f),
                        Clone(src, "OZ_BayLamp", new Color(1f, 0.92f, 0.78f), 0.5f), Clone(src, "OZ_BaySill", new Color(0.95f, 0.62f, 0.12f), 0.3f),
                        Clone(src, "OZ_BayCrate", new Color32(150, 122, 78, 255), 0.25f) };
            if (m[2].HasProperty(EmissionId)) { m[2].EnableKeyword("_EMISSION"); m[2].SetColor(EmissionId, new Color(1f, 0.85f, 0.6f) * 3.2f); }
            if (m[3].HasProperty(EmissionId)) { m[3].EnableKeyword("_EMISSION"); m[3].SetColor(EmissionId, new Color(1f, 0.55f, 0.1f) * 0.6f); }
            matCache[src] = m;
            return m;
        }
        static Material Clone(Material src, string name, Color c, float smooth)
        {
            var m = new Material(src) { name = name };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c); else m.color = c;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, smooth);
            if (m.HasProperty(VcId)) m.SetFloat(VcId, 0f);
            if (m.HasProperty(PfaId)) m.SetFloat(PfaId, 0f);
            if (m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            return m;
        }
    }
}
