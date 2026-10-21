using System.Collections.Generic;
using OpenZ.Sim;

namespace OpenZ
{
    /// <summary>
    /// The cells of the gun zone picture (OzGunZone). A cell is one gun: a 2 x 2 tile spot (4 x 4 m, the gun's own footprint) that
    /// the placement rule allows, never a generic square. The cells never overlap; together they lie on a 2-tile lattice whose
    /// phase is chosen per zone so that the most cells fit (a tie goes to the lattice that lines up with the building's walls;
    /// once shown, a zone keeps its phase while that phase holds nearly as many cells as the best one, so the grid never jumps
    /// by a tile because a gun was put down), and where an object or the zone edge leaves a gap in that lattice, an allowed spot
    /// one tile off fills it if it overlaps no cell. The "+" in a cell is where the gun's centre stands when it is put into that cell (the ghost snaps there). A joint
    /// ("+" where two or more cells meet at a corner) is drawn only where the rule also allows a gun centred on that corner, so
    /// every mark on the ground is a real place for a gun. The area (fill and outline) stays the union of all allowed spots, as
    /// before. Corner brackets sit on the outer corners of each piece of the area. The objects the rule tests are marked per tile
    /// round the zone (EncodeObjects): the zone's outline and brackets fade next to them.
    /// Plain C#, no engine types: the headless proof builds the same cells from the same rule.
    /// </summary>
    public sealed class GunCells
    {
        /// <summary>The zone in tiles (local tile (i, j) = map tile (zx + i, zy + j); i east, j south).</summary>
        public int zw, zh;
        /// <summary>spot[j * zw + i]: the rule allows a ground gun with its top-left tile at local (i, j).</summary>
        public bool[] spot;
        /// <summary>area[j * zw + i]: the tile lies under at least one allowed ground spot (GunCover.tile).</summary>
        public bool[] area;
        /// <summary>The top-left local tile index (j * zw + i) of each cell.</summary>
        public readonly List<int> cells = new List<int>();
        /// <summary>Local tile index -> the cell it belongs to, or -1.</summary>
        public int[] owner;
        /// <summary>Tile corners (cj * (zw + 1) + ci = the north-west corner of local tile (ci, cj)) that carry a joint "+".</summary>
        public readonly List<int> joints = new List<int>();
        /// <summary>Outer corners of the area: corner index (as joints) and kind (0 = north-west corner of the area, 1 = north-east,
        /// 2 = south-west, 3 = south-east).</summary>
        public readonly List<int> bracketCorner = new List<int>(), bracketKind = new List<int>();
        /// <summary>hole[j * zw + i]: a tile outside the area that the area closes in (an object inside the zone), as opposed to the
        /// open ground outside the zone; its frame is drawn like a cell border.</summary>
        public bool[] hole;
        public int phaseX, phaseY, lattice, shifted, loose;
        /// <summary>The phase last built (phaseX | phaseY &lt;&lt; 1), for <see cref="Build"/>'s keepPhase.</summary>
        public int Phase => (phaseX & 1) | (phaseY & 1) << 1;
        /// <summary>A kept phase stays while it holds at least the best phase's lattice cells minus this share of them (at least
        /// <see cref="KeepMinCells"/>).</summary>
        public const float KeepShare = 0.1f;
        public const int KeepMinCells = 4;
        /// <summary>Corner brackets need a piece of area at least this many tiles wide and high.</summary>
        public const int BracketMinTiles = 3;
        /// <summary>A corner bracket's L (arms of about 2.3 m along the two edges, the outline under them cut) reaches into the second
        /// tile along each edge: a bracket stands only on a corner whose edges run straight for this many tiles both ways, never
        /// where an arm would run on past a step of the edge (a stub into the area, the step's outline cut away).</summary>
        public const int BracketArmTiles = 2;
        /// <summary>When the corner nearest a corner of the piece's bounding box is not straight, the nearest straight one stands in if
        /// it lies at most this many tiles farther off; else that corner has no bracket.</summary>
        public const double BracketSlackTiles = 1.0;

        public bool Spot(int i, int j) => i >= 0 && j >= 0 && i < zw && j < zh && spot[j * zw + i];
        public bool Area(int i, int j) => i >= 0 && j >= 0 && i < zw && j < zh && area[j * zw + i];
        public bool Hole(int i, int j) => i >= 0 && j >= 0 && i < zw && j < zh && hole[j * zw + i];
        public int Owner(int i, int j) => i >= 0 && j >= 0 && i < zw && j < zh ? owner[j * zw + i] : -1;

