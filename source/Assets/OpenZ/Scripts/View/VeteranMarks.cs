using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.UI;

namespace OpenZ
{
    /// <summary>
    /// veterancy_v1, deviation DV12 "veteran crews" (Sim/ZVeterancy.cs): everything the player sees and hears of it. The view
    /// only READS the sim (ZObject.Veteran / Kills / PromotedAt and the Promoted event); nothing here writes sim state, uses sim
    /// random numbers or changes sim timing, so a lockstep peer's sim is unaffected by what its screen shows.
    ///  * a golden star right next to Zod's small health bar (selected / hovered / damaged units: OpenZUnitOverlay.Draw calls
    ///    <see cref="BarStar"/>), popping in with a bounce for 1.2 s after the promotion;
    ///  * a golden star in the top-right corner of the unit panel's portrait (OpenZHud.RefreshUnitPanel calls <see cref="PortraitStar"/>);
    ///  * on promotion: a golden star + "VETERAN" chip rises over the unit for 2.6 s (any team, on screen), Alan's promotion line
    ///    for the player's crews (vo_promoted) and Rachel's "Unit promoted." when that crew is off screen (ann_unit_promoted);
    ///  * buttoned up: BoardingView never pops a veteran commander out of the hatch (<see cref="ButtonedUp"/>);
    ///  * quicker aim: UnitAim slews a veteran's turret / torso <see cref="AimBoost"/> x faster (<see cref="AimSlew"/>). View only:
    ///    Zod's sim has no aim time; UnitAim only holds the DRAWN shot until the gun points at the target.
    /// </summary>
    [DefaultExecutionOrder(40)]                                                          // after ZGame ticked (-50), before CombatView (50) clears the events
    public class VeteranMarks : MonoBehaviour
    {
        /// <summary>Star colours: warm gold with a dark bronze rim (reads on sand, concrete and the dark HUD).</summary>
        public static readonly Color Gold = new Color(1f, 0.80f, 0.26f, 1f), GoldHi = new Color(1f, 0.93f, 0.62f, 1f), Rim = new Color(0.30f, 0.17f, 0.04f, 1f);
        /// <summary>Turret / torso slew speed factor for veteran crews (view only).</summary>
        public static float AimBoost = 1.35f;
        /// <summary>Star size next to the health bar (reference px) and in the portrait.</summary>
        public static float BarStarSize = 13f, PortraitStarSize = 34f;
        [Tooltip("Alan's promotion line for the player's crews")] public bool voice = true;
        [Tooltip("Rachel's 'Unit promoted.' when the promoted crew is off screen")] public bool announce = true;
        [Tooltip("Rising star + VETERAN chip over a unit when it is promoted")] public bool popup = true;

        static Sprite star;
        ZGame game; Canvas canvas; RectTransform root; OpenZHud hud;
        sealed class Pop { public int id; public float t0; public RectTransform rt; public Image img; public (Image bg, Image edge, OzText text) chip; public CanvasGroup cg; }
        readonly List<Pop> pops = new List<Pop>();

        // ------------------------------------------------------------------ static hooks (one line each in the patched files)
        /// <summary>BoardingView guard: a veteran crew stays inside, the hatch commander never pops out (the lid itself still
        /// follows Zod's rolls in the sim; the veteran perk is the reduced snipe chance, ZObject.SnipeChanceAgainst).</summary>
        public static bool ButtonedUp(ZObject o) => o != null && o.VeteranActive;

        /// <summary>UnitAim.LateUpdate: swap the unit's slew for a faster one while its crew is veteran (and back if the crew
        /// changes). baseSlew / on are two fields of the UnitAim.</summary>
        public static void AimSlew(ZObject o, ref UnitAim.Slew slew, ref UnitAim.Slew baseSlew, ref bool on)
        {
            bool vet = o != null && o.VeteranActive && AimBoost > 1f;
            if (vet == on) return;
            if (vet)
            {
                baseSlew = slew; var s = slew;
                s.yawSpeed *= AimBoost; s.pitchSpeed *= AimBoost; s.yawSmooth /= AimBoost; s.pitchSmooth /= AimBoost;
                slew = s;
            }
            else slew = baseSlew;
            on = vet;
        }

