using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The camera's lower (south) pan limit gives way just far enough to lift a building, flag or gun out from under the HUD.
    ///
    /// Without it the lower screen edge stops at the map's lower edge (RtsCamera.Place keeps the focus min(0.42 h, 0.45 mapY)
    /// north of it), so at a wide zoom everything near that edge sits in the bottom rows of the screen, where the unit panel,
    /// the minimap and the command bar are, and cannot be scrolled out from under them.
    ///
    /// The rule: for the current zoom, screen and HUD scale, every building, flag and gun is tested: can the camera, inside
    /// its limits, show the whole of it clear of every panel? If one cannot, and lifting it higher on the screen (the camera
    /// moving south) frees it, the limit moves south by exactly that much, never more: the screen centre never passes the
    /// object itself, and the bottom screen row never looks further past the map edge than the outer rock band reaches
    /// (ReachCap). Nothing near the edge, a close zoom or an ending running: the limit is the old one. The limit does not read
    /// whether the HUD is shown at the moment: the pause menu and the hide key switch the HUD off, and the view must stay where it is.
    /// </summary>
    public static class OzCamEdge
    {
        /// <summary>Off = the old limit everywhere.</summary>
        public static bool Enabled = true;
        /// <summary>Canvas units kept free around every panel: "clear" means clear by this much.</summary>
        public const float MarginU = 8f;
        /// <summary>The exit button's place on the unit panel (panel-local, as built by the HUD): it stands above the panel's top edge.</summary>
        public const float ExitX = 330f, ExitY = -38f, ExitW = 132f, ExitH = 32f;
        /// <summary>How far past the lower map edge (m, measured on flat ground) the bottom screen row may look, so that more than half of
        /// that row is still outer rock. The camera stands south of the edge and looks north and down, so a surface H m high is met
        /// 0.34 x H m FURTHER out than flat ground. Desert: rock fills a third or more of a row out to 38 m and stands 12 to 18 m high:
        /// with 34 the bottom row shows the ground out to 40 m (with 40 it would show 45 m out: four fifths bare dunes). Car park: rock
        /// to 17 m, about 10 m high: with 16 the bottom row shows out to 21 m; open harbour water after it.</summary>
        public static float ReachCapL02 = 34f, ReachCapL17 = 16f, ReachCapOther = 16f;
        public static float ReachCap(string level) => level == "L02" ? ReachCapL02 : level == "L17" ? ReachCapL17 : ReachCapOther;
        /// <summary>Car park: a standing extra travel at the lower edge, as a fraction of the camera height (m of travel per m of
        /// height), whether or not an object asks for it. Its lowest flags stand 16 m from the lower map edge: on the old limit they
        /// sit low on the screen, just above the panels, and the view cannot be pushed further. With this the view goes a little
        /// lower at every zoom and they come up clear of the panels. The reach cap still holds: the bottom screen row never looks
        /// further past the map edge than the outer rock reaches. Other maps: 0.</summary>
        public static float StandingL17 = 0.12f;
        public static float Standing(string level) => level == "L17" ? StandingL17 : 0f;

        /// <summary>Read-outs of the last call (checks): the extra travel the objects ask for (m), the limit in use (m the focus stays
        /// north of the lower map edge), how far past that edge the bottom screen row looks (m, negative = inside the map), and whether
        /// the reach cap is holding the limit back.</summary>
        public static float Need { get; private set; }
        public static float SouthNow { get; private set; }
        public static float Reach { get; private set; }
        public static bool CapHolds { get; private set; }

        struct Box { public float x0, x1, zs, zn, H; }

        static readonly List<Vector4> rects = new List<Vector4>(12);       // x = left, y = right, z = bottom, w = top (screen fractions, margin included)
        static readonly List<Vector2> blocked = new List<Vector2>(12);     // focus-x intervals (open) in which the object overlaps a panel
        static readonly float[] cpx = new float[8], ck = new float[8];
        static float sinP, cosP, cotP, tanF, aspect, g0;
        static Camera camC;
        static float eased = -1f, easedAt;
        static float kH = -1f, kScale, kAt = -99f, kNeed; static int kW, kHt, kCount; static Vector2 kMap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { eased = -1f; kH = -1f; kAt = -99f; camC = null; Need = 0f; SouthNow = 0f; Reach = 0f; CapHolds = false; }

        /// <summary>Checks: forget the easing and the cached answer, the next call starts fresh.</summary>
        public static void Snap() { eased = -1f; kH = -1f; kAt = -99f; }

        /// <summary>RtsCamera.Place: the distance (m) the focus has to stay north of the lower map edge. southToday = the old rule's value.</summary>
        public static float South(RtsCamera cam, float h, float southToday)
        {
            if (!Enabled || !Application.isPlaying || cam == null) return southToday;
            if (camC == null || camC.gameObject != cam.gameObject) camC = cam.GetComponent<Camera>();
            float pitch = cam.pitch * Mathf.Deg2Rad;
            tanF = Mathf.Tan((camC ? camC.fieldOfView : 32f) * 0.5f * Mathf.Deg2Rad);
            sinP = Mathf.Sin(pitch); cosP = Mathf.Cos(pitch); cotP = cosP / Mathf.Max(0.01f, sinP);
            g0 = cotP - 1f / Mathf.Tan(pitch + Mathf.Atan(tanF));              // the bottom screen row meets flat ground g0 x h south of the focus
            float target = southToday; Need = 0f; CapHolds = false;
            var g = ZGame.I; var hud = OpenZHud.I;
            int sw = camC ? camC.pixelWidth : Screen.width, sh = camC ? camC.pixelHeight : Screen.height;
            // not tied to the HUD being shown: the pause menu and the hide key switch it off, and the view must not slide north under them
            if (g != null && g.World != null && hud != null && !RtsCamera.InputLocked && !cam.unclamped && sw > 0 && sh > 0)
            {
                Need = NeedFor(cam, g, h, southToday, sw, sh);
                float want = southToday - Mathf.Max(Need, Standing(g.level) * h), floor = h * g0 - ReachCap(g.level);
                CapHolds = want < floor - 0.01f;
                target = Mathf.Min(southToday, Mathf.Max(want, floor));
            }
            // more room at once; less room eased, so the view is never snapped north when an object stops asking (zooming in, a gun lost)
            float now = Time.unscaledTime;
            if (eased < 0f || target <= eased) eased = target;
            else eased = Mathf.MoveTowards(eased, target, Mathf.Max(30f, h * 1.2f) * Mathf.Max(0f, now - easedAt));
            easedAt = now;
            SouthNow = Mathf.Min(eased, southToday); Reach = h * g0 - SouthNow;
            return SouthNow;
        }

        static float NeedFor(RtsCamera cam, ZGame g, float h, float southToday, int sw, int sh)
        {
            var objs = g.World.Objects; float scale = OzText.UiScale, now = Time.unscaledTime;
            if (Mathf.Abs(h - kH) < 0.02f && sw == kW && sh == kHt && Mathf.Abs(scale - kScale) < 0.0005f && objs.Count == kCount && cam.mapSizeMeters == kMap && now - kAt < 0.5f && now >= kAt)
                return kNeed;
            kH = h; kW = sw; kHt = sh; kScale = scale; kCount = objs.Count; kMap = cam.mapSizeMeters; kAt = now;
            aspect = (float)sw / sh;
            BuildRects(sw, sh, scale);
            float side = Mathf.Min(h * 0.3f, cam.mapSizeMeters.x * 0.45f);
            float X0 = side, X1 = cam.mapSizeMeters.x - side, Z0 = -cam.mapSizeMeters.y + southToday, Z1 = -Mathf.Min(10f, cam.mapSizeMeters.y * 0.1f);
            float need = 0f;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o == null || o.Type == null) continue;
                bool site = (o.Kind == ZKind.Building && !o.Type.StartsWith("bridge")) || (o.Kind == ZKind.Cannon && !o.Destroyed) || (o.Kind == ZKind.MapItem && o.Type == "flag");
                if (!site) continue;
                Box b; b.x0 = o.LocX / 8f; b.x1 = (o.LocX + o.WidthPix) / 8f; b.zn = -o.LocY / 8f; b.zs = -(o.LocY + o.HeightPix) / 8f; b.H = BodyHeight(o.Type);
                float n = NeedSouth(b, h, X0, X1, Z0, Z1);
                if (n > need) need = n;
            }
            kNeed = need; return need;
        }

        /// <summary>Body height (m) for the screen box: upper estimates by type; only the top of the box depends on it.</summary>
        static float BodyHeight(string type)
        {
            switch (type)
            {
                case "fort_front": case "fort_back": return 12f;
                case "robot_factory": case "vehicle_factory": return 9f;
                case "radar": return 10f;
                case "repair": return 7f;
                case "flag": return 5f;
                default: return 3.5f;                                            // guns
            }
        }

        /// <summary>The always-on HUD panels as screen fractions, from the layout numbers the HUD itself places them by.</summary>
        static void BuildRects(int sw, int sh, float uiScale)
        {
            rects.Clear();
            float ch = OzLayout.RefH / Mathf.Clamp(uiScale, OzText.ScaleMin, OzText.ScaleMax), cw = sw * ch / sh;
            Add(OzLayout.TopStrip, cw, ch); Add(OzLayout.TerrTab, cw, ch); Add(OzLayout.MenuButton, cw, ch);
            Add(OzLayout.CommandBar, cw, ch); Add(OzLayout.Minimap, cw, ch);
            var pr = OzLayout.UnitPanelFor(cw);
            Add(pr, cw, ch);
            Add(new OzRect(pr.a, pr.x + OzLayout.AlertTab.x, pr.y + OzLayout.AlertTab.y, OzLayout.AlertTab.w, OzLayout.AlertTab.h), cw, ch);
            Add(new OzRect(pr.a, pr.x + ExitX, pr.y + ExitY, ExitW, ExitH), cw, ch);
        }

        static void Add(OzRect r, float cw, float ch)
        {
            float x = r.XOn(cw);
            bool fromBottom = r.a == OzAnchor.BL || r.a == OzAnchor.BC || r.a == OzAnchor.BR;
            float top = fromBottom ? OzLayout.RefH - r.y : ch - r.y;             // canvas units above the screen's lower edge
            rects.Add(new Vector4((x - MarginU) / cw, (x + r.w + MarginU) / cw, (top - r.h - MarginU) / ch, (top + MarginU) / ch));
        }

        /// <summary>Metres of extra south travel that bring this object clear (0: it is clear inside the old limits, or moving south cannot help).</summary>
        static float NeedSouth(Box o, float h, float X0, float X1, float Z0, float Z1)
        {
            float step = h / 180f;
            float zMax = Mathf.Min(Z1, o.zn + g0 * h + 1f), zMin = o.zs;         // the lift stops when the focus reaches the object itself
            float zc = Mathf.Clamp(0.5f * (o.zs + o.zn), Mathf.Min(Z0, Z1), Z1);
            if (Feasible(o, h, zc, X0, X1)) return 0f;
            for (float z = Mathf.Max(Z0, zMin); z <= zMax; z += step) if (Feasible(o, h, z, X0, X1)) return 0f;
            float prev = Mathf.Min(Z0, zMax);
            for (float z = prev - step; z >= zMin; z -= step)
            {
                if (Feasible(o, h, z, X0, X1))
                {
                    float good = z, bad = prev;
                    for (int i = 0; i < 10; i++) { float mid = 0.5f * (good + bad); if (Feasible(o, h, mid, X0, X1)) good = mid; else bad = mid; }
                    return Z0 - good;
                }
                prev = z;
            }
            return 0f;
        }

        /// <summary>With the focus at depth fz: is there a focus x in [X0, X1] that shows the whole body on screen and clear of every panel?
        /// The body's screen height does not depend on the focus x; each corner's screen x falls linearly as the focus x grows, so every
        /// panel blocks one open interval of focus x and the answer is decided at the interval ends.</summary>
        static bool Feasible(Box o, float h, float fz, float X0, float X1)
        {
            float cz = fz - h * cotP, ly = 9f, hy = -9f; int n = 0;
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                    for (int iy = 0; iy < 2; iy++)
                    {
                        float dy = (iy == 0 ? 0f : o.H) - h, dz = (iz == 0 ? o.zs : o.zn) - cz;
                        float depth = -dy * sinP + dz * cosP;
                        if (depth < 0.01f) return false;
                        float vy = 0.5f + (dy * cosP + dz * sinP) / (2f * depth * tanF);
                        if (vy < ly) ly = vy; if (vy > hy) hy = vy;
                        cpx[n] = ix == 0 ? o.x0 : o.x1; ck[n] = 2f * depth * tanF * aspect; n++;
                    }
            if (ly < 0f || hy > 1f) return false;
            float lo = X0, hi = X1;
            for (int i = 0; i < 8; i++) { lo = Mathf.Max(lo, cpx[i] - 0.5f * ck[i]); hi = Mathf.Min(hi, cpx[i] + 0.5f * ck[i]); }
            if (lo > hi) return false;
            blocked.Clear();
            for (int r = 0; r < rects.Count; r++)
            {
                var p = rects[r];
                if (!(hy > p.z && ly < p.w)) continue;
                float a = float.MinValue, b = float.MaxValue;
                for (int i = 0; i < 8; i++) { a = Mathf.Max(a, cpx[i] - (p.x - 0.5f) * ck[i]); b = Mathf.Min(b, cpx[i] - (p.y - 0.5f) * ck[i]); }
                if (b < a) blocked.Add(new Vector2(b, a));
            }
            if (Free(lo) || Free(hi)) return true;
            for (int j = 0; j < blocked.Count; j++)
            {
                float b = blocked[j].x, a = blocked[j].y;
                if (b >= lo && b <= hi && Free(b)) return true;
                if (a >= lo && a <= hi && Free(a)) return true;
            }
            return false;
        }

        static bool Free(float x)
        {
            for (int j = 0; j < blocked.Count; j++) if (x > blocked[j].x + 1e-4f && x < blocked[j].y - 1e-4f) return false;
            return true;
        }

        /// <summary>Minimap: the view frame's four ground corners in the map picture's own units (lower-left, lower-right, upper-right,
        /// upper-left). When the camera looks past the lower map edge the two lower corners slide up their side lines to the map's
        /// lower border, so the frame keeps its lower line there and stays closed.</summary>
        public static void KeepFrameBottom(Vector2[] fr, float inset)
        {
            if (!Enabled || fr == null || fr.Length < 4) return;
            if (Mathf.Abs(fr[0].y - fr[1].y) > 0.5f) return;                     // a turned view (ending orbit): left as it is
            if (fr[0].y >= inset) return;
            fr[0] = SlideUp(fr[0], fr[3], inset); fr[1] = SlideUp(fr[1], fr[2], inset);
        }

        static Vector2 SlideUp(Vector2 low, Vector2 high, float y)
        {
            if (low.y >= y || high.y <= y) return low;
            return Vector2.Lerp(low, high, (y - low.y) / (high.y - low.y));
        }
    }
}