        /// <summary>prefX / prefY = the lattice phase that lines up with the building (its top-left local tile, modulo 2);
        /// keepPhase = the phase this zone was shown with before (-1 = none): kept while it holds nearly as many cells as the best.</summary>
        public void Build(int w, int h, bool[] spots, bool[] areaTiles, int prefX, int prefY, bool fillGaps = true, int keepPhase = -1)
        {
            zw = w; zh = h; spot = spots; area = areaTiles;
            int n = zw * zh;
            if (owner == null || owner.Length != n) owner = new int[n];
            if (hole == null || hole.Length != n) hole = new bool[n];
            for (int k = 0; k < n; k++) owner[k] = -1;
            cells.Clear(); joints.Clear(); bracketCorner.Clear(); bracketKind.Clear();
            lattice = shifted = loose = 0;

            // the lattice phase with the most cells (on a tie the building's own phase, then the first in order)
            int best = -1, pref = (prefX & 1) | (prefY & 1) << 1; phaseX = prefX & 1; phaseY = prefY & 1;
            int keepCount = -1;
            for (int t = 0; t < 5; t++)
            {
                int ph = t == 0 ? pref : t - 1;
                if (t > 0 && ph == pref) continue;
                int px = ph & 1, py = ph >> 1, c = 0;
                for (int j = py; j < zh; j += 2) for (int i = px; i < zw; i += 2) if (spot[j * zw + i]) c++;
                if (ph == keepPhase) keepCount = c;
                if (c > best) { best = c; phaseX = px; phaseY = py; }
            }
            if (keepPhase >= 0 && keepPhase < 4 && keepCount >= best - System.Math.Max(KeepMinCells, (int)(best * KeepShare)))
            { phaseX = keepPhase & 1; phaseY = keepPhase >> 1; }
            for (int j = phaseY; j < zh; j += 2)
                for (int i = phaseX; i < zw; i += 2)
                    if (spot[j * zw + i] && Free(i, j)) { Take(i, j); lattice++; }
            if (fillGaps)
                for (int j = 0; j < zh; j++)
                    for (int i = 0; i < zw; i++)
                        if (spot[j * zw + i] && Free(i, j)) { Take(i, j); shifted++; }
            for (int k = 0; k < n; k++) if (area[k] && owner[k] < 0) loose++;

            // joints: corners shared by two or more cells where a gun centred on the corner is allowed too
            var count = new Dictionary<int, int>();
            foreach (int c in cells)
            {
                int i = c % zw, j = c / zw;
                Bump(count, j * (zw + 1) + i); Bump(count, j * (zw + 1) + i + 2); Bump(count, (j + 2) * (zw + 1) + i); Bump(count, (j + 2) * (zw + 1) + i + 2);
            }
            foreach (var kv in count)
            {
                if (kv.Value < 2) continue;
                int ci = kv.Key % (zw + 1), cj = kv.Key / (zw + 1);
                if (Spot(ci - 1, cj - 1)) joints.Add(kv.Key);
            }
            joints.Sort();

            // holes: tiles off the area that the open ground outside the zone cannot reach (4-neighbour flood from the zone's rim)
            for (int k = 0; k < n; k++) hole[k] = false;
            var open = new bool[n]; var stack = new Stack<int>();
            for (int i = 0; i < zw; i++) { Seed(open, stack, i, 0); Seed(open, stack, i, zh - 1); }
            for (int j = 0; j < zh; j++) { Seed(open, stack, 0, j); Seed(open, stack, zw - 1, j); }
            while (stack.Count > 0)
            {
                int k = stack.Pop(), i = k % zw, j = k / zw;
                Seed(open, stack, i + 1, j); Seed(open, stack, i - 1, j); Seed(open, stack, i, j + 1); Seed(open, stack, i, j - 1);
            }
            for (int k = 0; k < n; k++) hole[k] = !area[k] && !open[k];

            // corner brackets: per piece of area (4-neighbour), at the convex corner nearest each corner of the piece's bounding box
            // whose two edges run straight under the bracket's arms (ArmsStraight)
            var piece = new int[n]; for (int k = 0; k < n; k++) piece[k] = -1;
            int pieces = 0;
            for (int s = 0; s < n; s++)
            {
                if (!area[s] || piece[s] >= 0) continue;
                int x0 = zw, y0 = zh, x1 = -1, y1 = -1;
                stack.Push(s); piece[s] = pieces;
                var tiles = new List<int>();
                while (stack.Count > 0)
                {
                    int k = stack.Pop(), i = k % zw, j = k / zw; tiles.Add(k);
                    if (i < x0) x0 = i; if (i > x1) x1 = i; if (j < y0) y0 = j; if (j > y1) y1 = j;
                    Grow(piece, stack, pieces, i + 1, j); Grow(piece, stack, pieces, i - 1, j); Grow(piece, stack, pieces, i, j + 1); Grow(piece, stack, pieces, i, j - 1);
                }
                if (x1 - x0 + 1 >= BracketMinTiles && y1 - y0 + 1 >= BracketMinTiles)
                    for (int kind = 0; kind < 4; kind++)
                    {
                        // kind 0: the piece's tile lies south-east of the corner (the north-west corner of the area), 1: south-west, 2: north-east, 3: north-west
                        int bx = (kind & 1) == 0 ? x0 : x1 + 1, by = (kind & 2) == 0 ? y0 : y1 + 1;
                        int bestK = -1; long bestD = long.MaxValue, anyD = long.MaxValue;
                        foreach (int k in tiles)
                        {
                            int i = k % zw, j = k / zw;
                            int ci = (kind & 1) == 0 ? i : i + 1, cj = (kind & 2) == 0 ? j : j + 1;
                            int ox = (kind & 1) == 0 ? -1 : 1, oy = (kind & 2) == 0 ? -1 : 1;   // the three tiles beyond the corner
                            if (InPiece(piece, pieces, i + ox, j) || InPiece(piece, pieces, i, j + oy) || InPiece(piece, pieces, i + ox, j + oy)) continue;
                            long d = (long)(ci - bx) * (ci - bx) + (long)(cj - by) * (cj - by);
                            if (d < anyD) anyD = d;
                            if (d < bestD && ArmsStraight(piece, pieces, i, j, ox, oy)) { bestD = d; bestK = cj * (zw + 1) + ci; }   // the L's arms on straight edges only
                        }
                        // a straight corner at most BracketSlackTiles farther off stands in for a nearest corner whose edge steps under an arm
                        if (bestK >= 0 && System.Math.Sqrt(bestD) <= System.Math.Sqrt(anyD) + BracketSlackTiles) { bracketCorner.Add(bestK); bracketKind.Add(kind); }
                    }
                pieces++;
            }
        }

