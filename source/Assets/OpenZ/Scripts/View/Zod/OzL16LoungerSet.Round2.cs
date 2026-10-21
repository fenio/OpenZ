// OpenZ (GPL-3.0) - Level 16: what the lounger set adds to its two ending scenes and to the level's animals while a scene plays.
//  - Objects of the level that stand in an ending's lens (the data file's endWin / endLose "hide") are not drawn while that scene
//    plays: view only, they are drawn again when the next scene begins or the level is left.
//  - The burning fort's flame columns stand "fireScale" times as high for the scene's far shot (OzFortInferno.ScaleColumns).
//  - The campfire of the endings (the data file's block "campfire"): a copy of one of the level's campfires in front of the foot
//    ends. By the level's own fire rule it burns from dusk to dawn and is out by day (logs and ash). While it burns: the level's
//    flame tongues and smoke, a crackle loop, and ONE warm flickering light of the scene's own (not in the night-light budget, on
//    only while an ending plays and under its end screen) that lights the two robots.
//  - While the briefing or an ending plays, the level's small flowers that stand in the set (one joined mesh for the whole level:
//    pink shapes between the loungers and by the side table) are not drawn within FlowerClear m of it; in normal play, when the
//    set is not drawn, they are.
//  - While the briefing or an ending plays, no animal of the level is drawn at the set or in the scene's picture
//    (JungleFauna.KeepOut): view only, the animals go on by their own rules.
// Play check: return OpenZ.OzL16LoungerSet.ExtrasStatus();
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16LoungerSet
    {
        /// <summary>Tunables (Play): the campfire light's strength and reach as factors of the data file's, the crackle's gain.</summary>
        public static float FireLightGain = 1f, FireRangeGain = 1f, FireLoopGain = 0.5f;
        /// <summary>Checks: the campfire burns, its light's strength now, objects of the level not drawn for the scene, the fort's flame columns scaled.</summary>
        public static bool FireBurning; public static float FireLightNow; public static int HiddenForScene; public static float FortFireScale = 1f;

        readonly List<Renderer> stagedOff = new List<Renderer>();
        GameObject fireGo, fireOut, fireLit; Light fireLight; AudioSource fireLoop; L16Fires.KindRow fireKind; DayNight dayClock;
        Vector3 fireFlameOff = new Vector3(0f, 0.25f, 0f), fireSmokeOff = new Vector3(0f, 1.2f, 0f), fireLightLocal;
        float fireLevel, fireNextSmoke, fireScaleWant = 1f; bool fireTried, fireScaled, animalsLogged; const float FireSeed = 0.37f;
        /// <summary>The level's joined flower mesh, and how near the set its flowers are not drawn while a scene plays (m).</summary>
        public const string FlowerMesh = "PLT_Flowers_placeholder"; public static float FlowerClear = 7f;
        /// <summary>Checks: triangles of the flower mesh not drawn while a scene plays.</summary>
        public static int FlowersCut;
        MeshFilter flowerMf; Mesh flowerFull, flowerNear; bool flowerTried;

        void BuildFlowers()
        {
            flowerTried = true;
            var go = GameObject.Find(FlowerMesh); var mf = go ? go.GetComponent<MeshFilter>() : null; var m = mf ? mf.sharedMesh : null;
            if (!m || !m.isReadable || m.subMeshCount != 1) return;
            var v = m.vertices; var t = m.triangles; var tr = go.transform; Vector3 c = root.position; float r2 = FlowerClear * FlowerClear;
            var near = new bool[v.Length];
            for (int i = 0; i < v.Length; i++) { var d = tr.TransformPoint(v[i]) - c; d.y = 0f; near[i] = d.sqrMagnitude < r2; }
            var keep = new List<int>(t.Length); int cut = 0;
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                if (near[t[i]] || near[t[i + 1]] || near[t[i + 2]]) { cut++; continue; }
                keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
            }
            if (cut == 0 || keep.Count == 0) return;
            flowerNear = Instantiate(m); flowerNear.name = m.name + " (scene)"; flowerNear.hideFlags = HideFlags.DontSave;
            flowerNear.SetTriangles(keep, 0);
            flowerMf = mf; flowerFull = m; FlowersCut = cut;
            Debug.Log($"[OpenZ L16 loungers] while a scene plays {cut} flower triangles within {FlowerClear:F0} m of the set are not drawn");
        }

        void FlowersForScene(bool sceneOn)
        {
            if (sceneOn && !flowerTried) BuildFlowers();
            if (!flowerMf || !flowerNear) return;
            var want = sceneOn ? flowerNear : flowerFull;
            if (flowerMf.sharedMesh != want) flowerMf.sharedMesh = want;
        }

        void SceneExtrasBegin(bool won, OzLoungerEndStage es)
        {
            RestoreHidden(); animalsLogged = false;
            if (es != null && es.hide != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var n in es.hide)
                {
                    if (string.IsNullOrEmpty(n)) continue;
                    var go = GameObject.Find(n);
                    if (!go) { Warn("object " + n + " of the ending's hide list is not in the level"); continue; }
                    int k = 0;
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; stagedOff.Add(r); k++; }
                    sb.Append(sb.Length > 0 ? ", " : "").Append(n).Append(" (").Append(k).Append(" parts)");
                }
                if (stagedOff.Count > 0) Debug.Log($"[OpenZ L16 loungers] ending {(won ? "win" : "lose")}: not drawn while the scene plays: {sb}");
            }
            HiddenForScene = stagedOff.Count;
            fireScaleWant = es != null && es.fireScale > 0f ? es.fireScale : 1f; fireScaled = Mathf.Abs(fireScaleWant - 1f) < 0.001f;
            CampfireBegin(won);
        }

        void RestoreHidden() { foreach (var r in stagedOff) if (r) r.enabled = true; stagedOff.Clear(); HiddenForScene = 0; }

        void SceneExtrasEnd() { FlowersForScene(false); if (flowerNear) Destroy(flowerNear); RestoreHidden(); JungleFauna.KeepOut = false; JungleFauna.KeepOutCam = null; FireBurning = false; FireLightNow = 0f; }

        float DayClock() { if (!dayClock) dayClock = FindFirstObjectByType<DayNight>(); return dayClock ? dayClock.timeOfDay : NightGrade.TimeOfDay; }

        void BuildCampfire()
        {
            fireTried = true;
            var c = data.campfire; if (c == null || string.IsNullOrEmpty(c.copyOf)) return;
            var fires = L16Fires.I; var src = fires ? fires.transform.Find(c.copyOf) : null;
            if (!src) { Warn("the campfire's model " + c.copyOf + " is not in the level: the endings have no campfire"); return; }
            fireGo = Instantiate(src.gameObject, root); fireGo.name = "L16LoungerCampfire";
            foreach (var col in fireGo.GetComponentsInChildren<Collider>(true)) Destroy(col);
            var o = fireGo.transform.Find("Out"); var l = fireGo.transform.Find("Lit"); fireOut = o ? o.gameObject : null; fireLit = l ? l.gameObject : null;
            var f = fires.Find(c.copyOf.StartsWith("Fire_") ? c.copyOf.Substring(5) : c.copyOf);
            if (f != null) { fireKind = f.k; fireFlameOff = f.flame - src.position; fireSmokeOff = f.smoke - src.position; }
            Color lc; if (!ColorUtility.TryParseHtmlString(c.lightColor, out lc)) lc = new Color(1f, 0.62f, 0.28f);
            var lg = new GameObject("L16LoungerCampfireLight"); lg.transform.SetParent(root, false);
            fireLightLocal = new Vector3(c.lightX, c.lightY, c.lightZ); lg.transform.localPosition = fireLightLocal;
            fireLight = lg.AddComponent<Light>(); fireLight.type = LightType.Point; fireLight.color = lc; fireLight.range = c.lightRange; fireLight.intensity = 0f;
            fireLight.shadows = LightShadows.None; fireLight.renderMode = LightRenderMode.ForcePixel; fireLight.enabled = false;
            var clip = DetailKit.Pool3D.Clip(L16Fires.ResLoopCamp);
            if (clip) { fireLoop = fireGo.AddComponent<AudioSource>(); fireLoop.clip = clip; fireLoop.loop = true; fireLoop.playOnAwake = false; fireLoop.spatialBlend = 0f; fireLoop.volume = 0f; }
            fireGo.SetActive(false);
        }

        void CampfireBegin(bool won)
        {
            if (!fireTried) BuildCampfire();
            if (!fireGo) return;
            var c = data.campfire;
            fireGo.transform.localPosition = new Vector3(c.x, c.y, c.z); fireGo.transform.localRotation = Quaternion.identity;
            var p = fireGo.transform.position; if (game) p.y = game.GroundY(p.x, p.z); fireGo.transform.position = p;
            fireGo.SetActive(true);
            float t = DayClock(); bool want = CampfireWanted(t, c);                                    // round 4: in an ending by day too (OzL16LoungerSet.Campfire.cs)
            fireLevel = want ? 1f : 0f; fireNextSmoke = 0f;
            if (fireLit) fireLit.SetActive(want);
            if (fireOut) fireOut.SetActive(!want);
            if (fireLoop)
            {
                if (want && !fireLoop.isPlaying) { fireLoop.Play(); OzSoundLog.Add("L16Loungers", "end_campfire_crackle", fireLoop.clip.name, "LOOP 2D ending scene (under the voices)", LoopVolume()); }
                else if (!want && fireLoop.isPlaying) fireLoop.Stop();
            }
            Debug.Log($"[OpenZ L16 loungers] ending {(won ? "win" : "lose")} campfire at ({p.x:F1}, {p.z:F1}): {(want ? "burns" : "out (logs and ash)")} at day clock {t:F3} (it burns {c.onAt:F3} to {c.offAt:F3}); its light {c.lightIntensity * FireLightGain:F1} strong, {c.lightRange * FireRangeGain:F1} m reach, {(want ? "on" : "off")}");
            if (want)
            {   // run 16: it burns from the scene's first frame: flame tongues, light and embers set now (the set's own tick comes a
                // frame later), and a smoke column standing as if it had burned for a while
                SceneExtrasTick(0f);
                SmokePrewarm(p + fireSmokeOff, CampSmokeRise(), CampSmoke(), CampSmokeAlpha() * fireLevel);
            }
        }

        static float LoopVolume() => (CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f) * FireLoopGain;

        void SceneExtrasTick(float dt)
        {
            if (!fireScaled)
            {   // the fort's blaze is built when the fort falls: its columns are scaled once it is there
                var inf = OzFortInferno.Last;
                if (inf && (inf.Centre - endFort).sqrMagnitude < 45f * 45f && inf.ScaleColumns(fireScaleWant))
                { fireScaled = true; FortFireScale = fireScaleWant; Debug.Log($"[OpenZ L16 loungers] the fort's flame columns stand {fireScaleWant:F1} times as high for the scene"); }
            }
            if (!animalsLogged && endClock >= 8f) { animalsLogged = true; Debug.Log($"[OpenZ L16 loungers] animals of the level kept out of the scene's picture so far: {JungleFauna.KeptOutTotal} (now {JungleFauna.KeptOutNow})"); }
            if (!fireGo || !fireGo.activeSelf) return;
            var c = data.campfire; float now = Time.unscaledTime; bool calm = OzMotion.Reduced;
            bool want = CampfireWanted(DayClock(), c);
            fireLevel = Mathf.MoveTowards(fireLevel, want ? 1f : 0f, dt / (want ? 1.2f : 2.5f));
            bool lit = fireLevel > 0.02f;
            if (fireLit && fireLit.activeSelf != lit) fireLit.SetActive(lit);
            if (fireOut && fireOut.activeSelf == lit) fireOut.SetActive(!lit);
            if (lit && fireLit) { if (emberMpb == null) emberMpb = new MaterialPropertyBlock(); EmberGlow(fireLit, EmberK(), emberMpb); }   // item 180: dim embers at night
            float n = Mathf.PerlinNoise(FireSeed * 37.1f, now * c.flickerSpeed), breath = Mathf.PerlinNoise(FireSeed * 11.3f + 5f, now * 0.4f);
            float flick = calm ? 1f - c.flicker * 0.5f : (1f - c.flicker + c.flicker * n) * (0.97f + 0.06f * breath);
            if (fireLight)
            {
                if (fireLight.enabled != lit) fireLight.enabled = lit;
                fireLight.intensity = c.lightIntensity * FireLightGain * fireLevel * flick * CampLightK(); fireLight.range = c.lightRange * FireRangeGain * (0.98f + 0.04f * flick);
                fireLight.transform.localPosition = calm ? fireLightLocal
                    : fireLightLocal + new Vector3(Mathf.PerlinNoise(FireSeed * 3f, now * 2.1f) - 0.5f, 0f, Mathf.PerlinNoise(FireSeed * 7f, now * 1.7f) - 0.5f) * c.wander;
                FireLightNow = lit ? fireLight.intensity : 0f;
            }
            FireBurning = lit;
            CampFillTick(lit, flick);                                                                     // round 4: the warm fill on the two robots at night
            if (fireLoop)
            {
                if (lit && !fireLoop.isPlaying) { fireLoop.Play(); OzSoundLog.Add("L16Loungers", "end_campfire_crackle", fireLoop.clip.name, "LOOP 2D ending scene (under the voices)", LoopVolume()); }
                fireLoop.volume = LoopVolume() * fireLevel;
                if (!lit && fireLoop.isPlaying) fireLoop.Stop();
            }
            if (!lit) return;
            Vector3 at = fireGo.transform.position;
            if (fireKind != null && OpenZPyroFire.TonguesReady)
            {   // the level's own campfire flame: its kind row's tongues
                int tn = CampTongues(fireKind.tongues); float r = 0.22f * fireKind.flame_w;
                for (int j = 0; j < tn; j++)
                {
                    float a = FireSeed * 40f + j * 2.4f, s = j == 0 ? 1f : 0.85f - 0.07f * j;
                    var p = at + fireFlameOff + Vector3.up * CampFlameUp() + (j == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                    OpenZPyroFire.Tongue(p, Mathf.Repeat(FireSeed + 0.37f * j, 1f), s, 0.6f + 0.15f * j, fireLevel * CampFade(), fireKind.flame_h * s * CampFlameH(), fireKind.flame_w * CampFlameW);
                }
            }
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled && now >= fireNextSmoke)
            {
                fireNextSmoke = now + 0.5f; float ph = Mathf.Sin(FireSeed * 6.28f + now * 3.7f);
                fxp.EmitChannel("leg_smoke", at + fireSmokeOff + new Vector3(ph * 0.12f, 0f, Mathf.Cos(FireSeed * 9f + now * 3.1f) * 0.12f),
                    new Vector3(0.18f + ph * 0.1f, (1.1f + 0.4f * Random.value) * CampSmokeRise(), 0.1f), 2.8f + 0.6f * Random.value, 1.2f * (0.85f + 0.3f * Random.value), CampSmoke(), CampSmokeAlpha() * fireLevel);
            }
        }

        void SceneExtrasLate()
        {
            bool sceneOn = built && (briefOn || endOn);
            FlowersForScene(sceneOn);
            JungleFauna.KeepOut = sceneOn;
            if (sceneOn) { JungleFauna.KeepOutAt = root.position; JungleFauna.KeepOutCam = endOn ? Camera.main : cam; }
            if (!endOn && fireGo && fireGo.activeSelf) { fireGo.SetActive(false); if (fireLight) fireLight.enabled = false; CampFillOff(); FireBurning = false; FireLightNow = 0f; }
        }

        /// <summary>One-line state for the Play check.</summary>
        public static string ExtrasStatus() => !I ? "lounger set extras: no set in this scene"
            : $"lounger set extras: campfire {(I.fireGo ? (FireBurning ? "burns" : I.fireGo.activeSelf ? "out" : "not shown") : "none")}, light {FireLightNow:F1}, objects not drawn {HiddenForScene}, flower triangles cut {FlowersCut}, fort flame columns x {FortFireScale:F1}, animals kept out {JungleFauna.KeptOutTotal} (now {JungleFauna.KeptOutNow})";
    }
}
