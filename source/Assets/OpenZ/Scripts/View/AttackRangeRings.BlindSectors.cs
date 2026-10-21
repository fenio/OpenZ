using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary>
    /// Whenever this ring shows for a gun (a selected gun of any team, a fort's
    /// tower gun, the gun being placed: AttackRangeRings.Placement), the cells inside its range that the rules refuse to fire into
    /// because something stands in the way are laid on the ground under the ring: a slight darkening (blindDarkDay, a touch less
    /// by night) and a faint dotted line from the gun along each sector's sides (much finer and fainter than the ring's dots), no
    /// marks. The shape is OzBlindSectorsCore's: the union of the wedges the rules' own tile lines leave behind each blocker, exact
    /// at every cell centre (the counted checks). It bursts out with the ring (the ring's radius), holds and fades with it (the
    /// ring's alpha, the placement's dim included), drawn in the overlay family (OpenZ/Overlay: depth tested, polygon offset, no depth
    /// write) as the lowest ground mark (its own queue, before the placement zone, the empty markers and the ring's dots) on the ground
    /// (ZGame.GroundY: terrain, decks, craters, sampled on a 1 m lattice that a placed gun's tile steps keep, re-sampled while it shows).
    /// While a ring shows, a cheap test of its box (OzBlindSectorsCore.Signature) redraws the sectors the moment a tree is felled or a
    /// rock, hut or building is destroyed or rebuilt; a placed gun's sectors follow its spot. Guns that fire over obstacles (the
    /// howitzer) only get the shadows of what still stops them (rocks, trees, huts, map objects: Zod's barrier rule). View only.
    /// </summary>
    public partial class AttackRangeRings
    {
        [Header("Blind sectors: where a gun cannot fire, shown with its ring")]
        [Tooltip("Show a gun's blind sectors with its range ring")] public bool blindSectors = true;
        [Tooltip("Strength of the grey over the blind ground by day / by night (its colour: blindVeilDay / blindVeilNight; K333c: slightly darker grey, every blind sector clear beside the open ground)")] [Range(0f, 0.6f)] public float blindDarkDay = 0.4f, blindDarkNight = 0.42f;
        [Tooltip("Strength of the dashed side lines from the gun by day / by night (K333b)")] [Range(0f, 1f)] public float blindLineDay = 0.5f, blindLineNight = 0.34f;
        [Tooltip("The side lines' colour: a pale tint, this much of the team colour")] [Range(0f, 1f)] public float blindLineTeam = 0.3f;
        [Tooltip("The side lines' dashes (m at 40 m camera distance, they grow with the zoom like the ring's dots; K333b: thin dashes)")] public float blindDotLength = 0.3f, blindDotWidth = 0.07f;
        [Tooltip("Above the sampled ground (m)")] public float blindLift = 0.1f;
        [Tooltip("Ground samples per frame: first pass / refresh while it shows (craters)")] public int blindFirstSamples = 400, blindRefreshSamples = 64;

        const int DotV = 7, DotT = 6;                                                                        // a soft dot: centre + 6 rim vertices

        sealed partial class Blind                                                                       // K333b adds the ring's blind arcs (AttackRangeRings.BlindFromGun.cs)
        {
            public readonly OzBlindSectorsCore core = new OzBlindSectorsCore();
            public readonly List<OzBlindSectorsCore> others = new List<OzBlindSectorsCore>();          // the other guns of a shared ring (cells blind for all of them)
            public ZWorld world; public int gx = int.MinValue, gy, range, ex, guns; public bool direct; public long sig; public double ringX, ringY;
            public string typeRef, gunType;
            public Mesh mesh; public Vector3[] v; public Color32[] c; public int[] tri; public float[] baseA, dC; public bool[] got;
            public int nFill, nDots, nV, nTri, cursor, missing; public bool line0;
            public Vector3[] dotP; public Vector2[] dotDir; public float[] dotA;
            public float lastA = -1f, lastR = -1f, lastNight = -1f, lastZs = -1f, lastLine = -1f; public bool dirtyV;
            public Color32 tint;
            public readonly OzGroundLattice ground = new OzGroundLattice();                                // the ground heights, kept across a placed gun's tile steps
            public int refresh, shownFrame = -10; public bool newGot;                                 // newGot: vertices got their height this frame (their colours follow)
            public float t0 = float.NaN;                                                                 // the ring's start (Begin) these sectors belong to
        }

        readonly Dictionary<Ring, Blind> blinds = new Dictionary<Ring, Blind>();
        readonly List<OzBlindSectorsCore> spareCores = new List<OzBlindSectorsCore>();
        Material blindMat;

        /// <summary>Rings that drew blind sectors this frame / their blind cells' sectors (for the checks and the proof).</summary>
        public int BlindShown { get; private set; }
        int blindFrame = -1;

        Material BlindMat()
        {
            if (blindMat) return blindMat;
            var baseMat = overlayMaterial;
            if (!baseMat) { var so = GetComponent<SelectionOverlay>(); if (!so) so = Object.FindFirstObjectByType<SelectionOverlay>(); if (so) baseMat = so.overlayMaterial; }
            if (!baseMat) { var sh = Shader.Find("OpenZ/Overlay"); if (!sh) return null; baseMat = new Material(sh); }
            blindMat = new Material(baseMat) { name = "OZ_RangeRing_Blind" };
            blindMat.SetColor("_Color", Color.white);                                                        // the vertex colours carry everything
            blindMat.SetFloat("_ZTest", (float)CompareFunction.LessEqual);                                     // under the units, as the ring
            if (blindMat.HasProperty("_Scan")) blindMat.SetFloat("_Scan", 0f);
            // the lowest of the ground marks (Transparent+48 with the scene's overlay material): before the placement zone and the empty
            // markers (Transparent+49: OzGunZone, TeamReadability) and the ring's dots, selection rings and route dots (Transparent+50).
            // A queue of its own: in a shared queue the order with those marks would follow the camera distance and pop.
            blindMat.renderQueue = Mathf.Max((int)RenderQueue.Transparent + 1, blindMat.renderQueue - 2);
            return blindMat;
        }

        void BlindDestroy()
        {
            foreach (var kv in blinds) if (kv.Value.mesh) Destroy(kv.Value.mesh);
            BlindGreyDestroy();                                                                           // K333c: the grey dots (AttackRangeRings.BlindClearer.cs)
            blinds.Clear();
            if (blindMat) Destroy(blindMat);
        }

        /// <summary>Draw's hook: this ring's blind sectors, if it is a gun's ring (or the gun being placed).</summary>
        void DrawBlind(Ring r, float age, float zs)
        {
            if (!blindSectors || !game || game.World == null || r.a <= 0.001f) return;
            if (blindFrame != Time.frameCount) { blindFrame = Time.frameCount; BlindShown = 0; }
            var w = game.World;
            bool placing = r.type != null && r.type.StartsWith(PlaceKey, System.StringComparison.Ordinal);
            blinds.TryGetValue(r, out var b);
            if (!placing && (r.units.Count == 0 || r.units[0] == null || r.units[0].Kind != ZKind.Cannon))
            {
                // its gun left the selection: the sectors fade with the ring. Only the sectors of this very ring (a pooled ring that now
                // shows another unit's range never brings back the sectors of the gun it showed before)
                if (b != null && b.mesh && r.units.Count == 0 && b.nTri > 0 && b.guns > 0 && b.t0 == r.t0) Show(b, r, age, zs);
                else if (b != null && r.units.Count > 0) b.guns = 0;
                return;
            }
            if (b == null) { b = new Blind(); blinds[r] = b; }
            b.t0 = r.t0;
            if (b.shownFrame < Time.frameCount - 1)
            {   // shown again after a pause (a new selection of the same gun, a new placement): the ground is sampled afresh (craters
                // may be new); while it shows frame after frame (a placed gun's tile steps) the samples are kept
                b.ground.Clear();
                if (b.got != null) for (int i = 0; i < b.nV; i++) b.got[i] = false;
                b.missing = b.nV; b.cursor = 0;
            }
            b.shownFrame = Time.frameCount;

            // ---- the gun(s): centre, range, fire, exemption (the rules' own values; for a placement as ZWorld.CreateObject will give them)
            int gx, gy, range, ex, guns; bool direct;
            if (placing)
            {
                var px = ZGame.WorldToPx(r.center); gx = px.x; gy = px.y; range = r.radiusPx; guns = 0;
                if (!ReferenceEquals(b.typeRef, r.type)) { b.typeRef = r.type; b.gunType = r.type.Substring(PlaceKey.Length); }
                direct = OzBlindSectorsCore.DirectFireType(b.gunType);
                ex = gx == b.gx && gy == b.gy && b.world == w ? b.ex : OzBlindSectorsCore.ExemptAt(w, gx, gy);
            }
            else
            {
                var o = r.units[0];
                gx = o.CenterX; gy = o.CenterY; range = o.AttackRadius; direct = o.LosActive; ex = w.Sight.ExemptOf(o); guns = r.units.Count;
            }
            double ringX = r.center.x * 8.0, ringY = -r.center.z * 8.0;                                         // the drawn ring's centre (a tower gun's pad)
            bool changed = b.world != w || gx != b.gx || gy != b.gy || range != b.range || direct != b.direct || ex != b.ex || guns != b.guns
                           || System.Math.Abs(ringX - b.ringX) > 1.0 || System.Math.Abs(ringY - b.ringY) > 1.0;
            long sig = 0;
            if (!changed)
            {
                sig = b.core.Signature(w);
                for (int i = 0; i < b.others.Count; i++) sig = unchecked(sig * 31 + b.others[i].Signature(w));
                changed = sig != b.sig;
            }
            if (changed)
            {
                b.world = w; b.gx = gx; b.gy = gy; b.range = range; b.direct = direct; b.ex = ex; b.guns = guns; b.ringX = ringX; b.ringY = ringY;
                b.core.Compute(w, gx, gy, range, direct, ex, ringX, ringY);
                while (b.others.Count > 0) { spareCores.Add(b.others[b.others.Count - 1]); b.others.RemoveAt(b.others.Count - 1); }
                for (int i = 1; i < r.units.Count && !placing; i++)
                {
                    var o = r.units[i]; if (o == null || o.Destroyed || o.Kind != ZKind.Cannon) continue;
                    OzBlindSectorsCore oc;
                    if (spareCores.Count > 0) { oc = spareCores[spareCores.Count - 1]; spareCores.RemoveAt(spareCores.Count - 1); } else oc = new OzBlindSectorsCore();
                    oc.Compute(w, o.CenterX, o.CenterY, o.AttackRadius, o.LosActive, w.Sight.ExemptOf(o), ringX, ringY);
                    b.others.Add(oc);
                }
                sig = b.core.Signature(w);
                for (int i = 0; i < b.others.Count; i++) sig = unchecked(sig * 31 + b.others[i].Signature(w));
                b.sig = sig;
                BuildBlind(b, r);
            }
            if (b.nTri == 0) return;
            Show(b, r, age, zs);
        }

        // ------------------------------------------------------------------ the mesh from the core (map px -> world m)
        void BuildBlind(Blind b, Ring r)
        {
            var core = b.core;
            int nf = core.Verts.Count, nd = core.Dots.Count;
            b.nFill = nf; b.nDots = nd; b.nV = nf + nd * DotV; b.nTri = core.Tris.Count + nd * DotT * 3;
            if (b.v == null || b.v.Length < b.nV)
            {
                int cap = Mathf.NextPowerOfTwo(Mathf.Max(256, b.nV));
                b.v = new Vector3[cap]; b.c = new Color32[cap]; b.baseA = new float[cap]; b.dC = new float[cap]; b.got = new bool[cap];
            }
            if (b.tri == null || b.tri.Length < b.nTri) b.tri = new int[Mathf.NextPowerOfTwo(Mathf.Max(768, b.nTri))];
            if (b.dotP == null || b.dotP.Length < nd) { int cap = Mathf.NextPowerOfTwo(Mathf.Max(64, nd)); b.dotP = new Vector3[cap]; b.dotDir = new Vector2[cap]; b.dotA = new float[cap]; }
            float y0 = game.GroundY((float)(b.gx / 8.0), (float)(-b.gy / 8.0)) + blindLift;
            // the ground lattice over the new mesh: the nodes already sampled under it are kept (a placed gun's tile step)
            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
            for (int i = 0; i < nf; i++) { var q = core.Verts[i]; if (q.x < bx0) bx0 = q.x; if (q.x > bx1) bx1 = q.x; if (q.y < by0) by0 = q.y; if (q.y > by1) by1 = q.y; }
            for (int d = 0; d < nd; d++) { var q = core.Dots[d]; if (q.x < bx0) bx0 = q.x; if (q.x > bx1) bx1 = q.x; if (q.y < by0) by0 = q.y; if (q.y > by1) by1 = q.y; }
            if (nf + nd > 0) b.ground.Frame(bx0 - OzGroundLattice.Step, by0 - OzGroundLattice.Step, bx1 + OzGroundLattice.Step, by1 + OzGroundLattice.Step);
            for (int i = 0; i < nf; i++)
            {
                var q = core.Verts[i];
                float a = q.a;
                for (int k = 0; k < b.others.Count && a > 0f; k++) if (!b.others[k].BlindPoint(q.x, q.y)) a = 0f;   // a shared ring: blind for every gun of it
                b.v[i] = new Vector3((float)(q.x / 8.0), y0, (float)(-q.y / 8.0));
                b.baseA[i] = a; b.dC[i] = (float)(q.dC / 8.0); b.got[i] = false;
            }
            for (int d = 0; d < nd; d++)
            {
                var q = core.Dots[d];
                float a = q.a;
                if (b.others.Count > 0) a = SharedDot(b, d, a);                                                  // K333b: a shared ring keeps a whole line from the gun (or a mark) where it is blind for every gun
                b.dotP[d] = new Vector3((float)(q.x / 8.0), y0, (float)(-q.y / 8.0));
                b.dotDir[d] = new Vector2((float)q.dx, (float)-q.dy);
                b.dotA[d] = a;
                int vb = nf + d * DotV;
                for (int k = 0; k < DotV; k++) { b.baseA[vb + k] = k == 0 ? a : 0f; b.dC[vb + k] = (float)(q.dC / 8.0); b.got[vb + k] = false; }
            }
            for (int i = 0; i < core.Tris.Count; i++) b.tri[i] = core.Tris[i];
            for (int d = 0, t = core.Tris.Count; d < nd; d++)
            {
                int vb = nf + d * DotV;
                for (int k = 0; k < 6; k++) { b.tri[t++] = vb; b.tri[t++] = vb + 1 + (k + 1) % 6; b.tri[t++] = vb + 1 + k; }
            }
            // the side lines: a pale tint with a little of the team colour (the ring's ground-disc colour)
            Team team = r.team;
            Color tc = team == Team.Red ? TeamColors.DiscRed : team == Team.Blue ? TeamColors.DiscBlue : TeamColors.DiscEmpty;
            float mx = Mathf.Max(1f, Mathf.Max(tc.r, Mathf.Max(tc.g, tc.b)));
            var pale = new Color(0.93f, 0.95f, 1f);
            var lc = Color.Lerp(pale, new Color(tc.r / mx, tc.g / mx, tc.b / mx), blindLineTeam);
            b.tint = new Color32((byte)(lc.r * 255f), (byte)(lc.g * 255f), (byte)(lc.b * 255f), 0);
            b.cursor = 0; b.missing = b.nV; b.lastA = b.lastR = b.lastNight = b.lastZs = b.lastLine = -1f; b.dirtyV = true;
            if (!b.mesh) { b.mesh = new Mesh { name = "OZ_RangeRing_Blind", indexFormat = IndexFormat.UInt32 }; b.mesh.MarkDynamic(); }
            b.mesh.Clear();
            PlaceDots(b, 1f);
            b.mesh.SetVertices(b.v, 0, b.nV);
            b.mesh.SetColors(b.c, 0, b.nV);
            b.mesh.SetTriangles(b.tri, 0, b.nTri, 0, false);
            b.mesh.bounds = new Bounds(new Vector3((float)(b.gx / 8.0), y0, (float)(-b.gy / 8.0)), new Vector3(b.range / 4f + 16f, 60f, b.range / 4f + 16f));
            BlindArcs(b, r);                                                                                 // K333b: the ring's blind arcs, the marks' tint (AttackRangeRings.BlindFromGun.cs)
        }

        /// <summary>The dots' rim at the zoom (the ring's dots grow with the camera distance; these do the same, much finer).</summary>
        void PlaceDots(Blind b, float zs)
        {
            float len = blindDotLength * 0.5f * zs, wid = blindDotWidth * 0.5f * zs;
            for (int d = 0; d < b.nDots; d++)
            {
                int vb = b.nFill + d * DotV;
                var p = b.dotP[d]; var t = new Vector3(b.dotDir[d].x, 0f, b.dotDir[d].y); var n = new Vector3(-t.z, 0f, t.x);
                b.v[vb] = p;
                float dl = len, dw = wid; DotSize(b, d, zs, ref dl, ref dw);                                     // K333b: a mark's arm has its own size
                for (int k = 0; k < 6; k++) b.v[vb + 1 + k] = p + t * (Hex[k].x * dl) + n * (Hex[k].y * dw);
            }
            b.lastZs = zs;
        }

        // ------------------------------------------------------------------ per frame: heights, fade, burst
        void Show(Blind b, Ring r, float age, float zs)
        {
            b.shownFrame = Time.frameCount;
            // ground under the vertices, from the lattice: every vertex whose 4 nodes are known gets its height at once, the missing
            // nodes are sampled within the budget (the first frames; after a placed gun's tile step only the strip it moved onto);
            // then a slow refresh of the nodes (a crater, a bridge's wobble)
            int nH = b.nFill + b.nDots;
            if (b.missing > 0 && nH > 0)
            {
                int budget = blindFirstSamples;
                for (int n = 0; n < nH && b.missing > 0; n++)
                {
                    int q = b.cursor;
                    bool dot = q >= b.nFill;
                    int vi = dot ? b.nFill + (q - b.nFill) * DotV : q;
                    if (!b.got[vi])
                    {
                        var p = dot ? b.dotP[q - b.nFill] : b.v[q];
                        if (!GroundAt(b, p.x, p.z, ref budget, out float g)) break;                    // out of samples this frame: on from here next frame
                        if (dot) SetDotY(b, q - b.nFill, g);
                        else SetFillY(b, q, g);
                    }
                    b.cursor = (q + 1) % nH;
                }
            }
            else if (nH > 0 && b.ground.Nodes > 0)
            {
                bool moved = false; int nodes = b.ground.Nodes;
                for (int n = 0, done = 0; n < nodes && done < blindRefreshSamples; n++)
                {
                    int k = b.refresh % nodes; b.refresh = k + 1;
                    if (!b.ground.NodeAt(k, out int ni, out int nj, out float old)) continue;
                    float y = game.GroundY(OzGroundLattice.WorldX(ni), OzGroundLattice.WorldZ(nj)); done++;
                    if (Mathf.Abs(y - old) > 0.002f) { b.ground.Set(ni, nj, y); moved = true; }
                }
                if (moved)
                    for (int q = 0; q < nH; q++)
                    {
                        bool dot = q >= b.nFill;
                        var p = dot ? b.dotP[q - b.nFill] : b.v[q];
                        if (!b.ground.At(p.x * 8.0, -p.z * 8.0, out float g, out _, out _)) continue;
                        if (dot) SetDotY(b, q - b.nFill, g); else SetFillY(b, q, g);
                    }
            }
            if (Mathf.Abs(zs - b.lastZs) > 0.02f * Mathf.Max(0.1f, b.lastZs)) { PlaceDots(b, zs); RestoreDotHeights(b); b.dirtyV = true; }
            // colours: the ring's alpha, the burst (the ring's radius), day / night
            float night = NightGrade.NightK;
            float dark = Mathf.Lerp(blindDarkDay, blindDarkNight, night) * r.a, line = Mathf.Lerp(blindLineDay, blindLineNight, night) * r.a;
            float curR = age >= expandTime ? float.MaxValue : r.curR;
            bool colours = b.dirtyV || b.newGot || Mathf.Abs(dark - b.lastA) > 0.0015f || Mathf.Abs(line - b.lastLine) > 0.0015f || curR != b.lastR || b.missing > 0
                           || Mathf.Abs(night - b.lastNight) > 0.002f;                                     // K333b: the veil's colour follows the night
            if (colours)
            {
                for (int i = 0; i < b.nV; i++)
                {
                    float k = b.got[i] ? b.baseA[i] : 0f;
                    if (k > 0f && curR < float.MaxValue) k *= Mathf.Clamp01((curR + 0.8f - b.dC[i]) / 0.8f);
                    b.c[i] = BlindColour(b, i, k, dark, line, night, r.a);                                  // K333b: the veil, a line's dash, a mark's arm (AttackRangeRings.BlindFromGun.cs)
                }
                b.lastA = dark; b.lastLine = line; b.lastR = curR; b.lastNight = night; b.newGot = false;
                if (b.dirtyV) { b.mesh.SetVertices(b.v, 0, b.nV); b.dirtyV = false; }
                b.mesh.SetColors(b.c, 0, b.nV);
            }
            var m = BlindFillMat(BlindMat());                                                             // K333c: the grey ground (OpenZ/BlindGrey) when the camera's opaque copy is made, else the plain overlay (AttackRangeRings.BlindClearer.cs)
            if (m) Graphics.DrawMesh(b.mesh, Matrix4x4.identity, m, 0, null, 0, null, ShadowCastingMode.Off, false);
            BlindShown++;
        }

        /// <summary>The ground at world (x, z) from the lattice, sampling its missing nodes (ZGame.GroundY) while the budget lasts;
        /// false when the budget ran out first.</summary>
        bool GroundAt(Blind b, float x, float z, ref int budget, out float g)
        {
            double px = x * 8.0, py = -z * 8.0;
            for (int guard = 0; guard < 5; guard++)
            {
                if (b.ground.At(px, py, out g, out int mi, out int mj)) return true;
                if (budget <= 0) return false;
                budget--;
                if (mi == int.MinValue) { g = game.GroundY(x, z); return true; }                             // outside the lattice (never for a framed mesh)
                b.ground.Set(mi, mj, game.GroundY(OzGroundLattice.WorldX(mi), OzGroundLattice.WorldZ(mj)));
            }
            g = game.GroundY(x, z);
            return true;
        }

        void SetFillY(Blind b, int i, float g)
        {
            float y = g + blindLift;
            if (!b.got[i]) { b.got[i] = true; b.missing--; b.newGot = true; }
            if (Mathf.Abs(y - b.v[i].y) > 0.002f) { b.v[i].y = y; b.dirtyV = true; }
        }

        void SetDotY(Blind b, int d, float g)
        {
            int vb = b.nFill + d * DotV;
            float y = g + blindLift + 0.02f;                                                                  // a hair over the darkening
            if (!b.got[vb]) { for (int k = 0; k < DotV; k++) b.got[vb + k] = true; b.missing -= DotV; b.newGot = true; }
            if (Mathf.Abs(y - b.dotP[d].y) > 0.002f || b.v[vb].y != y)
            {
                b.dotP[d].y = y;
                for (int k = 0; k < DotV; k++) b.v[vb + k].y = y;
                b.dirtyV = true;
            }
        }

        void RestoreDotHeights(Blind b)
        {
            for (int d = 0; d < b.nDots; d++) { int vb = b.nFill + d * DotV; float y = b.dotP[d].y; for (int k = 0; k < DotV; k++) b.v[vb + k].y = y; }
        }

        // ------------------------------------------------------------------ audit (Play mode, for the proof steps)
        /// <summary>For every gun on the map (the neutral ones with their table range, as the gun being placed): its sectors made as a
        /// ring makes them, every cell inside its range: the rules' verdict (OzBlindSectorsCore.Refused: range, EngageBarrierBetween,
        /// the line of sight for direct fire) against the drawn mesh at the cell centre. violations = the cells that differ (must be 0).</summary>
        public static string AuditBlind(out int violations)
        {
            violations = 0;
            var sb = new StringBuilder("gun blind sectors (AttackRangeRings.BlindSectors: OzBlindSectorsCore, drawn with the ring)\n");
            var z = I; var g = z ? (z.game ? z.game : (z.input && z.input.game ? z.input.game : ZGame.I)) : null;
            if (!g || g.World == null) { violations = 1; return sb.Append("FAIL no game in the scene\n").ToString(); }
            var w = g.World; var core = new OzBlindSectorsCore();
            int guns = 0, cells = 0, refused = 0, drawn = 0;
            foreach (var o in w.Objects)
            {
                if (o.Kind != ZKind.Cannon || o.Destroyed) continue;
                int range = o.AttackRadius > 0 ? o.AttackRadius : OzGunRange.RadiusPx(w, o.Type);
                if (range <= 0) continue;
                guns++;
                bool direct = o.AttackRadius > 0 ? o.LosActive : OzBlindSectorsCore.DirectFireType(o.Type);
                int ex = w.Sight.ExemptOf(o);
                var vp = g.Views.TryGetValue(o.Id, out var view) && view ? view.transform.position : g.CenterWorld(o, false);   // the ring's centre, as Middle
                double ringX = vp.x * 8.0, ringY = -vp.z * 8.0;
                core.Compute(w, o.CenterX, o.CenterY, range, direct, ex, ringX, ringY);
                int c = 0, rf = 0, dr = 0, bad = 0, rt = range / 16 + 4, ax = o.CenterX >> 4, ay = o.CenterY >> 4;
                for (int y = Mathf.Max(0, ay - rt); y <= Mathf.Min(w.Map.height - 1, ay + rt); y++)
                    for (int x = Mathf.Max(0, ax - rt); x <= Mathf.Min(w.Map.width - 1, ax + rt); x++)
                    {
                        int cx = x * 16 + 8, cy = y * 16 + 8;
                        double dx = o.CenterX - cx, dy = o.CenterY - cy, rx = ringX - cx, ry = ringY - cy;
                        bool rules = OzBlindSectorsCore.Refused(w, o.CenterX, o.CenterY, range, direct, ex, cx, cy);
                        bool mesh = core.MeshAlphaAt(cx, cy) >= 0.5;
                        if (System.Math.Sqrt(dx * dx + dy * dy) > range || System.Math.Sqrt(rx * rx + ry * ry) > range) { if (mesh && !rules) bad++; continue; }   // past the ring: drawn only where refused
                        c++;
                        if (rules) rf++; if (mesh) dr++; if (rules != mesh) bad++;
                    }
                cells += c; refused += rf; drawn += dr; violations += bad;
                double off = System.Math.Sqrt((ringX - o.CenterX) * (ringX - o.CenterX) + (ringY - o.CenterY) * (ringY - o.CenterY)) / 8.0;
                sb.Append(bad == 0 ? "OK   " : "FAIL ").Append(o.Type).Append(" (").Append(o.LocX >> 4).Append(',').Append(o.LocY >> 4).Append(") ").Append(o.Owner)
                  .Append(ex > 0 ? " on its fort's tower" : "").Append(off > 0.05 ? ", its ring " + off.ToString("0.0") + " m from its rules centre" : "").Append(": range ").Append(range).Append(" px, ").Append(direct ? "direct fire" : "fires over obstacles (barriers still stop it)")
                  .Append("; ").Append(c).Append(" cells, ").Append(rf).Append(" refused, ").Append(dr).Append(" drawn, differ ").Append(bad)
                  .Append("; ").Append(core.Runs.Count).Append(" sector(s), ").Append(core.LineEdges).Append(" side lines; blockers ").Append(core.BarrierTiles).Append(" barrier + ")
                  .Append(core.SightTiles).Append(" sight tiles\n");
            }
            sb.Append(violations == 0 ? "OK   " : "FAIL ").Append(guns).Append(" guns, ").Append(cells).Append(" cells, ").Append(refused).Append(" refused by the rules, ").Append(drawn)
              .Append(" drawn blind, ").Append(violations).Append(" differ; rings drawing sectors now: ").Append(z ? z.BlindShown : 0).Append('\n');
            return sb.ToString();
        }
    }
}
