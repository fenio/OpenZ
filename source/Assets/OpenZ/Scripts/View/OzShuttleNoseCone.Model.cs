#if UNITY_EDITOR || OZ_NOSECONE_HEADLESS
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OpenZ
{
    /// <summary>
    /// K256d2 (test only, never in a player build): the drawn shuttle read from the project's own model files, in the ship's root space, and
    /// ray and cone tests against it, for the nose searchlight's counted check (Editor/OzShuttleNoseConeCount.cs, also outside the editor)
    /// and its Play proof (View/OzShuttleNoseConeProof.cs). The same reader and tests as the K344 kit's OzKickflipShipModel (a copy, so
    /// this kit does not need that one): the hull with its doorway, the pods, the legs (Models/L16/Shuttle/L16_Shuttle.fbx), the lift
    /// nozzles, the belly lamp housings, the closed ramp leaf and the bay (Resources/OpenZ/Vehicles/L16_ShuttleKit.fbx); root = (-x, z, -y).
    /// </summary>
    public sealed class OzNoseConeShipModel
    {
        public const string ShipFbx = "Assets/OpenZ/Models/L16/Shuttle/L16_Shuttle.fbx", KitFbx = "Assets/OpenZ/Resources/OpenZ/Vehicles/L16_ShuttleKit.fbx";
        static readonly string[] ShipParts = { "SH_Hull", "SH_Engine_L", "SH_Engine_R", "SH_Leg_BL", "SH_Leg_BR", "SH_Leg_FL", "SH_Leg_FR" };
        static readonly string[] KitParts = { "SH_Hull_Door", "SH_LiftNozzle_L", "SH_LiftNozzle_R", "SH_BellyLamp_L", "SH_BellyLamp_R", "DR_RampLeaf", "DR_Bay" };
        /// <summary>The nose searchlight's head (OzShuttleNightLights.NosePivot (0, 0.80, 2.45), NoseScale 0.45): x, y, z, radius.</summary>
        public static readonly double[][] NoseHead = MakeNoseHead();
        static double[][] MakeNoseHead()
        {
            var l = new List<double[]> { new[] { 0.0, 0.80 - 0.02 * 0.45, 2.45, 0.25 } };
            for (int k = 0; k < 8; k++) l.Add(new[] { 0.0, 0.80 + 0.15 + 0.05 * k, 2.45, 0.06 });
            return l.ToArray();
        }

        /// <summary>The triangles (9 numbers each, root space, m), their part's name; the door kit's markers (root space).</summary>
        public double[] T = new double[0];
        public string[] Part = new string[0];
        public int Tris => Part.Length;
        public readonly Dictionary<string, int> PartTris = new Dictionary<string, int>();
        public readonly Dictionary<string, double[]> Markers = new Dictionary<string, double[]>();
        public string Error;

        public static OzNoseConeShipModel Load(string projectRoot)
        {
            var m = new OzNoseConeShipModel();
            try
            {
                var tri = new List<double>(); var part = new List<string>();
                foreach (var (file, parts) in new[] { (ShipFbx, ShipParts), (KitFbx, KitParts) })
                {
                    var path = Path.Combine(projectRoot ?? "", file);
                    if (!File.Exists(path)) { m.Error = "missing " + path; return m; }
                    var root = Fbx.Read(File.ReadAllBytes(path));
                    var meshes = Fbx.Meshes(root, file == KitFbx ? m.Markers : null);
                    foreach (var p in parts)
                    {
                        if (!meshes.TryGetValue(p, out var t)) { m.Error = "the part " + p + " is not in " + file; return m; }
                        tri.AddRange(t); for (int i = 0; i < t.Count / 9; i++) part.Add(p);
                        m.PartTris[p] = t.Count / 9;
                    }
                }
                m.T = tri.ToArray(); m.Part = part.ToArray();
            }
            catch (Exception e) { m.Error = e.GetType().Name + ": " + e.Message; }
            return m;
        }

        /// <summary>The angle (deg) between two directions.</summary>
        public static double Angle(double ax, double ay, double az, double bx, double by, double bz)
        {
            double na = Math.Sqrt(ax * ax + ay * ay + az * az), nb = Math.Sqrt(bx * bx + by * by + bz * bz);
            if (na < 1e-12 || nb < 1e-12) return 0;
            double c = (ax * bx + ay * by + az * bz) / (na * nb);
            return Math.Acos(c < -1 ? -1 : c > 1 ? 1 : c) * (180.0 / Math.PI);
        }

        // ------------------------------------------------------------------ the probe of one lamp
        const int BinsPhi = 72, BinsLat = 36; const double BinDeg = 5.0, BinHalfDeg = 3.65;
        public sealed class Probe
        {
            public double Ox, Oy, Oz, Reach; public bool NoseHeadInWay; public int[][] Bins; public int Listed;
        }

        static void Dir(double phiDeg, double latDeg, out double x, out double y, out double z)
        {
            double p = phiDeg * Math.PI / 180.0, l = latDeg * Math.PI / 180.0;
            x = Math.Cos(l) * Math.Sin(p); y = -Math.Cos(l) * Math.Cos(p); z = Math.Sin(l);
        }

        /// <summary>A probe for beams that start within `reach` m of (ox, oy, oz) (root space); noseHead: the nose searchlight's head is in their way.</summary>
        public Probe ProbeFor(double ox, double oy, double oz, double reach, bool noseHead)
        {
            int n = Tris; var cx = new double[n]; var cy = new double[n]; var cz = new double[n]; var cosR = new double[n]; var nearOf = new double[n];
            for (int i = 0; i < n; i++)
            {
                int b = i * 9; double ux = 0, uy = 0, uz = 0, near = 1e9;
                var vx = new double[3]; var vy = new double[3]; var vz = new double[3];
                for (int v = 0; v < 3; v++)
                {
                    double dx = T[b + v * 3] - ox, dy = T[b + v * 3 + 1] - oy, dz = T[b + v * 3 + 2] - oz, d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    near = Math.Min(near, d); if (d < 1e-9) d = 1e-9;
                    vx[v] = dx / d; vy[v] = dy / d; vz[v] = dz / d; ux += dx; uy += dy; uz += dz;
                }
                near = Math.Min(near, PointTriangle(ox, oy, oz, b)); nearOf[i] = near;                    // the triangle's nearest point
                double un = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                double r;
                if (near <= reach + 0.05 || un < 1e-9) r = 180.0;
                else
                {
                    ux /= un; uy /= un; uz /= un; r = 0;
                    for (int v = 0; v < 3; v++) r = Math.Max(r, Angle(ux, uy, uz, vx[v], vy[v], vz[v]));
                    r += Math.Asin(Math.Min(1.0, (reach + 0.02) / near)) * 180.0 / Math.PI + 0.5;
                    if (r >= 80.0) r = 180.0;                                                                   // a cap that wide is no longer convex: always tested
                }
                cx[i] = ux; cy[i] = uy; cz[i] = uz;
                cosR[i] = r + BinHalfDeg >= 180.0 ? -2.0 : Math.Cos((r + BinHalfDeg) * Math.PI / 180.0);   // the cap grown by a bin's half diagonal
            }
            var p = new Probe { Ox = ox, Oy = oy, Oz = oz, Reach = reach, NoseHeadInWay = noseHead, Bins = new int[BinsPhi * BinsLat][] };
            var list = new List<int>();
            for (int j = 0; j < BinsLat; j++)
                for (int k = 0; k < BinsPhi; k++)
                {
                    Dir(-180.0 + (k + 0.5) * BinDeg, -90.0 + (j + 0.5) * BinDeg, out double bx, out double by, out double bz);
                    list.Clear();
                    for (int i = 0; i < n; i++)
                        if (bx * cx[i] + by * cy[i] + bz * cz[i] >= cosR[i]) list.Add(i);
                    list.Sort((x, y) => nearOf[x].CompareTo(nearOf[y])); p.Bins[j * BinsPhi + k] = list.ToArray(); p.Listed += list.Count;   // the nearest first: a blocked ray stops early
                }
            return p;
        }

        /// <summary>The distance from a point to triangle b (its nearest point: inside, on an edge or a corner).</summary>
        double PointTriangle(double px, double py, double pz, int b)
        {
            double ax = T[b], ay = T[b + 1], az = T[b + 2], bx = T[b + 3], by = T[b + 4], bz = T[b + 5], cx = T[b + 6], cy = T[b + 7], cz = T[b + 8];
            double abx = bx - ax, aby = by - ay, abz = bz - az, acx = cx - ax, acy = cy - ay, acz = cz - az, apx = px - ax, apy = py - ay, apz = pz - az;
            double d1 = abx * apx + aby * apy + abz * apz, d2 = acx * apx + acy * apy + acz * apz;
            double qx, qy, qz;
            if (d1 <= 0 && d2 <= 0) { qx = ax; qy = ay; qz = az; }
            else
            {
                double bpx = px - bx, bpy = py - by, bpz = pz - bz, d3 = abx * bpx + aby * bpy + abz * bpz, d4 = acx * bpx + acy * bpy + acz * bpz;
                double cpx = px - cx, cpy = py - cy, cpz = pz - cz, d5 = abx * cpx + aby * cpy + abz * cpz, d6 = acx * cpx + acy * cpy + acz * cpz;
                double vc = d1 * d4 - d3 * d2, vb = d5 * d2 - d1 * d6, va = d3 * d6 - d5 * d4;
                if (d3 >= 0 && d4 <= d3) { qx = bx; qy = by; qz = bz; }
                else if (vc <= 0 && d1 >= 0 && d3 <= 0) { double v = d1 / (d1 - d3); qx = ax + v * abx; qy = ay + v * aby; qz = az + v * abz; }
                else if (d6 >= 0 && d5 <= d6) { qx = cx; qy = cy; qz = cz; }
                else if (vb <= 0 && d2 >= 0 && d6 <= 0) { double w = d2 / (d2 - d6); qx = ax + w * acx; qy = ay + w * acy; qz = az + w * acz; }
                else if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0) { double w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); qx = bx + w * (cx - bx); qy = by + w * (cy - by); qz = bz + w * (cz - bz); }
                else
                {
                    double den = va + vb + vc; if (Math.Abs(den) < 1e-18) return 0;
                    double v = vb / den, w = vc / den; qx = ax + abx * v + acx * w; qy = ay + aby * v + acy * w; qz = az + abz * v + acz * w;
                }
            }
            double dx = px - qx, dy = py - qy, dz = pz - qz;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static int BinOf(double x, double y, double z)
        {
            double phi = Math.Atan2(x, -y) * 180.0 / Math.PI, lat = Math.Asin(z < -1 ? -1 : z > 1 ? 1 : z) * 180.0 / Math.PI;
            int k = (int)Math.Floor((phi + 180.0) / BinDeg), j = (int)Math.Floor((lat + 90.0) / BinDeg);
            k = ((k % BinsPhi) + BinsPhi) % BinsPhi; j = j < 0 ? 0 : j >= BinsLat ? BinsLat - 1 : j;
            return j * BinsPhi + k;
        }

        /// <summary>Does the ray o + t d (t in [tmin, tmax], d a unit direction) meet the ship? (o within the probe's reach of its lamp)</summary>
        public bool Ray(Probe p, double ox, double oy, double oz, double dx, double dy, double dz, double tmin, double tmax)
        {
            double dn = Math.Sqrt(dx * dx + dy * dy + dz * dz); dx /= dn; dy /= dn; dz /= dn;
            double off = Math.Sqrt((ox - p.Ox) * (ox - p.Ox) + (oy - p.Oy) * (oy - p.Oy) + (oz - p.Oz) * (oz - p.Oz));
            int[] cand = null;
            if (off <= p.Reach + 1e-6) cand = p.Bins[BinOf(dx, dy, dz)];
            int n = cand != null ? cand.Length : Tris;
            for (int q = 0; q < n; q++)
            {
                int b = (cand != null ? cand[q] : q) * 9;
                double e1x = T[b + 3] - T[b], e1y = T[b + 4] - T[b + 1], e1z = T[b + 5] - T[b + 2], e2x = T[b + 6] - T[b], e2y = T[b + 7] - T[b + 1], e2z = T[b + 8] - T[b + 2];
                double hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x, det = e1x * hx + e1y * hy + e1z * hz;
                if (Math.Abs(det) < 1e-12) continue;
                double inv = 1.0 / det, sx = ox - T[b], sy = oy - T[b + 1], sz = oz - T[b + 2], u = inv * (sx * hx + sy * hy + sz * hz);
                if (u < 0 || u > 1) continue;
                double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x, v = inv * (dx * qx + dy * qy + dz * qz);
                if (v < 0 || u + v > 1) continue;
                double t = inv * (e2x * qx + e2y * qy + e2z * qz);
                if (t >= tmin && t <= tmax) return true;
            }
            if (p.NoseHeadInWay)
                foreach (var s in NoseHead)
                {
                    double cx = ox - s[0], cy = oy - s[1], cz = oz - s[2], bb = cx * dx + cy * dy + cz * dz, cc = cx * cx + cy * cy + cz * cz - s[3] * s[3], disc = bb * bb - cc;
                    if (disc < 0) continue;
                    double sq = Math.Sqrt(disc), t1 = -bb - sq, t2 = -bb + sq;
                    if ((t1 >= tmin && t1 <= tmax) || (t2 >= tmin && t2 <= tmax) || (t1 < tmin && t2 > tmax)) return true;
                }
            return false;
        }

        /// <summary>Does a beam cone (apex o, axis a, half angle halfDeg, from tmin to tmax along it) meet the ship? Its axis and `edges` rays
        /// round its rim.</summary>
        public bool Cone(Probe p, double ox, double oy, double oz, double ax, double ay, double az, double halfDeg, double tmin, double tmax, int edges = 16)
        {
            double an = Math.Sqrt(ax * ax + ay * ay + az * az); ax /= an; ay /= an; az /= an;
            if (Ray(p, ox, oy, oz, ax, ay, az, tmin, tmax)) return true;
            double ux = 0, uy = 1, uz = 0; if (Math.Abs(ay) > 0.9) { ux = 1; uy = 0; }
            double px = ay * uz - az * uy, py = az * ux - ax * uz, pz = ax * uy - ay * ux, pn = Math.Sqrt(px * px + py * py + pz * pz); px /= pn; py /= pn; pz /= pn;
            double qx = ay * pz - az * py, qy = az * px - ax * pz, qz = ax * py - ay * px, t = Math.Tan(halfDeg * Math.PI / 180.0);
            for (int e = 0; e < edges; e++)
            {
                double w = 2.0 * Math.PI * e / edges, c = Math.Cos(w) * t, s = Math.Sin(w) * t;
                if (Ray(p, ox, oy, oz, ax + c * px + s * qx, ay + c * py + s * qy, az + c * pz + s * qz, tmin, tmax)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ a binary FBX file, as far as the meshes go
        static class Fbx
        {
            public sealed class Node { public string Name; public List<object> Props = new List<object>(); public List<Node> Kids = new List<Node>(); }

            public static Node Read(byte[] b)
            {
                if (b.Length < 30 || Encoding.ASCII.GetString(b, 0, 20) != "Kaydara FBX Binary  ") throw new InvalidDataException("not a binary FBX file");
                uint ver = BitConverter.ToUInt32(b, 23); bool wide = ver >= 7500;
                var root = new Node { Name = "root" }; long pos = 27;
                while (pos < b.Length - 200)
                {
                    var n = ReadNode(b, ref pos, wide); if (n == null) break;
                    root.Kids.Add(n);
                }
                return root;
            }

            static Node ReadNode(byte[] b, ref long pos, bool wide)
            {
                long end, nprops;
                if (wide) { end = (long)BitConverter.ToUInt64(b, (int)pos); nprops = (long)BitConverter.ToUInt64(b, (int)pos + 8); pos += 24; }
                else { end = BitConverter.ToUInt32(b, (int)pos); nprops = BitConverter.ToUInt32(b, (int)pos + 4); pos += 12; }
                int nl = b[pos]; pos += 1;
                if (end == 0) return null;
                var n = new Node { Name = Encoding.ASCII.GetString(b, (int)pos, nl) }; pos += nl;
                for (long i = 0; i < nprops; i++) n.Props.Add(ReadProp(b, ref pos));
                while (pos < end)
                {
                    var k = ReadNode(b, ref pos, wide);
                    if (k == null) break;
                    n.Kids.Add(k);
                }
                pos = end;
                return n;
            }

            static object ReadProp(byte[] b, ref long pos)
            {
                char t = (char)b[pos]; pos += 1; int p = (int)pos;
                switch (t)
                {
                    case 'Y': pos += 2; return (long)BitConverter.ToInt16(b, p);
                    case 'C': pos += 1; return (long)b[p];
                    case 'I': pos += 4; return (long)BitConverter.ToInt32(b, p);
                    case 'F': pos += 4; return (double)BitConverter.ToSingle(b, p);
                    case 'D': pos += 8; return BitConverter.ToDouble(b, p);
                    case 'L': pos += 8; return BitConverter.ToInt64(b, p);
                    case 'S': case 'R': { int len = (int)BitConverter.ToUInt32(b, p); pos += 4 + len; return t == 'S' ? (object)Encoding.UTF8.GetString(b, p + 4, len) : new byte[0]; }
                    case 'f': case 'd': case 'l': case 'i': case 'b':
                    {
                        int count = (int)BitConverter.ToUInt32(b, p), enc = (int)BitConverter.ToUInt32(b, p + 4), clen = (int)BitConverter.ToUInt32(b, p + 8);
                        pos += 12 + clen;
                        int size = t == 'f' || t == 'i' ? 4 : t == 'b' ? 1 : 8;
                        byte[] raw;
                        if (enc == 1)
                        {
                            raw = new byte[count * size];
                            using (var ms = new MemoryStream(b, p + 12 + 2, clen - 2))                       // a zlib stream: its 2 byte header, then deflate
                            using (var z = new DeflateStream(ms, CompressionMode.Decompress))
                            {
                                int got = 0; while (got < raw.Length) { int r = z.Read(raw, got, raw.Length - got); if (r <= 0) break; got += r; }
                                if (got < raw.Length) throw new InvalidDataException("a packed array came out short");
                            }
                        }
                        else { raw = new byte[count * size]; Buffer.BlockCopy(b, p + 12, raw, 0, raw.Length); }
                        var v = new double[count];
                        for (int i = 0; i < count; i++)
                            v[i] = t == 'f' ? BitConverter.ToSingle(raw, i * 4) : t == 'd' ? BitConverter.ToDouble(raw, i * 8) : t == 'i' ? BitConverter.ToInt32(raw, i * 4) : t == 'l' ? BitConverter.ToInt64(raw, i * 8) : raw[i];
                        return v;
                    }
                    default: throw new InvalidDataException("unknown FBX property type " + t);
                }
            }

            static Node Child(Node n, string name) { foreach (var k in n.Kids) if (k.Name == name) return k; return null; }
            static string ObjName(Node n) { var s = n.Props.Count > 1 ? n.Props[1] as string : null; if (s == null) return ""; int i = s.IndexOf("\u0000\u0001", StringComparison.Ordinal); return i >= 0 ? s.Substring(0, i) : s; }

            /// <summary>{mesh model name: its triangles in root space (9 numbers each)}; markers: the Null models' translations (root space).</summary>
            public static Dictionary<string, List<double>> Meshes(Node root, Dictionary<string, double[]> markers)
            {
                var objs = Child(root, "Objects"); var conns = Child(root, "Connections");
                var geo = new Dictionary<long, Node>(); var mdl = new Dictionary<long, Node>();
                foreach (var k in objs.Kids) { if (k.Name == "Geometry") geo[(long)k.Props[0]] = k; else if (k.Name == "Model") mdl[(long)k.Props[0]] = k; }
                double[] Translation(Node m)
                {
                    var p70 = Child(m, "Properties70"); var t = new double[3];
                    if (p70 != null)
                        foreach (var p in p70.Kids)
                        {
                            var key = p.Props.Count > 0 ? p.Props[0] as string : null;
                            if (key == "Lcl Translation") { t[0] = (double)p.Props[4]; t[1] = (double)p.Props[5]; t[2] = (double)p.Props[6]; }
                            else if ((key == "Lcl Rotation" || key == "GeometricTranslation" || key == "GeometricRotation" || key == "PreRotation") && geo.Count > 0
                                     && (Math.Abs((double)p.Props[4]) > 1e-6 || Math.Abs((double)p.Props[5]) > 1e-6 || Math.Abs((double)p.Props[6]) > 1e-6) && (m.Props.Count > 2 && (m.Props[2] as string) == "Mesh"))
                                throw new InvalidDataException(ObjName(m) + ": " + key + " (only translations are read)");
                        }
                    return t;
                }
                if (markers != null)
                    foreach (var m in mdl.Values)
                        if (m.Props.Count > 2 && (m.Props[2] as string) == "Null") { var t = Translation(m); markers[ObjName(m)] = new[] { -t[0], t[2], -t[1] }; }
                var outp = new Dictionary<string, List<double>>();
                foreach (var c in conns.Kids)
                {
                    if (c.Name != "C" || c.Props.Count < 3 || (c.Props[0] as string) != "OO") continue;
                    long a = (long)c.Props[1], pa = (long)c.Props[2];
                    if (!geo.TryGetValue(a, out var g) || !mdl.TryGetValue(pa, out var m)) continue;
                    var t = Translation(m);
                    var V = (double[])Child(g, "Vertices").Props[0]; var I = (double[])Child(g, "PolygonVertexIndex").Props[0];
                    var tri = new List<double>(); var poly = new List<int>();
                    void Put(int vi) { double x = V[vi * 3] + t[0], y = V[vi * 3 + 1] + t[1], z = V[vi * 3 + 2] + t[2]; tri.Add(-x); tri.Add(z); tri.Add(-y); }
                    foreach (var dv in I)
                    {
                        int iv = (int)dv;
                        if (iv < 0) { poly.Add(-iv - 1); for (int k = 1; k + 1 < poly.Count; k++) { Put(poly[0]); Put(poly[k]); Put(poly[k + 1]); } poly.Clear(); }
                        else poly.Add(iv);
                    }
                    outp[ObjName(m)] = tri;
                }
                return outp;
            }
        }
    }

    /// <summary>
    /// K256d2 (test only): the nose searchlight's drawn beam (OzShuttleNightLights: NosePivot (0, 0.80, 2.45), NoseScale 0.45, the pitch
    /// node 0.02 under the pivot, the beam cone from 0.36 in front of the drum, as long as the reach (42 m) + 0.8, its drawn half angle
    /// atan(tan(angle / 2) x 1.2)) against the ship's model, in the ship's root space.
    /// </summary>
    public static class OzNoseConeBeam
    {
        const double D2R = Math.PI / 180.0;
        public static readonly double[] Pivot = { 0.0, 0.80 - 0.02 * 0.45, 2.45 };
        public const double Reach = 0.36 * 0.45, BeamLen = 42.8;
        /// <summary>Beam tests made (all callers).</summary>
        public static int Tests;

        /// <summary>The drawn cone's half angle (deg) for a cone of angleDeg.</summary>
        public static double DrawnHalfDeg(double angleDeg) => Math.Atan(Math.Tan(Math.Max(2.0, Math.Min(40.0, angleDeg * 0.5)) * D2R) * 1.2) / D2R;

        /// <summary>The probe of the nose searchlight's beams (they start within Reach of its drum's pivot).</summary>
        public static OzNoseConeShipModel.Probe ProbeOf(OzNoseConeShipModel m) => m.ProbeFor(Pivot[0], Pivot[1], Pivot[2], Reach, false);

        /// <summary>Does the drawn beam at this aim (turn + right, drop under the horizon; deg) and cone (deg) meet the ship? Its axis and five
        /// rings of rays (64, 52, 40, 28, 16: under 1.3 deg apart on its rim) at the full, 0.8, 0.6, 0.4 and 0.2 of its half angle (+ widenDeg).</summary>
        public static bool Through(OzNoseConeShipModel m, OzNoseConeShipModel.Probe p, double yaw, double pitch, double angleDeg, double widenDeg = 0)
        {
            Tests++;
            double y = yaw * D2R, q = pitch * D2R, dx = Math.Sin(y) * Math.Cos(q), dy = -Math.Sin(q), dz = Math.Cos(y) * Math.Cos(q);
            double ox = Pivot[0] + Reach * dx, oy = Pivot[1] + Reach * dy, oz = Pivot[2] + Reach * dz, h = DrawnHalfDeg(angleDeg) + widenDeg;
            return m.Cone(p, ox, oy, oz, dx, dy, dz, h, 0.004, BeamLen, 64) || m.Cone(p, ox, oy, oz, dx, dy, dz, h * 0.8, 0.004, BeamLen, 52)
                || m.Cone(p, ox, oy, oz, dx, dy, dz, h * 0.6, 0.004, BeamLen, 40) || m.Cone(p, ox, oy, oz, dx, dy, dz, h * 0.4, 0.004, BeamLen, 28)
                || m.Cone(p, ox, oy, oz, dx, dy, dz, h * 0.2, 0.004, BeamLen, 16);
        }

        /// <summary>The turn and drop (deg) of a direction in the ship's root space.</summary>
        public static void Aim(double x, double y, double z, out double yaw, out double pitch)
        {
            yaw = Math.Atan2(x, z) / D2R; pitch = Math.Atan2(-y, Math.Max(1e-9, Math.Sqrt(x * x + z * z))) / D2R;
        }
    }
}
#endif