        /// <summary>OpenZUnitOverlay.Draw: the golden star right of the health bar (child of the bar, so it hides with it).</summary>
        public static void BarStar(RectTransform bar, ZObject o, float alpha)
        {
            if (!bar) return;
            var t = bar.Find("VetStar"); Image img;
            bool vet = o != null && o.VeteranActive && o.MaxHealth > 0;
            if (!t)
            {
                if (!vet) return;
                img = MakeStar(bar, "VetStar", BarStarSize);
                var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f); rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(3f, 0f);
            }
            else img = t.GetComponent<Image>();
            if (!img) return;
            if (img.enabled != vet) img.enabled = vet;
            if (!vet) return;
            img.rectTransform.localScale = Vector3.one * PopScale(o);
            var c = img.color; if (Mathf.Abs(c.a - alpha) > 0.004f) { c.a = alpha; img.color = c; }
        }

        /// <summary>OpenZHud.RefreshUnitPanel: the golden star in the portrait's top-right corner (u = the shown unit, may be null).</summary>
        public static void PortraitStar(RectTransform unitPanel, ZObject u)
        {
            if (!unitPanel) return;
            var portrait = unitPanel.Find("Portrait"); if (!portrait) portrait = unitPanel;
            var t = portrait.Find("VetStar"); Image img;
            bool vet = u != null && u.VeteranActive;
            if (!t)
            {
                if (!vet) return;
                img = MakeStar(portrait, "VetStar", PortraitStarSize);
                var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(-PortraitStarSize * 0.42f, -PortraitStarSize * 0.42f);
                img.transform.SetAsLastSibling();                                              // over the frame
            }
            else img = t.GetComponent<Image>();
            if (!img) return;
            if (img.enabled != vet) img.enabled = vet;
            if (vet) img.rectTransform.localScale = Vector3.one * PopScale(u);
        }

