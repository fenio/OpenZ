using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>vehicle_lights package: the glow meshes (OpenZ/VehicleGlow, one mesh per vehicle type and mount node, shared
    /// by every vehicle of that type) and the headlight haze-cone meshes (the night package's NightBeam look); VehicleLights
    /// draws them with Graphics.DrawMesh at the mount node's matrix.
    /// No imported art (open-source clean).</summary>
    public static class VehicleLightMeshes
    {
        /// <summary>One lamp element: 4 vertices at the same centre, expanded by the shader (see VehicleGlow.shader).</summary>
        public struct Elem { public Vector3 pos, nrm; public float size; public int mode; public Color rgb; public float ch; }

        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        public static Material GlowMaterial(string name)
        {
            var m = Resources.Load<Material>("OpenZ/VehicleLights/M_VehicleGlow");      // keeps the shader in player builds
            if (m) m = new Material(m);
            else
            {
                var s = Shader.Find("OpenZ/VehicleGlow");
                if (!s) { Debug.LogWarning("[OpenZ] VehicleLights: shader OpenZ/VehicleGlow missing (README_IMPORT.md step 2)"); s = Shader.Find("Hidden/InternalErrorShader"); }
                m = new Material(s);
            }
            m.name = name; m.hideFlags = HideFlags.DontSave;
            return m;
        }

        public static int Channel(string kind) => kind switch
        {
            "head" => 0, "tail" => 1, "brake" => 2, "beaconA" => 3, "beaconB" => 4, "engine" => 5, "eye" => 6, "work" => 7, _ => 3,
        };

        static void Add(List<Elem> l, int mode, Vector3 p, Vector3 n, float size, Color c, float ch) =>
            l.Add(new Elem { mode = mode, pos = p, nrm = n, size = size, rgb = c, ch = ch });

        /// <summary>The elements of one socket (positions / sizes already in the mount's local units).</summary>
        public static void AddSocket(List<Elem> l, VLSocket s, Vector3 pos, Vector3 nrm, float unit)
        {
            var rgb = s.rgb != null && s.rgb.Length >= 3 ? new Color(s.rgb[0], s.rgb[1], s.rgb[2], 1f) : Color.white;
            nrm = nrm.sqrMagnitude > 1e-6f ? nrm.normalized : Vector3.up;
            var dirN = s.directional ? nrm : Vector3.zero;
            float size = s.size * unit, halo = s.halo * unit, team = s.colour == "team" ? 10f : 0f;
            int ch = Channel(s.kind);
            switch (s.kind)
            {
                case "head":
                case "eye":
                    Add(l, 1, pos, nrm, size, rgb, ch);                                          // lens glass
                    Add(l, 2, pos + nrm * (0.02f * unit), dirN, size * 1.5f, rgb, ch);          // hot bulb
                    Add(l, 0, pos + nrm * (0.05f * unit), dirN, halo, rgb * 0.35f, ch);         // halo
                    break;
                case "tail":
                    Add(l, 1, pos, nrm, size, rgb, 1);                                           // tail light (lights on)
                    Add(l, 0, pos + nrm * (0.03f * unit), dirN, halo * 0.8f, rgb * 0.3f, 1);
                    Add(l, 1, pos, nrm, size * 1.05f, rgb, 2);                                   // + brake (brighter, bigger)
                    Add(l, 2, pos + nrm * (0.02f * unit), dirN, size * 1.7f, rgb * 0.8f, 2);
                    Add(l, 0, pos + nrm * (0.04f * unit), dirN, halo * 1.2f, rgb * 0.35f, 2);
                    break;
                case "engine":
                    Add(l, 2, pos, dirN, size * 1.2f, rgb, 5);                                   // exhaust / vent heat
                    Add(l, 0, pos, dirN, halo, rgb * 0.4f, 5);
                    break;
                default:                                                                          // beacons, work light
                    Add(l, 2, pos, Vector3.zero, size * 1.5f, rgb, ch + team);
                    Add(l, 0, pos, Vector3.zero, halo, rgb * 0.4f, ch + team);
                    break;
            }
        }

        static readonly Vector2[] Corners = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };

        /// <summary>The glow mesh of one mount node (cached by key: every vehicle of a type shares it).</summary>
        public static Mesh GlowMesh(string key, List<Elem> elems)
        {
            if (cache.TryGetValue(key, out var m) && m) return m;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u0 = new List<Vector4>(); var u1 = new List<Vector4>(); var t = new List<int>();
            var b = new Bounds(); bool any = false; float maxR = 0.2f;
            foreach (var e in elems)
            {
                int i0 = v.Count;
                for (int k = 0; k < 4; k++)
                {
                    v.Add(e.pos); n.Add(e.nrm);
                    u0.Add(new Vector4(Corners[k].x, Corners[k].y, e.size, e.mode));
                    u1.Add(new Vector4(e.rgb.r, e.rgb.g, e.rgb.b, e.ch));
                }
                t.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                if (!any) { b = new Bounds(e.pos, Vector3.zero); any = true; } else b.Encapsulate(e.pos);
                maxR = Mathf.Max(maxR, e.size);
            }
            m = new Mesh { name = "VL_Glow_" + key, hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u0); m.SetUVs(1, u1); m.SetTriangles(t, 0);
            b.Expand(maxR * 2f + 1f);                                                            // billboards expand + camera pull
            m.bounds = b;
            cache[key] = m;
            return m;
        }

        /// <summary>Headlight haze cones (NightMeshes.Cone per lamp, apex on the lens, dipped by pitchBeam), one mesh.</summary>
        public static Mesh BeamMesh(string key, List<Vector3> apexes, Vector3 fwd, VLHeadlight hl, float unit)
        {
            if (cache.TryGetValue("beam|" + key, out var m) && m) return m;
            if (apexes.Count == 0 || hl.beamLength <= 0.1f) return null;
            var cone = NightMeshes.Cone();
            var cv = cone.vertices; var cn = cone.normals; var cuv = cone.uv; var ct = cone.triangles;
            var flat = new Vector3(fwd.x, 0f, fwd.z); flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.back;
            float pb = hl.pitchBeam * Mathf.Deg2Rad;
            var dir = (flat * Mathf.Cos(pb) + Vector3.down * Mathf.Sin(pb)).normalized;
            var right = Vector3.Cross(Vector3.up, dir).normalized; var up = Vector3.Cross(dir, right);
            float L = hl.beamLength * unit, R = Mathf.Tan(Mathf.Clamp(hl.beamAngle, 3f, 60f) * Mathf.Deg2Rad) * L;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            foreach (var a in apexes)
            {
                int o = v.Count;
                var apex = a + flat * (0.04f * unit);
                for (int i = 0; i < cv.Length; i++)
                {
                    v.Add(apex + right * (cv[i].x * R) + up * (cv[i].y * R) + dir * (cv[i].z * L));
                    n.Add((right * (cn[i].x / R) + up * (cn[i].y / R) + dir * (cn[i].z / L)).normalized);   // inverse-transpose of the scale
                    uv.Add(cuv[i]);
                }
                for (int i = 0; i < ct.Length; i++) t.Add(ct[i] + o);
            }
            m = new Mesh { name = "VL_Beams_" + key, hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            cache["beam|" + key] = m;
            return m;
        }
    }
}