        bool Free(int i, int j) => owner[j * zw + i] < 0 && owner[j * zw + i + 1] < 0 && owner[(j + 1) * zw + i] < 0 && owner[(j + 1) * zw + i + 1] < 0;

        void Take(int i, int j)
        {
            int c = cells.Count; cells.Add(j * zw + i);
            owner[j * zw + i] = owner[j * zw + i + 1] = owner[(j + 1) * zw + i] = owner[(j + 1) * zw + i + 1] = c;
        }

        static void Bump(Dictionary<int, int> d, int k) { d.TryGetValue(k, out int v); d[k] = v + 1; }

        void Seed(bool[] open, Stack<int> st, int i, int j)
        {
            if (i < 0 || j < 0 || i >= zw || j >= zh) return;
            int k = j * zw + i;
            if (open[k] || area[k]) return;
            open[k] = true; st.Push(k);
        }

        void Grow(int[] piece, Stack<int> st, int p, int i, int j)
        {
            if (i < 0 || j < 0 || i >= zw || j >= zh) return;
            int k = j * zw + i;
            if (!area[k] || piece[k] >= 0) return;
            piece[k] = p; st.Push(k);
        }

        bool InPiece(int[] piece, int p, int i, int j) => i >= 0 && j >= 0 && i < zw && j < zh && piece[j * zw + i] == p;

        /// <summary>From the corner tile (i, j) of piece p (the corner on its (ox, oy) side): both edges at that corner run straight for
        /// <see cref="BracketArmTiles"/> tiles (the tiles along each arm in the piece, the tiles beyond them off the area).</summary>
        bool ArmsStraight(int[] piece, int p, int i, int j, int ox, int oy)
        {
            for (int t = 0; t < BracketArmTiles; t++)
            {
                if (!InPiece(piece, p, i - ox * t, j) || Area(i - ox * t, j + oy)) return false;   // along x: the edge on the oy side
                if (!InPiece(piece, p, i, j - oy * t) || Area(i + ox, j - oy * t)) return false;   // along y: the edge on the ox side
            }
            return true;
        }

