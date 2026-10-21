using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit KD checks (menu OpenZ/V1.1/KD ...), items 210-212: the fort blast's mushroom smoke keeps its motion blur, its
    /// ground dust is soft fog, the killed cannon is a toned-down burnt wreck with torn break faces.
    /// <list type="bullet">
    /// <item>Fort Blast (Play, a level with an enemy fort, the camera on it at the standard zoom): the enemy fort falls
    /// (OpenZVictory.DebugFortFalls); for this check only the cut to the loungers waits 12 s and the ending camera keeps 70 m. At the
    /// blast +1, +3, +6, +10 s it prints the mushroom's blur as the puff shader draws it (the global shutter, the materials' exposure,
    /// the velocities the renderer gets; with and without the churn), the fog (wall and skirt puffs, the base surge as fog, the most
    /// alive against the pool), the thrown cannon in flight (+1 / +3 s: its render state and its brightness), and at +10 s the cannon
    /// wreck (materials swapped, burn, pieces dressed, cuts within bounds, bars within bounds, the dressing's time), the BRIGHTNESS
    /// (one frame rendered without the effects: the cannon pieces against the fort's concrete debris, PASS 0.55-1.10) with the
    /// largest piece's render state, a Tone search when it is off, then the frame times against the BEFORE run.</item>
    /// <item>... (BEFORE: the old looks): the same with the new looks switched off (the old crown and exposure, the old dust puffs, the
    /// old charred cannon) for the before / after numbers; it stores its frame times for the next run. A fresh Play session for each.</item>
    /// <item>... + Pauses For Pictures: the editor pauses at +1, +3, +6 and +10 s (take the picture, un-pause).</item>
    /// </list>
    /// Test tools only: nothing is saved but the BEFORE frame times (editor preferences); every setting it changes is put back.
    /// </summary>
    public static class OzKDCheck
    {
        const string M = "OpenZ/V1.1/KD ";
        const string PrefMed = "OpenZ.KD.BeforeMedianMs", PrefP95 = "OpenZ.KD.BeforeP95Ms";
        static readonly float[] At = { 1f, 3f, 6f, 10f };
        static readonly float[] Tones = { 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.45f };
        static int step; static bool pics, before; static float t0, until;
        static readonly StringBuilder sb = new StringBuilder();
        static readonly List<float> frames = new List<float>();
        static bool passAll;
        // put back afterwards
        static float pid0, fz0, pz0, roll0, smear0, churn0; static bool fog0, surge0, wreck0;

        [MenuItem(M + "Fort Blast: Smoke Blur, Ground Fog, Cannon Wreck (Play)")]
        static void Run() => Start(false, false);

        [MenuItem(M + "Fort Blast + Pauses For Pictures (Play)")]
        static void RunPictures() => Start(true, false);

        [MenuItem(M + "Fort Blast, BEFORE: the old looks (Play)")]
        static void RunBefore() => Start(false, true);

        static void Start(bool pictures, bool old)
        {
            var g = ZGame.I;
            if (!Application.isPlaying || !g) { Debug.LogWarning("[OpenZ KD] enter Play first (a level with an enemy fort), the camera on it"); return; }
            pid0 = OpenZVictory.PostImpactDelay; fz0 = OpenZEnding.FortZoom; pz0 = OpenZEnding.PushInZoom;
            roll0 = OpenZNukeMushroom.CrownRollSeconds; smear0 = OpenZNukeMushroom.Smear; churn0 = OpenZNukeMushroom.ChurnShare;
            fog0 = OzBlastFog.Enabled; surge0 = OpenZNukeMushroom.BaseSurgeFog; wreck0 = OzFortGunWreckLook.Enabled;
            OpenZVictory.PostImpactDelay = 12f; OpenZEnding.FortZoom = 70f; OpenZEnding.PushInZoom = 70f;
            if (old)
            {
                OpenZNukeMushroom.CrownRollSeconds = 0f; OpenZNukeMushroom.Smear = OzFxSmooth.SmearBigPuff; OpenZNukeMushroom.ChurnShare = 0f;
                OzBlastFog.Enabled = false; OpenZNukeMushroom.BaseSurgeFog = false; OzFortGunWreckLook.Enabled = false;
            }
            pics = pictures; before = old; step = 0; passAll = true; frames.Clear(); sb.Length = 0;
            OzBlastFog.WallPuffs = 0; OzBlastFog.SkirtPuffs = 0; OzBlastFog.PeakAlive = 0; OzBlastFog.WallHeld = 0;
            OzFortGunWreckLook.KilledCount = 0; OzFortGunWreckLook.MaterialsSwapped = 0; OzFortGunWreckLook.DressedPieces = 0; OzFortGunWreckLook.DressedCuts = 0;
            OzFortGunWreckLook.RebarCount = 0; OzFortGunWreckLook.DressTris = 0; OzFortGunWreckLook.CapFans = 0; OzFortGunWreckLook.SkippedCuts = 0;
            int n = OpenZVictory.DebugFortFalls(true);
            if (n == 0) { Debug.LogWarning("[OpenZ KD] no enemy fort standing"); PutBack(); return; }
            t0 = -1f; until = Time.realtimeSinceStartup + 120f;
            float shutter = Shader.GetGlobalVector("_OZFxSmear").x;
            sb.AppendLine($"[OpenZ KD] fort blast check ({(old ? "BEFORE: the old looks" : "the new looks")}), {n} enemy fort part(s) destroyed; MOTION BLUR option {(OzFxSmooth.On ? "ON" : "OFF")}, the shader's global shutter {shutter:0.000} s");
            if (!old) Line("the MOTION BLUR option is on and the effects' shutter runs (global _OZFxSmear.x > 0; it was never switched off)", OzFxSmooth.On && shutter > 0f, $"shutter {shutter:0.000} s");
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ KD] the enemy fort falls; numbers at +1, +3, +6, +10 s" + (pictures ? "; the editor pauses at each for the picture" : ""));
        }

        static void Line(string what, bool ok, string info = "")
        {
            if (!ok) passAll = false;
            sb.AppendLine($"  {(ok ? "PASS" : "FAIL")}  {what}{(info.Length > 0 ? ": " + info : "")}");
        }

        static void Info(string what) => sb.AppendLine("  info  " + what);

        static void PutBack()
        {
            OpenZVictory.PostImpactDelay = pid0; OpenZEnding.FortZoom = fz0; OpenZEnding.PushInZoom = pz0;
            OpenZNukeMushroom.CrownRollSeconds = roll0; OpenZNukeMushroom.Smear = smear0; OpenZNukeMushroom.ChurnShare = churn0;
            OzBlastFog.Enabled = fog0; OpenZNukeMushroom.BaseSurgeFog = surge0; OzFortGunWreckLook.Enabled = wreck0;
        }

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; PutBack(); return; }
            if (EditorApplication.isPaused) return;
            if (t0 < 0f)
            {
                if (OpenZNukeMushroom.Current && OpenZNukeMushroom.Current.Age > 0f) t0 = Time.time - OpenZNukeMushroom.Current.Age;
                else if (Time.realtimeSinceStartup > until) { Debug.LogWarning("[OpenZ KD] no mushroom appeared"); EditorApplication.update -= Tick; PutBack(); }
                return;
            }
            frames.Add(Time.unscaledDeltaTime * 1000f);
            float since = Time.time - t0;
            if (step < At.Length && since >= At[step])
            {
                Sample(At[step], since);
                step++;
                if (pics) { EditorApplication.isPaused = true; Debug.Log($"[OpenZ KD] paused at blast +{since:0.00} s: take the picture (RTS camera, standard zoom, full screen), then un-pause"); }
                if (step == At.Length) Finish();
            }
        }

        static void Sample(float at, float since)
        {
            var cam = Camera.main;
            var mu = OpenZNukeMushroom.Current;
            if (mu)
            {
                mu.KdBlurStats(0.10f, cam, out int n, out int bl, out float mean, out int rollBl);
                float share = n > 0 ? bl / (float)n : 0f, rollShare = n > 0 ? rollBl / (float)n : 0f;
                string info = $"{bl} of {n} cap puffs ({share * 100f:0} %) drawn blurred, {rollBl} ({rollShare * 100f:0} %) by the roll alone (without the churn), mean blur {mean * 100f:0.0} % of a puff, puff speed mean {mu.PuffSpeedMean:0.00} / most {mu.PuffSpeedMax:0.00} m/s; base surge as fog {mu.FogLive}";
                if (at <= 1f && !before)
                {
                    mu.KdSmear(out float fs, out float bs);
                    Line("the mushroom's front and back puffs use the new exposure (_Smear = OpenZNukeMushroom.Smear)", Mathf.Abs(fs - OpenZNukeMushroom.Smear) < 1e-3f && Mathf.Abs(bs - OpenZNukeMushroom.Smear) < 1e-3f,
                         $"front {fs:0.00}, back {bs:0.00}, Smear {OpenZNukeMushroom.Smear:0.00} (x the shutter = {OpenZNukeMushroom.Smear * OzFxSmooth.SmearShutter:0.00} s)");
                }
                if (before || at < 3f) Info($"+{at:0} s ({since:0.00}) mushroom blur: {info}");
                else Line($"+{at:0} s ({since:0.00}) the mushroom's cap is blurred along its motion (at least 60 % of its puffs by 10 % of their size or more, at least 45 % by the roll alone)", share >= 0.6f && rollShare >= 0.45f, info);
            }
            else Info($"+{at:0} s: no mushroom");
            Info($"+{at:0} s {OzBlastFog.Status(cam)}");
            var yaw = OzFortGunWreckLook.LastYaw;
            if (at <= 3f && yaw && yaw.gameObject.activeInHierarchy)
            {   // the thrown cannon in flight (before it lands and breaks): what it is drawn with, and how bright it reads
                if (at <= 1f) foreach (var r in yaw.GetComponentsInChildren<MeshRenderer>()) if (r.enabled) Info($"+{at:0} s thrown cannon in flight, render state: {State(r, yaw)}");
                Brightness(cam, $"+{at:0} s the thrown cannon IN FLIGHT", new[] { yaw }, false);
            }
            if (at >= 10f && !before)
            {
                Line("the fort's dust is fog: the fog shader is there and the dust wall and the skirt were drawn as fog", OzBlastFog.LastShader == "OpenZ/BlastFog" && OzBlastFog.WallPuffs > 0 && OzBlastFog.SkirtPuffs >= 10,
                     $"shader {OzBlastFog.LastShader}, wall puffs {OzBlastFog.WallPuffs}, skirt puffs {OzBlastFog.SkirtPuffs}");
                Line("the fog never filled its pool (no puff dropped)", OzBlastFog.PeakAlive < OzBlastFog.Capacity,
                     $"most alive at once {OzBlastFog.PeakAlive} of {OzBlastFog.Capacity}; wall puffs held back to keep the skirt's room {OzBlastFog.WallHeld}");
                Line("the killed cannon wears the wreck paint (swapped at its toss) and the toned-down burn", OzFortGunWreckLook.KilledCount > 0 && OzFortGunWreckLook.MaterialsSwapped > 0, OzFortGunWreckLook.LastState);
                if (OzFortGunBreakup.Breaks > 0)
                {
                    int pcs = OzFortGunWreckLook.DressedPieces, cuts = OzFortGunWreckLook.DressedCuts, bars = OzFortGunWreckLook.RebarCount;
                    Line("the pieces' cut faces are torn steel with rims, ribs and rebar, one dressing per real cut (not per cap fan)",
                         pcs >= 8 && cuts >= 5 && bars >= 10 && cuts <= 4 * pcs && bars <= OzFortGunWreckLook.RebarMax * cuts,
                         $"{pcs} pieces, {cuts} cuts (<= 4 a piece; from {OzFortGunWreckLook.CapFans} cap fans, {OzFortGunWreckLook.SkippedCuts} slivers left plain), {bars} bars (<= {OzFortGunWreckLook.RebarMax} a cut), {OzFortGunWreckLook.DressTris} triangles, {OzFortGunWreckLook.LastDressMs:0.0} ms on the landing frame (tell if over 4 ms)");
                }
            }
            if (at >= 10f)
            {
                var pieces = PieceRoots();
                var big = OzFortGunBreakup.LastPieces.Where(p => p).OrderByDescending(p => p.MassKg).FirstOrDefault();
                if (big) { var vis = big.transform.Find(big.PieceName); if (vis && vis.TryGetComponent<MeshRenderer>(out var br)) Info($"+10 s the largest piece ({big.PieceName}, {big.MassKg:0} kg) render state: {State(br, big.transform)}"); }
                float ratio = Brightness(cam, before ? "+10 s the old cannon pieces" : null, pieces, false);
                if (!before)
                {   // item 212 round 3: the pieces' OWN pixels against the concrete chunks beside them (the same light and vignette), OzKD3Probe.cs
                    var own = OzKD3Probe.MeasureNow(cam, pieces, false);
                    Line("the resting pieces sit in the brightness range of the concrete chunks beside them (ratio 0.60-1.00, their own pixels)", own.Ratio >= OzKD3Probe.BandLo && own.Ratio <= OzKD3Probe.BandHi,
                         own.Info + $"; the old point sampling read {ratio:0.00} of all debris; {OzFortGunRestLook.State}");
                    if (own.Ratio > 0f && (own.Ratio < OzKD3Probe.BandLo || own.Ratio > OzKD3Probe.BandHi)) Info("the resting pieces are off the band: run OpenZ/V1.1/KD3 Cannon Rest Darkness A/B (Play) in a new session; its last lines name the cause and the rest look's numbers to set");
                }
            }
        }

        static List<Transform> PieceRoots() => OzFortGunBreakup.LastPieces.Where(p => p).Select(p => p.transform).ToList();

        /// <summary>The wreck is off the debris' range: find the number that brings it in (measured, one frame each), and say which.</summary>
        static void Probes(Camera cam, List<Transform> pieces, float ratio)
        {
            var rows = new List<string>(); float bestTone = -1f, bestR = 0f;
            foreach (var t in Tones)
            {
                if (OzFortGunWreckLook.ProbeTone(t) == 0) { Info("Tone search: the pieces wear no wreck material (see the render state above): the wreck look did not reach them"); break; }
                float r = Brightness(cam, null, pieces, false);
                rows.Add($"{t:0.00} -> {r:0.00}");
                if (r >= 0.55f && r <= 1.10f && (bestTone < 0f || Mathf.Abs(r - 0.8f) < Mathf.Abs(bestR - 0.8f))) { bestTone = t; bestR = r; }
            }
            OzFortGunWreckLook.ProbeTone(OzFortGunWreckLook.Tone);
            Info($"Tone search on the resting pieces (Tone -> ratio): {string.Join(", ", rows)}: " + (bestTone > 0f
                ? $"set OzFortGunWreckLook.Tone = {bestTone:0.00}f (ratio {bestR:0.00}) and run again in a new session"
                : "no Tone lands in 0.55-1.10: the paint values on the pieces are not the cause, see the probes below"));
            if (bestTone > 0f || ratio > 1.10f) return;
            OzFortGunWreckLook.Probe(1); float r1 = Brightness(cam, null, pieces, false);
            OzFortGunWreckLook.Probe(2); float r2 = Brightness(cam, null, pieces, false);
            Info($"darkness probe: as is {ratio:0.00}, each piece repainted by its own TeamPaint (keeps the team stripes) {r1:0.00}, the standard lit shader (DROPS the team stripes; a probe only) {r2:0.00}: " +
                 (r1 >= 0.55f ? "set OzFortGunWreckLook.RepaintPieces = true" : r2 >= 0.55f ? "the TeamLit shader path darkens it (the standard shader does not): read the render state lines and report; do not ship StandardLitPieces" : "neither: the darkness is not in the pieces' paint or shader (the light: see the sun line of the render state)") +
                 " (the probes leave the pieces so for this session: restart Play for pictures)");
        }

        /// <summary>A renderer as drawn: layer, rendering layers, probes, shadows, every slot's material / shader and the paint values
        /// its property blocks hand the shader (raw, linear), and whether the sun reaches it.</summary>
        static string State(Renderer r, Transform own)
        {
            var s = new StringBuilder();
            s.Append($"{r.name}: layer {LayerMask.LayerToName(r.gameObject.layer)} ({r.gameObject.layer}), rendering layers {r.renderingLayerMask}, light probes {r.lightProbeUsage}, receives shadows {r.receiveShadows}, casts {r.shadowCastingMode}");
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb); s.Append("; renderer-wide block: " + (mpb.isEmpty ? "none" : Block(mpb)));
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                r.GetPropertyBlock(mpb, i);
                string mc = m && m.HasProperty("_BaseColor") ? V(m.GetColor("_BaseColor")) : "-";
                s.Append($"\n        slot {i}: {(m ? m.name : "none")} / {(m && m.shader ? m.shader.name : "-")}, material _BaseColor (sRGB) {mc}, block (as the shader reads it): {(mpb.isEmpty ? "none" : Block(mpb))}");
            }
            s.Append("\n        " + Sun(r, own));
            return s.ToString();
        }

        static readonly string[] Paint = { "_BaseColor", "_TeamColor", "_OZFortPaint", "_OZTeamVivid", "_AccentLo", "_AccentHi" };

        static string Block(MaterialPropertyBlock b)
        {
            var parts = new List<string>();
            foreach (var p in Paint)
            {
                if (p.StartsWith("_Accent")) { if (b.HasFloat(p)) parts.Add($"{p} {b.GetFloat(p):0.###}"); }
                else if (b.HasVector(p) || b.HasColor(p)) parts.Add($"{p} {V(b.GetVector(p))}");
            }
            if (b.HasTexture("_BaseMap")) { var t = b.GetTexture("_BaseMap"); parts.Add("_BaseMap override " + (t ? t.name : "null")); }
            return parts.Count > 0 ? string.Join(", ", parts) : "(no paint values)";
        }

        static string V(Vector4 v) => $"({v.x:0.###}, {v.y:0.###}, {v.z:0.###}, {v.w:0.###})";

        /// <summary>The main light and whether it reaches the renderer: its culling mask and rendering layers against the renderer's, and a
        /// ray from the top of the renderer toward the sun (the renderer's own colliders ignored).</summary>
        static string Sun(Renderer r, Transform own)
        {
            var sun = RenderSettings.sun;
            if (!sun) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled && (!sun || l.intensity > sun.intensity)) sun = l;
            if (!sun) return "sun: no directional light found";
            int layer = r.gameObject.layer;
            bool culled = (sun.cullingMask & (1 << layer)) == 0;
            bool layers = (sun.renderingLayerMask & (int)r.renderingLayerMask) != 0;
            var toSun = -sun.transform.forward; var b = r.bounds;
            var from = b.center + Vector3.up * b.extents.y + toSun * 0.05f;
            string shade = "in the sun";
            foreach (var h in Physics.RaycastAll(from, toSun, 400f).OrderBy(h => h.distance))
            {
                if (own && h.transform.IsChildOf(own)) continue;
                shade = $"SHADOWED by {h.transform.name} at {h.distance:0.0} m"; break;
            }
            return $"sun {sun.name}: intensity {sun.intensity:0.00}, colour {V(sun.color)}, shadows {sun.shadows}, culling mask {(culled ? "EXCLUDES" : "includes")} this layer, light rendering layers {(layers ? "match" : "DO NOT match")} ({sun.renderingLayerMask} / {r.renderingLayerMask}); toward the sun: {shade}";
        }

        /// <summary>One frame without the effects (particles, the mushroom meshes): the median display luminance (linear) of the given
        /// roots' visible points against the fort's concrete debris. asLine = the PASS line; label = an info line; null = quiet (probes).</summary>
        static float Brightness(Camera cam, string label, IEnumerable<Transform> roots, bool asLine)
        {
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
            string info = $"cannon median {mg:0.000} ({gun.Count} points), fort concrete debris median {md:0.000} ({deb.Count} points), ratio {(md > 0 ? mg / md : 0f):0.00} (display luminance, linear; effects hidden for the frame)";
            if (asLine && !few)
                Line("the cannon wreck sits in the fort debris' brightness range (ratio 0.55-1.10)", md > 0 && mg / md >= 0.55f && mg / md <= 1.10f,
                     info + $"; Charred {OzFortGunWreckLook.Charred:0.00}, Tone {OzFortGunWreckLook.Tone:0.00} (model: about 0.78 for paint B, 0.93 for paint A)");
            else if (asLine || !string.IsNullOrEmpty(label))
                Info("brightness" + (string.IsNullOrEmpty(label) ? "" : ", " + label) + ": " + info + (few ? " (too few visible points for a verdict: turn the camera onto them and run again)" : ""));
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
                    if (Physics.Raycast(cam.transform.position, dir / d, out var hit, d + 1f) && !hit.transform.IsChildOf(root) && hit.distance < d - 0.25f) continue;   // hidden behind something
                    var px = tex.GetPixel(Mathf.Clamp((int)(vp.x * W), 0, W - 1), Mathf.Clamp((int)(vp.y * H), 0, H - 1)).linear;
                    into.Add(px.r * 0.2126f + px.g * 0.7152f + px.b * 0.0722f);
                }
            }
        }

        static float Median(List<float> a) { if (a.Count == 0) return 0f; var s = a.OrderBy(x => x).ToList(); return s[s.Count / 2]; }

        static void Finish()
        {
            EditorApplication.update -= Tick;
            var s = frames.OrderBy(x => x).ToList();
            if (s.Count > 0)
            {
                float med = s[s.Count / 2], p95 = s[Mathf.Min(s.Count - 1, (int)(s.Count * 0.95f))];
                Info($"frames from the blast to +10 s: {s.Count}, median {med:0.00} ms, 95th share {p95:0.00} ms, longest {s[s.Count - 1]:0.00} ms");
                if (before) { EditorPrefs.SetFloat(PrefMed, med); EditorPrefs.SetFloat(PrefP95, p95); Info("stored as the BEFORE frame times for the next run"); }
                else if (pics) Info("(the pictures run pauses: its frame times are not compared)");
                else
                {
                    float b95 = EditorPrefs.GetFloat(PrefP95, -1f), bmed = EditorPrefs.GetFloat(PrefMed, -1f);
                    if (b95 > 0f) Line("the frame cost stays near today's (95th share <= the BEFORE run's x 1.15)", p95 <= b95 * 1.15f, $"95th {p95:0.00} ms against BEFORE {b95:0.00} ms (x{p95 / b95:0.00}), median {med:0.00} against {bmed:0.00} ms; if over: OzBlastFog.SkirtPerRing 6, SkirtSizeMax 14 and WallEvery 3 (out/fog_fill.txt), run again");
                    else Line("the frame cost stays near today's", false, "no BEFORE run stored in this editor: run the BEFORE menu first, in its own Play session");
                }
            }
            sb.AppendLine(before ? "  (BEFORE run: info only)" : passAll ? "  RESULT: PASS" : "  RESULT: FAIL (see the lines above)");
            PutBack();
            Debug.Log(sb.ToString());
        }
    }
}
