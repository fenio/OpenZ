using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 (View/OzHeroSquadView.cs): the campfire beside a hero squad's level-start loungers (the level data's hero entry "camp":
    /// [tile x, tile y]). Scenery of the view only, like the level's own fires (L16Fires.cs): nothing in the rules, it closes no tile,
    /// it cannot hurt a unit. It is made only when the hero squad is made (the switch on), so 'Zod exact' has none.
    ///  * Model: a copy of the level's own campfire (the L16Fires model "Fire_C1", the one the endings' campfire copies), its lit state.
    ///  * It burns for the first StartBurnSeconds of game time from the level start (day or night), then as a camp does: lit at night,
    ///    out by day by the level's own night share, going out and lighting up softly over BurnFade seconds (flames, smoke, light and
    ///    crackle together); small, in the endings' campfire look (OzL16LoungerSet.Campfire.cs: the same flame height, brightness,
    ///    tongue count and smoke for day and night, blended by the level's night share). The endings' own campfire always burns.
    ///  * Light: a warm point light at night only (by day the sun lights the camp), one fixture of the night light budget like every
    ///    fire of the level (NightBudgetShare), so it comes out of the game's budget of real lights, not on top of it.
    ///  * Sound: the level's campfire crackle loop (sd_campfire_crackle_loop, the loop the level's burning campfires play), its own 3D
    ///    source at the level fires' loop volume and reach, in the sound log once when it starts.
    ///  * It stays as scenery when the loungers go away.
    /// Play check: OzHeroCampfire.Status().
    /// </summary>
    public sealed class OzHeroCampfire : MonoBehaviour
    {
        /// <summary>The level fire whose model it copies (id in Resources/OpenZ/Night/L16_fires.json; else the first campfire).</summary>
        public static string CopyOf = "C1";
        /// <summary>The light's strength at night as a share of the level campfire kind's (the endings' night share is 1).</summary>
        public static float NightLightK = 1f;
        /// <summary>Checks: campfires made, burning now, the light's strength now, frames drawn by day / by night, the crackle playing.</summary>
        public static int Made, DayFrames, NightFrames; public static float LightNow; public static bool CrackleOn;
        /// <summary>Seconds of game time from the level start it burns whatever the hour, and seconds it takes to go out or light up.</summary>
        public static float StartBurnSeconds = 60f, BurnFade = 4f;
        /// <summary>Checks: how much it burns now (0 out, 1 burning) and the game time it read.</summary>
        public static float BurnNow; public static double GameSecondsNow;
        public static readonly System.Collections.Generic.List<string> Log = new System.Collections.Generic.List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Made = DayFrames = NightFrames = 0; LightNow = 0f; CrackleOn = false; Log.Clear(); }

        L16Fires.KindRow kind; Vector3 flameOff, smokeOff, lightOff; Light lamp; NightFixture fix; AudioSource loop; float seed, nextSmoke, seenAt = -100f; bool logged;
        Transform litT, outT; float burnK = 1f;
        public int TileX, TileY;

        static void Say(string s) { if (Log.Count > 50) Log.RemoveAt(0); Log.Add(s); Debug.Log("[OpenZ HeroSquad] " + s); }

        /// <summary>Makes the campfire on tile (tx, ty) of the map (null: the level has no campfire model to copy).</summary>
        public static OzHeroCampfire Make(ZGame g, int tx, int ty) => Make(g, null, tx, ty);

        /// <summary>KC4b: the same at a world point (the start camp's plan, OzHeroCampPlan; null = the middle of tile (tx, ty)).</summary>
        public static OzHeroCampfire Make(ZGame g, Vector3? where, int tx, int ty)
        {
            var fires = L16Fires.I;
            L16Fires.Fire src = null;
            if (fires) foreach (var f in fires.Fires) if (f.campfire && (src == null || f.row.id == CopyOf)) src = f;
            var srcT = fires && src != null ? fires.transform.Find("Fire_" + src.row.id) : null;
            if (!srcT) { Say($"campfire on tile ({tx},{ty}): the level has no campfire model to copy (L16Fires): none drawn"); return null; }
            var host = new GameObject($"HeroCampfire_{tx}_{ty}");
            var at = where ?? ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8); at.y = g ? g.GroundY(at.x, at.z) : srcT.position.y;   // KC4b: the camp plan's point
            host.transform.position = at;
            var model = Instantiate(srcT.gameObject, host.transform); model.name = "Model";
            model.transform.localPosition = Vector3.zero; model.transform.localRotation = srcT.rotation;
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) Destroy(col);
            var o = model.transform.Find("Out"); var l = model.transform.Find("Lit");
            if (o) o.gameObject.SetActive(false);
            if (l) l.gameObject.SetActive(true);                                                          // it burns: the glowing logs and embers
            var c = host.AddComponent<OzHeroCampfire>(); c.litT = l; c.outT = o;
            c.TileX = tx; c.TileY = ty; c.kind = src.k; c.seed = (tx * 73 + ty * 151) % 997 / 997f;
            c.flameOff = src.flame - srcT.position; c.smokeOff = src.smoke - srcT.position; c.lightOff = src.lightAt - srcT.position;
            Color lc; if (!ColorUtility.TryParseHtmlString(src.k.color, out lc)) lc = new Color(1f, 0.62f, 0.28f);
            var lg = new GameObject("Light"); lg.transform.SetParent(host.transform, false); lg.transform.localPosition = c.lightOff;
            c.lamp = lg.AddComponent<Light>(); c.lamp.type = LightType.Point; c.lamp.color = lc; c.lamp.range = src.k.range; c.lamp.intensity = 0f;
            c.lamp.shadows = LightShadows.None; c.lamp.renderMode = LightRenderMode.ForcePixel; c.lamp.enabled = false;
            c.fix = new NightFixture { Kind = NightKind.Uplight, Id = $"herocamp_{tx}_{ty}", Light = c.lamp, Col = lc, ColLinear = lc.linear, Intensity = src.k.intensity * NightLightK,
                                       Range = src.k.range, Priority = src.k.priority, ShadowCandidate = false };
            NightBudgetShare.Fixtures.Add(c.fix);
            var clip = DetailKit.Pool3D.Clip(L16Fires.ResLoopCamp);
            if (clip)
            {
                c.loop = host.AddComponent<AudioSource>(); c.loop.clip = clip; c.loop.loop = true; c.loop.playOnAwake = false; c.loop.spatialBlend = 1f; c.loop.dopplerLevel = 0f;
                c.loop.rolloffMode = AudioRolloffMode.Linear; c.loop.minDistance = 4f; c.loop.maxDistance = 30f; c.loop.volume = 0f;
            }
            Made++;
            Say($"campfire on tile ({tx},{ty}) at ({at.x:F1}, {at.z:F1}): a copy of the level's Fire_{src.row.id}, burning day and night, crackle {(clip ? "ok" : "MISSING")}");
            return c;
        }

        static float Bus() => CombatAudio.I ? CombatAudio.I.volume * CombatAudio.BusGain(CombatAudio.Bus.World) : 0.55f;

        void OnDestroy() { if (fix != null) NightBudgetShare.Fixtures.Remove(fix); }

        void Update()
        {
            float now = Time.time, night = Mathf.Clamp01(NightGrade.NightK);
            bool calm = OzMotion.Reduced;
            // burning: the first StartBurnSeconds of the game whatever the hour, then lit at night and out by day, softly
            var gw = ZGame.I ? ZGame.I.World : null; double gt = gw != null ? gw.Time : Time.timeSinceLevelLoad; GameSecondsNow = gt;
            float want = gt < StartBurnSeconds ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.65f, night));
            burnK = Mathf.MoveTowards(burnK, want, Time.deltaTime / Mathf.Max(0.1f, BurnFade)); BurnNow = burnK;
            bool lit = burnK > 0.5f;
            if (litT && litT.gameObject.activeSelf != lit) litT.gameObject.SetActive(lit);
            if (outT && outT.gameObject.activeSelf == lit) outT.gameObject.SetActive(!lit);
            float n = Mathf.PerlinNoise(seed * 37.1f, now * kind.speed), breath = Mathf.PerlinNoise(seed * 11.3f + 5f, now * 0.4f);
            float flick = calm ? 1f - kind.flicker * 0.5f : (1f - kind.flicker + kind.flicker * n) * (0.97f + 0.06f * breath);
            if (night < 0.5f) DayFrames++; else NightFrames++;
            // light: night only, inside the night budget
            bool hook = NightBudgetShare.HookFrame >= Time.frameCount - 2;
            float lo = night * flick * burnK; fix.LightOut = lo;
            float k = lo * (hook ? fix.RealK : 1f); bool on = k > 0.002f;
            if (lamp.enabled != on) lamp.enabled = on;
            var nl = NightLights.I;
            if (on)
            {
                lamp.intensity = kind.intensity * NightLightK * k * (nl ? nl.lightGain : 0.6f) * (L16Fires.I ? L16Fires.I.fireLightGain : 1f);
                lamp.range = kind.range * (0.98f + 0.04f * flick);
                if (!calm) lamp.transform.localPosition = lightOff + new Vector3(Mathf.PerlinNoise(seed * 3f, now * 2.1f) - 0.5f, 0f, Mathf.PerlinNoise(seed * 7f, now * 1.7f) - 0.5f) * 0.16f;
            }
            LightNow = on ? lamp.intensity : 0f;
            // crackle: while it burns, with it
            if (loop)
            {
                loop.volume = (L16Fires.I ? L16Fires.I.campLoopVol : 0.25f) * Bus() * burnK;
                if (burnK < 0.01f) { if (loop.isPlaying) loop.Stop(); }
                else if (!loop.isPlaying) loop.Play();
                CrackleOn = loop.isPlaying;
                if (!logged && loop.isPlaying) { logged = true; OzSoundLog.Add("HeroSquad", "hero_campfire_crackle", loop.clip.name, "LOOP 3D campfire by the loungers", loop.volume, transform.position); }
            }
            // flame tongues and smoke in the endings' campfire look (small; day and night blended by the night share)
            var cam = Camera.main;
            if (burnK < 0.02f) return;                                                                   // out: no flames, no smoke
            if (cam && !DetailKit.OnScreen(cam, transform.position + flameOff, 0.2f)) return;
            bool fresh = now - seenAt > 1.5f; seenAt = now;                                              // run 16: it comes on screen: its smoke column stands at once
            Vector3 at = transform.position;
            if (OpenZPyroFire.TonguesReady)
            {
                int tn = Mathf.Clamp(kind.tongues, 1, Mathf.Max(1, OzL16LoungerSet.CampTonguesMost)); float r = 0.22f * kind.flame_w;
                float h = Mathf.Lerp(OzL16LoungerSet.DayFlameH, OzL16LoungerSet.NightFlameH, night) * burnK, fade = Mathf.Lerp(OzL16LoungerSet.DayFlameFade, OzL16LoungerSet.NightFlameFade, night);
                for (int j = 0; j < tn; j++)
                {
                    float a = seed * 40f + j * 2.4f, s = j == 0 ? 1f : 0.85f - 0.07f * j;
                    var p = at + flameOff + Vector3.up * Mathf.Lerp(OzL16LoungerSet.DayFlameUp, OzL16LoungerSet.NightFlameUp, night) + (j == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                    OpenZPyroFire.Tongue(p, Mathf.Repeat(seed + 0.37f * j, 1f), s, 0.6f + 0.15f * j, fade, kind.flame_h * s * h, kind.flame_w * OzL16LoungerSet.CampFlameW);
                }
            }
            var fxp = OpenZFxPlayer.I;
            if (fxp && fxp.isActiveAndEnabled && Time.deltaTime > 0f && (now >= nextSmoke || fresh))
            {
                nextSmoke = now + 0.5f; float ph = Mathf.Sin(seed * 6.28f + now * 3.7f);
                var col = Color.Lerp(OzL16LoungerSet.DaySmoke, OzL16LoungerSet.NightSmoke, night);
                float alpha = Mathf.Lerp(OzL16LoungerSet.DaySmokeAlpha, OzL16LoungerSet.NightSmokeAlpha, night) * burnK, rise = Mathf.Lerp(OzL16LoungerSet.DaySmokeRise, OzL16LoungerSet.NightSmokeRise, night);
                if (fresh) OzL16LoungerSet.SmokePrewarm(at + smokeOff, rise, col, alpha);
                fxp.EmitChannel("leg_smoke", at + smokeOff + new Vector3(ph * 0.12f, 0f, Mathf.Cos(seed * 9f + now * 3.1f) * 0.12f),
                    new Vector3(0.18f + ph * 0.1f, (1.1f + 0.4f * Random.value) * rise, 0.1f), 2.8f + 0.6f * Random.value, 1.2f * (0.85f + 0.3f * Random.value), col, alpha);
            }
        }

        /// <summary>Play check in one line.</summary>
        public static string Status()
        {
            var all = FindObjectsByType<OzHeroCampfire>(FindObjectsSortMode.None);
            var sb = new System.Text.StringBuilder($"hero campfires {all.Length} (made {Made}), burning {BurnNow:F2} at game time {GameSecondsNow:F0} s, night share {Mathf.Clamp01(NightGrade.NightK):F2}, light {LightNow:F1}, crackle {(CrackleOn ? "playing" : "silent")}, frames by day {DayFrames} / by night {NightFrames}");
            foreach (var c in all) sb.Append($" | tile ({c.TileX},{c.TileY}) at ({c.transform.position.x:F1}, {c.transform.position.z:F1})");
            return sb.ToString();
        }
    }
}
