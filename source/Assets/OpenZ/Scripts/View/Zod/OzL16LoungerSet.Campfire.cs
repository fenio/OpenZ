// OpenZ (GPL-3.0) - Level 16 endings, round 4: the campfire by day and by night.
//  - In both endings the campfire burns by day too (it was lit only from dusk to dawn, by the level's fire hours): by day a modest
//    flame and a darker, denser smoke that reads against the daylight; its light is weaker by day (the sun lights the two robots).
//  - At night it is a campfire, not a bonfire: fewer and lower flame tongues, less bright (the bloom made a tall plume of them),
//    a thin pale smoke that rises less; and a warm fill light that stands between the fire and the two robots, close to them, lights
//    them warmly (the fire's own light reached them from 6 m and left them dim; a stronger fire light had burnt the palm beside it
//    out to white). It flickers with the fire, reaches the robots and their loungers only, and is on only while the scene plays at night.
//  - Day and night blend with the level's own night share (NightGrade.NightK), so dusk and dawn have an in-between fire.
//  - SceneSpots: where the set and the endings' camera stand, for the fort's blast (its trees never fall onto the set or the camera).
// Play check: return OpenZ.OzL16LoungerSet.CampfireStatus();
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16LoungerSet
    {
        /// <summary>The endings' campfire burns by day too (false = only by the level's fire hours, as before).</summary>
        public static bool CampfireByDay = true;
        /// <summary>Flame tongues: height and brightness by day and at night (shares of the level's campfire kind), width, most tongues,
        /// and how far their roots stand over the kind's flame point (m). At night the tongues stood INSIDE the log tepee, where their
        /// hot cores merged under the night bloom into one glowing yellow-white triangle framed by the logs: now they rise from the top
        /// of the logs (0.25 m up), taller and less bright, so the flame shapes carry the fire and the logs glow only as embers.</summary>
        public static float DayFlameH = 0.8f, NightFlameH = 1.2f, DayFlameFade = 1f, NightFlameFade = 0.35f, CampFlameW = 0.85f;
        public static float DayFlameUp = 0f, NightFlameUp = 0.25f;
        public static int CampTonguesMost = 4;
        /// <summary>The fire light's strength by day and at night (shares of the data file's). Item 180: at night the full light (18,
        /// 1.25 m over the logs) burnt the logs out to a white cone and lit the smoke into a glowing ball: a share of it now; the warm
        /// fill on the robots (below) stays.</summary>
        public static float DayLightK = 0.35f, NightLightK = 0.3f;
        /// <summary>Item 180: the glow of the lit logs and ash (their material's own emission) at night as a share of the material's: embers
        /// glow dim orange-red under the flame, never a white cone (by day the material's own).</summary>
        public static float NightEmberK = 0.18f;
        /// <summary>Smoke: colour, strength and rise by day and at night.</summary>
        public static Color DaySmoke = new Color(0.27f, 0.27f, 0.25f), NightSmoke = new Color(0.12f, 0.11f, 0.10f);
        public static float DaySmokeAlpha = 0.72f, NightSmokeAlpha = 0.22f, DaySmokeRise = 1f, NightSmokeRise = 0.6f;
        /// <summary>The warm fill on the two robots at night: strength, reach (m), height over their ground (m), and where it stands on the
        /// way from the robots' middle to the fire (m from the robots).</summary>
        public static float FillIntensity = 2.6f, FillRange = 3.6f, FillHeight = 1.5f, FillFromRobots = 1.6f;
        public static Color FillColour = new Color(1f, 0.6f, 0.3f);
        /// <summary>Checks: the fill light's strength now, frames the campfire burned by day in an ending.</summary>
        public static float FillNow; public static int FireDayFrames;
        Light fireFill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCampfire() { FillNow = 0f; FireDayFrames = 0; }

        static float NightShare() => Mathf.Clamp01(NightGrade.NightK);

        /// <summary>The campfire burns now: in an ending always (by day too), else by the level's fire hours.</summary>
        bool CampfireWanted(float dayClock, OzLoungerCampfire c) => (endOn && CampfireByDay) || NightGrade.LightsOn(dayClock, c.onAt, c.offAt);

        int CampTongues(int kindTongues) => Mathf.Clamp(kindTongues, 1, Mathf.Max(1, CampTonguesMost));
        float CampFlameH() => Mathf.Lerp(DayFlameH, NightFlameH, NightShare());
        float CampFade() => Mathf.Lerp(DayFlameFade, NightFlameFade, NightShare());
        float CampFlameUp() => Mathf.Lerp(DayFlameUp, NightFlameUp, NightShare());
        float CampLightK() => Mathf.Lerp(DayLightK, NightLightK, NightShare());
        Color CampSmoke() => Color.Lerp(DaySmoke, NightSmoke, NightShare());
        float CampSmokeAlpha() => Mathf.Lerp(DaySmokeAlpha, NightSmokeAlpha, NightShare());
        float CampSmokeRise() => Mathf.Lerp(DaySmokeRise, NightSmokeRise, NightShare());

        /// <summary>SceneExtrasTick, every frame the campfire stands: the warm fill on the two robots at night.</summary>
        void CampFillTick(bool lit, float flick)
        {
            float night = NightShare();
            if (lit && endOn && night < 0.5f) FireDayFrames++;
            bool on = lit && endOn && night > 0.05f && fireGo;
            if (on && !fireFill)
            {
                var go = new GameObject("L16LoungerCampfireFill"); go.transform.SetParent(root, false);
                fireFill = go.AddComponent<Light>(); fireFill.type = LightType.Point; fireFill.shadows = LightShadows.None;
                fireFill.renderMode = LightRenderMode.ForcePixel; fireFill.enabled = false;
            }
            if (!fireFill) return;
            if (fireFill.enabled != on) fireFill.enabled = on;
            if (!on) { FillNow = 0f; return; }
            Vector3 mid = group.position, fire = fireGo.transform.position, to = fire - mid; to.y = 0f;
            Vector3 p = mid + (to.sqrMagnitude > 0.01f ? to.normalized * Mathf.Min(FillFromRobots, to.magnitude * 0.5f) : Vector3.zero);
            p.y = mid.y + FillHeight;
            fireFill.transform.position = p;
            fireFill.color = FillColour; fireFill.range = FillRange;
            fireFill.intensity = FillIntensity * night * fireLevel * flick;
            FillNow = fireFill.intensity;
        }

        void CampFillOff() { if (fireFill) fireFill.enabled = false; FillNow = 0f; }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        /// <summary>Item 180: the lit campfire model's glowing materials (embers, glowing ash) at `k` x their own emission (1 = as the
        /// material is: no block). Shared by the endings' campfire and the start camp's (OzHeroCampfire).</summary>
        public static void EmberGlow(GameObject lit, float k, MaterialPropertyBlock mpb)
        {
            if (!lit || mpb == null) return;
            foreach (var r in lit.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    var m = ms[i]; if (!m || !m.HasProperty(EmissionId)) continue;
                    var e = m.GetColor(EmissionId); if (e.maxColorComponent < 0.05f) continue;
                    r.GetPropertyBlock(mpb, i); mpb.SetColor(EmissionId, k >= 0.999f ? e : e * k); r.SetPropertyBlock(mpb, i);
                }
            }
        }
        /// <summary>Run 16: the puffs of a standing smoke column (as if the fire had burned for a few seconds), so a cut to a campfire
        /// never shows the fire without its smoke: each puff where a puff of that age would be, the rest of its life still to come.</summary>
        public static int SmokePrewarmPuffs = 5;
        public static void SmokePrewarm(Vector3 from, float rise, Color col, float alpha)
        {
            var fxp = OpenZFxPlayer.I; if (!fxp || !fxp.isActiveAndEnabled || alpha <= 0.001f) return;
            for (int i = 1; i <= SmokePrewarmPuffs; i++)
            {
                float age = 0.5f * i, life = 2.8f + 0.6f * Random.value; if (age > life - 0.4f) break;
                var vel = new Vector3(0.18f, 1.3f * rise, 0.1f);
                fxp.EmitChannel("leg_smoke", from + vel * age, vel, life - age, 1.2f * (0.85f + 0.3f * Random.value) * (1f + 0.15f * age), col, alpha);
            }
        }

        /// <summary>The embers' share now (1 by day, NightEmberK at full night).</summary>
        public static float EmberK() => Mathf.Lerp(1f, NightEmberK, NightShare());
        MaterialPropertyBlock emberMpb;

        /// <summary>For the fort's blast and the trees round it (OzTreeFall.Blast.cs): where the set stands and where the endings' camera
        /// will stand for either ending (the same placement as Begin: the set turned toward the fort with each ending's own staging), on
        /// the ground. Returns how many spots were written (0: no set in this level).</summary>
        public static int SceneSpots(Vector3 fort, Vector3[] into)
        {
            var s = I; if (!s || !s.root || into == null || into.Length == 0) return 0;
            Vector3 place = new Vector3(s.root.position.x, 0f, s.root.position.z); int n = 0;
            into[n++] = place;
            Vector3 to = fort - place; to.y = 0f; if (to.sqrMagnitude < 1f) to = Vector3.forward;
            var look = Quaternion.LookRotation(to.normalized, Vector3.up);
            var d = s.data;
            if (d != null)
                foreach (var es in new[] { d.endWin, d.endLose })
                    if (es != null && es.camFov > 1f && n < into.Length)
                    {
                        Vector3 cam = place + look * Quaternion.Euler(0f, es.fortLeftDeg, 0f) * new Vector3(es.camX, 0f, es.camZ); cam.y = 0f;
                        into[n++] = cam;
                    }
            if (n < into.Length)
            {   // the endings' own camera numbers as they stand now (the staging of the data file writes them at Begin)
                Vector3 cam = place + ZodBriefing.EndingSetRotation(place, fort) * new Vector3(ZodBriefing.EndingCamPos.x, 0f, ZodBriefing.EndingCamPos.z); cam.y = 0f;
                into[n++] = cam;
            }
            return n;
        }

        /// <summary>Play check of the campfire in one line.</summary>
        public static string CampfireStatus() => !I ? "campfire: no set"
            : $"campfire: {(FireBurning ? "burns" : "not burning")}, by day too {CampfireByDay}, night share {NightShare():F2}, flame height x {I.CampFlameH():F2}, brightness x {I.CampFade():F2}, tongues at most {CampTonguesMost}, light {FireLightNow:F1}, fill on the robots {FillNow:F2}, frames burning by day {FireDayFrames}";
    }
}