        /// <summary>The shader's data (Shaders/OzGunZoneTiles.shader), after Build. cellTexels: 4 bytes per zone tile, rows from the
        /// north: r = 1 area, 2 in a cell, 4 east half of its cell, 8 south half, 16 hole, 32 ground beyond the area that may carry the
        /// outline; g, b, a = the eight neighbours (1 N, 2 S, 4 W, 8 E, 16 NW, 32 NE, 64 SW, 128 SE) in the area, holes, outline
        /// ground. cornerTexels: 4 bytes per tile corner, r = 1 a joint. bracketData: 4 floats per bracket: the corner (zone metres,
        /// x east, y south), its kind (+ 4 when the area's tile at the corner is that same corner of a cell: the shader then draws
        /// a second, inner L on that cell's border), the outline's offset there (outside the area where the ground beyond carries
        /// it, else inside).
        /// outlineGround(i, j) = local tile (i, j) (also outside the zone) is plain ground or road.</summary>
        public byte[] cellTexels = new byte[0], cornerTexels = new byte[0];
        public float[] bracketData = new float[0];

        public void Encode(System.Func<int, int, bool> outlineGround, float outlineOffset, int maxBrackets)
        {
            int n = zw * zh;
            if (cellTexels.Length != n * 4) cellTexels = new byte[n * 4];
            if (cornerTexels.Length != (zw + 1) * (zh + 1) * 4) cornerTexels = new byte[(zw + 1) * (zh + 1) * 4];
            System.Array.Clear(cornerTexels, 0, cornerTexels.Length);
            bool Draw(int i, int j) => !Area(i, j) && outlineGround(i, j);
            int[] dx = { 0, 0, -1, 1, -1, 1, -1, 1 }, dy = { -1, 1, 0, 0, -1, -1, 1, 1 };
            for (int j = 0; j < zh; j++)
                for (int i = 0; i < zw; i++)
                {
                    int k = j * zw + i, own = owner[k];
                    int r = 0, g = 0, b = 0, a = 0;
                    if (area[k]) r |= 1;
                    if (own >= 0)
                    {
                        r |= 2;
                        int c0 = cells[own];
                        if (i > c0 % zw) r |= 4;
                        if (j > c0 / zw) r |= 8;
                    }
                    if (hole[k]) r |= 16;
                    if (Draw(i, j)) r |= 32;
                    for (int q = 0; q < 8; q++)
                    {
                        int ii = i + dx[q], jj = j + dy[q], bit = 1 << q;
                        if (Area(ii, jj)) g |= bit;
                        if (Hole(ii, jj)) b |= bit;
                        if (Draw(ii, jj)) a |= bit;
                    }
                    cellTexels[k * 4] = (byte)r; cellTexels[k * 4 + 1] = (byte)g; cellTexels[k * 4 + 2] = (byte)b; cellTexels[k * 4 + 3] = (byte)a;
                }
            foreach (int k in joints) cornerTexels[k * 4] = 1;
            int nb = System.Math.Min(bracketCorner.Count, maxBrackets);
            if (bracketData.Length != nb * 4) bracketData = new float[nb * 4];
            for (int q = 0; q < nb; q++)
            {
                int k = bracketCorner[q], kind = bracketKind[q], ci = k % (zw + 1), cj = k / (zw + 1);
                int oi = (kind & 1) == 0 ? ci - 1 : ci, oj = (kind & 2) == 0 ? cj - 1 : cj;   // the tile beyond the corner, outside the area
                int ti = (kind & 1) == 0 ? ci : ci - 1, tj = (kind & 2) == 0 ? cj : cj - 1;   // the area's own tile at the corner
                int own = Owner(ti, tj), inner = 0;
                if (own >= 0)
                {
                    int c0 = cells[own], c0i = c0 % zw, c0j = c0 / zw;
                    if (ti == ((kind & 1) == 0 ? c0i : c0i + 1) && tj == ((kind & 2) == 0 ? c0j : c0j + 1)) inner = 4;
                }
                bracketData[q * 4] = ci * 2f; bracketData[q * 4 + 1] = cj * 2f; bracketData[q * 4 + 2] = kind | inner;
                bracketData[q * 4 + 3] = Draw(oi, oj) ? -outlineOffset : outlineOffset;
            }
        }

        /// <summary>The objects on and round the zone's tiles, for the shader (_Objects: the zone's outline and corner brackets fade
        /// next to them). objectTexels: 4 bytes per zone tile, rows from the north: r = 1 an object stands on the tile, g = the same
        /// for its eight neighbours (1 N, 2 S, 4 W, 8 E, 16 NW, 32 NE, 64 SW, 128 SE; also outside the zone), b = a = 0. The objects
        /// are the ones the placement rule tests (ZWorld.CannonPlacable: every object but robots and vehicles, so trees, rocks,
        /// props, buildings and guns); a tile is an object's when the object's rectangle overlaps it. zx, zy = the zone's
        /// north-west tile on the map. objectTiles = the object tiles in the zone and in the ring of tiles round it.</summary>
        public byte[] objectTexels = new byte[0];
        public int objectTiles;
        bool[] objectGrid = new bool[0];
        static readonly int[] NbX = { 0, 0, -1, 1, -1, 1, -1, 1 }, NbY = { -1, 1, 0, 0, -1, -1, 1, 1 };

