using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// normals per section, hard edges between sections. Used by GunNose and ApcDome.</summary>
    public static class OzLathe
    {
        /// <summary>One section = a polyline of (r, h) points with their outward normals (nr, nh) in the profile plane.</summary>
        public sealed class Sec { public readonly List<Vector4> p = new List<Vector4>(); public Sec Add(float r, float h, float nr, float nh) { var n = new Vector2(nr, nh).normalized; p.Add(new Vector4(r, h, n.x, n.y)); return this; } }

        /// <summary>A rounded corner: arc around (cr, ch) with radius rad from angle a0 to a1 (deg; 0 = +r, 90 = +h).</summary>
        public static Sec Arc(float cr, float ch, float rad, float a0, float a1, int steps)
        {
            var s = new Sec();
            for (int i = 0; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad;
                s.Add(cr + Mathf.Cos(a) * rad, ch + Mathf.Sin(a) * rad, Mathf.Cos(a), Mathf.Sin(a));
            }
            return s;
        }

        /// <summary>Turns the sections around `axis` (0 = x, 1 = y, 2 = z; sign flips the height direction) at `origin`, `seg`
        /// segments. colour = the vertex colour of every vertex (null = none).</summary>
        public static Mesh Build(string name, IList<Sec> secs, int seg, int axis, float sign, Vector3 origin, Color? colour = null)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            Vector3 A = Vector3.zero; A[axis] = sign;
            Vector3 U = Vector3.zero; U[(axis + 1) % 3] = 1f; Vector3 W = Vector3.zero; W[(axis + 2) % 3] = 1f;
            foreach (var s in secs)
            {
                for (int k = 0; k + 1 < s.p.Count; k++)
                {
                    Vector4 a = s.p[k], b = s.p[k + 1];
                    for (int i = 0; i < seg; i++)
                    {
                        float t0 = i * Mathf.PI * 2f / seg, t1 = (i + 1) * Mathf.PI * 2f / seg;
                        Vector3 d0 = U * Mathf.Cos(t0) + W * Mathf.Sin(t0), d1 = U * Mathf.Cos(t1) + W * Mathf.Sin(t1);
                        Vector3 p00 = origin + d0 * a.x + A * a.y, p01 = origin + d1 * a.x + A * a.y, p10 = origin + d0 * b.x + A * b.y, p11 = origin + d1 * b.x + A * b.y;
                        Vector3 n00 = (d0 * a.z + A * a.w).normalized, n01 = (d1 * a.z + A * a.w).normalized, n10 = (d0 * b.z + A * b.w).normalized, n11 = (d1 * b.z + A * b.w).normalized;
                        int i0 = v.Count; v.Add(p00); v.Add(p01); v.Add(p11); v.Add(p10); n.Add(n00); n.Add(n01); n.Add(n11); n.Add(n10);
                        var want = n00 + n01 + n10 + n11;
                        var face = Vector3.Cross(p01 - p00, p11 - p00); if (face.sqrMagnitude < 1e-12f) face = Vector3.Cross(p11 - p00, p10 - p00);
                        if (Vector3.Dot(face, want) >= 0f) tri.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                        else tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                    }
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0);
            if (colour.HasValue) { var c = new Color[v.Count]; for (int i = 0; i < c.Length; i++) c[i] = colour.Value; m.colors = c; }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Box (centre c, half size h) as a mesh part appended to lists; flat normals.</summary>
        public static Mesh Boxes(string name, IList<(Vector3 c, Vector3 h)> boxes)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var tri = new List<int>();
            foreach (var bx in boxes)
                for (int ax = 0; ax < 3; ax++)
                    for (int sg = -1; sg <= 1; sg += 2)
                    {
                        var nn = Vector3.zero; nn[ax] = sg; int u = (ax + 1) % 3, w = (ax + 2) % 3;
                        Vector3 P(float a, float b) { var p = Vector3.zero; p[ax] = sg * bx.h[ax]; p[u] = a * bx.h[u]; p[w] = b * bx.h[w]; return bx.c + p; }
                        Vector3 a0 = P(-1, -1), a1 = P(1, -1), a2 = P(1, 1), a3 = P(-1, 1);
                        int i0 = v.Count; v.Add(a0); v.Add(a1); v.Add(a2); v.Add(a3); for (int k = 0; k < 4; k++) n.Add(nn);
                        if (Vector3.Dot(Vector3.Cross(a1 - a0, a2 - a0), nn) >= 0f) tri.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                        else tri.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                    }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// Gun nose collar: the gun (type "gun", every tower gun and the
    /// field guns) had a 0.56 m tube between its 0.74 m breech block and a flat 0.74 m end ring. Now: the tube is 18 % thicker
    /// (r 0.28 -> 0.33) and the nose carries a chunky rounded muzzle collar like the fort cannon's, scaled to the small gun
    /// (r 0.47, 0.50 m long, 0.10 m rounded edges, a 0.17 m bore sunk 0.13 m, its floor 3 cm in front of the old muzzle face), same material and vertex palette as the barrel,
    /// on the recoil node (the recoil still moves it). The muzzle socket moves to the new nose (flash + shell leave it).
    /// View only; called by UnitView.Bind before UnitAim measures the gun. PlayerPrefs oz_gun_nose = 0 turns it off.
    /// </summary>
    public static class GunNose
    {
        public const float TubeR = 0.28f, TubeNewR = 0.33f, CollarR = 0.47f, Back = -1.72f, Front = -2.22f, Edge = 0.10f, BoreR = 0.17f, BoreDepth = 0.13f;
        /// <summary>
        /// day vs the fort cannon barrel's ~95). The barrel parts (tube, mantlet / pitch block, collar) take the fort cannon's grey-blue
        /// gunmetal instead, each dark entry at its own relative brightness (bore / muzzle face stay darker); the 0.4 sheen stays.
        /// Picked live in Play (L02, the tower gun next to the fort cannon, day + night).</summary>
        public static readonly Color Gunmetal = new Color(0.36f, 0.405f, 0.495f, 0f);
        const float RefLum = 0.2126f * 0.054f + 0.7152f * 0.067f + 0.0722f * 0.053f;
        /// <summary>Collar team band (vertex alpha 1 = TeamLit's team mask, like the gun's owner strips): 0.10 m round the collar wall.</summary>
        public const float BandHalf = 0.05f;
        public static int Applied;
        static readonly Dictionary<Mesh, Mesh> tubeCache = new Dictionary<Mesh, Mesh>(), pitchCache = new Dictionary<Mesh, Mesh>();
        static Mesh collarMesh;

        /// <summary>Dark metal palette entries (luminance under 0.1, no team alpha) -> Gunmetal at their relative brightness.</summary>
        public static Color Metal(Color c)
        {
            float l = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
            if (l >= 0.1f || c.a >= 0.3f) return c;
            float k = l / RefLum;
            return new Color(Gunmetal.r * k, Gunmetal.g * k, Gunmetal.b * k, c.a);
        }

        static void Recolour(Mesh m)
        {
            var c = m.colors; if (c == null || c.Length == 0) return;
            for (int i = 0; i < c.Length; i++) c[i] = Metal(c[i]);
            m.colors = c;
        }

        public static bool Apply(GameObject unit, string type)
        {
            if (!unit || type != "gun" || PlayerPrefs.GetInt("oz_gun_nose", 1) == 0) return false;
            Transform recoil = null, surf = null, muzzle = null, pitch = null;
            foreach (var t in unit.GetComponentsInChildren<Transform>(true))
            {
                if (!recoil && t.name == "CannonBarrelRecoil") recoil = t;
                else if (!surf && t.name == "CannonBarrelRecoil_Surface") surf = t;
                else if (!muzzle && t.name == "CannonMuzzle") muzzle = t;
                else if (!pitch && t.name == "CannonBarrelPitch_Surface") pitch = t;
            }
            if (!recoil || !surf || recoil.Find("OZ_GunNoseCollar")) return false;
            var mf = surf.GetComponent<MeshFilter>(); var mr = surf.GetComponent<MeshRenderer>();
            if (!mf || !mf.sharedMesh || !mr || !mf.sharedMesh.isReadable) return false;
            var src = mf.sharedMesh;
            if (!tubeCache.TryGetValue(src, out var tube))
            {   // the tube (every vertex on r 0.28: the tube wall and the steps' inner rims) goes out to r 0.33, still inside the 0.37 rings
                tube = Object.Instantiate(src); tube.name = src.name + "_nose";
                var v = tube.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    float r = Mathf.Sqrt(v[i].x * v[i].x + v[i].y * v[i].y);
                    if (Mathf.Abs(r - TubeR) < 0.012f && v[i].z < -1.0f) { float k = TubeNewR / r; v[i].x *= k; v[i].y *= k; }
                }
                tube.vertices = v; tube.RecalculateBounds();
                Recolour(tube);
                tubeCache[src] = tube;
            }
            mf.sharedMesh = tube;
            var pf = pitch ? pitch.GetComponent<MeshFilter>() : null;
            if (pf && pf.sharedMesh && pf.sharedMesh.isReadable)
            {
                if (!pitchCache.TryGetValue(pf.sharedMesh, out var pm)) { pm = Object.Instantiate(pf.sharedMesh); pm.name = pf.sharedMesh.name + "_metal"; Recolour(pm); pitchCache[pf.sharedMesh] = pm; }
                pf.sharedMesh = pm;
            }
            // face's yellowish dark entry fell in TeamLit's warm paint band and turned fort cream / field khaki
            Color metal = new Color(0.054f, 0.067f, 0.053f, 0f);
            var sc = src.colors; var sv = src.vertices;
            if (sc.Length == sv.Length) for (int i = 0; i < sv.Length; i++) { float r = Mathf.Sqrt(sv[i].x * sv[i].x + sv[i].y * sv[i].y); if (Mathf.Abs(r - TubeR) < 0.012f && sv[i].z < -1.2f) { metal = sc[i]; break; } }
            var bore = new Color(metal.r * 0.7f, metal.g * 0.7f, metal.b * 0.7f, metal.a);
            if (!collarMesh) collarMesh = CollarMesh(Metal(metal), Metal(bore));
            var go = new GameObject("OZ_GunNoseCollar");
            go.transform.SetParent(recoil, false);
            go.AddComponent<MeshFilter>().sharedMesh = collarMesh;
            var cr = go.AddComponent<MeshRenderer>(); cr.sharedMaterials = mr.sharedMaterials;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; cr.receiveShadows = true;
            if (muzzle && muzzle.parent == recoil) muzzle.localPosition = new Vector3(muzzle.localPosition.x, muzzle.localPosition.y, Front - 0.02f);
            Applied++;
            return true;
        }

        /// <summary>Collar along -z of the recoil node: back face (from the tube out), rounded back edge, wall, rounded front edge,
        /// front face in to the bore, bore wall, bore floor. The profile's h = -z (forward).</summary>
        static Mesh CollarMesh(Color metal, Color bore)
        {
            float hb = -Back, hf = -Front, R = CollarR, e = Edge;                                // h grows forward (toward the nose)
            float hm = (hb + hf) * 0.5f;
            var outer = new List<OzLathe.Sec>
            {
                new OzLathe.Sec().Add(TubeNewR - 0.01f, hb, 0f, -1f).Add(R - e, hb, 0f, -1f),        // back face (faces the breech)
                OzLathe.Arc(R - e, hb + e, e, -90f, 0f, 4),                                          // rounded back edge
                new OzLathe.Sec().Add(R, hb + e, 1f, 0f).Add(R, hm - BandHalf, 1f, 0f),              // wall (breech side of the band)
                new OzLathe.Sec().Add(R, hm + BandHalf, 1f, 0f).Add(R, hf - e, 1f, 0f),              // wall (nose side of the band)
                OzLathe.Arc(R - e, hf - e, e, 0f, 90f, 4),                                           // rounded front edge
                new OzLathe.Sec().Add(R - e, hf, 0f, 1f).Add(BoreR + 0.035f, hf, 0f, 1f),            // front face
                OzLathe.Arc(BoreR + 0.035f, hf - 0.035f, 0.035f, 90f, 180f, 3),                      // rounded bore lip
            };
            var inner = new List<OzLathe.Sec>
            {
                new OzLathe.Sec().Add(BoreR, hf - 0.035f, -1f, 0f).Add(BoreR, hf - BoreDepth, -1f, 0f), // bore wall (faces the axis)
                new OzLathe.Sec().Add(BoreR, hf - BoreDepth, 0f, 1f).Add(0f, hf - BoreDepth, 0f, 1f),  // bore floor (faces the nose)
            };
            var a = OzLathe.Build("OZ_GunNoseCollar_outer", outer, 20, 2, -1f, Vector3.zero, metal);
            var b = OzLathe.Build("OZ_GunNoseCollar_bore", inner, 20, 2, -1f, Vector3.zero, bore);
            var band = OzLathe.Build("OZ_GunNoseCollar_band", new List<OzLathe.Sec> { new OzLathe.Sec().Add(R, hm - BandHalf, 1f, 0f).Add(R, hm + BandHalf, 1f, 0f) }, 20, 2, -1f, Vector3.zero, Color.white);   // white + alpha 1 = team colour (the owner strips' entry)
            var m = new Mesh { name = "OZ_GunNoseCollar" };
            m.CombineMeshes(new[] { new CombineInstance { mesh = a, transform = Matrix4x4.identity }, new CombineInstance { mesh = b, transform = Matrix4x4.identity }, new CombineInstance { mesh = band, transform = Matrix4x4.identity } }, true, false);
            Object.Destroy(a); Object.Destroy(b); Object.Destroy(band);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// roof (clear of the roof team panel): a gunmetal ring on the roof, a low khaki dome with a team-colour band (owner part:
    /// TeamPaint gives it the team colour, dark grey when neutral) and a small periscope head with a dark glass window + a stub
    /// antenna. The head slowly spins (40 deg/s) while the APC has crew aboard and eases to rest when it is empty or dead; a soft
    /// servo whirr loop (veh_apc_dome_loop, the radar dish loop pitched up) follows it while it turns, only near the camera (at
    /// most MaxLoops at once, logged in OzSoundLog). When the wreck breaks apart the head and the ring fly off as their own
    /// debris pieces (WreckPieces.Single) with the rest of the APC. View only.
    /// </summary>
    public class ApcDome : MonoBehaviour
    {
        public const float Speed = 40f, Ease = 1.1f, RingR = 0.50f, DomeR = 0.43f, DomeH = 0.26f, LoopRange = 45f;
        public const int MaxLoops = 3;
        /// <summary>Proof counters: spin starts / stops (crew in / out), whirr loop starts.</summary>
        public static int SpinStarts, SpinStops, LoopStarts, Built;
        static int loopsLive;
        static Mesh ringMesh, domeMesh, bandMesh, headMesh, glassMesh;
        static readonly Dictionary<Material, Material[]> matCache = new Dictionary<Material, Material[]>();

        UnitView view; Transform frame, head; float spin, angle; bool wantSpin, looping; string loopKey;
        public float SpinRate => spin;
        public Transform Head => head;

        public static ApcDome Ensure(UnitView v)
        {
            if (!v || v.Obj == null || v.Obj.Type != "apc") return null;
            var d = v.GetComponent<ApcDome>();
            if (d) return d;
            d = v.gameObject.AddComponent<ApcDome>();
            if (!d.Build(v)) { Destroy(d); return null; }
            return d;
        }

        bool Build(UnitView v)
        {
            view = v;
            var model = v.transform.Find("Model");
            var mr = model ? model.GetComponent<MeshRenderer>() : null; var mf = model ? model.GetComponent<MeshFilter>() : null;
            if (!mr || !mf || !mf.sharedMesh) return false;
            // the rear roof, clear of the roof team panel (m06_apc_mat rule: root z -0.96..0.96) and the cab: roof height under the
            // ring measured on the model mesh (the highest roof point under 9 samples, so the ring never sinks into the roof)
            var centre = new Vector3(0f, 0f, 1.45f);
            float roof = RoofY(v.transform, model, mf.sharedMesh, centre, RingR * 0.9f);
            if (roof < 1.5f) roof = 2.40f;
            frame = new GameObject("OZ_ApcDome").transform;
            frame.SetParent(v.transform, false);
            frame.localPosition = new Vector3(centre.x, roof, centre.z);
            var mats = Mats(mr.sharedMaterial);
            if (!ringMesh) BuildMeshes();
            Part(frame, "OZ_ApcDome_Ring", ringMesh, mats[1]);
            head = new GameObject("OZ_ApcDome_Head").transform;
            head.SetParent(frame, false); head.localPosition = new Vector3(0f, 0.07f, 0f);
            Part(head, "OZ_ApcDome_Shell", domeMesh, mats[0]);
            Part(head, "OZ_ApcDome_owner_band", bandMesh, mats[3]);
            Part(head, "OZ_ApcDome_Scope", headMesh, mats[1]);
            Part(head, "OZ_ApcDome_Glass", glassMesh, mats[2]);
            angle = (v.Obj.Id * 47) % 360;
            head.localRotation = Quaternion.Euler(0f, angle, 0f);
            loopKey = "apcdome:" + v.Obj.Id;
            var tp = v.GetComponent<TeamPaint>(); if (tp) tp.Apply();
            Built++;
            return true;
        }

        static float RoofY(Transform root, Transform model, Mesh mesh, Vector3 c, float r)
        {
            var v = mesh.vertices; var t = mesh.triangles; var m = root.worldToLocalMatrix * model.localToWorldMatrix;
            var p = new Vector3[v.Length]; for (int i = 0; i < v.Length; i++) p[i] = m.MultiplyPoint3x4(v[i]);
            float best = -1f;
            for (int s = 0; s < 9; s++)
            {
                float a = s * 45f * Mathf.Deg2Rad; var q = s == 8 ? c : c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a0 = p[t[i]], a1 = p[t[i + 1]], a2 = p[t[i + 2]];
                    float d = (a1.x - a0.x) * (a2.z - a0.z) - (a2.x - a0.x) * (a1.z - a0.z); if (Mathf.Abs(d) < 1e-8f) continue;
                    float u = ((q.x - a0.x) * (a2.z - a0.z) - (a2.x - a0.x) * (q.z - a0.z)) / d, w = ((a1.x - a0.x) * (q.z - a0.z) - (q.x - a0.x) * (a1.z - a0.z)) / d;
                    if (u < 0f || w < 0f || u + w > 1f) continue;
                    float y = a0.y + u * (a1.y - a0.y) + w * (a2.y - a0.y); if (y > best) best = y;
                }
            }
            return best;
        }

        static void Part(Transform parent, string name, Mesh mesh, Material m)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
        }

        static void BuildMeshes()
        {
            // ring: skirt 10 cm into the roof, rounded top edge, flat top in to the dome
            ringMesh = OzLathe.Build("OZ_ApcDome_Ring", new List<OzLathe.Sec>
            {
                new OzLathe.Sec().Add(RingR, -0.10f, 1f, 0f).Add(RingR, 0.035f, 1f, 0f),
                OzLathe.Arc(RingR - 0.035f, 0.035f, 0.035f, 0f, 90f, 3),
                new OzLathe.Sec().Add(RingR - 0.035f, 0.07f, 0f, 1f).Add(DomeR - 0.04f, 0.07f, 0f, 1f),
            }, 24, 1, 1f, Vector3.zero);
            // band (team colour): a short upright ring at the dome's foot
            bandMesh = OzLathe.Build("OZ_ApcDome_Band", new List<OzLathe.Sec>
            {
                new OzLathe.Sec().Add(DomeR + 0.012f, -0.005f, 1f, 0f).Add(DomeR + 0.012f, 0.055f, 1f, 0f),
                new OzLathe.Sec().Add(DomeR + 0.012f, 0.055f, 0f, 1f).Add(DomeR - 0.01f, 0.055f, 0f, 1f),
            }, 24, 1, 1f, Vector3.zero);
            // dome: a squashed hemisphere over the band
            var arc = new OzLathe.Sec();
            for (int i = 0; i <= 8; i++)
            {
                float a = i / 8f * 90f * Mathf.Deg2Rad; float r = DomeR * Mathf.Cos(a), h = 0.05f + DomeH * Mathf.Sin(a);
                arc.Add(r, h, Mathf.Cos(a) / DomeR, Mathf.Sin(a) / DomeH);
            }
            domeMesh = OzLathe.Build("OZ_ApcDome_Shell", new List<OzLathe.Sec> { arc }, 24, 1, 1f, Vector3.zero);
            // periscope: post + head on the front of the dome (front = -z), stub antenna at the back
            headMesh = OzLathe.Boxes("OZ_ApcDome_Scope", new List<(Vector3, Vector3)>
            {
                (new Vector3(0f, 0.30f, -0.15f), new Vector3(0.055f, 0.06f, 0.055f)),
                (new Vector3(0f, 0.39f, -0.17f), new Vector3(0.11f, 0.055f, 0.085f)),
                (new Vector3(0.19f, 0.33f, 0.15f), new Vector3(0.018f, 0.14f, 0.018f)),
                (new Vector3(0.19f, 0.48f, 0.15f), new Vector3(0.03f, 0.018f, 0.03f)),
            });
            glassMesh = OzLathe.Boxes("OZ_ApcDome_Glass", new List<(Vector3, Vector3)> { (new Vector3(0f, 0.395f, -0.258f), new Vector3(0.085f, 0.028f, 0.006f)) });
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"),
                            SmoothId = Shader.PropertyToID("_Smoothness"), TeamModeId = Shader.PropertyToID("_TeamMode"), VcId = Shader.PropertyToID("_UseVertexColor"), PfaId = Shader.PropertyToID("_PaintFromAlpha");
        /// <summary>[0] APC khaki paint, [1] gunmetal, [2] dark glass, [3] owner band (team colour via TeamPaint's owner part).</summary>
        static Material[] Mats(Material src)
        {
            if (matCache.TryGetValue(src, out var m) && m[0]) return m;
            m = new[] { Clone(src, "OZ_ApcDomePaint", new Color32(176, 136, 80, 255), 0.30f, true),
                        Clone(src, "OZ_ApcDomeMetal", new Color(0.42f, 0.44f, 0.47f), 0.35f, true),
                        Clone(src, "OZ_ApcDomeGlass", new Color(0.05f, 0.06f, 0.07f), 0.75f, true),
                        Clone(src, "OZ_ApcDomeBand", new Color(0.62f, 0.60f, 0.56f), 0.32f, false) };
            matCache[src] = m;
            return m;
        }
        static Material Clone(Material src, string name, Color c, float smooth, bool neverTeam)
        {
            var m = new Material(src) { name = name };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, null);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c); else m.color = c;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, smooth);
            if (m.HasProperty(VcId)) m.SetFloat(VcId, 0f);
            if (m.HasProperty(PfaId)) m.SetFloat(PfaId, 0f);                                             // Play check: no texture = alpha 1 = "team panel" in toy paint mode
            if (neverTeam && m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);
            return m;
        }

        void LateUpdate()
        {
            if (!head || view == null || view.Obj == null) return;
            var o = view.Obj;
            bool crew = !o.Destroyed && o.KillAt < 0 && o.Owner != Team.Neutral && o.CrewAboard > 0;
            if (crew != wantSpin)
            {
                wantSpin = crew;
                if (crew) SpinStarts++; else SpinStops++;
                OzSoundLog.Add("DOME", "apc_dome", null, crew ? "spin up (crew aboard)" : "ease to rest (empty)", 0f, frame.position);
            }
            float dt = Time.deltaTime;
            spin = Mathf.MoveTowards(spin, wantSpin ? Speed : 0f, Speed / Ease * dt);
            if (spin > 0.01f) { angle = (angle + spin * dt) % 360f; head.localRotation = Quaternion.Euler(0f, angle, 0f); }
            Whirr();
        }

        void Whirr()
        {
            var ad = AudioDirector.I; if (!ad) return;
            float k = spin / Speed;
            bool near = false;
            var cam = Camera.main;
            if (cam && k > 0.08f)
            {
                var d = frame.position - cam.transform.position;
                if (d.sqrMagnitude < LoopRange * LoopRange) { var vp = cam.WorldToViewportPoint(frame.position); near = vp.z > 0f && vp.x > -0.1f && vp.x < 1.1f && vp.y > -0.1f && vp.y < 1.1f; }
            }
            if (near && !looping && loopsLive < MaxLoops)
            {
                if (ad.StartLoop(loopKey, "veh_apc_dome_loop", frame.position, head, 0.35f + 0.65f * k, 0.8f + 0.2f * k) > 0)
                {
                    looping = true; loopsLive++; LoopStarts++;
                    OzSoundLog.Add("DOME", "veh_apc_dome_loop", "sd_radar_dish_loop", "loop start", 0.35f + 0.65f * k, frame.position);
                }
            }
            else if (looping && near) ad.SetLoop(loopKey, 0.35f + 0.65f * k, 0.8f + 0.2f * k);
            else if (looping && !near) StopWhirr(0.6f);
        }

        void StopWhirr(float fade)
        {
            if (!looping) return;
            looping = false; loopsLive = Mathf.Max(0, loopsLive - 1);
            if (AudioDirector.I) AudioDirector.I.StopLoop(loopKey, fade);
            OzSoundLog.Add("DOME", "veh_apc_dome_loop", "sd_radar_dish_loop", "loop stop", 0f, frame ? frame.position : (Vector3?)null);
        }

        void OnDisable() => StopWhirr(0.2f);

        /// <summary>UnitView.BreakApart: the head and the ring leave as their own debris pieces before the rest breaks.</summary>
        public static void BreakOff(UnitView v, Vector3 centre, float power, int id)
        {
            var d = v ? v.GetComponent<ApcDome>() : null;
            if (!d || !d.frame) return;
            d.StopWhirr(0.1f);
            if (d.head) { WreckPieces.Single(d.head, centre, power, id); d.head = null; }
            var ring = d.frame.Find("OZ_ApcDome_Ring");
            if (ring) WreckPieces.Single(ring, centre, power * 0.8f, id + 7919);
            d.enabled = false;
        }
    }
}