        /// <summary>1 normally; a bounce (0 -> 1.35 -> 1) over 1.2 s after the promotion (sim time).</summary>
        static float PopScale(ZObject o)
        {
            var w = ZGame.I ? ZGame.I.World : null;
            if (w == null || o.PromotedAt < 0) return 1f;
            float k = (float)(w.Time - o.PromotedAt) / 1.2f;
            if (k >= 1f || k < 0f) return 1f;
            float up = Mathf.Clamp01(k / 0.35f), down = Mathf.Clamp01((k - 0.35f) / 0.65f);
            return k < 0.35f ? Mathf.SmoothStep(0f, 1.35f, up) : Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0f, 1f, down));
        }

        static Image MakeStar(Transform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = 5 };
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false); rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>(); img.sprite = Star(); img.raycastTarget = false; img.preserveAspect = true; img.color = Color.white;
            return img;
        }

        /// <summary>A crisp five-point golden star (vertical gold gradient, bright centre facet, dark bronze rim), made once at
        /// run time: 128 px, 4x4 supersampled, so it stays sharp at every HUD scale.</summary>
        public static Sprite Star()
        {
            if (star) return star;
            const int N = 128; var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "OZ_VeteranStar", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[N * N];
            var outer = StarPoly(0.47f, 0.20f); var inner = StarPoly(0.37f, 0.155f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int cOut = 0, cIn = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / 4f) / N - 0.5f, (y + (sy + 0.5f) / 4f) / N - 0.5f);
                            if (Inside(outer, p)) cOut++;
                            if (Inside(inner, p)) cIn++;
                        }
                    float aOut = cOut / 16f, aIn = cIn / 16f;
                    float v = (float)y / N;
                    var fill = Color.Lerp(new Color(0.93f, 0.62f, 0.14f), GoldHi, Mathf.SmoothStep(0.15f, 0.95f, v));
                    var q = new Vector2((float)x / N - 0.5f, (float)y / N - 0.5f);
                    fill = Color.Lerp(fill, Color.white, 0.35f * Mathf.Clamp01(1f - q.magnitude / 0.16f));   // soft facet glint
                    var c = Color.Lerp(Rim, fill, aOut > 0f ? aIn / aOut : 0f); c.a = aOut;
                    px[y * N + x] = c;
                }
            tex.SetPixels(px); tex.Apply(true, true);
            star = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            star.name = "OZ_VeteranStar";
            return star;
        }

        static Vector2[] StarPoly(float ro, float ri)
        {
            var p = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f, r = (i & 1) == 0 ? ro : ri;
                p[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r - 0.02f);
            }
            return p;
        }

        static bool Inside(Vector2[] poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }

        // ------------------------------------------------------------------ promotion: pop-up + voice
        void Start()
        {
            game = ZGame.I;
            hud = FindFirstObjectByType<OpenZHud>();
            var go = new GameObject("OpenZ_VeteranMarks", typeof(RectTransform)) { layer = 5 };
            go.transform.SetParent(transform, false);
            canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = (hud ? hud.sortingOrder : 100) - 9;                          // over the unit marks, under the HUD panels
            var sc = go.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080); sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; sc.matchWidthOrHeight = 1f;
            root = Hud.Node("Root", go.transform); root.Fill(); root.pivot = Vector2.zero;
        }

        void Update()
        {
            if (!game) game = ZGame.I;
            if (!game || game.World == null) return;
            var ev = game.World.Events;                                                        // read only; CombatView clears the list later
            for (int i = 0; i < ev.Count; i++)
            {
                if (ev[i].type != ZEventType.Promoted) continue;
                var o = game.World.GetAny(ev[i].objId);
                if (o == null) continue;
                OnPromoted(o);
            }
        }

        void OnPromoted(ZObject o)
        {
            var cam = Cam();
            bool onScreen = false;
            if (cam)
            {
                var sp = cam.WorldToViewportPoint(Pos(o));
                onScreen = sp.z > 0 && sp.x > 0.02f && sp.x < 0.98f && sp.y > 0.02f && sp.y < 0.98f;
            }
            Debug.Log($"[OpenZ] DV12 promoted: {o} after {o.Kills} kills at {game.World.Time:0.0} s{(onScreen ? "" : " (off screen)")}");
            if (o.Owner == game.player)
            {
                if (voice) AudioDirector.Say("vo_promoted");
                if (announce && !onScreen) AudioDirector.Announce("ann_unit_promoted");
            }
            if (popup && root && onScreen) pops.Add(NewPop(o.Id));
            Shimmer(o);
        }

        Pop NewPop(int id)
        {
            var p = new Pop { id = id, t0 = Time.unscaledTime };
            p.rt = Hud.Node("Promoted", root); p.rt.anchorMin = p.rt.anchorMax = Vector2.zero; p.rt.pivot = new Vector2(0.5f, 0f); p.rt.sizeDelta = new Vector2(84, 54);
            p.cg = p.rt.gameObject.AddComponent<CanvasGroup>(); p.cg.blocksRaycasts = false; p.cg.interactable = false;
            p.img = MakeStar(p.rt, "Star", 30f);
            var srt = p.img.rectTransform; srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 1f); srt.pivot = new Vector2(0.5f, 1f); srt.anchoredPosition = Vector2.zero;
            // the chip is sized to its measured text (was a fixed 64 u box with 0.2 em tracking, the word overran it) and centred under the star
            p.chip = Hud.Chip(p.rt, "Chip", new OzRect(0, 34, 64, 20), "VETERAN", OzUi.A(OzUi.GM950, 0.92f), Gold, 11, 0.12f, OzUi.A(Gold, 0.6f), 3);
            float cw = Mathf.Ceil(OzControlsTable.TextWidth(p.chip.text) + 22f), pw = Mathf.Max(84f, cw + 8f);
            p.rt.sizeDelta = new Vector2(pw, 56);
            p.chip.bg.transform.parent.GetComponent<RectTransform>().Place(new OzRect((pw - cw) * 0.5f, 34, cw, 20));
            ChipWidth = cw;
            return p;
        }

        /// <summary></summary>
        public static float ChipWidth { get; private set; }

        void LateUpdate()
        {
            UpdateChevrons(); UpdateSweeps();
            if (pops.Count == 0) return;
            var cam = Cam();
            bool hide = !cam || (hud && hud.Modal);
            float sf = canvas && canvas.scaleFactor > 0 ? canvas.scaleFactor : 1f;
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i]; float age = Time.unscaledTime - p.t0;
                var o = game && game.World != null ? game.World.GetAny(p.id) : null;
                if (age > 2.6f || o == null || o.Destroyed) { if (p.rt) Destroy(p.rt.gameObject); pops.RemoveAt(i); continue; }
                var sp = hide ? Vector3.back : cam.WorldToScreenPoint(Pos(o) + Vector3.up * (o.Kind == ZKind.Robot ? 2.4f : 3.2f));
                bool vis = sp.z > 0;
                p.rt.gameObject.SetActive(vis);
                if (!vis) continue;
                float rise = 26f * Mathf.SmoothStep(0f, 1f, age / 2.6f);
                p.rt.anchoredPosition = new Vector2(sp.x, sp.y) / sf + new Vector2(0f, rise);
                float k = Mathf.Clamp01(age / 0.3f);
                p.img.rectTransform.localScale = Vector3.one * (k < 1f ? Mathf.Lerp(0.2f, 1.25f, k) : Mathf.Lerp(1.25f, 1f, Mathf.Clamp01((age - 0.3f) / 0.3f)));
                p.img.rectTransform.localRotation = Quaternion.Euler(0, 0, 18f * (1f - k));
                p.cg.alpha = age < 2.0f ? 1f : 1f - (age - 2.0f) / 0.6f;
            }
        }

        Camera Cam() => hud && hud.rts ? hud.rts.GetComponent<Camera>() : Camera.main;

        // on both shoulder pads of every veteran robot (fitted per robot type from its baked shoulder part: the top-outer face of the
        // pad, parented to the upper-arm bone so they ride the animation; subtle metallic gold with a faint glow so they read at RTS
        // zoom and at night; they pop in with a bounce at the promotion and fly off with the pop death), plus the promotion effect: a
        // golden ring sweeping up each promoted body, rising sparkles, a warm flash and a chime (fx_vet_promote, 3D) for any team.
        public static bool Chevrons = true;
        public static float ChevronWidth = 0.5f;
        public static int ChevronsAttached { get; private set; }
        public static int Shimmers { get; private set; }
        static Mesh chevMesh; static Material chevMat;
        sealed class Chev { public UnitView view; public Transform[] marks; public Vector3[] scale; public float t0; }
        readonly Dictionary<int, Chev> chevs = new Dictionary<int, Chev>();
        readonly List<int> chevGone = new List<int>();
        float chevScan;

        void UpdateChevrons()
        {
            if (!Chevrons || !game || game.World == null) return;
            if ((chevScan -= Time.deltaTime) <= 0f)
            {
                chevScan = 0.3f;
                foreach (var kv in game.Views)
                {
                    var v = kv.Value; if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying || !v.gameObject.activeInHierarchy) continue;
                    if (v.Obj.VeteranActive && !chevs.ContainsKey(kv.Key)) Attach(v);
                }
                chevGone.Clear();
                foreach (var kv in chevs) { var c = kv.Value; if (!c.view || c.view.Obj == null || (!c.view.Obj.VeteranActive && !c.view.Dying)) chevGone.Add(kv.Key); }
                foreach (var id in chevGone)
                {
                    var c = chevs[id]; chevs.Remove(id);
                    if (c.view && c.marks != null) foreach (var m in c.marks) if (m && m.parent) Destroy(m.gameObject);
                }
            }
            foreach (var c in chevs.Values)
            {
                if (c.marks == null) continue;
                float k = Time.time - c.t0; if (k < 0f || k > 0.85f) continue;
                float sc = k < 0.3f ? Mathf.SmoothStep(0.05f, 1.35f, k / 0.3f) : Mathf.Lerp(1.35f, 1f, Mathf.SmoothStep(0f, 1f, (k - 0.3f) / 0.55f));
                for (int i = 0; i < c.marks.Length; i++) if (c.marks[i]) c.marks[i].localScale = c.scale[i] * sc;
            }
        }

        static string ShortBone(string n)
        {
            int i = n.LastIndexOf("mixamorig", System.StringComparison.OrdinalIgnoreCase);
            return (i >= 0 ? n.Substring(i + 9) : n).TrimStart(':', '_');
        }

        void Attach(UnitView v)
        {
            SkinnedMeshRenderer body = null;
            foreach (var s in v.GetComponentsInChildren<SkinnedMeshRenderer>()) if (s.sharedMesh && (!body || s.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = s;
            var c = new Chev { view = v, t0 = -10f };
            chevs[v.Obj.Id] = c;
            if (!body) return;
            Transform la = null, ra = null, chest = null;
            foreach (var b in body.bones)
            {
                if (!b) continue;
                var n = ShortBone(b.name);
                if (n == "LeftArm") la = b; else if (n == "RightArm") ra = b; else if (n == "Spine2") chest = b;
            }
            var ps = RobotPopDeath.PartsOf(v.Obj.Type);
            var marks = new List<Transform>(); var scales = new List<Vector3>();
            foreach (var arm in new[] { la, ra })
            {
                if (!arm) continue;
                var m = Fit(v, arm, chest, ps); if (!m) continue;
                marks.Add(m); scales.Add(m.localScale);
            }
            c.marks = marks.ToArray(); c.scale = scales.ToArray();
            var w = game.World;
            if (w != null && v.Obj.PromotedAt >= 0 && w.Time - v.Obj.PromotedAt < 3.0) { c.t0 = Time.time; foreach (var m in c.marks) m.localScale = Vector3.one * 0.01f; }
            ChevronsAttached += c.marks.Length;
        }

        Transform Fit(UnitView v, Transform arm, Transform chest, RobotPopDeath.PartSet ps)
        {
            var up = v.transform.up;
            var outward = arm.position - (chest ? chest.position : v.transform.position + up * 2.2f); outward -= Vector3.Project(outward, up);
            if (outward.sqrMagnitude < 1e-5f) outward = v.transform.right; outward.Normalize();
            float hs = Mathf.Clamp(Vector3.Dot(arm.position - v.transform.position, up) / 2.6f, 0.5f, 1.6f);   // BS-01 shoulder ~2.6 m
            Vector3 pos = arm.position + outward * 0.2f * hs + up * 0.1f * hs;
            if (ps != null)
                for (int i = 0; i < ps.mesh.Length; i++)
                {
                    if (ps.bone[i] != arm.name || !ps.mesh[i].isReadable) continue;
                    var verts = ps.mesh[i].vertices; var ctr = ps.centre[i];
                    float best = float.MinValue;
                    var sc = new float[verts.Length]; var wp = new Vector3[verts.Length];
                    for (int j = 0; j < verts.Length; j++)
                    {
                        wp[j] = arm.TransformPoint(ctr + verts[j]); var d = wp[j] - arm.position;
                        sc[j] = Vector3.Dot(d, outward) + 0.6f * Vector3.Dot(d, up); if (sc[j] > best) best = sc[j];
                    }
                    var sum = Vector3.zero; int cnt = 0; float band = 0.07f * hs;                       // the pad's top-outer face, not one rim vertex
                    for (int j = 0; j < verts.Length; j++) if (sc[j] >= best - band) { sum += wp[j]; cnt++; }
                    if (cnt > 0) pos = sum / cnt;
                    break;
                }
            var nrm = (outward + up * 0.45f).normalized;
            var go = new GameObject("OZ_VetChevron") { layer = v.gameObject.layer };
            go.AddComponent<MeshFilter>().sharedMesh = ChevMesh();
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = ChevMat(v.GetComponentInChildren<SkinnedMeshRenderer>()); mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var t = go.transform;
            t.SetPositionAndRotation(pos + nrm * 0.05f * hs, Quaternion.LookRotation(nrm, up));      // clear of the dome's curve
            t.localScale = Vector3.one * ChevronWidth * hs;
            t.SetParent(arm, true);
            return t;
        }

        /// <summary>Two stacked "^" bars (unit width, thin, flat shaded): the sergeant's chevrons.</summary>
        static Mesh ChevMesh()
        {
            if (chevMesh) return chevMesh;
            var ci = new List<CombineInstance>();
            var bar = UnitBox();
            float ang = Mathf.Atan2(0.28f, 0.5f) * Mathf.Rad2Deg, len = Mathf.Sqrt(0.5f * 0.5f + 0.28f * 0.28f) + 0.1f;
            for (int k = 0; k < 2; k++)
            {
                float y0 = 0.12f - k * 0.3f;
                for (int side = -1; side <= 1; side += 2)
                {
                    var ctr = new Vector3(side * 0.25f, y0, 0f);
                    var m = Matrix4x4.TRS(ctr, Quaternion.Euler(0f, 0f, -side * ang), new Vector3(len, 0.12f, 0.05f));
                    ci.Add(new CombineInstance { mesh = bar, transform = m });
                }
            }
            chevMesh = new Mesh { name = "OZ_VetChevron" };
            chevMesh.CombineMeshes(ci.ToArray(), true, true);
            chevMesh.RecalculateBounds();
            return chevMesh;
        }

        static Mesh UnitBox()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            void F(Vector3 nn, Vector3 a, Vector3 b)
            {
                var c = Vector3.Cross(a, b); int i = v.Count;
                v.Add((nn - a - b) * 0.5f); v.Add((nn + a - b) * 0.5f); v.Add((nn + a + b) * 0.5f); v.Add((nn - a + b) * 0.5f);
                for (int k = 0; k < 4; k++) n.Add(nn);
                if (Vector3.Dot(c, nn) > 0f) { t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2); }
                else { t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            }
            F(Vector3.right, Vector3.up, Vector3.forward); F(Vector3.left, Vector3.up, Vector3.forward);
            F(Vector3.up, Vector3.right, Vector3.forward); F(Vector3.down, Vector3.right, Vector3.forward);
            F(Vector3.forward, Vector3.right, Vector3.up); F(Vector3.back, Vector3.right, Vector3.up);
            var m = new Mesh(); m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); return m;
        }

        /// <summary>Gold in the game's own shader (a copy of the robot's OpenZ/TeamLit with team paint, vertex colour and maps off, so it
        /// is lit like the robot; URP Lit read almost white from its reflections), soft sheen, a faint warm glow for night / RTS zoom.</summary>
        static Material ChevMat(Renderer template)
        {
            if (chevMat) return chevMat;
            Material src = null;
            if (template) foreach (var m in template.sharedMaterials) if (m && m.shader && m.shader.name == "OpenZ/TeamLit") { src = m; break; }
            var gold = new Color(1f, 0.7f, 0.2f);
            if (src)
            {
                chevMat = new Material(src) { name = "OZ_VetChevron" };
                void F(string n, float x) { if (chevMat.HasProperty(n)) chevMat.SetFloat(n, x); }
                if (chevMat.HasProperty("_BaseMap")) chevMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                F("_TeamMode", 0f); F("_UseVertexColor", 0f); F("_Accent", 0f); F("_CreamToTeam", 0f); F("_UseNormalMap", 0f); F("_UseMaskMap", 0f);
                F("_Triplanar", 0f); F("_TopBlend", 0f); F("_Metallic", 0.5f); F("_Smoothness", 0.62f);
                chevMat.DisableKeyword("_NORMALMAP");
            }
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit"); if (!sh) return null;
                chevMat = new Material(sh) { name = "OZ_VetChevron" };
                if (chevMat.HasProperty("_Metallic")) chevMat.SetFloat("_Metallic", 0.35f);
                if (chevMat.HasProperty("_Smoothness")) chevMat.SetFloat("_Smoothness", 0.55f);
                chevMat.EnableKeyword("_EMISSION");
            }
            if (chevMat.HasProperty("_BaseColor")) chevMat.SetColor("_BaseColor", gold);
            if (chevMat.HasProperty("_EmissionColor")) chevMat.SetColor("_EmissionColor", new Color(0.32f, 0.2f, 0.04f));
            chevMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return chevMat;
        }

        sealed class Sweep { public int id; public float t0, h; }
        readonly List<Sweep> sweeps = new List<Sweep>();

        void Shimmer(ZObject o)
        {
            var list = new List<ZObject> { o };
            if (o.Kind == ZKind.Robot) foreach (var m in o.Minions) if (m != null && !m.Destroyed) list.Add(m);
            foreach (var u in list)
            {
                if (!game.Views.TryGetValue(u.Id, out var v) || !v || !v.gameObject.activeInHierarchy) continue;
                sweeps.Add(new Sweep { id = u.Id, t0 = Time.time, h = u.Kind == ZKind.Robot ? 3.4f : 2.8f });
                Shimmers++;
            }
            var p = Pos(o);
            AudioDirector.Fire("fx_vet_promote", p + Vector3.up * 1.5f, o.Id);
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled) fxp.AddLight(p + Vector3.up * 2f, new Color(1f, 0.8f, 0.35f), 14f, 6f, 0.9f, 1.6f, 0f, false);
        }

        void UpdateSweeps()
        {
            if (sweeps.Count == 0 || !game) return;
            var fxp = OpenZFxPlayer.I; bool fx = fxp && fxp.isActiveAndEnabled;
            for (int i = sweeps.Count - 1; i >= 0; i--)
            {
                var s = sweeps[i]; float k = (Time.time - s.t0) / 0.9f;
                if (k > 1f || !game.Views.TryGetValue(s.id, out var v) || !v) { sweeps.RemoveAt(i); continue; }
                if (!fx) continue;
                var c = v.transform.position; float y = Mathf.Lerp(0.1f, s.h, Mathf.SmoothStep(0f, 1f, k)), r = s.h * 0.2f;
                for (int j = 0; j < 4; j++)
                {   // the golden ring sweeping up the body
                    float a = Random.Range(0f, Mathf.PI * 2f); var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    fxp.EmitChannel("leg_spark", c + d * r + Vector3.up * y, d * 0.5f + Vector3.up * 0.9f, Random.Range(0.35f, 0.6f), Random.Range(0.08f, 0.14f), (j & 1) == 0 ? Gold : GoldHi, 1f);
                }
                if (Random.value < 0.5f)                                                              // sparkles drifting up
                    fxp.EmitChannel("m12_pop", c + Random.insideUnitSphere * 0.8f + Vector3.up * Random.Range(0.5f, s.h), Vector3.up * Random.Range(0.6f, 1.4f), Random.Range(0.4f, 0.8f), Random.Range(0.1f, 0.18f), GoldHi, 1f);
            }
        }

        Vector3 Pos(ZObject o) => game.Views.TryGetValue(o.Id, out var v) && v && v.gameObject.activeInHierarchy ? v.transform.position : game.CenterWorld(o);
    }
}