        public void EncodeObjects(IList<ZObject> objects, int zx, int zy)
        {
            int gw = zw + 2, gh = zh + 2;
            if (objectGrid.Length != gw * gh) objectGrid = new bool[gw * gh]; else System.Array.Clear(objectGrid, 0, objectGrid.Length);
            for (int q = 0; q < objects.Count; q++)
            {
                var o = objects[q];
                if (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) continue;                 // as CannonPlacable: they never block a spot
                int x0 = System.Math.Max(0, (o.LocX >> 4) - zx + 1), x1 = System.Math.Min(gw, ((o.LocX + o.WidthPix + 15) >> 4) - zx + 1);
                int y0 = System.Math.Max(0, (o.LocY >> 4) - zy + 1), y1 = System.Math.Min(gh, ((o.LocY + o.HeightPix + 15) >> 4) - zy + 1);
                for (int j = y0; j < y1; j++)
                    for (int i = x0; i < x1; i++) objectGrid[j * gw + i] = true;
            }
            objectTiles = 0;
            for (int k = 0; k < objectGrid.Length; k++) if (objectGrid[k]) objectTiles++;
            int n = zw * zh;
            if (objectTexels.Length != n * 4) objectTexels = new byte[n * 4];
            for (int j = 0; j < zh; j++)
                for (int i = 0; i < zw; i++)
                {
                    int nb = 0;
                    for (int q = 0; q < 8; q++) if (objectGrid[(j + 1 + NbY[q]) * gw + i + 1 + NbX[q]]) nb |= 1 << q;
                    int k = (j * zw + i) * 4;
                    objectTexels[k] = (byte)(objectGrid[(j + 1) * gw + i + 1] ? 1 : 0); objectTexels[k + 1] = (byte)nb; objectTexels[k + 2] = 0; objectTexels[k + 3] = 0;
                }
        }

        /// <summary>Counted proof of objectTexels: every bit (each zone tile's own and its eight neighbours') against the objects the
        /// placement rule tests, asked as the rule asks them (does the object's rectangle overlap the tile, in map pixels). Returns
        /// the wrong bits (must be 0).</summary>
        public int VerifyObjects(IList<ZObject> objects, int zx, int zy)
        {
            if (objectTexels.Length != zw * zh * 4) return zw * zh * 9;
            int bad = 0;
            for (int j = 0; j < zh; j++)
                for (int i = 0; i < zw; i++)
                {
                    int k = (j * zw + i) * 4;
                    if ((objectTexels[k] != 0) != ObjectOn(objects, zx + i, zy + j)) bad++;
                    for (int q = 0; q < 8; q++) if ((((objectTexels[k + 1] >> q) & 1) != 0) != ObjectOn(objects, zx + i + NbX[q], zy + j + NbY[q])) bad++;
                }
            return bad;
        }

        static bool ObjectOn(IList<ZObject> objects, int tx, int ty)
        {
            int l = tx * 16, r = l + 16, t = ty * 16, btm = t + 16;
            for (int q = 0; q < objects.Count; q++)
            {
                var o = objects[q];
                if (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) continue;
                if (!(l >= o.LocX + o.WidthPix || r <= o.LocX || t >= o.LocY + o.HeightPix || btm <= o.LocY)) return true;
            }
            return false;
        }

        /// <summary>Counted proof: every cell is an allowed spot, no two cells share a tile, every cell tile is an area tile, every
        /// joint is an allowed gun centre. Returns the number of violations (must be 0).</summary>
        public int Verify()
        {
            int bad = 0; var seen = new HashSet<int>();
            foreach (int c in cells)
            {
                int i = c % zw, j = c / zw;
                if (!Spot(i, j)) bad++;
                for (int q = 0; q < 4; q++)
                {
                    int k = (j + (q >> 1)) * zw + i + (q & 1);
                    if (!seen.Add(k)) bad++;
                    if (!area[k]) bad++;
                }
            }
            foreach (int k in joints) { int ci = k % (zw + 1), cj = k / (zw + 1); if (!Spot(ci - 1, cj - 1)) bad++; }
            return bad;
        }
    }
}
