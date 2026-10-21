using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1, item 172 (the main fort cannon's death landing): a big piece blown off by a death (the fort's top cannon,
    /// FortGunView; also the turret of a tank or a gun, UnitView.TossTurret, which flies the same kind of arc) used to stop on its
    /// landing frame like a paperweight: the cannon's turn eased to a stop before the ground (SmoothStep), it never bounced, slid or
    /// rocked, and its resting height came from the renderer box (the box round each part's turned box, not its real shape: lying on
    /// its side the round cannon hovered 0.2-0.5 m over flat ground); a tank turret snapped to a new pose on its landing frame. Now the
    /// landing is HEAVY: the cannon still turns hard as it hits (<see cref="FallBlend"/>), then on the landing frame a burst of dust and
    /// stone chips, the heavy concrete impact sound (dst_fortgun_land_heavy, three takes) on top of the metal crash it had, the small
    /// camera kick it had (OzShakeGate: on screen and the shake option); after it one short low bounce, a short skid along its flight
    /// and a damped rock, and it lies ON the ground the whole time: every frame its REAL lowest vertex (the meshes' own vertices, turned
    /// as they are drawn) is put 5 cm into the ground under that vertex, so it follows the ground along the skid, the rock tips it
    /// about the end that goes down (never into the ground) and the settle frame has no height step. A part whose mesh cannot be read
    /// falls back to its box corners, 0.2 m in (as release 1.0). It waits while the game is paused. View only.
    /// Switch: <see cref="Enabled"/> (off = the landing of release 1.0).
    /// </summary>
    public class OzHeavyLanding : MonoBehaviour
    {
        public static bool Enabled = true;
        /// <summary>The tank / gun turret tosses get the same landing (no snap; bounce, rock, ground fit), smaller.</summary>
        public static bool Turrets = true;
        public const string SoundId = "dst_fortgun_land_heavy";
        /// <summary>How deep the real lowest vertex lies in the ground (m), and a box corner's (a part whose mesh cannot be read).</summary>
        public const float Sink = 0.05f, BoxSink = 0.2f;
        /// <summary>Statistics (Play checks): landings started, the last one's kind, its touch-down speed (m/s), how its parts were fitted
        /// (LastFit: "vertices" = every part by its real mesh vertices, else how many by box corners), whether its resting contact is a
        /// real vertex (LastContactVertex), the gap between its lowest point and the ground under that point after it settled (m, + =
        /// above, - = in; measured with the exact ground probe, a box corner counted 15 cm higher), the height step on the settle frame
        /// (m), how far the renderer box's bottom sat under its real lowest vertex (m: what a box fit floated), its settle time (s),
        /// bounce height (m), skid (m), and the heavy landing sounds and dust bursts made.</summary>
        public static int Landings, Sounds, DustBursts, Settled;
        public static string LastKind = "-", LastFit = "-";
        public static bool LastContactVertex;
        public static float LastSpeed, LastGap, LastPop, LastBoxGap, LastSettle, LastBounce, LastSkid, LastAt = -1f;

        const float Cell = 0.5f;                                  // the ground lattice under the piece (m): one ground probe per node, bilinear between
        const int MaxPoints = 2000, Directions = 384;             // a mesh with more distinct vertices keeps its outermost ones (a lowest point always is one)

        struct Part { public Transform space; public Vector3[] pts; public float sink; }
        readonly List<Part> parts = new List<Part>();
        readonly Dictionary<long, float> lattice = new Dictionary<long, float>();
        static readonly Dictionary<Mesh, Vector3[]> pointCache = new Dictionary<Mesh, Vector3[]>();
        static Vector3[] wp = new Vector3[4096]; static float[] ws = new float[4096];

        Vector3 basePos, skidDir, rockAxis, under, touch, contactAt; Quaternion fromRot, restRot;
        float t, startOffY, offT, bounceH, bounceT, skid, skidT, rockDeg, turnT, settleT, lastY; bool second, fits; string kind, fit = "-";

        /// <summary>FortGunView.UpdateToss: the rotation blend of the last part of the flight. Off: release 1.0's SmoothStep (the turn
        /// slows to a stop before the ground). On: w^2 from 60 % of the flight, so the cannon is still turning hard when it hits.</summary>
        public static float FallBlend(float k)
        {
            if (!Enabled) return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, k));
            float w = Mathf.InverseLerp(0.6f, 1f, k);
            return w * w;
        }

        /// <summary>FortGunView.UpdateToss, the landing frame (after its blast, dust, metal chips, camera kick and metal crash sound): the
        /// heavy landing of the fort's cannon. p = where it touched down; the arc's numbers give the touch-down velocity.</summary>
        public static void FortGun(Transform piece, Vector3 p, Vector3 from, Vector3 to, float apex, float duration, int emitter)
        {
            if (!Enabled || !piece) return;
            float T = Mathf.Max(0.1f, duration);
            var vel = new Vector3((to.x - from.x) / T, ((to.y - from.y) - 4f * apex) / T, (to.z - from.z) / T);
            if (OzWaterLanding.OpenWaterAt(p, out _)) return;                                             // over open water: as before (no ground to land on)
            var l = Begin(piece, piece.rotation, vel, "fort cannon", 0.22f, 0.30f, 7f, 0f);
            var fxp = OpenZFxPlayer.I; var fx = FxManager.I;
            var under = l.under; var hit = l.touch;                                                      // the ground under its middle, and where it touches it (p is its pivot, about 3 m up)
            if (fxp && fxp.isActiveAndEnabled) { fxp.DemolitionDust(under, 7f, 7f, 0.5f); fxp.Dust(hit, 2.4f); }   // a wide, low ring of dust out of the impact; puffs 5.2-7.4 m, a building collapse's size
            else if (fx) fx.DemolitionDust(under, 7f, 7f, 1.2f, 0.7f, 1.3f);                                // (this path: puffs 4.1-5.8 m)
            if (fx) { fx.Dust(under + l.skidDir * 1.2f, 1.6f); fx.Breakup(hit + Vector3.up * 0.4f, "stone", 5f, 18, 1.5f); }   // concrete chips fly off the ground it hits
            DustBursts++;
            if (AudioDirector.Has(SoundId)) { AudioDirector.Fire(SoundId, p, emitter); Sounds++; }
        }

        /// <summary>UnitView.UpdateToss, the landing frame of a tank / gun turret: the rotation to set now. Over open water: `rest` at once
        /// (the water landing rule takes it, as before). On ground: it keeps the pose it flew in and turns into `rest` in 0.16 s while it
        /// bounces a little, rocks and lies on the ground.</summary>
        public static Quaternion TurretRest(Transform piece, Vector3 p, Quaternion rest, Vector3 vel)
        {
            if (!Enabled || !Turrets || !piece || OzWaterLanding.OpenWaterAt(p, out _)) return rest;
            Begin(piece, rest, vel, "turret", 0.12f, 0.22f, 9f, 0.16f);
            return piece.rotation;
        }

        static OzHeavyLanding Begin(Transform piece, Quaternion rest, Vector3 vel, string kind, float bounceMax, float bounceTime, float rock, float turn)
        {
            var old = piece.GetComponent<OzHeavyLanding>(); if (old) Destroy(old);
            var l = piece.gameObject.AddComponent<OzHeavyLanding>();
            var h = new Vector3(vel.x, 0f, vel.z);
            l.kind = kind; l.fromRot = piece.rotation; l.restRot = rest; l.turnT = turn;
            l.skidDir = h.sqrMagnitude > 0.01f ? h.normalized : Vector3.forward;
            l.rockAxis = Vector3.Cross(Vector3.up, l.skidDir);
            float down = Mathf.Abs(Mathf.Min(0f, vel.y));
            l.bounceH = Mathf.Clamp(0.012f * down, 0.04f, bounceMax);                                   // heavy: a few cm to 0.2 m, never a rubber ball
            l.bounceT = bounceTime;
            l.skid = Mathf.Clamp(h.magnitude * 0.12f, 0.15f, kind == "turret" ? 0.6f : 1.2f);
            l.skidT = 0.55f; l.rockDeg = rock; l.settleT = Mathf.Max(l.bounceT, l.skidT) + 0.45f;
            l.basePos = piece.position; l.lastY = piece.position.y;
            l.Collect();
            l.fits = l.Lift(out float lift, out int c);                                                // in the pose it touched down in
            var pp = piece.position;
            l.under = new Vector3(pp.x, l.fits ? l.Ground(pp.x, pp.z) : ZGame.I ? ZGame.I.GroundY(pp.x, pp.z) : pp.y, pp.z);
            l.touch = l.fits ? new Vector3(wp[c].x, l.Ground(wp[c].x, wp[c].z), wp[c].z) : l.under;
            l.contactAt = l.touch;
            l.startOffY = l.fits ? -lift : 0f;                                                          // + = its lowest point is still above the ground: it falls the rest
            l.offT = l.startOffY > 0f ? Mathf.Clamp(l.startOffY / Mathf.Max(3f, down), 0.02f, 0.12f) : 0.08f;   // at its touch-down speed; - = it was in: out in 0.08 s
            Landings++; LastKind = kind; LastFit = l.fit; LastSpeed = vel.magnitude; LastBounce = l.bounceH; LastSkid = l.skid; LastAt = Time.time;
            return l;
        }

        void Update()
        {
            var g = ZGame.I;
            float dt = g && g.paused ? 0f : Time.deltaTime;
            if (dt <= 0f) return;                                                                       // paused: the landing waits, as the cannon's flight does
            t += dt;
            if (t >= settleT) { Settle(); return; }
            float bounce = t < bounceT ? bounceH * 4f * (t / bounceT) * (1f - t / bounceT) : 0f;
            float k = Mathf.Clamp01(t / skidT), s = skid * (1f - (1f - k) * (1f - k));
            float rock = rockDeg * Mathf.Exp(-t / 0.16f) * Mathf.Sin(2f * Mathf.PI * t / 0.34f);
            var rot = restRot;
            if (turnT > 0f && t < turnT) { float u = t / turnT; rot = Quaternion.Slerp(fromRot, restRot, 1f - (1f - u) * (1f - u)); }   // tips over onto its rest side, fast
            rot = Quaternion.AngleAxis(rock, rockAxis) * rot;
            float off = startOffY > 0f ? startOffY * Mathf.Max(0f, 1f - t / offT) : startOffY * (1f - Mathf.SmoothStep(0f, 1f, t / offT));
            var at = basePos + skidDir * s;
            transform.SetPositionAndRotation(new Vector3(at.x, transform.position.y, at.z), rot);
            float lift = 0f; int c = -1;
            bool fitted = fits && Lift(out lift, out c);
            float y = fitted ? transform.position.y + lift : basePos.y;                                 // its real lowest point 5 cm into the ground under it: along the skid, through the rock
            if (fitted) contactAt = wp[c] + Vector3.up * (lift + off + bounce);
            lastY = y + off + bounce;
            transform.position = new Vector3(at.x, lastY, at.z);
            if (!second && t >= bounceT)
            {   // the bounce comes down: a second, smaller puff
                second = true;
                if (FxManager.I) FxManager.I.Dust(fits ? contactAt : transform.position, kind == "turret" ? 0.6f : 1.2f);   // where it touches down again
            }
        }

        void Settle()
        {
            var at = basePos + skidDir * skid;
            transform.SetPositionAndRotation(new Vector3(at.x, transform.position.y, at.z), restRot);
            if (fits && Lift(out float lift, out _)) transform.position += Vector3.up * lift;
            else transform.position = new Vector3(at.x, basePos.y, at.z);
            LastPop = Mathf.Abs(transform.position.y - lastY);
            LastGap = fits ? ExactGap(out LastBoxGap, out LastContactVertex) : 0f;
            if (!fits) { LastBoxGap = 0f; LastContactVertex = false; }
            LastFit = fit; LastKind = kind; LastSettle = t; Settled++;
            Destroy(this);
        }

        // ------------------------------------------------------------------ the fit by the real vertices

        /// <summary>The solid parts drawn under the piece: each readable mesh with its distinct vertices (only those its index lists use:
        /// the cannon's split ring and head meshes share one vertex list), else the 8 corners of its mesh box; any other renderer
        /// (skinned) the corners of its box now, carried with the piece.</summary>
        void Collect()
        {
            parts.Clear(); int vtx = 0, box = 0;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r is BillboardRenderer || r is SpriteRenderer) continue;
                if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                    if (!mesh) continue;
                    var pts = Points(mesh);
                    if (pts != null) { if (pts.Length > 0) { parts.Add(new Part { space = r.transform, pts = pts, sink = Sink }); vtx++; } continue; }
                    parts.Add(new Part { space = r.transform, pts = Corners(mesh.bounds), sink = BoxSink }); box++;
                    continue;
                }
                var c = Corners(r.bounds); var inv = transform.worldToLocalMatrix;
                for (int i = 0; i < c.Length; i++) c[i] = inv.MultiplyPoint3x4(c[i]);
                parts.Add(new Part { space = transform, pts = c, sink = BoxSink }); box++;
            }
            fit = box == 0 ? (vtx > 0 ? "vertices" : "-") : vtx + " parts by vertices, " + box + " by box corners";
        }

        /// <summary>A readable mesh's distinct vertices that its index lists use (cached per mesh), null when it cannot be read. Over
        /// MaxPoints: only the outermost ones in 384 directions.</summary>
        static Vector3[] Points(Mesh m)
        {
            if (pointCache.TryGetValue(m, out var cached)) return cached;
            Vector3[] res = null;
            if (m.isReadable)
            {
                var v = m.vertices; var seen = new HashSet<Vector3>(); var list = new List<Vector3>();
                for (int s = 0; s < m.subMeshCount; s++)
                    foreach (int i in m.GetIndices(s))
                        if (i >= 0 && i < v.Length && seen.Add(v[i])) list.Add(v[i]);
                res = list.Count > MaxPoints ? Outermost(list) : list.ToArray();
            }
            if (pointCache.Count > 64) { var dead = new List<Mesh>(); foreach (var key in pointCache.Keys) if (!key) dead.Add(key); foreach (var key in dead) pointCache.Remove(key); }
            pointCache[m] = res;
            return res;
        }

        static Vector3[] Outermost(List<Vector3> pts)
        {
            var keep = new HashSet<int>(); float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int d = 0; d < Directions; d++)
            {
                float y = 1f - 2f * (d + 0.5f) / Directions, r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)), a = ga * d;
                var dir = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                int best = 0; float bv = float.MinValue;
                for (int j = 0; j < pts.Count; j++) { float x = Vector3.Dot(pts[j], dir); if (x > bv) { bv = x; best = j; } }
                keep.Add(best);
            }
            var res = new Vector3[keep.Count]; int n = 0;
            foreach (int j in keep) res[n++] = pts[j];
            return res;
        }

        static Vector3[] Corners(Bounds b)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++) c[i] = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            return c;
        }

        /// <summary>Every point of every part in the world as drawn now (wp) with its sink (ws); returns the count, the lowest height and
        /// the ground box round them.</summary>
        int Fill(out float minY, out float x0, out float x1, out float z0, out float z1)
        {
            int n = 0; minY = x0 = z0 = float.MaxValue; x1 = z1 = float.MinValue;
            foreach (var pa in parts)
            {
                if (!pa.space) continue;
                if (n + pa.pts.Length > wp.Length) { int size = Mathf.NextPowerOfTwo(n + pa.pts.Length); System.Array.Resize(ref wp, size); System.Array.Resize(ref ws, size); }
                var m = pa.space.localToWorldMatrix;
                foreach (var p in pa.pts)
                {
                    var q = m.MultiplyPoint3x4(p); wp[n] = q; ws[n] = pa.sink; n++;
                    if (q.y < minY) minY = q.y;
                    if (q.x < x0) x0 = q.x;
                    if (q.x > x1) x1 = q.x;
                    if (q.z < z0) z0 = q.z;
                    if (q.z > z1) z1 = q.z;
                }
            }
            return n;
        }

        /// <summary>How far to move the piece up (+) or down (-) from where it is drawn now so that its lowest point lies at its sink in
        /// the ground under that point (vertices 5 cm, box corners 0.2 m); contact = the index of that point in wp. Only points within the
        /// ground's own rise under the piece (+ 0.55 m) of the lowest one are tried. False without ground or parts.</summary>
        bool Lift(out float lift, out int contact)
        {
            lift = 0f; contact = -1;
            if (!ZGame.I || parts.Count == 0) return false;
            int n = Fill(out float minY, out float x0, out float x1, out float z0, out float z1);
            if (n == 0) return false;
            float band = Band(x0, x1, z0, z1), best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var q = wp[i]; if (q.y > minY + band) continue;
                float e = q.y - Ground(q.x, q.z) + ws[i];                                               // 0 when this point lies at its sink
                if (e < best) { best = e; contact = i; }
            }
            if (contact < 0) return false;
            lift = -best;
            return true;
        }

        float Band(float x0, float x1, float z0, float z1)
        {
            float a = Ground(x0, z0), b = Ground(x1, z0), c = Ground(x0, z1), d = Ground(x1, z1), e = Ground((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
            float hi = Mathf.Max(Mathf.Max(a, b), Mathf.Max(Mathf.Max(c, d), e)), lo = Mathf.Min(Mathf.Min(a, b), Mathf.Min(Mathf.Min(c, d), e));
            return hi - lo + 0.4f + (BoxSink - Sink);
        }

        /// <summary>The settled gap, measured apart from the fit: the 48 lowest points by the lattice, each with the exact ground probe
        /// under it (ZGame.GroundY); a box corner counts 15 cm higher (it stands off the real shape). vertex = the lowest is a real
        /// vertex. boxGap = how far the renderer box's bottom sits under the real lowest vertex (what a box fit floated).</summary>
        float ExactGap(out float boxGap, out bool vertex)
        {
            boxGap = 0f; vertex = false; var g = ZGame.I;
            int n = Fill(out float minY, out float x0, out float x1, out float z0, out float z1);
            if (!g || n == 0) return 0f;
            float band = Band(x0, x1, z0, z1), lowV = float.MaxValue;
            var cand = new List<KeyValuePair<float, int>>();
            for (int i = 0; i < n; i++)
            {
                var q = wp[i];
                if (ws[i] == Sink && q.y < lowV) lowV = q.y;
                if (q.y <= minY + band) cand.Add(new KeyValuePair<float, int>(q.y - Ground(q.x, q.z) + ws[i], i));
            }
            cand.Sort((a, b) => a.Key.CompareTo(b.Key));
            float best = float.MaxValue;
            for (int k = 0; k < cand.Count && k < 48; k++)
            {
                int i = cand[k].Value; var q = wp[i];
                float gap = q.y - g.GroundY(q.x, q.z) + ws[i] - Sink;
                if (gap < best) { best = gap; vertex = ws[i] == Sink; }
            }
            if (lowV < float.MaxValue) { var b = RendererBox(transform); if (b.size != Vector3.zero) boxGap = lowV - b.min.y; }
            return best == float.MaxValue ? 0f : best;
        }

        float Ground(float x, float z)
        {
            float fx = x / Cell, fz = z / Cell; int i = Mathf.FloorToInt(fx), j = Mathf.FloorToInt(fz); float u = fx - i, w = fz - j;
            return Mathf.Lerp(Mathf.Lerp(Node(i, j), Node(i + 1, j), u), Mathf.Lerp(Node(i, j + 1), Node(i + 1, j + 1), u), w);
        }

        float Node(int i, int j)
        {
            long key = ((long)i << 32) ^ (uint)j;
            if (!lattice.TryGetValue(key, out float h)) { var g = ZGame.I; h = g ? g.GroundY(i * Cell, j * Cell) : 0f; lattice[key] = h; }
            return h;
        }

        static Bounds RendererBox(Transform t)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return any ? b : new Bounds(t.position, Vector3.zero);
        }
    }
}
