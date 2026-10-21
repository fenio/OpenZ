using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Item 212, round 2: WHY the killed fort cannon reads dark against the fort's concrete debris, decided in ONE Play session by
    /// measurement (menu OpenZ/V1.1/KD2 Cannon Wreck Brightness A/B (Play); a level with an enemy fort, the camera on it at the
    /// standard zoom). The enemy fort falls (as the KD check: the cut to the loungers waits 12 s, the ending camera keeps 70 m).
    /// Every probe changes ONE thing on the cannon, renders one frame without the effects, measures the median display luminance of
    /// the cannon's visible points against the fort debris' (the KD check's measure, PASS band 0.55-1.10) and puts the thing back:
    /// <list type="bullet">
    /// <item>+1 s, the thrown cannon IN FLIGHT (its own mesh, nothing cut yet): as is; drawn with the debris' own chunk material and
    /// tint (the same mesh, light, normals, probes, shadows: only the albedo is the chunk's); the wreck paint on the chunk material
    /// (only the texture level differs from the chunk look); light probes off; reflection probes off; shadows off; paint blocks cleared.</item>
    /// <item>+10 s, the RESTING PIECES: as is; the debris look; the wreck paint on the debris material; the break faces as paint with the
    /// torn rims / ribs / rebar hidden; mesh normals flipped; light probes off; reflection probes off; shadows off; the scorch decals
    /// hidden; the mask map off (metallic 0, smoothness 0.16); the paint blocks cleared; the paint's Tone x2.</item>
    /// </list>
    /// The debris' own render state (material, shader, tint) is printed, and a verdict from the numbers. Test tool only: nothing is
    /// saved; every setting it changes is put back (the probes too, in the same frame).
    /// </summary>
    public static class OzKD2Probe
    {
        const string M = "OpenZ/V1.1/KD2 ";
        const float BandLo = 0.60f, BandHi = 1.00f;
        static readonly StringBuilder sb = new StringBuilder();
        static float t0, until; static int step;
        static float pid0, fz0, pz0;

        [MenuItem(M + "Cannon Wreck Brightness A/B (Play)")]
        static void Run()
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KD2] enter Play first (a level with an enemy fort), the camera on it"); return; }
            pid0 = OpenZVictory.PostImpactDelay; fz0 = OpenZEnding.FortZoom; pz0 = OpenZEnding.PushInZoom;
            OpenZVictory.PostImpactDelay = 12f; OpenZEnding.FortZoom = 70f; OpenZEnding.PushInZoom = 70f;
            sb.Length = 0; step = 0;
            int n = OpenZVictory.DebugFortFalls(true);
            if (n == 0) { Debug.LogWarning("[OpenZ KD2] no enemy fort standing"); PutBack(); return; }
            t0 = -1f; until = Time.realtimeSinceStartup + 120f;
            sb.AppendLine($"[OpenZ KD2] cannon wreck brightness A/B, {n} enemy fort part(s) destroyed; {OzFortGunWreckTone.State}; wreck look {(OzFortGunWreckLook.Enabled ? "on" : "OFF")}, Tone {OzFortGunWreckLook.Tone:0.00}, burn {OzFortGunWreckLook.Charred:0.00}");
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ KD2] the enemy fort falls; the A/B at +1 s (in flight) and +10 s (the resting pieces)");
        }

        static void PutBack() { OpenZVictory.PostImpactDelay = pid0; OpenZEnding.FortZoom = fz0; OpenZEnding.PushInZoom = pz0; }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; PutBack(); return; }
            if (EditorApplication.isPaused) return;
            if (t0 < 0f)
            {
                if (OpenZNukeMushroom.Current && OpenZNukeMushroom.Current.Age > 0f) t0 = Time.time - OpenZNukeMushroom.Current.Age;
                else if (Time.realtimeSinceStartup > until) { Debug.LogWarning("[OpenZ KD2] no mushroom appeared"); EditorApplication.update -= Tick; PutBack(); }
                return;
            }
            float since = Time.time - t0;
            if (step == 0 && since >= 1f) { step = 1; Flight(since); }
            if (step == 1 && since >= 10f) { step = 2; Rest(since); Finish(); }
        }

        // ------------------------------------------------------------------ the probes
        sealed class Saved
        {
            public Renderer r; public Material[] mats; public MaterialPropertyBlock[] blocks; public LightProbeUsage lp; public ReflectionProbeUsage rp; public bool shadows;
        }

        static List<Saved> Save(IEnumerable<Renderer> rs)
        {
            var list = new List<Saved>();
            foreach (var r in rs)
            {
                if (!r) continue;
                var s = new Saved { r = r, mats = r.sharedMaterials, blocks = new MaterialPropertyBlock[r.sharedMaterials.Length], lp = r.lightProbeUsage, rp = r.reflectionProbeUsage, shadows = r.receiveShadows };
                for (int i = 0; i < s.blocks.Length; i++) { s.blocks[i] = new MaterialPropertyBlock(); r.GetPropertyBlock(s.blocks[i], i); }
                list.Add(s);
            }
            return list;
        }

        static void Restore(List<Saved> saved)
        {
            foreach (var s in saved)
            {
                if (!s.r) continue;
                s.r.sharedMaterials = s.mats;
                for (int i = 0; i < s.blocks.Length; i++) s.r.SetPropertyBlock(s.blocks[i].isEmpty ? null : s.blocks[i], i);
                s.r.lightProbeUsage = s.lp; s.r.reflectionProbeUsage = s.rp; s.r.receiveShadows = s.shadows;
            }
        }

        static bool IsWreckSlot(Material m) => m && (m.name.EndsWith("_Wreck") || m.name.StartsWith("FortGun_BreakFace") || m.name == "FortGun_Rebar" || m.name.StartsWith("FortGunRam_"));

        static Material chunkMat;
        /// <summary>The debris' own look: the chunk library's outer material (flat-shaded concrete tile) with the level's tint.</summary>
        static Material ChunkMat()
        {
            if (chunkMat) return chunkMat;
            ConcreteChunkLib.Load();
            var src = ConcreteChunkLib.Outer ? ConcreteChunkLib.Outer : Resources.Load<Material>(ConcreteChunkLib.Root + "Materials/OZC_Outer");
            if (!src) return null;
            chunkMat = new Material(src) { name = "KD2_probe_chunk" };
            return chunkMat;
        }

        /// <summary>Probe: every wreck slot drawn exactly as a concrete chunk (its material, its tint block).</summary>
        static void AsChunk(List<Saved> saved)
        {
            var cm = ChunkMat(); if (!cm) return;
            var tint = ConcreteChunkLib.LevelOuter * 0.98f; tint.a = 1f;
            foreach (var s in saved)
            {
                var mats = (Material[])s.mats.Clone(); bool any = false;
                for (int i = 0; i < mats.Length; i++) if (IsWreckSlot(mats[i])) { mats[i] = cm; any = true; }
                if (!any) continue;
                s.r.sharedMaterials = mats;
                var b = new MaterialPropertyBlock(); b.SetColor("_BaseColor", tint);
                for (int i = 0; i < mats.Length; i++) if (mats[i] == cm) s.r.SetPropertyBlock(b, i);
            }
        }

        static readonly Dictionary<Texture, Material> paintOnChunk = new Dictionary<Texture, Material>();
        /// <summary>Probe: the wreck's own albedo texture on the chunk material (the paint block kept): only the texture level differs from the chunk look.</summary>
        static void PaintOnChunk(List<Saved> saved)
        {
            var cm = ChunkMat(); if (!cm) return;
            foreach (var s in saved)
            {
                var mats = (Material[])s.mats.Clone(); bool any = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || !mats[i].name.EndsWith("_Wreck")) continue;
                    var tex = mats[i].GetTexture("_BaseMap"); if (!tex) continue;
                    if (!paintOnChunk.TryGetValue(tex, out var pm) || !pm) { pm = new Material(cm) { name = "KD2_probe_paint_on_chunk" }; pm.SetTexture("_BaseMap", tex); paintOnChunk[tex] = pm; }
                    mats[i] = pm; any = true;
                }
                if (any) s.r.sharedMaterials = mats;
            }
        }

        static void BreakFacesAsPaint(List<Saved> saved, List<GameObject> dressing)
        {
            foreach (var s in saved)
            {
                var mats = (Material[])s.mats.Clone(); bool any = false;
                for (int i = 1; i < mats.Length; i++) if (mats[i] && mats[i].name.StartsWith("FortGun_BreakFace") && mats[0] && mats[0].name.EndsWith("_Wreck")) { mats[i] = mats[0]; any = true; }
                if (any) { s.r.sharedMaterials = mats; for (int i = 1; i < mats.Length; i++) if (mats[i] == mats[0]) s.r.SetPropertyBlock(s.blocks[0].isEmpty ? null : s.blocks[0], i); }
            }
            foreach (var d in dressing) if (d) d.SetActive(false);
        }

        static void FlipNormals(List<Saved> saved, bool back)
        {
            foreach (var s in saved)
            {
                if (!s.r || s.r.name.StartsWith("FortGunRam_") || !s.r.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = mf.sharedMesh; if (!m.name.Contains("_")) continue;                    // the generated pieces only, never a shared asset mesh
                var n = m.normals; if (n == null || n.Length == 0) continue;
                for (int i = 0; i < n.Length; i++) n[i] = -n[i];
                m.normals = n;
            }
        }

        static void MaskOff(List<Saved> saved, Dictionary<Material, Vector3> keep, bool back)
        {
            foreach (var s in saved)
                foreach (var m in s.mats)
                {
                    if (!m || !m.name.EndsWith("_Wreck") || !m.HasProperty("_UseMaskMap")) continue;
                    if (!back) { if (!keep.ContainsKey(m)) keep[m] = new Vector3(m.GetFloat("_UseMaskMap"), m.GetFloat("_Metallic"), m.GetFloat("_Smoothness")); m.SetFloat("_UseMaskMap", 0f); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.16f); }
                    else if (keep.TryGetValue(m, out var v)) { m.SetFloat("_UseMaskMap", v.x); m.SetFloat("_Metallic", v.y); m.SetFloat("_Smoothness", v.z); }
                }
        }

        static List<Renderer> Scorch()
        {
            var list = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && r.sharedMaterial && r.sharedMaterial.shader && r.sharedMaterial.shader.name == "OpenZ/ScorchDecal") list.Add(r);
            return list;
        }

        static string Row(string what, float r, float asIs) => $"  probe  {what}: ratio {r:0.00} (as is {asIs:0.00}, x{(asIs > 0 ? r / asIs : 0f):0.00})";

        static void Flight(float since)
        {
            var cam = Camera.main; var yaw = OzFortGunWreckLook.LastYaw;
            if (!cam || !yaw || !yaw.gameObject.activeInHierarchy) { sb.AppendLine("  info  +1 s: no thrown cannon in flight (the wreck look did not run?)"); return; }
            var roots = new[] { yaw };
            var rs = yaw.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList();
            var saved = Save(rs);
            float r0 = Brightness(cam, roots, out string info0);
            sb.AppendLine($"+{since:0.00} s, the thrown cannon IN FLIGHT: as is: {info0}");
            float asIs = r0;
            AsChunk(saved); float rc = Brightness(cam, roots, out _); Restore(saved); sb.AppendLine(Row("drawn as a concrete chunk (the chunk library's material + tint, same mesh and light)", rc, asIs));
            PaintOnChunk(saved); float rp = Brightness(cam, roots, out _); Restore(saved); sb.AppendLine(Row("the wreck paint texture on the chunk material (only the texture level differs)", rp, asIs));
            foreach (var s in saved) s.r.lightProbeUsage = LightProbeUsage.Off; float rl = Brightness(cam, roots, out _); Restore(saved); sb.AppendLine(Row("light probes off", rl, asIs));
            foreach (var s in saved) s.r.reflectionProbeUsage = ReflectionProbeUsage.Off; float rr = Brightness(cam, roots, out _); Restore(saved); sb.AppendLine(Row("reflection probes off", rr, asIs));
            foreach (var s in saved) s.r.receiveShadows = false; float rsh = Brightness(cam, roots, out _); Restore(saved); sb.AppendLine(Row("shadows not received", rsh, asIs));
            foreach (var s in saved) for (int i = 0; i < s.blocks.Length; i++) s.r.SetPropertyBlock(null, i); float rb = Brightness(cam, roots, out _); Restore(saved); sb.AppendLine(Row("paint blocks cleared (material values)", rb, asIs));
            float rAgain = Brightness(cam, roots, out _);
            sb.AppendLine($"  info  everything put back: as is again {rAgain:0.00}");
            flightAsIs = asIs; flightChunk = rc; flightPaintOnChunk = rp; flightMax = Mathf.Max(Mathf.Abs(rl - asIs), Mathf.Max(Mathf.Abs(rr - asIs), Mathf.Max(Mathf.Abs(rsh - asIs), Mathf.Abs(rb - asIs))));
        }

        static float flightAsIs, flightChunk, flightPaintOnChunk, flightMax, restAsIs, restChunk, restPaintOnChunk, restFaces, restFlip, restOther, restTone;

        static void Rest(float since)
        {
            var cam = Camera.main;
            var pieces = OzFortGunBreakup.LastPieces.Where(p => p).Select(p => p.transform).ToList();
            if (!cam || pieces.Count == 0) { sb.AppendLine("  info  +10 s: no cannon pieces (the cannon landed whole, or over water)"); return; }
            int rested = OzFortGunBreakup.LastPieces.Count(p => p && p.Rested);
            var rs = new List<Renderer>(); var dressing = new List<GameObject>();
            foreach (var p in pieces) { rs.AddRange(p.GetComponentsInChildren<Renderer>().Where(r => r.enabled)); foreach (var t in p.GetComponentsInChildren<Transform>(true)) if (t.name == "OZ_WreckDress") dressing.Add(t.gameObject); }
            var saved = Save(rs);
            float asIs = Brightness(cam, pieces, out string info0);
            sb.AppendLine($"+{since:0.00} s, the RESTING PIECES ({pieces.Count}, {rested} at rest): as is: {info0}");
            sb.AppendLine("  info  " + Debris());
            AsChunk(saved); float rc = Brightness(cam, pieces, out _); Restore(saved); sb.AppendLine(Row("drawn as concrete chunks (the chunk library's material + tint, same meshes and light)", rc, asIs));
            PaintOnChunk(saved); float rp = Brightness(cam, pieces, out _); Restore(saved); sb.AppendLine(Row("the wreck paint texture on the chunk material (only the texture level differs)", rp, asIs));
            BreakFacesAsPaint(saved, dressing); float rf = Brightness(cam, pieces, out _); Restore(saved); foreach (var d in dressing) if (d) d.SetActive(true); sb.AppendLine(Row("break faces drawn as paint, the torn rims / ribs / rebar hidden", rf, asIs));
            FlipNormals(saved, false); float rn = Brightness(cam, pieces, out _); FlipNormals(saved, true); sb.AppendLine(Row("mesh normals flipped (inverted normals would get BRIGHTER here)", rn, asIs));
            foreach (var s in saved) s.r.lightProbeUsage = LightProbeUsage.Off; float rl = Brightness(cam, pieces, out _); Restore(saved); sb.AppendLine(Row("light probes off", rl, asIs));
            foreach (var s in saved) s.r.reflectionProbeUsage = ReflectionProbeUsage.Off; float rr = Brightness(cam, pieces, out _); Restore(saved); sb.AppendLine(Row("reflection probes off", rr, asIs));
            foreach (var s in saved) s.r.receiveShadows = false; float rsh = Brightness(cam, pieces, out _); Restore(saved); sb.AppendLine(Row("shadows not received", rsh, asIs));
            var sc = Scorch(); foreach (var r in sc) r.enabled = false; float rd = Brightness(cam, pieces, out _); foreach (var r in sc) if (r) r.enabled = true; sb.AppendLine(Row($"scorch decals hidden ({sc.Count} marks)", rd, asIs));
            var keep = new Dictionary<Material, Vector3>(); MaskOff(saved, keep, false); float rm = Brightness(cam, pieces, out _); MaskOff(saved, keep, true); sb.AppendLine(Row("mask map off, metallic 0, smoothness 0.16 on the paint", rm, asIs));
            foreach (var s in saved) for (int i = 0; i < s.blocks.Length; i++) s.r.SetPropertyBlock(null, i); float rb = Brightness(cam, pieces, out _); Restore(saved); sb.AppendLine(Row("paint blocks cleared (material values, no stripes)", rb, asIs));
            OzFortGunWreckLook.ProbeTone(OzFortGunWreckLook.Tone * 2f); float rt = Brightness(cam, pieces, out _); OzFortGunWreckLook.ProbeTone(OzFortGunWreckLook.Tone); sb.AppendLine(Row("the paint's Tone x2 (the paint slots only)", rt, asIs));
            float rAgain = Brightness(cam, pieces, out _);
            sb.AppendLine($"  info  everything put back: as is again {rAgain:0.00}");
            restAsIs = asIs; restChunk = rc; restPaintOnChunk = rp; restFaces = rf; restFlip = rn; restTone = rt;
            restOther = Mathf.Max(Mathf.Abs(rl - asIs), Mathf.Max(Mathf.Abs(rr - asIs), Mathf.Max(Mathf.Abs(rsh - asIs), Mathf.Max(Mathf.Abs(rd - asIs), Mathf.Max(Mathf.Abs(rm - asIs), Mathf.Abs(rb - asIs))))));
        }

        /// <summary>The fort debris' own render state (the reference of the ratio): the first two pieces' materials and tints.</summary>
        static string Debris()
        {
            var s = new StringBuilder("the fort debris (the reference):");
            int n = 0; var mpb = new MaterialPropertyBlock();
            foreach (var p in Object.FindObjectsByType<BuildingBreaker.Piece>(FindObjectsSortMode.None))
            {
                var r = p ? p.GetComponentInChildren<MeshRenderer>() : null; if (!r || !r.enabled) continue;
                var m = r.sharedMaterial; r.GetPropertyBlock(mpb, 0);
                string tint = mpb.HasColor("_BaseColor") || mpb.HasVector("_BaseColor") ? $"block _BaseColor {V(mpb.GetVector("_BaseColor"))} (linear)" : "no block tint";
                string tex = mpb.HasTexture("_BaseMap") ? "block _BaseMap " + (mpb.GetTexture("_BaseMap") ? mpb.GetTexture("_BaseMap").name : "null") : m && m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") ? "material _BaseMap " + m.GetTexture("_BaseMap").name : "no base map";
                s.Append($" [{r.name}: {(m ? m.name : "none")} / {(m && m.shader ? m.shader.name : "-")}, {tint}, {tex}, light probes {r.lightProbeUsage}, shadows {r.receiveShadows}]");
                if (++n >= 2) break;
            }
            return n > 0 ? s.ToString() : "the fort debris: no piece renderer found";
        }

        static string V(Vector4 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###})";

        static void Finish()
        {
            EditorApplication.update -= Tick;
            sb.AppendLine("VERDICT (from the numbers above):");
            bool albedo = restChunk >= 0.75f && restPaintOnChunk <= Mathf.Max(0.55f, restChunk * 0.6f);
            sb.AppendLine($"  {(albedo ? "CAUSE" : "not shown")}: the albedo LEVEL of the wreck's surfaces. The same pieces drawn as concrete chunks read {restChunk:0.00}; with the wreck paint texture on the chunk material {restPaintOnChunk:0.00} (as is {restAsIs:0.00})"
                          + (albedo ? ": the mesh, light, normals, probes, shadows and blocks give the chunk brightness; only the texture levels bring it down" : ": read the probe rows"));
            sb.AppendLine($"  the break faces, rims, ribs and bars: as paint / hidden the pieces read {restFaces:0.00} (as is {restAsIs:0.00}): {(Mathf.Abs(restFaces - restAsIs) >= 0.08f ? "a real share of the darkness sits in those fixed surfaces (not moved by the paint's Tone)" : "little difference")}");
            sb.AppendLine($"  the paint's Tone x2 alone: {restTone:0.00}: {(restTone < BandLo ? "a paint multiplier alone cannot land the median in the band" : "would land it")}");
            sb.AppendLine($"  normals: flipped {restFlip:0.00} vs {restAsIs:0.00}: {(restFlip < restAsIs - 0.02f ? "the normals are right (flipping darkens)" : restFlip > restAsIs + 0.05f ? "INVERTED normals: report" : "no effect (double-sided lighting?)")}");
            sb.AppendLine($"  light probes, reflection probes, shadows, scorch decals, mask map, paint blocks: largest change {restOther:0.00} at rest, {flightMax:0.00} in flight: {(restOther < 0.10f ? "none of them is the cause" : "one of them matters: read its row")}");
            bool landed = restAsIs >= BandLo && restAsIs <= BandHi, landedF = flightAsIs >= BandLo && flightAsIs <= BandHi;
            sb.AppendLine($"  {(landed ? "PASS" : "FAIL")}  the cannon wreck at rest reads {restAsIs:0.00} of the fort debris (band {BandLo:0.00}-{BandHi:0.00})");
            sb.AppendLine($"  {(landedF ? "PASS" : "FAIL")}  the thrown cannon in flight reads {flightAsIs:0.00} of the fort debris (band {BandLo:0.00}-{BandHi:0.00})");
            if (!landed) sb.AppendLine(restAsIs < BandLo ? "  -> too dark still: raise OzFortGunWreckLook.Tone (x1.2 per 0.1) and, if the break faces read darker than the paint, lower OzFortGunWreckTone.FaceMetallic to 0.05; run again in a new session"
                                                        : "  -> too bright: lower OzFortGunWreckLook.Tone to 0.9 (or 0.8); run again in a new session");
            PutBack();
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ the measure (as the KD check: one frame without the effects)
        static float Brightness(Camera cam, IEnumerable<Transform> roots, out string info)
        {
            info = "";
            if (!cam) return 0f;
            var hidden = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.enabled && (r is ParticleSystemRenderer || r.GetComponentInParent<NukeFx>() || r.GetComponentInParent<OpenZNukeMushroom>())) { r.enabled = false; hidden.Add(r); }
            int W = Mathf.Max(320, cam.pixelWidth), H = Mathf.Max(200, cam.pixelHeight);   // item 212 round 3: the frame at the camera's own aspect (a 16:9 frame mapped with the view's aspect read the edge points beside the pieces)
            var rt = RenderTexture.GetTemporary(W, H, 24); var prevT = cam.targetTexture; var prevA = RenderTexture.active;
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            cam.targetTexture = prevT; RenderTexture.active = prevA; RenderTexture.ReleaseTemporary(rt);
            foreach (var r in hidden) if (r) r.enabled = true;
            var gun = new List<float>(); var deb = new List<float>();
            foreach (var t in roots) if (t) Points(cam, tex, W, H, t, gun);
            foreach (var p in Object.FindObjectsByType<BuildingBreaker.Piece>(FindObjectsSortMode.None)) if (p) Points(cam, tex, W, H, p.transform, deb);
            Object.Destroy(tex);
            float mg = Median(gun), md = Median(deb);
            bool few = gun.Count < 20 || deb.Count < 20;
            info = $"cannon median {mg:0.000} ({gun.Count} points), fort debris median {md:0.000} ({deb.Count} points), ratio {(md > 0 ? mg / md : 0f):0.00} (display luminance, linear; effects hidden)" + (few ? " (too few visible points: turn the camera onto them)" : "");
            return !few && md > 0 ? mg / md : 0f;
        }

        static void Points(Camera cam, Texture2D tex, int W, int H, Transform root, List<float> into)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var m = mf.sharedMesh; if (!m || !m.isReadable || mf.name == "OZ_WreckDress") continue;
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mr.enabled) continue;
                var v = m.vertices; var c = m.bounds.center; int stepV = Mathf.Max(1, v.Length / 160);
                for (int i = 0; i < v.Length; i += stepV)
                {
                    var w = mf.transform.TransformPoint(Vector3.Lerp(v[i], c, 0.08f));
                    var vp = cam.WorldToViewportPoint(w); if (vp.z <= 0f || vp.x < 0.01f || vp.x > 0.99f || vp.y < 0.01f || vp.y > 0.99f) continue;
                    var dir = w - cam.transform.position; float d = dir.magnitude;
                    if (Physics.Raycast(cam.transform.position, dir / d, out var hit, d + 1f) && !hit.transform.IsChildOf(root) && hit.distance < d - 0.25f) continue;
                    var px = tex.GetPixel(Mathf.Clamp((int)(vp.x * W), 0, W - 1), Mathf.Clamp((int)(vp.y * H), 0, H - 1)).linear;
                    into.Add(px.r * 0.2126f + px.g * 0.7152f + px.b * 0.0722f);
                }
            }
        }

        static float Median(List<float> a) { if (a.Count == 0) return 0f; var s = a.OrderBy(x => x).ToList(); return s[s.Count / 2]; }
    }
}
