using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// Live minimap from the Zod tile grid (zmini_map.cpp) in the kit's tactical-map frame (guide 9.5, Minimap/mm_frame:
    /// header band, amber tab, compass ticks, RED / BLUE zone legend, recessed 300 x 300 well with the screen overlay).
    /// Terrain classes are coloured like Z's own level palettes, every zone tinted with its owner's colour (Zod: team colour x 0.4)
    /// with a border line. On top, crisp kit markers: buildings (owner colour), flags,
    /// every robot / vehicle / cannon (team colour, INK ring on selected own units), the camera frustum (Zod's yellow outline,
    /// now a 1.5 u amber polyline), rally routes of your factories and pings (under attack, new unit, orders).
    /// Left click / drag = fly the camera there; right click = order the selected units to that point with no target
    /// (zplayer.cpp:2752-2780: attack-move unless near hostiles, Ctrl / Alt / Shift / Z like the map), or set rally points
    /// when a production window is open and nothing is selected. With the rally tool armed, a left click sets the rally point.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class OpenZMinimap : MonoBehaviour
    {
        [Tooltip("Largest map image edge in HUD canvas units (the kit well is 300 x 300)")] public float maxSide = 300f;
        [Tooltip("Object layer refreshes per second")] public float refreshHz = 10f;
        [Tooltip("Zone tint strength (Zod fills zones with team colour x 0.4)")] [Range(0, 1)] public float zoneTint = 0.3f;

        public Vector2 PanelSize { get; private set; }
        public RectTransform MapRect { get; private set; }
        public RectTransform Panel => panel;
        /// <summary>Left drag is flying the camera (tooltips stay away, guide 9.11).</summary>
        public bool Dragging => dragFly;
        public bool PointerOverMap => hud != null && MapRect && OpenZHud.Contains(MapRect, hud.Pointer) && !hud.Modal;

        OpenZHud hud; ZGame game; ZWorld W;
        RectTransform panel, markers; RawImage img; Texture2D tex;
        Color32[] terrain, baseBuf, prevBase, buf;
        int k, tw, th, mw, mh;
        int k0 = 4, ek = 1;                                                                       // ui_quality_v1: the look's reference density, edge width in texels
        int[] tileZone; Team[] shownOwners;
        /// <summary>NB41 view rule (DV-view, parity report): enemy unit dots only while the player owns an intact radar.
        /// PlayerPrefs oz_radar_dots 0 = always show them (the old minimap).</summary>
        public static bool RadarRule { get { if (radarRule < 0) radarRule = PlayerPrefs.GetInt("oz_radar_dots", 1); return radarRule == 1; } set => radarRule = value ? 1 : 0; }
        static int radarRule = -1;
        public static bool HasRadar { get; private set; }
        float nextRefresh, zoneBlendAt = -10f;
        readonly Image[] frustum = new Image[4], frustumShade = new Image[4];
        readonly List<Image> linePool = new List<Image>(); int lineUsed;
        readonly List<Image> markPool = new List<Image>(); int markUsed;
        readonly List<Image> unitPool = new List<Image>(); int unitUsed;
        readonly List<Image> bldPool = new List<Image>(); int bldUsed;
        sealed class Ping { public Image img; public float t0, delay, r0, r1, dur; public Color c; public Vector2 at; }
        readonly List<Ping> pings = new List<Ping>();
        bool dragFly, rallyPress; Vector2 lastFly;

        // ------------------------------------------------------------------ build
        public void Build(OpenZHud h)
        {
            hud = h; game = h.game; W = game.World; mw = W.Map.width; mh = W.Map.height;
            k0 = Mathf.Clamp(Mathf.CeilToInt(360f / Mathf.Max(mw, mh)), 2, 5);                     // reference texels per tile: L02 (72x90) 4, L17 (110x128) 3
            // were magnified 1.7-2x (soft zone borders and cliff lines). Now at least one texel per screen pixel (L02 7-8, L17 5-6 at
            // 1600p); stripes, plate seams and outlines keep their reference proportions (ek texels = one reference texel).
            float mapPx = Mathf.Min(OzLayout.MinimapWell.w, maxSide) * OzPx.K;
            k = Mathf.Clamp(Mathf.CeilToInt(mapPx / Mathf.Max(mw, mh)), k0, 8);
            ek = Mathf.Max(1, Mathf.RoundToInt(k / (float)k0));
            tw = mw * k; th = mh * k;
            var fr = OzLayout.Minimap;
            PanelSize = fr.Size;
            panel = Hud.Node("Minimap", h.Root).Place(fr);
            h.AddBlocker(panel);
            if (OzKit.GetSprite("Minimap/mm_frame") != null)
            {
                Hud.Shadow(panel, "Minimap/mm_frame_mask");
                Hud.Kit(panel, "Frame", "Minimap/mm_frame", new OzRect(0, 0, fr.w, fr.h), null, Image.Type.Simple);
            }
            else Hud.Plate(panel, "lg", 28f, OzUi.Amber);
            panel.gameObject.AddComponent<Canvas>();                                                   // nested canvas: per-frame frustum updates rebuild only this panel
            string lvl = game.level == "L17" ? "L17 · CAR PARK" : game.level == "L02" ? "L02 · DESERT" : game.level;
            Hud.Label(panel, "Level", lvl, 10, OzUi.Muted, 0.16f, OzStyle.Plain).At(OzLayout.MinimapTitleX, OzLayout.MinimapLevelY, OzH.L, false, 200);

            // well: the map fitted keeping its aspect (zmini_map.cpp:24-59), letterbox bands hatched with coordinate ticks
            var well = OzLayout.MinimapWell;
            var wr = Hud.Node("Well", panel).Place(well);
            if (!OzKit.Available) Hud.Img(wr, "Bed", OzUi.A(OzUi.GM950, 0.92f)).rectTransform.Fill();
            float s = Mathf.Min(well.w, maxSide) / Mathf.Max(mw, mh);
            var size = new Vector2(mw * s, mh * s);
            var mr = new OzRect((well.w - size.x) * 0.5f, (well.h - size.y) * 0.5f, size.x, size.y);
            if (mr.x > 0.5f)                                                                              // side bands (L02 240 x 300, L17 258 x 300)
            {
                Hud.Tiled(wr, "BandL", "Textures/tex_hatch", new OzRect(0, 0, mr.x, well.h), OzUi.A(OzUi.Khaki, 0.04f));
                Hud.Tiled(wr, "BandR", "Textures/tex_hatch", new OzRect(mr.x + mr.w, 0, well.w - mr.x - mr.w, well.h), OzUi.A(OzUi.Khaki, 0.04f));
                for (int ty = 0; ty <= mh; ty += 10)                                                      // coordinate ticks every 10 tiles on both inner edges
                {
                    float y = mr.y + ty * s;
                    Hud.Img(wr, "TickL" + ty, OzUi.A(OzUi.Khaki, 0.3f)).rectTransform.Place(new OzRect(mr.x - 5, y - 1, 4, 2));
                    Hud.Img(wr, "TickR" + ty, OzUi.A(OzUi.Khaki, 0.3f)).rectTransform.Place(new OzRect(mr.x + mr.w + 1, y - 1, 4, 2));
                }
            }
            MapRect = Hud.Node("Map", wr).Place(mr);
            MapRect.gameObject.AddComponent<RectMask2D>();
            var ir = Hud.Node("Image", MapRect); ir.Fill();
            img = ir.gameObject.AddComponent<RawImage>(); img.raycastTarget = false;
            tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "OpenZ_Minimap" };
            img.texture = tex;
            markers = Hud.Node("Markers", MapRect); markers.Fill(); markers.pivot = Vector2.zero;
            for (int i = 0; i < 4; i++)
            {
                frustumShade[i] = Hud.Img(MapRect, "FrustumShade" + i, OzUi.A(Color.black, 0.6f)); frustumShade[i].rectTransform.anchorMin = frustumShade[i].rectTransform.anchorMax = Vector2.zero;
            }
            for (int i = 0; i < 4; i++) { frustum[i] = Hud.Img(MapRect, "Frustum" + i, OzUi.Amber); frustum[i].rectTransform.anchorMin = frustum[i].rectTransform.anchorMax = Vector2.zero; }
            var ov = Hud.Kit(wr, "ScreenOverlay", "Minimap/mm_screen_overlay", new OzRect(0, 0, well.w, well.h), null, Image.Type.Simple);  // scanlines, vignette, glass, viewfinder
            ov.enabled = OzKit.Available;
            BakeTerrain();
            buf = new Color32[terrain.Length]; baseBuf = new Color32[terrain.Length]; prevBase = new Color32[terrain.Length];
            tileZone = new int[mw * mh];
            for (int ty = 0; ty < mh; ty++) for (int tx = 0; tx < mw; tx++) tileZone[ty * mw + tx] = W.GetZoneIndex(tx * 16 + 8, ty * 16 + 8);
            shownOwners = new Team[W.Zones.Count];
            RebuildZones();
            System.Array.Copy(baseBuf, prevBase, baseBuf.Length);
            RefreshObjects();
        }

        // ------------------------------------------------------------------ terrain (once)
        struct Pal { public Color32 ground, road, water, deep, cliff, cliffDark; public bool plates; }
        Pal Palette()
        {
            if (game.level == "L17")                                                                          // Z level 17 (refs/zod_maps/L17_z1996.png)
                return new Pal { ground = new Color32(86, 86, 84, 255), road = new Color32(118, 116, 110, 255), water = new Color32(92, 104, 58, 255),
                                 deep = new Color32(70, 82, 44, 255), cliff = new Color32(172, 168, 158, 255), cliffDark = new Color32(138, 134, 126, 255), plates = true };
            return new Pal { ground = new Color32(158, 120, 80, 255), road = new Color32(126, 124, 118, 255), water = new Color32(78, 168, 160, 255),   // Z level 2; turquoise water like the 3D river
                             deep = new Color32(56, 128, 128, 255), cliff = new Color32(190, 136, 86, 255), cliffDark = new Color32(158, 106, 64, 255), plates = false };
        }

        static float Hash(int x, int y) { unchecked { int h = x * 374761393 + y * 668265263; h = (h ^ (h >> 13)) * 1274126177; return ((h ^ (h >> 16)) & 1023) / 1023f; } }

        void BakeTerrain()
        {
            var p = Palette();
            terrain = new Color32[tw * th];
            for (int ty = 0; ty < mh; ty++)
                for (int tx = 0; tx < mw; tx++)
                {
                    var cls = W.Map.ClassAt(tx, ty);
                    bool solid = cls == ZClass.Impassable;
                    Color32 c = cls == ZClass.Road ? p.road : cls == ZClass.Water ? p.water : cls == ZClass.DeepWater ? p.deep : solid ? p.cliff : p.ground;
                    float n = 0.95f + 0.1f * Hash(tx, ty);
                    bool eN = solid && !IsSolid(tx, ty - 1), eS = solid && !IsSolid(tx, ty + 1), eW = solid && !IsSolid(tx - 1, ty), eE = solid && !IsSolid(tx + 1, ty);
                    for (int sy = 0; sy < k; sy++)
                        for (int sx = 0; sx < k; sx++)
                        {
                            Color32 q = c;
                            if (solid && !p.plates && (((ty * k + sy) * k0 / k) % 3 == 0)) q = p.cliffDark;       // striated sandstone (reference rows)
                            if (p.plates && (sx < ek || sy < ek) && cls == ZClass.Ground) q = Mul(q, 0.9f);        // riveted concrete plates
                            float f = n;
                            if ((eN && sy < ek) || (eS && sy >= k - ek) || (eW && sx < ek) || (eE && sx >= k - ek)) f *= 0.62f;   // crisp cliff outline
                            q = Mul(q, f);
                            terrain[Idx(tx * k + sx, ty * k + sy)] = q;
                        }
                }
        }

        bool IsSolid(int tx, int ty) => tx < 0 || ty < 0 || tx >= mw || ty >= mh || W.Map.ClassAt(tx, ty) == ZClass.Impassable;
        /// <summary>Texel index from top-down texel coordinates (Zod y grows south; texture rows grow north).</summary>
        int Idx(int x, int yTop) => (th - 1 - yTop) * tw + x;
        static Color32 Mul(Color32 c, float f) => new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), 255);
        static Color32 Mix(Color32 a, Color c, float t) => new Color32((byte)Mathf.Lerp(a.r, c.r * 255f, t), (byte)Mathf.Lerp(a.g, c.g * 255f, t), (byte)Mathf.Lerp(a.b, c.b * 255f, t), 255);

        // ------------------------------------------------------------------ zones (when an owner changes; cross-fade 300 ms, guide 7.3)
        void RebuildZones()
        {
            for (int i = 0; i < W.Zones.Count; i++) shownOwners[i] = W.Zones[i].Owner;
            System.Array.Copy(terrain, baseBuf, terrain.Length);
            for (int ty = 0; ty < mh; ty++)
                for (int tx = 0; tx < mw; tx++)
                {
                    int z = tileZone[ty * mw + tx]; if (z < 0) continue;
                    var owner = W.Zones[z].Owner; if (owner == Team.Neutral) continue;
                    var tc = TeamColors.Of(owner); var edge = OzUi.Mark(owner);
                    bool eN = ZoneAt(tx, ty - 1) != z, eS = ZoneAt(tx, ty + 1) != z, eW = ZoneAt(tx - 1, ty) != z, eE = ZoneAt(tx + 1, ty) != z;
                    for (int sy = 0; sy < k; sy++)
                        for (int sx = 0; sx < k; sx++)
                        {
                            int i = Idx(tx * k + sx, ty * k + sy);
                            bool border = (eN && sy < ek) || (eS && sy >= k - ek) || (eW && sx < ek) || (eE && sx >= k - ek);
                            baseBuf[i] = border ? Mix(baseBuf[i], edge, 0.85f) : Mix(baseBuf[i], tc, zoneTint);
                        }
                }
        }
        int ZoneAt(int tx, int ty) => tx < 0 || ty < 0 || tx >= mw || ty >= mh ? -1 : tileZone[ty * mw + tx];

        /// <summary>A zone changed owner: refresh now (the cross-fade runs over the next refreshes).</summary>
        public void FlashZones() => nextRefresh = 0f;

        // ------------------------------------------------------------------ objects (refreshHz)
        void RefreshObjects()
        {
            bool zonesChanged = false;
            for (int i = 0; i < W.Zones.Count; i++) if (W.Zones[i].Owner != shownOwners[i]) { zonesChanged = true; break; }
            if (zonesChanged)
            {
                float kb = Mathf.Clamp01((OzMotion.Now - zoneBlendAt) / 0.3f);                              // freeze the current blend as the start
                if (kb < 1f) for (int i = 0; i < prevBase.Length; i++) prevBase[i] = Color32.Lerp(prevBase[i], baseBuf[i], kb);
                else System.Array.Copy(baseBuf, prevBase, baseBuf.Length);
                RebuildZones(); zoneBlendAt = OzMotion.Now;
            }
            float blend = OzMotion.Reduced ? 1f : Mathf.Clamp01((OzMotion.Now - zoneBlendAt) / 0.3f);
            if (blend >= 1f) System.Array.Copy(baseBuf, buf, buf.Length);
            else for (int i = 0; i < buf.Length; i++) buf[i] = Color32.Lerp(prevBase[i], baseBuf[i], blend);
            var sel = hud.input.Selection;
            // texture layer: bridges, rocks, grenade crates (buildings, flags and units are crisp markers on top)
            foreach (var o in W.Objects)
            {
                if (o.Kind == ZKind.Building)
                {
                    if (o.IsBridge && !o.Destroyed) Rect(o.LocX, o.LocY, o.WidthPix, o.HeightPix, new Color32(132, 118, 96, 255), 1f, 0);
                    continue;
                }
                if (o.Kind != ZKind.MapItem || o.Destroyed || o.KillAt >= 0) continue;
                if (o.Type == "grenades") Rect(o.LocX, o.LocY, o.WidthPix, o.HeightPix, OzUi.AmberHi, 0.7f, 1, OzUi.GM950);
                else if (o.IsDestroyableImpass) Rect(o.LocX, o.LocY, o.WidthPix, o.HeightPix, Mul(Palette().ground, 0.72f), 0.9f, 0);   // rock cell = ground x 0.72
            }
            tex.SetPixels32(buf); tex.Apply(false);

            // marker layer (guide 9.5): buildings in the owner colour, flags, then every unit (Zod draws every object)
            bldUsed = unitUsed = 0;
            foreach (var o in W.Objects)
            {
                if (o.Kind == ZKind.Building && !o.IsBridge)
                {
                    var im = NextPooled(bldPool, ref bldUsed, "Building", "Minimap/mm_building", Image.Type.Sliced);
                    var c = o.Destroyed ? new Color32(58, 56, 52, 255) : (Color32)(o.Owner == Team.Neutral ? (Color)OzUi.Neutral : OzUi.Mark(o.Owner));
                    if (im.color != (Color)c) im.color = c;
                    var a = PxToLocal(o.LocX + o.WidthPix * 0.1f, o.LocY + o.HeightPix * 0.1f); var b = PxToLocal(o.LocX + o.WidthPix * 0.9f, o.LocY + o.HeightPix * 0.9f);
                    im.rectTransform.anchoredPosition = new Vector2(a.x, b.y); im.rectTransform.sizeDelta = new Vector2(Mathf.Max(4f, b.x - a.x), Mathf.Max(4f, a.y - b.y));
                }
                else if (o.Kind == ZKind.MapItem && o.Type == "flag" && !o.Destroyed)
                {
                    var im = NextPooled(bldPool, ref bldUsed, "Flag", "Minimap/mm_flag", Image.Type.Simple);
                    var c = o.Owner == Team.Neutral ? (Color)OzUi.Ink2 : OzUi.Mark(o.Owner);
                    if (im.color != c) im.color = c;
                    var p = PxToLocal(o.CenterX, o.CenterY);
                    im.rectTransform.anchoredPosition = p + new Vector2(-2f, 0f); im.rectTransform.sizeDelta = new Vector2(8, 9);
                }
            }
            // Zod the radar has no effect, bradar.cpp: a view-only rule, listed in the parity report). Buildings, flags and zones
            // stay (map knowledge); enemy robots, vehicles and cannons need an intact radar of ours.
            bool radar = !RadarRule;
            if (!radar) foreach (var b in W.Objects) if (b.Kind == ZKind.Building && b.Type == "radar" && !b.Destroyed && b.Owner == hud.Player) { radar = true; break; }
            HasRadar = radar;
            foreach (var o in W.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.KillAt >= 0) continue;
                if (!radar && o.Owner != hud.Player && o.Owner != Team.Neutral) continue;
                bool selected = o.Owner == hud.Player && sel.Contains(o.Leader ?? o);
                string rel = o.Kind == ZKind.Robot ? "Minimap/mm_unit" : o.Kind == ZKind.Vehicle ? "Minimap/mm_vehicle" : "Minimap/mm_cannon";
                float sz = o.Kind == ZKind.Robot ? 5f : o.Kind == ZKind.Vehicle ? 6f : 5f;
                var p = PxToLocal((float)o.PosX + o.WidthPix * 0.5f, (float)o.PosY + o.HeightPix * 0.5f);
                if (selected)
                {
                    var ring = NextPooled(unitPool, ref unitUsed, "Select", "Minimap/mm_select", Image.Type.Simple);
                    ring.color = OzUi.Ink; ring.rectTransform.anchoredPosition = p; ring.rectTransform.sizeDelta = new Vector2(9, 9);
                }
                var im = NextPooled(unitPool, ref unitUsed, "Unit", rel, Image.Type.Simple);
                var c = OzUi.Mark(o.Owner); if (im.color != c) im.color = c;
                im.rectTransform.anchoredPosition = p; im.rectTransform.sizeDelta = new Vector2(sz, sz);
            }
            for (int i = bldUsed; i < bldPool.Count; i++) bldPool[i].gameObject.SetActiveFast(false);
            for (int i = unitUsed; i < unitPool.Count; i++) unitPool[i].gameObject.SetActiveFast(false);
        }

        /// <summary>Pooled marker: the pool entry's sprite follows `rel` (buildings and flags share one pool).</summary>
        Image NextPooled(List<Image> pool, ref int used, string name, string rel, Image.Type type)
        {
            if (used >= pool.Count)
            {
                var im = Hud.Img(markers, name, Color.white);
                im.rectTransform.anchorMin = im.rectTransform.anchorMax = Vector2.zero; im.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                pool.Add(im);
            }
            var r = pool[used++];
            var s = OzKit.GetSprite(rel);
            if (r.sprite != s) { r.sprite = s; r.type = s != null && s.border != Vector4.zero ? type : Image.Type.Simple; }
            bool bld = rel.EndsWith("mm_building", System.StringComparison.Ordinal);
            if (bld || rel.EndsWith("mm_flag", System.StringComparison.Ordinal)) r.rectTransform.pivot = bld ? new Vector2(0, 0) : new Vector2(0.5f, 0.5f);
            r.gameObject.SetActiveFast(true);
            return r;
        }

        /// <summary>Zod px rectangle scaled by `shrink` around its centre (Zod draws 80 %), filled, with an optional outline.</summary>
        void Rect(int px, int py, int w, int h, Color fill, float shrink, int outline, Color edge = default, int minPx = 1)
        {
            outline *= ek; minPx *= ek;                                                                  // ui_quality_v1: reference proportions
            float cx = (px + w * 0.5f) * k / 16f, cy = (py + h * 0.5f) * k / 16f;
            float hw = Mathf.Max(minPx * 0.5f, w * shrink * k / 32f), hh = Mathf.Max(minPx * 0.5f, h * shrink * k / 32f);
            int x0 = Mathf.RoundToInt(cx - hw), x1 = Mathf.RoundToInt(cx + hw) - 1, y0 = Mathf.RoundToInt(cy - hh), y1 = Mathf.RoundToInt(cy + hh) - 1;
            Color32 f = fill, e = edge;
            for (int y = y0 - outline; y <= y1 + outline; y++)
            {
                if (y < 0 || y >= th) continue;
                for (int x = x0 - outline; x <= x1 + outline; x++)
                {
                    if (x < 0 || x >= tw) continue;
                    bool inside = x >= x0 && x <= x1 && y >= y0 && y <= y1;
                    if (!inside && outline == 0) continue;
                    buf[Idx(x, y)] = inside ? f : e;
                }
            }
        }

        // ------------------------------------------------------------------ coordinates
        /// <summary>Screen pixel over the map image to Zod pixels.</summary>
        public bool ScreenToPx(Vector2 screen, out Vector2Int px)
        {
            px = default;
            if (!OpenZHud.Contains(MapRect, screen)) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(MapRect, screen, null, out var lp);
            var r = MapRect.rect;
            float u = (lp.x - r.xMin) / r.width, v = (lp.y - r.yMin) / r.height;
            px = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(u * W.Map.WidthPix), 0, W.Map.WidthPix - 1), Mathf.Clamp(Mathf.RoundToInt((1 - v) * W.Map.HeightPix), 0, W.Map.HeightPix - 1));
            return true;
        }

        /// <summary>Zod pixels to the map image's local position (origin bottom-left).</summary>
        public Vector2 PxToLocal(float px, float py)
        {
            var r = MapRect.rect;
            return new Vector2(px / W.Map.WidthPix * r.width, (1 - py / W.Map.HeightPix) * r.height);
        }

        Vector3 PxToGround(Vector2Int px) { var p = ZGame.PxToWorld(px.x, px.y); p.y = game.GroundY(p.x, p.z); return p; }

        // ------------------------------------------------------------------ frame
        static readonly int NightId = Shader.PropertyToID("_OZNight");
        void Update()
        {
            if (img) { float n = Mathf.Clamp01(Shader.GetGlobalFloat(NightId)); float k = Mathf.Lerp(1f, 0.62f, n); img.color = new Color(k, k, k * 1.04f, 1f); }
            if (hud == null || W == null || !MapRect) return;
            var m = Mouse.current; var kb = Keyboard.current; if (m == null) return;
            Vector2 mp = hud.Pointer;
            bool modal = hud.Modal;
            bool over = !modal && OpenZHud.Contains(MapRect, mp);
            bool shift = kb != null && kb.shiftKey.isPressed;

            // left: fly (Zod: press flies, drag follows, zplayer_events.cpp:149-162, 255-257) or the armed rally tool
            if (m.leftButton.wasPressedThisFrame && over && hud.PressCapture == OpenZHud.Capture.Ui && hud.HotButton == null)
            {
                if (hud.Rally && hud.Rally.Armed) rallyPress = true;
                else { dragFly = true; FlyTo(mp); }
            }
            if (dragFly) { if (m.leftButton.isPressed && !modal) { if ((mp - lastFly).sqrMagnitude > 1f && over) FlyTo(mp); } else dragFly = false; }
            if (rallyPress && m.leftButton.wasReleasedThisFrame)
            {
                rallyPress = false;
                if (!modal && ScreenToPx(mp, out var rp)) { hud.Rally.AddPoint(rp.x, rp.y, shift); PingOrder(rp); }
            }
            // right release: orders with no target, or rally points
            if (m.rightButton.wasReleasedThisFrame && hud.hudVisible && !modal && ScreenToPx(mp, out var px))
            {
                if (hud.input.Selection.Count > 0) { Order(px, kb, mp); PingOrder(px); }
                else if (hud.Production && hud.Production.Building != null) { hud.Rally.AddPoint(px.x, px.y, shift); PingOrder(px); }
            }

            if (Time.unscaledTime >= nextRefresh || (OzMotion.Now - zoneBlendAt < 0.35f)) { nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, refreshHz); RefreshObjects(); }
            DrawFrustum();
            DrawRally();
            DrawPings();
        }

        void FlyTo(Vector2 mp)
        {
            lastFly = mp;
            if (!hud.rts || !ScreenToPx(mp, out var px)) return;
            hud.rts.FlyTo(ZGame.PxToWorld(px.x, px.y));
        }

        /// <summary>zplayer.cpp:2746-2780 over the minimap: MOVE to the point, no hovered object, attack_to unless the unit is
        /// already near hostiles, Ctrl forces attack-move, Alt plain move; added to PlayerInput's own pending lists so Shift
        /// chains and Z-nearest work across map and minimap clicks.</summary>
        void Order(Vector2Int px, Keyboard kb, Vector2 mp)
        {
            bool ctrl = kb != null && kb.ctrlKey.isPressed, alt = kb != null && kb.altKey.isPressed, shift = kb != null && kb.shiftKey.isPressed, z = kb != null && OzBindings.Held(OzAction.NearestOnly);   /* menu_exe_v1: bound key (Z) */
            var br = hud.Bridge; var pend = br?.Pending; var sel = hud.input.Selection;
            foreach (var u in sel)
            {
                var wp = ZWaypoint.Move(px.x, px.y, !(br?.NearHostiles(u) ?? false));
                if (ctrl) wp.attackTo = true;
                if (alt) wp.attackTo = false;
                if (pend != null) { if (!pend.TryGetValue(u, out var l)) pend[u] = l = new List<ZWaypoint>(); l.Add(wp); }
                else W.SendWaypoints(hud.Player, u, new[] { wp });
            }
            br?.SetMarker(PxToGround(px));
            if (pend != null && !shift && !br.Flush(z, mp))
            {
                foreach (var kv in pend) W.SendWaypoints(hud.Player, kv.Key, kv.Value);
                pend.Clear();
            }
        }

        // ------------------------------------------------------------------ pings (guide 7.3)
        Ping NextPing()
        {
            foreach (var p in pings) if (!p.img.gameObject.activeSelf) return p;
            var im = Hud.Kit(MapRect, "Ping", "Bits/ring", new OzRect(0, 0, 10, 10), Color.white, Image.Type.Simple);
            im.rectTransform.anchorMin = im.rectTransform.anchorMax = Vector2.zero; im.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            var n = new Ping { img = im }; pings.Add(n); return n;
        }

        void AddPing(Vector2 at, Color c, float r0, float r1, float dur, float delay)
        {
            var p = NextPing(); p.at = at; p.c = c; p.r0 = r0; p.r1 = r1; p.dur = dur; p.delay = delay; p.t0 = OzMotion.Now;
            p.img.rectTransform.anchoredPosition = at; p.img.rectTransform.sizeDelta = Vector2.one * r0 * 2f; p.img.color = new Color(c.r, c.g, c.b, 0f);
            p.img.gameObject.SetActive(true); p.img.rectTransform.SetAsLastSibling();
        }

        /// <summary>Under attack / territory lost: three ALERT rings 6 -> 34 u, 700 ms, 150 ms apart.</summary>
        public void PingAlert(int px, int py) { var at = PxToLocal(px, py); for (int i = 0; i < 3; i++) AddPing(at, OzUi.Alert, 6, 34, 0.7f, i * 0.15f); }
        /// <summary>New unit: one small INK ring 4 -> 16 u, 400 ms.</summary>
        public void PingNew(int px, int py) => AddPing(PxToLocal(px, py), OzUi.Ink, 4, 16, 0.4f, 0f);
        void PingOrder(Vector2Int px) => AddPing(PxToLocal(px.x, px.y), OzUi.Ink, 13, 3, 0.6f, 0f);

        void DrawPings()
        {
            float now = OzMotion.Now;
            foreach (var p in pings)
            {
                if (!p.img.gameObject.activeSelf) continue;
                float k = (now - p.t0 - p.delay) / Mathf.Max(0.01f, p.dur);
                if (k >= 1f || OzMotion.Reduced && k > 0.3f) { p.img.gameObject.SetActive(false); continue; }
                if (k < 0f) { p.img.color = new Color(p.c.r, p.c.g, p.c.b, 0f); continue; }
                float r = Mathf.Lerp(p.r0, p.r1, OzEase.OutCubic(k));
                p.img.rectTransform.sizeDelta = Vector2.one * r * 2f;
                p.img.color = new Color(p.c.r, p.c.g, p.c.b, 1f - k);
            }
        }

        // ------------------------------------------------------------------ camera frustum (Zod: yellow view rectangle; guide: 1.5 u AMBER polyline)
        static readonly Vector2[] Corners = { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        readonly Vector2[] fr = new Vector2[4];
        void DrawFrustum()
        {
            var cam = hud.rts ? hud.rts.GetComponent<Camera>() : Camera.main; if (!cam) return;
            for (int i = 0; i < 4; i++)
            {
                var ray = cam.ViewportPointToRay(new Vector3(Corners[i].x, Corners[i].y, 0));
                if (!game.GroundPoint(ray, out var g)) g = ray.GetPoint(300);
                var px = ZGame.WorldToPx(g);
                fr[i] = PxToLocal(px.x, px.y);
            }
            float w = OzPx.Line(1.5f), ws = OzPx.Line(3f);
            OzCamEdge.KeepFrameBottom(fr, ws * 0.5f);                                                    // the camera may look past the lower map edge: the frame's lower line stays on the map's lower border (OzCamEdge.cs)
            for (int i = 0; i < 4; i++) { Line(frustumShade[i], fr[i], fr[(i + 1) % 4], ws); Line(frustum[i], fr[i], fr[(i + 1) % 4], w); }
        }

        static void Line(Image im, Vector2 a, Vector2 b, float w)
        {
            var d = b - a; var rt = im.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = (a + b) * 0.5f;
            rt.sizeDelta = new Vector2(d.magnitude + w, w);
            rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        // ------------------------------------------------------------------ rally routes on the map (AMBER diamonds, dotted INK line)
        Image NextLine()
        {
            if (lineUsed >= linePool.Count) { var im = Hud.Img(MapRect, "RallyDot", OzUi.Ink); im.rectTransform.anchorMin = im.rectTransform.anchorMax = Vector2.zero; im.rectTransform.pivot = new Vector2(0.5f, 0.5f); linePool.Add(im); }
            var r = linePool[lineUsed++]; r.gameObject.SetActiveFast(true); return r;
        }
        Image NextMark()
        {
            if (markUsed >= markPool.Count) { var im = Hud.Kit(MapRect, "RallyMark", "Minimap/mm_rally", new OzRect(0, 0, 8, 8), OzUi.Amber, Image.Type.Simple); im.rectTransform.anchorMin = im.rectTransform.anchorMax = Vector2.zero; im.rectTransform.pivot = new Vector2(0.5f, 0.5f); markPool.Add(im); }
            var r = markPool[markUsed++]; r.gameObject.SetActiveFast(true); return r;
        }

        readonly List<Vector2> pts = new List<Vector2>();
        void DrawRally()
        {
            lineUsed = markUsed = 0;
            var open = hud.Production ? hud.Production.Building : null;
            foreach (var b in W.Objects)
            {
                if (!b.IsProducer || b.Owner != hud.Player) continue;
                bool isOpen = b == open;
                var list = isOpen && hud.Rally && hud.Rally.PendingFor(b) is IReadOnlyList<ZWaypoint> pend && pend.Count > 0 ? pend : (IReadOnlyList<ZWaypoint>)b.RallyPoints;
                if (list.Count == 0) continue;
                float alpha = isOpen ? 1f : 0.5f;
                pts.Clear();
                if (b.GetCreationMovePoint(out int ex, out int ey)) pts.Add(PxToLocal(ex, ey));
                for (int wi = 0; wi < list.Count; wi++) { var w = list[wi]; pts.Add(PxToLocal(w.x, w.y)); }     // index loop: no boxed enumerator per frame
                for (int i = 1; i < pts.Count; i++)                                                        // 1 u dotted INK 50 % line
                {
                    var a = pts[i - 1]; var c = pts[i]; float len = Vector2.Distance(a, c);
                    for (float s = 2f; s < len - 2f; s += 4f)
                    {
                        var d = NextLine(); d.color = OzUi.A(OzUi.Ink, 0.5f * alpha);
                        d.rectTransform.anchoredPosition = Vector2.Lerp(a, c, s / len); d.rectTransform.sizeDelta = new Vector2(1.2f, 1.2f);
                    }
                }
                for (int i = 1; i < pts.Count; i++)
                {
                    var mk = NextMark(); mk.color = OzUi.A(OzUi.Amber, alpha);
                    mk.rectTransform.anchoredPosition = pts[i]; float s = i == pts.Count - 1 ? 8f : 6f; mk.rectTransform.sizeDelta = new Vector2(s, s);
                }
            }
            for (int i = lineUsed; i < linePool.Count; i++) linePool[i].gameObject.SetActiveFast(false);
            for (int i = markUsed; i < markPool.Count; i++) markPool[i].gameObject.SetActiveFast(false);
        }

        void OnDestroy() { if (tex) Destroy(tex); }
    }
}
