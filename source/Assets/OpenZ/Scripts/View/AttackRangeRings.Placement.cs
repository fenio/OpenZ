using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Version 1.1: a gun's range ring IS the units' range ring, nothing of its own. A selected gun goes through Rebuild like every
    /// unit: the same ring (bursts out of the gun, holds, fades), the same feathered dots, spacing, size, lift, crawl, material and
    /// team colour; only the radius is the gun's own range. The gun being placed gets the same ring too, made by the same Begin,
    /// Timing and Draw (Alloc, Build, Mat): OzGunRange says which gun and where, every frame of the placement; the ring bursts out
    /// of the spot as a unit's ring bursts out of its unit, follows the spot, holds while the placement lasts (a unit's ring holds
    /// for holdTime) and fades as a unit's ring fades when its unit leaves the selection (dropFade). Under a spot that takes no
    /// gun its dots are dimmed (OzGunRange.invalidDim), never hidden. The placement ring and the selection's rings never hide each
    /// other's dots (the placement ring has its own type key).
    /// </summary>
    public partial class AttackRangeRings
    {
        const string PlaceKey = "place:", AuditKey = "audit:";
        Ring place;
        bool placeOn;
        float placeDim = 1f;

        /// <summary>The gun being placed shows its ring now (for the checks).</summary>
        public bool PlacementShown => place != null && place.live && place.a > 0f;

        /// <summary>Every frame of a placement (OzGunRange.LateUpdate): on, the player's team, the gun type, its table range (px),
        /// the spot's centre and the dots' alpha factor (1, or dimmed under a spot that takes no gun). on = false: no placement
        /// (a running ring fades as a unit's ring does when its unit leaves the selection).</summary>
        public void Placement(bool on, Team team, string type, int radiusPx, Vector3 spot, float dim)
        {
            if (!game) game = input && input.game ? input.game : ZGame.I;
            float now = Time.unscaledTime;
            if (on && radiusPx > 0 && game)
            {
                string key = PlaceKey + type;
                if (place == null || !placeOn || place.dropAt >= 0f || place.radiusPx != radiusPx || place.team != team || place.type != key)
                {
                    if (place == null) place = pool.Count > 0 ? pool.Pop() : new Ring();
                    Begin(place, key, team, radiusPx, now);                                                     // a new ring, as for a new selection
                }
                place.center = new Vector3(spot.x, 0f, spot.z);
                placeOn = true; placeDim = Mathf.Clamp01(dim);
            }
            else if (placeOn) { placeOn = false; if (place != null && place.dropAt < 0f) place.dropAt = now; }
            if (place == null) return;
            if (!game || (place.dropAt >= 0f && now - place.dropAt >= dropFade)) { Recycle(place); place = null; placeOn = false; return; }
            float age = Mathf.Min(now - place.t0, expandTime + holdTime);                                       // the units' timing; the hold lasts while placing
            Timing(place, age, now);
            place.a *= placeDim;
            Draw(place, now, age, Camera.main);
        }

        void OnDisable()
        {
            if (place != null) { Recycle(place); place = null; }                                                 // its mesh goes with the pool (OnDestroy)
            placeOn = false;
        }

        // ------------------------------------------------------------------ audit (Play mode, for the proof steps)
        /// <summary>For every gun on the map: its ring made exactly as a selected gun's ring is made (Begin, Timing, Build at full
        /// size, at the camera's zoom), beside a unit's ring (a grunt's range) at the same centre; 360 points (for each degree the
        /// dot that the crawl brings there, its 13 vertices). Counted: the radius = (range + 3) px / 8, the dots' number from the
        /// same spacing, the same material instance, the same dot shape (x, z offsets), the same lift; no visible vertex under the
        /// walk ground the dots stand on (GroundY) on open ground or road. Reported, not counted: the same next to rock or water and
        /// off the map (where a unit's ring does the same), and the height over the drawn ground (TrackSurface lattice, exact on the
        /// 1 m grid) with where it is under it (rock / bank / off the map / open ground). violations = the lines that must be 0.</summary>
        public static string AuditGuns(out int violations)
        {
            violations = 0;
            var sb = new StringBuilder("gun range rings = the units' range ring (AttackRangeRings: Begin, Timing, Build, Mat, Draw)\n");
            var z = I; var g = z ? (z.game ? z.game : (z.input && z.input.game ? z.input.game : ZGame.I)) : null;
            if (!z || !g || g.World == null) { violations = 1; return sb.Append("FAIL no game / no AttackRangeRings in the scene\n").ToString(); }
            z.game = g;
            bool placer = OzGunRange.I && OzGunRange.I.isActiveAndEnabled;
            if (!placer) violations++;
            sb.Append(placer ? "OK   " : "FAIL ").Append("OzGunRange is in the scene: the gun being placed gets this same ring (Placement)\n");
            var unitDef = g.World.S != null ? g.World.S.Unit("grunt") : null;
            int unitPx = unitDef != null && unitDef.attack_radius > 0 ? unitDef.attack_radius : 120;
            var cam = Camera.main;
            var surf = TrackMarks.I ? TrackMarks.I.Surface : null;
            var gun = new Ring(); var unit = new Ring();
            int guns = 0;
            foreach (var o in g.World.Objects)
            {
                if (o.Kind != ZKind.Cannon || o.Destroyed || o.AttackRadius <= 0) continue;
                guns++;
                var c = g.Views.TryGetValue(o.Id, out var view) && view ? view.transform.position : g.CenterWorld(o, false); c.y = 0f;   // as Middle
                float age = z.expandTime + 0.5f * z.holdTime;                                                       // in the hold: full radius, full alpha
                z.Begin(gun, AuditKey + "gun", o.Owner, o.AttackRadius, 0f); gun.center = c; z.Timing(gun, age, age);
                z.Begin(unit, AuditKey + "unit", o.Owner, unitPx, 0f); unit.center = c; z.Timing(unit, age, age);
                float camDist = cam ? Vector3.Distance(cam.transform.position, c) : 40f;
                float size = Mathf.Clamp(camDist / 40f, z.zoomScale.x, z.zoomScale.y);                             // as Draw, after the landing swell
                float up = z.lift + 0.35f * (z.dotWidth * 0.5f * size);
                int want = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * gun.radius / Mathf.Max(0.2f, z.dotSpacing) / 4f) * 4, z.minDots, z.maxDots);
                bool radiusOk = Mathf.Abs(gun.radius - (o.AttackRadius + 3) / 8f) < 1e-5f, dotsOk = gun.n == want;
                bool matOk = z.Mat(gun.team) && ReferenceEquals(z.Mat(gun.team), z.Mat(unit.team));
                int shape = 0, liftOff = 0, underWalk = 0, underWalkEdge = 0, points = 0, rockU = 0, bankU = 0, offU = 0, openU = 0, drawnPts = 0;
                float minWalk = float.PositiveInfinity, maxWalk = float.NegativeInfinity, minDrawn = float.PositiveInfinity, maxDrawn = float.NegativeInfinity;
                float unitMaxWalk = float.NegativeInfinity, unitMinWalk = float.PositiveInfinity;
                for (int deg = 0; deg < 360; deg++)
                {
                    float t = deg / Mathf.Max(1e-3f, z.crawlDegPerSec);                                                // the crawl brings dot 0 to this degree
                    z.Build(gun, t, size); z.Build(unit, t, size);
                    GridH(gun.h, deg * Mathf.Deg2Rad, out _, out float hc);
                    if (Mathf.Abs(gun.v[0].y - hc - up) > 1e-4f) liftOff++;                                            // the units' lift, never more
                    for (int q = 1; q < V; q++)
                    {
                        Vector3 a = gun.v[q] - gun.v[0], b = unit.v[q] - unit.v[0];
                        if (Mathf.Abs(a.x - b.x) > 1e-4f || Mathf.Abs(a.z - b.z) > 1e-4f) shape++;                      // the same dot, only the radius differs
                    }
                    for (int q = 0; q < 7; q++)                                                                         // the visible part: centre + core (the rim's alpha is 0)
                    {
                        var p = gun.v[q]; points++;
                        float dw = p.y - g.GroundY(p.x, p.z);
                        minWalk = Mathf.Min(minWalk, dw); maxWalk = Mathf.Max(maxWalk, dw);
                        if (dw < 0f) { if (Edge(g, p)) underWalkEdge++; else underWalk++; }
                        var u = unit.v[q]; float du = u.y - g.GroundY(u.x, u.z);
                        unitMinWalk = Mathf.Min(unitMinWalk, du); unitMaxWalk = Mathf.Max(unitMaxWalk, du);
                        if (surf != null && surf.RenderY(p.x, p.z, out float ry))
                        {
                            float dd = p.y - ry; drawnPts++;
                            minDrawn = Mathf.Min(minDrawn, dd); maxDrawn = Mathf.Max(maxDrawn, dd);
                            if (dd < 0f)
                            {
                                if (!OnMap(g, p)) offU++;
                                else if (Near(g, p, ZClass.Impassable, ZClass.Impassable)) rockU++;
                                else if (Near(g, p, ZClass.Water, ZClass.DeepWater)) bankU++;
                                else openU++;
                            }
                        }
                    }
                }
                int bad = (radiusOk ? 0 : 1) + (dotsOk ? 0 : 1) + (matOk ? 0 : 1) + (shape > 0 ? 1 : 0) + (liftOff > 0 ? 1 : 0) + (underWalk > 0 ? 1 : 0);
                violations += bad;
                sb.Append(bad == 0 ? "OK   " : "FAIL ").Append(o.Type).Append(" #").Append(o.Id).Append(" (").Append(o.Owner).Append("): ring ").Append(gun.radius.ToString("0.000"))
                  .Append(" m = (").Append(o.AttackRadius).Append(" + 3) px / 8 ").Append(radiusOk ? "yes" : "NO").Append("; ").Append(gun.n).Append(" dots (the units' spacing: ")
                  .Append(want).Append(") ").Append(dotsOk ? "yes" : "NO").Append("; material ").Append(matOk ? z.Mat(gun.team).name + " = a unit's" : "DIFFERENT")
                  .Append("; dot shape differs from a unit's at ").Append(shape).Append(" vertices; lift ").Append((up * 100f).ToString("0.0")).Append(" cm (the units'), off at ").Append(liftOff)
                  .Append("; 360 points (").Append(points).Append(" visible vertices): over the walk ground ").Append((minWalk * 100f).ToString("+0.0;-0.0")).Append(" .. ")
                  .Append((maxWalk * 100f).ToString("+0.0;-0.0")).Append(" cm, under it on open ground ").Append(underWalk).Append(", next to rock / water / off the map ").Append(underWalkEdge).Append(" (a unit's ring here: ")
                  .Append((unitMinWalk * 100f).ToString("+0.0;-0.0")).Append(" .. ").Append((unitMaxWalk * 100f).ToString("+0.0;-0.0")).Append(" cm)");
                if (drawnPts > 0)
                    sb.Append("; over the drawn ground (lattice) ").Append((minDrawn * 100f).ToString("+0.0;-0.0")).Append(" .. ").Append((maxDrawn * 100f).ToString("+0.0;-0.0"))
                      .Append(" cm, under it ").Append(rockU + bankU + offU + openU).Append(" (rock ").Append(rockU).Append(", bank ").Append(bankU).Append(", off the map ").Append(offU).Append(", open ground ").Append(openU).Append(")");
                sb.Append('\n');
            }
            if (gun.mesh) Destroy(gun.mesh);
            if (unit.mesh) Destroy(unit.mesh);
            sb.Append("guns measured: ").Append(guns).Append("; the drawn ground read from ").Append(surf != null && surf.RenderHeights ? "the TER meshes' lattice" : "nowhere (no TrackSurface heights)").Append('\n');
            return sb.ToString();
        }

        /// <summary>p lies over a tile of the map.</summary>
        static bool OnMap(ZGame g, Vector3 p)
        {
            var px = ZGame.WorldToPx(p);
            int tx = px.x >> 4, ty = px.y >> 4;
            return px.x >= 0 && px.y >= 0 && tx < g.World.Map.width && ty < g.World.Map.height;
        }

        /// <summary>p is off the map, or its tile or one of the 8 round it is rock or water (there the walk ground and the drawn
        /// ground part: cliff faces, banks).</summary>
        static bool Edge(ZGame g, Vector3 p) => !OnMap(g, p) || Near(g, p, ZClass.Impassable, ZClass.Impassable) || Near(g, p, ZClass.Water, ZClass.DeepWater);

        /// <summary>The tile under p or one of its 8 neighbours is of class a or b.</summary>
        static bool Near(ZGame g, Vector3 p, ZClass a, ZClass b)
        {
            var px = ZGame.WorldToPx(p);
            int tx = px.x >> 4, ty = px.y >> 4;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = tx + dx, y = ty + dy;
                    if (x < 0 || y < 0 || x >= g.World.Map.width || y >= g.World.Map.height) continue;
                    var c = g.World.Map.ClassAt(x, y);
                    if (c == a || c == b) return true;
                }
            return false;
        }
    }
}
