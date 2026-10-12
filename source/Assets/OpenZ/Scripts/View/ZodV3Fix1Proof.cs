using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// <c></c>
    /// Over every bow window (briefing fist_pump 3.2-4.5 s and 12.1-13.1 s, nuke scheming 0-1.4 s, win_02 fist_pump 10.2-11.4 s,
    /// win_04 fist_pump 0.6-2.0 s, win_06 fist_pump 0-2.2 s) at 10 fps, at the REAL comms-window aspect: the feed frame (g_*), the same
    /// frame rendered again with the ember light off, and from the pair: ember-lit pixels on v3's gear (front-facing gear vertices
    /// splatted into a mask, the cigar's own capsule left out), ember-lit pixels anywhere else, hot-orange pixels on the gear; the
    /// cigar tip / cigar body / ember light to the nearest gear vertex (baked skin, cm); both fists' margin inside the window (% of its
    /// width); the chin-down shown. 1920x1080 feed frames at the old timestamps (f_*) + a gear mask for each (m_*), full-screen
    /// transmission-window screens (s_*), a 1080p window-aspect frame at the fist-pump peaks (w_*). win_04 a second time with the
    /// alternative clamp (variant B) to pick by eye; v1's briefing (the old orange chest blob at 4.0 s).
    /// </summary>
    public sealed class ZodV3Fix1Proof : MonoBehaviour
    {
        public static string Dir = global::OpenZ.OzRelease.Milestones("task_zod_v3_import/fix1");
        public static string Last = "";
        public static float BChin = 10f, BKeep = 0.06f;
        public static bool RunB = true, RunV1 = true;
        static ZodV3Fix1Proof run;
        public static bool Busy => run != null;

        static void Log(string s) { Directory.CreateDirectory(Dir); File.AppendAllText(Dir + "/proof_log.txt", System.DateTime.Now.ToString("HH:mm:ss.f") + " " + s + "\n"); Debug.Log("[ZodV3Fix1Proof] " + s); Last = s; }

        public static string Run()
        {
            if (run) return "busy";
            var go = new GameObject("ZodV3Fix1Proof"); run = go.AddComponent<ZodV3Fix1Proof>();
            run.StartCoroutine(run.Seq());
            return "started";
        }

        struct Win { public float a, b; public Win(float a, float b) { this.a = a; this.b = b; } }

        IEnumerator Seq()
        {
            float s0 = Time.unscaledTime;
            var g = ZGame.I;
            Log("task_zod_v3_import fix round 1 Play proof " + (g ? g.level : "?") + " " + Screen.width + "x" + Screen.height + ", setting " + ZodStage.ModelPrefState
                + ", ember bit " + ZodStage.EmberLightBit + ", ember out " + ZodStage.EmberOut + " m, range " + ZodStage.FaceLights.emberRange + " m");
            yield return Use(3);
            yield return Beat("v3_brief", "zod_brief_L02", ZodBriefing.Kind.Briefing, new[] { 1.5f, 4.0f }, new float[0], new float[0], new[] { new Win(3.2f, 4.5f), new Win(12.1f, 13.1f) }, false);
            yield return Beat("v3_nuke", "zod_nuke_01", ZodBriefing.Kind.Nuke, new[] { 1.0f, 2.5f }, new[] { 2.0f }, new float[0], new[] { new Win(0.0f, 1.4f) }, true);
            yield return Beat("v3_lose01", "zod_lose_01", ZodBriefing.Kind.Defeat, new[] { 0.4f, 1.2f, 4.6f }, new float[0], new float[0], new Win[0], false);
            yield return Beat("v3_win02", "zod_win_02", ZodBriefing.Kind.Victory, new[] { 1.0f, 2.4f, 4.0f }, new float[0], new[] { 10.9f }, new[] { new Win(10.2f, 11.4f) }, false);
            yield return Beat("v3_win04", "zod_win_04", ZodBriefing.Kind.Victory, new[] { 1.0f, 2.0f }, new[] { 1.2f, 1.5f }, new[] { 1.2f, 1.4f, 1.6f }, new[] { new Win(0.6f, 2.0f) }, false);
            yield return Beat("v3_win06", "zod_win_06", ZodBriefing.Kind.Victory, new[] { 1.0f }, new[] { 1.2f }, new[] { 0.9f, 1.2f, 1.5f }, new[] { new Win(-0.3f, 2.2f) }, false);
            if (RunB)
            {
                ZodStage.FaceRig rigV3;
                if (ZodStage.FaceRigs.TryGetValue("ZodCameo_v3", out rigV3) && rigV3.clipHeads != null && rigV3.clipHeads.Length > 0)
                {
                    var ch = rigV3.clipHeads[0]; float c0 = ch.chinDown, k0 = ch.softKeep;
                    ch.chinDown = BChin; ch.softKeep = BKeep;
                    Log("variant B: " + ch);
                    yield return Beat("v3B_win04", "zod_win_04", ZodBriefing.Kind.Victory, new float[0], new[] { 1.5f }, new[] { 1.2f, 1.4f, 1.6f }, new[] { new Win(0.6f, 2.0f) }, false);
                    ch.chinDown = c0; ch.softKeep = k0;
                    Log("variant A restored: " + ch);
                }
                else Log("variant B skipped: no clip heads on ZodCameo_v3");
            }
            if (RunV1)
            {
                yield return Use(1);
                yield return Beat("v1_brief", "zod_brief_L02", ZodBriefing.Kind.Briefing, new[] { 1.5f, 4.0f }, new float[0], new float[0], new[] { new Win(3.2f, 4.5f) }, false);
            }
            yield return Use(0);
            var tmp = ZodStage.Create(null, g ? g.level : "L02");
            Log("no override: " + ZodStage.ModelPrefState + " -> stage ModelVersion " + tmp.ModelVersion + " | " + tmp.FaceStatus());
            Destroy(tmp.gameObject);
            Log("DONE in " + (Time.unscaledTime - s0).ToString("F0") + " s");
            run = null; Destroy(gameObject);
        }

        IEnumerator Use(int m)
        {
            while (ZodBriefing.DebugLineId != null) yield return null;
            ZodStage.ForceModel = m;
            foreach (var st in FindObjectsByType<ZodStage>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Destroy(st.gameObject);
            yield return null; yield return null;
            yield return new WaitForSecondsRealtime(0.6f);
            Log("model override " + (m == 0 ? "off" : "v" + m));
        }

        // ------------------------------------------------------------------------------------------------ per stage parts
        ZodStage cur; Camera cam; Light ember; SkinnedMeshRenderer gear; Transform tip, cigarBody, lHand, rHand, head; Mesh baked;
        readonly List<Vector3> gv = new List<Vector3>(), gn = new List<Vector3>();
        string bakeSpace = "?"; bool[] isBrass;

        void Bind(ZodStage st)
        {
            if (cur == st) return;
            cur = st; cam = null; ember = null; gear = null; tip = cigarBody = lHand = rHand = head = null;
            foreach (var c in st.GetComponentsInChildren<Camera>(true)) { if (c.targetTexture == st.Feed) { cam = c; break; } if (!cam) cam = c; }
            foreach (var l in st.GetComponentsInChildren<Light>(true)) if (l.name == "Ember") ember = l;
            foreach (var r in st.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (r.name == ZodStage.GearSkinName) gear = r;
            var zod = st.transform.Find("Zod");                                                         // Zod's own bones (the boys have mixamorig hands too)
            foreach (var t in (zod ? zod : st.transform).GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "CigarTip") tip = t;
                else if (t.name == "Body" && t.parent && t.parent.name == "SideCigar") cigarBody = t;
                else if (t.name == "mixamorigLeftHand") lHand = t;
                else if (t.name == "mixamorigRightHand") rHand = t;
                else if (t.name == "mixamorigHead") head = t;
            }
            if (!baked) baked = new Mesh();
            isBrass = null;
            if (gear && gear.sharedMesh)
            {
                var sm = gear.sharedMesh; var gm = gear.sharedMaterials; isBrass = new bool[sm.vertexCount];
                for (int s = 0; s < sm.subMeshCount && s < gm.Length; s++) if (gm[s] && gm[s].name.Contains("Brass")) foreach (int i in sm.GetTriangles(s)) isBrass[i] = true;
            }
            string mats = "";
            if (gear) foreach (var m in gear.sharedMaterials) if (m) mats += m.name + " (smooth " + m.GetFloat("_Smoothness").ToString("0.##") + ", tint " + ((Color)m.GetColor("_BaseColor")).ToString("F2") + ") ";
            var ld = ember ? ember.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>() : null;
            var masks = new List<string>();
            foreach (var r in st.GetComponentsInChildren<Renderer>(true))
                if (r.transform.IsChildOf(st.transform.Find("Zod") ? st.transform.Find("Zod") : st.transform) && (r is SkinnedMeshRenderer || r.name == "Body" || r.name == "Ember" || r.name == "MouthInteriorShell"))
                    masks.Add(r.name + "=" + r.renderingLayerMask);
            Log("stage bound: model v" + st.ModelVersion + ", feed " + (st.Feed ? st.Feed.width + "x" + st.Feed.height : "none") + ", ember light " + (ember ? "mask " + ember.renderingLayerMask + " urp " + (ld ? ld.renderingLayers.ToString() : "-") + " range " + ember.range.ToString("0.###") : "MISSING")
                + ", gear renderer " + (gear ? gear.sharedMesh.vertexCount + " v, " + mats + ", lossy scale " + gear.transform.lossyScale.ToString("F3") : "none") + ", renderer masks [" + string.Join(", ", masks.ToArray()) + "]"
                + ", side cigar " + (cigarBody ? "yes" : "no") + " | " + st.FaceStatus());
            bakeSpace = "?";
        }

        /// <summary>World positions / normals of the gear skin this frame (the transform whose AABB matches the renderer's bounds).</summary>
        void BakeGear()
        {
            gv.Clear(); gn.Clear();
            if (!gear) return;
            gear.BakeMesh(baked, true);
            var v = baked.vertices; var n = baked.normals;
            var tr = gear.transform;
            Matrix4x4 mA = tr.localToWorldMatrix, mB = Matrix4x4.TRS(tr.position, tr.rotation, Vector3.one);
            if (bakeSpace == "?")
            {
                Bounds bA = new Bounds(mA.MultiplyPoint3x4(v[0]), Vector3.zero), bB = new Bounds(mB.MultiplyPoint3x4(v[0]), Vector3.zero);
                for (int i = 1; i < v.Length; i += 7) { bA.Encapsulate(mA.MultiplyPoint3x4(v[i])); bB.Encapsulate(mB.MultiplyPoint3x4(v[i])); }
                var rb = gear.bounds;
                float eA = (bA.center - rb.center).magnitude + (bA.size - rb.size).magnitude, eB = (bB.center - rb.center).magnitude + (bB.size - rb.size).magnitude;
                bakeSpace = eA <= eB ? "localToWorld" : "TRS1";
                Log("bake space " + bakeSpace + " (AABB error " + eA.ToString("F3") + " vs " + eB.ToString("F3") + " m; renderer bounds " + rb.center.ToString("F2") + " size " + rb.size.ToString("F2") + ")");
            }
            var M = bakeSpace == "localToWorld" ? mA : mB;
            for (int i = 0; i < v.Length; i++) { gv.Add(M.MultiplyPoint3x4(v[i])); gn.Add(M.MultiplyVector(n[i]).normalized); }
        }

        static float SegDist(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
            return (a + ab * t - p).magnitude;
        }

        // ------------------------------------------------------------------------------------------------ beats
        IEnumerator Beat(string tag, string id, ZodBriefing.Kind kind, float[] feedAt, float[] screenAt, float[] winAt, Win[] wins, bool press)
        {
            while (ZodBriefing.DebugLineId != null) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            ZodTalkRig.ResetChinStats();
            Log(tag + ": " + ZodBriefing.DebugLineAs(id, kind));
            ZodStage st = null; int fi = 0, si = 0, wi = 0, gi = 0; bool pA = false, pB = false, pC = false; float nextG = -99f; float tw = Time.unscaledTime;
            var clips = new List<string>(); string lastClip = null;
            int maxOnGear = 0, maxHot = 0, maxHotE = 0, maxElse = 0, samples = 0; float minTip = 99f, minSeg = 99f, minEmber = 99f, minFist = 99f, chinMax = 0f;
            string worst = "";
            while (Time.unscaledTime - tw < 60f)
            {
                yield return new WaitForEndOfFrame();
                if (ZodBriefing.DebugLineId != id) { if (Time.unscaledTime - tw > 1f) break; continue; }
                if (!st) st = FindFirstObjectByType<ZodStage>();
                if (!st) continue;
                Bind(st);
                float t = ZodBriefing.DebugLineT;
                if (st.CurrentClip != lastClip) { lastClip = st.CurrentClip; clips.Add(t.ToString("F1") + " " + lastClip); }
                if (fi < feedAt.Length && t >= feedAt[fi]) { Feed(st, "f_" + tag + "_" + feedAt[fi].ToString("F1") + "s", t, 1920, 1080, true); fi++; }
                if (si < screenAt.Length && t >= screenAt[si]) { ProofCapture.SaveJpg(Dir + "/s_" + tag + "_" + screenAt[si].ToString("F1") + "s.jpg", 0, 95); Log("screen s_" + tag + "_" + screenAt[si].ToString("F1") + "s at t " + t.ToString("F2") + " clip " + st.CurrentClip + " | " + st.ClipHeadStatus); si++; }
                if (wi < winAt.Length && t >= winAt[wi])
                {
                    int wh = 1080, ww = Mathf.RoundToInt(wh * (st.Feed ? st.Feed.width / (float)st.Feed.height : 4f / 3f));
                    Feed(st, "w_" + tag + "_" + winAt[wi].ToString("F1") + "s", t, ww, wh, false); wi++;
                }
                if (press && st.PressPlaced)
                {
                    float u = st.PressU;
                    if (!pA && u >= 0.10f) { pA = true; Feed(st, "f_" + tag + "_press", t, 1920, 1080, true); }
                    if (!pB && u >= ZodStage.SlamHit) { pB = true; Feed(st, "f_" + tag + "_contact", t, 1920, 1080, true); }
                    if (!pC && u >= 0.62f) { pC = true; Feed(st, "f_" + tag + "_after", t, 1920, 1080, true); }
                }
                bool inWin = false; foreach (var w in wins) if (t >= w.a && t <= w.b) inWin = true;
                if (!inWin || t < nextG) continue;
                nextG = t + 0.1f;
                // ---- the 10 fps sample at the real window aspect
                int H = 540, W = Mathf.RoundToInt(H * (st.Feed ? st.Feed.width / (float)st.Feed.height : 4f / 3f));
                var A = st.CaptureFeed(W, H);
                Texture2D B = null;
                if (ember) { bool was = ember.enabled; ember.enabled = false; B = st.CaptureFeed(W, H); ember.enabled = was; }
                Texture2D C = null;
                if (gear) { gear.forceRenderingOff = true; C = st.CaptureFeed(W, H); gear.forceRenderingOff = false; }
                if (!A) continue;
                string file = "g_" + tag + "_" + (gi++).ToString("D3") + "_" + t.ToString("F1") + "_" + st.CurrentClip + ".jpg";
                File.WriteAllBytes(Dir + "/" + file, A.EncodeToJPG(92));
                BakeGear();
                var m = Metrics(st, A, B, C, W, H);
                Destroy(A); if (B) Destroy(B); if (C) Destroy(C);
                float chin = ZodTalkRig.ChinDownOutPeak; ZodTalkRig.ResetChinStats();
                samples++;
                if (m.onGear > maxOnGear) { maxOnGear = m.onGear; worst = file; }
                maxHot = Mathf.Max(maxHot, m.hot); maxElse = Mathf.Max(maxElse, m.elsewhere);
                minTip = Mathf.Min(minTip, m.tipGap); minSeg = Mathf.Min(minSeg, m.segGap); minEmber = Mathf.Min(minEmber, m.emberGap); chinMax = Mathf.Max(chinMax, chin);
                if (st.CurrentClip == "fist_pump") minFist = Mathf.Min(minFist, m.fist);
                maxHotE = Mathf.Max(maxHotE, m.hotByEmber);
                Log(file + ": ember px on gear " + m.onGear + ", ember px elsewhere " + m.elsewhere + ", hot-orange px on gear " + m.hot + " (made by the ember " + m.hotByEmber + "; mask " + m.maskPx + " px)"
                    + ", cigar tip->gear " + Cm(m.tipGap) + ", cigar body clearance " + Cm(m.segGap) + ", ember light->gear " + Cm(m.emberGap)
                    + ", fists margin " + m.fistL.ToString("F1") + " / " + m.fistR.ToString("F1") + " % (top " + m.fistTop.ToString("F1") + " %), chin shown " + chin.ToString("F1") + " | " + st.ClipHeadStatus);
            }
            Log(tag + " done: clips [" + string.Join(", ", clips.ToArray()) + "], samples " + samples + ": max ember px on gear " + maxOnGear + (worst != "" ? " (" + worst + ")" : "") + ", max ember px elsewhere " + maxElse
                + ", max hot-orange px on gear " + maxHot + " (made by the ember " + maxHotE + "), min cigar tip->gear " + Cm(minTip) + ", min cigar body clearance " + Cm(minSeg) + ", min ember light->gear " + Cm(minEmber)
                + ", min fist margin in fist_pump " + (minFist < 98f ? minFist.ToString("F1") + " %" : "-") + ", max chin shown " + chinMax.ToString("F1"));
        }

        static string Cm(float m) => m > 90f ? "-" : (m * 100f).ToString("F1") + " cm";

        struct M { public int onGear, elsewhere, hot, hotByEmber, maskPx; public float tipGap, segGap, emberGap, fistL, fistR, fistTop, fist; }
        static bool Hot(Color32 c) => c.r >= 190 && c.b <= 0.45f * c.r && c.g >= 0.35f * c.r && c.g <= 0.72f * c.r;

        static int MaxDiff(Color32 a, Color32 b) => Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));

        M Metrics(ZodStage st, Texture2D A, Texture2D B, Texture2D C, int W, int H)
        {
            var r = new M { tipGap = 99f, segGap = 99f, emberGap = 99f, fistL = 99f, fistR = 99f, fistTop = 99f, fist = 99f };
            if (!cam) return r;
            float asp = cam.aspect; cam.aspect = W / (float)H;
            var mask = new bool[W * H]; var caps = new bool[W * H];
            Vector3 cp = cam.transform.position;
            for (int i = 0; i < gv.Count; i++)
            {
                if (Vector3.Dot(gn[i], cp - gv[i]) <= 0f) continue;
                var vp = cam.WorldToViewportPoint(gv[i]); if (vp.z <= 0f) continue;
                int x = Mathf.RoundToInt(vp.x * W), y = Mathf.RoundToInt(vp.y * H);
                for (int dy = -3; dy <= 3; dy++) for (int dx = -3; dx <= 3; dx++)
                {
                    int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                    mask[yy * W + xx] = true;
                }
            }
            // the cigar's own capsule (its body is lit by the ember on purpose) + bloom margin
            Vector3 a, b; float rad;
            if (cigarBody) { a = cigarBody.position - cigarBody.up * cigarBody.lossyScale.y; b = cigarBody.position + cigarBody.up * cigarBody.lossyScale.y; rad = cigarBody.lossyScale.x * 0.5f; }
            else if (tip) { a = tip.position - tip.forward * 0.11f; b = tip.position + tip.forward * 0.01f; rad = 0.011f; }
            else { a = b = Vector3.zero; rad = 0f; }
            if (rad > 0f)
            {
                Vector3 pa = cam.WorldToViewportPoint(a), pb = cam.WorldToViewportPoint(b + (b - a).normalized * 0.012f);
                Vector3 pr = cam.WorldToViewportPoint(b + cam.transform.right * (rad + 0.012f));
                float rpx = new Vector2((pr.x - cam.WorldToViewportPoint(b).x) * W, (pr.y - cam.WorldToViewportPoint(b).y) * H).magnitude + 3f;
                Vector2 A2 = new Vector2(pa.x * W, pa.y * H), B2 = new Vector2(pb.x * W, pb.y * H);
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A2.x, B2.x) - rpx)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(Mathf.Max(A2.x, B2.x) + rpx));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(A2.y, B2.y) - rpx)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(Mathf.Max(A2.y, B2.y) + rpx));
                Vector2 ab = B2 - A2;
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x, y); float tt = Mathf.Clamp01(Vector2.Dot(p - A2, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                    if ((A2 + ab * tt - p).magnitude <= rpx) caps[y * W + x] = true;
                }
            }
            var pa32 = A.GetPixels32(); var pb32 = B ? B.GetPixels32() : null; var pc32 = C ? C.GetPixels32() : null;
            for (int i = 0; i < pa32.Length; i++)
            {
                if (caps[i]) continue;
                var c = pa32[i];
                bool lit = false;
                if (pb32 != null) { var d = pb32[i]; int dm = Mathf.Max(Mathf.Abs(c.r - d.r), Mathf.Max(Mathf.Abs(c.g - d.g), Mathf.Abs(c.b - d.b))); lit = dm >= 8; }
                if (mask[i] && (pc32 == null || MaxDiff(c, pc32[i]) >= 6))                                   // the gear's footprint AND visible (differs without the gear)
                {
                    r.maskPx++;
                    if (lit) r.onGear++;
                    if (Hot(c)) { r.hot++; if (pb32 != null && !Hot(pb32[i])) r.hotByEmber++; }
                }
                else if (lit) r.elsewhere++;
            }
            // distances to the nearest gear vertex
            if (gv.Count > 0)
            {
                Vector3 emberP = ember ? ember.transform.position : Vector3.zero;
                for (int i = 0; i < gv.Count; i++)
                {
                    var v = gv[i];
                    if (tip) r.tipGap = Mathf.Min(r.tipGap, (v - tip.position).magnitude);
                    if (rad > 0f) r.segGap = Mathf.Min(r.segGap, SegDist(v, a, b) - rad);
                    if (ember) r.emberGap = Mathf.Min(r.emberGap, (v - emberP).magnitude);
                }
            }
            // the fists inside the window (the camera's real aspect = the feed's)
            cam.aspect = st.Feed ? st.Feed.width / (float)st.Feed.height : asp;
            foreach (var h in new[] { lHand, rHand })
            {
                if (!h) continue;
                Vector3 c = h.position + (h == lHand ? lHand : rHand).up * 0.06f;                          // the fist's centre past the wrist
                var vp = cam.WorldToViewportPoint(c); var ve = cam.WorldToViewportPoint(c + cam.transform.right * 0.08f);
                float rr = Mathf.Abs(ve.x - vp.x);
                float side = Mathf.Min(vp.x - rr, 1f - vp.x - rr) * 100f, top = (1f - vp.y - rr * cam.aspect) * 100f;
                if (h == lHand) r.fistL = side; else r.fistR = side;
                r.fistTop = Mathf.Min(r.fistTop, top);
            }
            r.fist = Mathf.Min(r.fistL, r.fistR);
            cam.aspect = asp;
            return r;
        }

        void Feed(ZodStage st, string name, float t, int w, int h, bool withMask)
        {
            var tex = st.CaptureFeed(w, h);
            if (!tex) return;
            File.WriteAllBytes(Dir + "/" + name + ".png", tex.EncodeToPNG());
            Color32[] pa = null, pc = null;
            if (withMask && gear) { pa = tex.GetPixels32(); gear.forceRenderingOff = true; var tc = st.CaptureFeed(w, h); gear.forceRenderingOff = false; if (tc) { pc = tc.GetPixels32(); Destroy(tc); } }
            Destroy(tex);
            if (withMask && gear && cam && pc != null)
            {
                BakeGear();
                float asp = cam.aspect; cam.aspect = w / (float)h;
                var mt = new Texture2D(w, h, TextureFormat.R8, false); var px = new byte[w * h]; Vector3 cp = cam.transform.position;
                for (int i = 0; i < gv.Count; i++)
                {
                    if (isBrass == null || i >= isBrass.Length || !isBrass[i]) continue;
                    if (Vector3.Dot(gn[i], cp - gv[i]) <= 0f) continue;
                    var vp = cam.WorldToViewportPoint(gv[i]); if (vp.z <= 0f) continue;
                    int x = Mathf.RoundToInt(vp.x * w), y = Mathf.RoundToInt(vp.y * h);
                    for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) { int xx = x + dx, yy = y + dy; if (xx >= 0 && yy >= 0 && xx < w && yy < h) px[yy * w + xx] = 255; }
                }
                for (int i = 0; i < px.Length; i++) if (px[i] != 0 && MaxDiff(pa[i], pc[i]) < 6) px[i] = 0;          // visible brass only
                cam.aspect = asp;
                mt.LoadRawTextureData(px); mt.Apply();
                File.WriteAllBytes(Dir + "/m_" + name.Substring(2) + ".png", mt.EncodeToPNG()); Destroy(mt);
            }
            Log(name + " t " + t.ToString("F2") + " clip " + st.CurrentClip + " | " + st.ClipHeadStatus);
        }
    }
}
