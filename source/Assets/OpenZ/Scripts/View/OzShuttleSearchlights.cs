using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's two belly searchlights (rule DV118, view only), built like the army searchlight (OzFieldSearchlight)
    /// without its stand: a lamp in each of the kit's belly housings (ShuttleRearDoor: SH_BellyLamp_L / _R, the MK_Search markers),
    /// aimed down and forward, each the game's own searchlight light: lens glow, halo, beam cone (OpenZ/NightBeam) whose length is the
    /// way down to the ground, and the ground pool picture (OpenZ/NightPool) where the beam lands, plus one unshadowed spot light that
    /// it offers to the night lights' budget (NightBudgetShare: a real light only inside the level's own count; not granted, the pool
    /// picture carries the light). ON at night (the lamp posts' times, 0.335 .. 0.895 of the day clock) while the ship is off the
    /// ground: full while its pads are under 8 m (take-off, landing, hover), 0.55 at cruise height; fading on in 0.35 s, off in 0.6 s,
    /// 1.5 s after the touchdown. The switch-on and switch-off are the game's searchlight sound (ngt_searchlight_on: 0.4, and 0.25 a
    /// little lower). LOW quality drops the halos and beams as for every lamp (NightLights.halos / beams). Destroyed: out at once with
    /// the light fizzle.
    /// </summary>
    public sealed partial class OzShuttleSearchlights
    {
        public static Color Colour = new Color(0.86f, 0.92f, 1f, 1f);
        public static float AngleDeg = 25f, Range = 26f, Intensity = 60f, Priority = 6f, OnAt = 0.335f, OffAt = 0.895f, LowFeetM = 8f, CruiseShare = 0.55f;   // K256e: 25 deg cones (was 34: the two read as one)
        /// <summary>Proof: switch-ons and switch-offs, frames with both lamps on, frames a real light was granted, frames the pool was drawn.</summary>
        public static int SwitchOns, SwitchOffs, OnFrames, RealFrames, PoolFrames;

        sealed class Lamp
        {
            public Transform head, beamT, poolT; public Renderer lens, halo, beam, pool; public Light light; public NightFixture fix;
            public float lensShown = -1f, haloShown = -1f, beamShown = -1f, poolShown = -1f, real; public Vector3 posL, aimL;
        }
        readonly Lamp[] lamps = new Lamp[2];
        readonly Transform root; readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
        public float Level { get; private set; }
        public bool On { get; private set; }
        public float RealShare { get; private set; }
        float offTimer; bool dead;

        public OzShuttleSearchlights(Transform shipRoot, ShuttleRearDoor door, int id)
        {
            root = shipRoot;
            for (int i = 0; i < 2; i++)
            {
                var L = new Lamp();
                L.posL = door ? (i == 0 ? door.SearchL : door.SearchR) : new Vector3(i == 0 ? 0.8f : -0.8f, 1.2f, 2.05f);
                var aim = door ? (i == 0 ? door.SearchAimL : door.SearchAimR) : L.posL + new Vector3(0f, -0.94f, 0.34f);
                L.aimL = (aim - L.posL).normalized;
                L.head = new GameObject("OZ_FxSearchHead" + i).transform; L.head.SetParent(root, false);
                L.head.localPosition = L.posL; L.head.localRotation = Quaternion.LookRotation(L.aimL, Vector3.up);
                L.lens = NightMeshes.Part("Lens", L.head, NightMeshes.Disc(), NightMeshes.Lens(), false, new Vector3(0f, 0f, 0.01f), Quaternion.identity, new Vector3(0.22f, 0.22f, 1f)).GetComponent<Renderer>();
                L.halo = NightMeshes.Part("Halo", L.head, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0f, 0f, 0.15f), Quaternion.identity, Vector3.one * 1.0f).GetComponent<Renderer>();
                var b = NightMeshes.Part("Beam", L.head, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0f, 0f, 0.02f), Quaternion.identity, Vector3.one);
                L.beam = b.GetComponent<Renderer>(); L.beamT = b.transform;
                var pg = NightMeshes.Part("OZ_FxSearchPool" + i, null, NightMeshes.Cube(), NightMeshes.Pool(), false, Vector3.zero, Quaternion.identity, Vector3.one);
                L.poolT = pg.transform; L.pool = pg.GetComponent<Renderer>();
                L.lens.enabled = L.halo.enabled = L.beam.enabled = L.pool.enabled = false;
                var lg = new GameObject("Light"); lg.transform.SetParent(L.head, false); lg.transform.localPosition = new Vector3(0f, 0f, 0.05f);
                L.light = lg.AddComponent<Light>(); L.light.type = LightType.Spot; L.light.range = Range; L.light.color = Colour; L.light.intensity = 0f; L.light.enabled = false;
                L.light.shadows = LightShadows.None; L.light.renderMode = LightRenderMode.ForcePixel; L.light.spotAngle = AngleDeg; L.light.innerSpotAngle = AngleDeg * 0.35f;
                L.fix = new NightFixture
                {
                    Kind = NightKind.Searchlight, Id = "shuttle_searchlight_" + id + "_" + i, Light = L.light, Root = root, Col = Colour, ColLinear = Colour.linear,
                    Intensity = Intensity, Range = Range, Angle = AngleDeg, Priority = Priority, ShadowCandidate = false, BeamK = 1f, PoolK = 1f,
                };
                NightBudgetShare.Fixtures.Add(L.fix);
                lamps[i] = L;
            }
        }

        public void Dispose()
        {
            Night?.Dispose();
            foreach (var L in lamps)
            {
                if (L == null) continue;
                if (L.fix != null) NightBudgetShare.Fixtures.Remove(L.fix);
                if (L.poolT) Object.Destroy(L.poolT.gameObject);
                if (L.head) Object.Destroy(L.head.gameObject);
            }
        }

        /// <summary>Destroyed: the lamps go out at once with the fizzle.</summary>
        public void Kill()
        {
            if (dead) return; dead = true;
            Night?.Kill();                                                                        // the other night lights go out with them
            if (Level > 0.05f && AudioDirector.Has("ngt_light_fizzle")) AudioDirector.Fire("ngt_light_fizzle", root.TransformPoint(lamps[0].posL), -1, 0.6f);
            Level = 0f; On = false;
            foreach (var L in lamps) Dark(L);
        }

        void SetColor(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (shown >= 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key; bool on = key > 0.0015f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb);
        }

        void Dark(Lamp L)
        {
            if (L == null) return;
            if (L.lens) L.lens.enabled = false; if (L.halo) L.halo.enabled = false; if (L.beam) L.beam.enabled = false; if (L.pool) L.pool.enabled = false;
            if (L.light) L.light.enabled = false; if (L.fix != null) L.fix.LightOut = 0f;
            L.lensShown = L.haloShown = L.beamShown = L.poolShown = -1f;
        }

        /// <summary>One frame: airborne = the rules', feetM = the pads over the ground (m).</summary>
        public void Step(float dt, bool airborne, float feetM)
        {
            if (dead) return;
            var g = ZGame.I; if (g && g.paused) dt = 0f;
            if (airborne) offTimer = 1.5f; else offTimer = Mathf.Max(0f, offTimer - dt);
            bool night = NightGrade.LightsOn(NightGrade.TimeOfDay, OnAt, OffAt);
            bool want = night && (airborne || offTimer > 0f);
            float share = feetM < LowFeetM ? 1f : Mathf.Lerp(1f, CruiseShare, Mathf.Clamp01((feetM - LowFeetM) / 3f));
            Night?.LandingWant(ref want, ref share);                                               // the landing lights' rule (OzShuttleNightLights)
            float prev = Level;
            Level = want ? Mathf.MoveTowards(Level, share, dt / 0.35f) : Mathf.MoveTowards(Level, 0f, dt / 0.6f);
            if (prev <= 0f && Level > 0f) { SwitchOns++; SwitchClick(true); }                               // K256e: through the ship's one-click-per-change switch (OzShuttleSearchlights.Night.cs)
            if (prev > 0f && Level <= 0f) { SwitchOffs++; SwitchClick(false); }
            On = Level > 0.001f;
            if (!On) { foreach (var L in lamps) Dark(L); RealShare = 0f; return; }
            OnFrames++;
            var nl = NightLights.I; float nk = NightGrade.NightK, dusk = Mathf.Lerp(0.25f, 1f, nk);
            bool halos = !nl || nl.halos, beams = !nl || nl.beams, pools = !nl || nl.pools;
            float haloGain = nl ? nl.haloGain : 0.35f, beamGain = nl ? nl.beamGain : 1f, poolGain = nl ? nl.poolGain : 0.22f;
            float lightGain = nl ? nl.lightGain * nl.searchLightGain : 3f;
            var c = Colour.linear; float outv = Level * (OzMotion.Reduced ? 0.96f : 0.975f + 0.025f * Mathf.PerlinNoise(Time.time * 8f, 0.4f));
            float realSum = 0f; bool anyPool = false;
            foreach (var L in lamps)
            {
                var lens = L.head.position; var dir = L.head.forward;
                float gy = g ? g.GroundY(lens.x, lens.z) : 0f;
                float dy = Mathf.Max(0.2f, -dir.y), drop = Mathf.Max(0.5f, lens.y - gy), dist = drop / dy, len = Mathf.Min(Range, dist + 0.5f);
                var hit = lens + dir * Mathf.Min(dist, Range);
                float ta = Mathf.Tan(AngleDeg * 0.5f * Mathf.Deg2Rad) * 1.2f;
                L.beamT.localScale = new Vector3(len * ta, len * ta, len + 0.4f);
                SetColor(L.lens, c * (7f * outv), ref L.lensShown);
                SetColor(L.halo, c * (halos ? haloGain * dusk * outv * 2.6f : 0f), ref L.haloShown);
                SetColor(L.beam, c * (beams ? beamGain * outv * Mathf.Pow(nk, 0.8f) * 0.42f : 0f), ref L.beamShown);
                L.fix.LightOut = outv; L.fix.Col = Colour; L.fix.ColLinear = c;
                L.real = NightBudgetShare.HookFrame >= Time.frameCount - 2 ? L.fix.RealK : 0f;
                float k = outv * L.real; bool lit = k > 0.002f;
                if (L.light.enabled != lit) L.light.enabled = lit;
                if (lit) L.light.intensity = Intensity * k * lightGain;
                realSum += L.real;
                // the pool on the ground where the beam lands (width 2 h tan(17 deg) x 1.2, longer along a slanting beam)
                float w = Mathf.Clamp(2f * drop * Mathf.Tan(AngleDeg * 0.5f * Mathf.Deg2Rad) * 1.2f, 1.2f, Range);
                var hz = new Vector3(dir.x, 0f, dir.z); if (hz.sqrMagnitude < 1e-4f) hz = root.forward; hz.y = 0f; hz.Normalize();
                float hgy = g ? g.GroundY(hit.x, hit.z) : gy;
                L.poolT.SetPositionAndRotation(new Vector3(hit.x, hgy + 0.35f, hit.z), Quaternion.LookRotation(hz, Vector3.up));
                L.poolT.localScale = new Vector3(w, 3.8f, w / Mathf.Max(0.35f, dy));
                float e = Intensity / Mathf.Max(1f, dist * dist);
                SetColor(L.pool, c * (pools ? poolGain * outv * dusk * (1f - L.real) * Mathf.Min(10f, e * 4f) : 0f), ref L.poolShown);
                anyPool |= L.pool && L.pool.enabled;
            }
            RealShare = realSum * 0.5f;
            if (RealShare > 0.01f) RealFrames++;
            if (anyPool) PoolFrames++;
        }
    }
}
