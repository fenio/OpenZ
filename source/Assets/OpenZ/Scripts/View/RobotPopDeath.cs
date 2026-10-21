using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>How a robot's death is drawn (deaths_mech_v1). The sim already sends Zod's two death flags (ZWorld.RelayObjectDeath =
    /// zserver.cpp:1747-1748: fire = a pyro flame hit in the last 1.5 s, missile = any splash hit in the last 1.5 s).</summary>
    public enum RobotDeathKind { Ragdoll, Melt, Pop }

    /// <summary>
    /// <c>if (do_missile_death) ERobotTurrent (the body flips through the air, die5, 33 frames, 3.5-4.4 s)
    /// else ERobotDeath(do_fire_death)</c>; ERobotDeath (erobotdeath.cpp:19,33-43) plays the 17-frame melt after a flame, else
    /// one of FOUR death animations picked by <c>rand() % 4</c> - and all four are small pop explosions (assets/units/robots/
    /// die1-4: a star flash with bits flying, a flash with green sparks and bits, a ring flash scattering dark bits, a flash with an
    /// expanding ring). So in Z: splash kills (grenade, rocket, tank / cannon / howitzer shell, MLRS, debris) throw the body,
    /// flame kills melt, and EVERY other kill (grunt / psycho / sniper bullets, lasers, jeep / gatling / APC guns) pops.
    /// <see cref="Mix"/> = Zod (default) mirrors that exactly: missile -> ragdoll, fire -> melt, otherwise pop (variant = hash % 4,
    /// deterministic from the dead robot's and the killer's ids, never Random in the sim). <see cref="DeathMix.Shares"/> is an
    /// option (listed as DV20 when on): a share of each weapon class pops, the rest ragdolls / melts.
    /// </summary>
    public static class RobotDeathRule
    {
        public enum DeathMix { Zod, Shares }
        public static DeathMix Mix = DeathMix.Zod;
        /// <summary>DeathMix.Shares only: pop share per killing hit (the rest keeps ragdoll, or melt for flames).</summary>
        public static float ShareBullet = 0.5f, ShareLaser = 0.8f, ShareSplash = 0.35f, ShareFlame = 0.4f;

        public static uint Hash(int a, int b, uint salt)
        {
            uint x = (uint)a * 0x9E3779B1u ^ Mix32((uint)b + salt * 0x85EBCA77u);
            return Mix32(x);
        }
        static uint Mix32(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
        public static float Unit(int a, int b, uint salt) => (Hash(a, b, salt) & 0xFFFFFF) / 16777216f;

        /// <summary>The death drawn for this kill. variant 0..3 = Zod's die1..die4 (uniform, like rand() % 4).</summary>
        public static RobotDeathKind Pick(int objId, int killerId, string killerType, bool fire, bool missile, out int variant)
        {
            variant = (int)(Hash(objId, killerId, 1u) & 3u);
            if (killerType == "electrobot" && !fire && !missile) return RobotDeathKind.Ragdoll;   // electrobot_v1: electrocuted, thrown (arcs ride the ragdoll)
            if (Mix == DeathMix.Zod) return missile ? RobotDeathKind.Ragdoll : fire ? RobotDeathKind.Melt : RobotDeathKind.Pop;
            float u = Unit(objId, killerId, 2u);
            if (missile) return u < ShareSplash ? RobotDeathKind.Pop : RobotDeathKind.Ragdoll;
            if (fire) return u < ShareFlame ? RobotDeathKind.Pop : RobotDeathKind.Melt;
            return u < (killerType == "laser" ? ShareLaser : ShareBullet) ? RobotDeathKind.Pop : RobotDeathKind.Ragdoll;
        }
    }

    /// <summary>
    /// deaths_mech_v1: the robot "pop" death. The robot model is hidden at once; in its place a quick bright HDR pop (additive core
    /// x12 + a heat-ramp fireball, gain in linear space so it blooms), a pooled flash light + ground light, two OpenZHitSparks bursts
    /// (hitsparks_v1 streaks, gravity + bounce), glowing sparks, small metal chips (M12 rubble), a smoke puff, a scorch decal (a splash
    /// in water) and a crunchy pop (rp_pop_a-f, CombatAudio "robot_pop": Destruction bus, 3D, 3 voices). Robot bits fly with a cheap
    /// (BS-01 kit: helmet, pack, hips plate, and the gun) plus chunky low-poly pieces in its colours (team shoulder domes, gunmetal
    /// plates and boots, a brass gauge wheel, hex bolts, a leg rod). Zod's four variants set the mix: 0 star (warm, many sparks),
    /// 1 electric (blue-white crackle, fizz), 2 scatter (many dark bits, darker smoke), 3 ring (big fireball + dust ring). Every
    /// count, direction, spin, size and the sound take come from a per-death seed, so no two pops look alike.
    /// Pooled: one manager, &lt;= <see cref="MaxPieces"/> piece objects reused (the oldest resting one is recycled), procedural meshes and
    /// one material built once, no per-death allocation after warm-up. More than <see cref="FullPerFrame"/> pops in one frame shrink.
    /// </summary>
    [DefaultExecutionOrder(96)]
    public class RobotPopDeath : MonoBehaviour
    {
        public const int MaxPieces = 140, FullPerFrame = 4;
        public const string SoundKey = "robot_pop";
        public static readonly string[] Bank = { "rp_pop_a", "rp_pop_b", "rp_pop_c", "rp_pop_d", "rp_pop_e", "rp_pop_f" };
        static readonly string[] Electric = { "rp_pop_b", "rp_pop_e" };
        public static float Gain = 0.9f, Gravity = 13f, PieceLifeMin = 3.8f, PieceLifeMax = 6.5f;
        public static int Pops { get; private set; }

        static RobotPopDeath inst;
        static RobotPopDeath I { get { if (!inst) inst = new GameObject("RobotPopDeath").AddComponent<RobotPopDeath>(); return inst; } }

        sealed class Piece
        {
            public GameObject go; public Transform t; public MeshFilter mf; public MeshRenderer mr;
            public Vector3 v, w; public float age, life, rad, restY; public bool active, rest, popping, wet, landed; public int bounces;
            public float smoke, smokeT; public bool heavy, concrete;
            public int glow;                                                                            // 0 = no glow part on it; else the fade step it shows + 1 (OzGlowFade)
            public int dead;                                                                            // item 228: -1 = no dead colour (a vehicle's part, a chip); else the dead step shown (OzDeadTint, from the age)
        }
        readonly List<Piece> pieces = new List<Piece>();
        readonly List<Renderer> parts = new List<Renderer>();
        MaterialPropertyBlock block, copy;
        Material pieceMat;
        Mesh dome, box, hex, gear, rod, boot;
        uint rs = 1u;
        int frame = -1, thisFrame;
        float lastClink = -10f;

        // ------------------------------------------------------------------------------------------------ API
        /// <summary>Draws the pop for `view` (a robot, alive this frame) and hides nothing itself: the caller hides the view
        /// right after. blastFrom = the killer / impact position (bits fly away from it). False = could not play (no game).</summary>
        public static bool Play(UnitView view, int variant, Vector3 blastFrom, uint seed)
        {
            if (!view || view.Obj == null || !ZGame.I) return false;
            return I.DoPlay(view, variant & 3, blastFrom, seed);
        }

        float R() { rs ^= rs << 13; rs ^= rs >> 17; rs ^= rs << 5; return (rs & 0xFFFFFF) / 16777216f; }
        float R(float a, float b) => a + (b - a) * R();

        bool DoPlay(UnitView view, int variant, Vector3 blastFrom, uint seed)
        {
            rs = seed | 1u; R();
            if (frame != Time.frameCount) { frame = Time.frameCount; thisFrame = 0; }
            bool full = thisFrame++ < FullPerFrame;
            Pops++;
            var game = ZGame.I; var root = view.transform;
            // body bounds of the drawn robot (BS-01 3.5 m, the chubby laser robot ~2.3 m)
            parts.Clear();
            bool any = false; var bb = new Bounds(root.position, Vector3.zero);
            foreach (var r in view.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { bb = r.bounds; any = true; } else bb.Encapsulate(r.bounds);
                if (r is MeshRenderer mr && mr.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh && r.bounds.size.magnitude > 0.15f && (!mr.sharedMaterial || mr.sharedMaterial.renderQueue < 2450)) parts.Add(r);
            }
            float baseY = root.position.y, height = any ? Mathf.Clamp(bb.max.y - baseY, 1.2f, 4.5f) : 3.5f, hs = height / 3.5f, sq = Mathf.Sqrt(hs);
            var c = any ? new Vector3(bb.center.x, baseY + height * 0.52f, bb.center.z) : root.position + Vector3.up * height * 0.5f;
            float gy = game.GroundY(c.x, c.z);
            var team = view.Obj.Owner;
            var away = c - blastFrom; away.y = 0f; if (away.sqrMagnitude < 1e-3f) away = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)); away.Normalize();
            var fxp = OpenZFxPlayer.I; bool fx = fxp && fxp.isActiveAndEnabled;
            bool wet = fx && fxp.WetAt(c, gy);
            bool electric = variant == 1;

            // ---- flash: additive core (x12, blooms) + heat-ramp fireball, light + ground light
            if (fx)
            {
                var hot = electric ? new Color(0.78f, 0.9f, 1f) : new Color(1f, 0.93f, 0.78f);
                fxp.EmitChannel("m12_core", c, Vector3.zero, R(0.08f, 0.12f), R(1.1f, 1.5f) * hs * (variant == 3 ? 1.3f : 1f), hot, 1f);
                fxp.EmitChannel("blast_core", c, Vector3.up * 0.4f, R(0.18f, 0.28f), R(1.3f, 1.9f) * hs * (variant == 3 ? 1.35f : 1f), OpenZFxCatalog.WreckCore, 1f);
                if (full)
                {
                    fxp.AddLight(c, electric ? new Color(0.55f, 0.8f, 1f) : new Color(1f, 0.62f, 0.3f), R(26f, 34f) * hs, R(7f, 9f) * sq, 0.22f, 2.2f, 0f, true);
                    fxp.GroundLight(new Vector3(c.x, gy, c.z), variant == 3 ? 0.9f : 0.65f, electric, true);
                }
            }
            // ---- sparks: hitsparks_v1 streak bursts (culled off screen by themselves) + glowing sparks / electric crackle
            int bursts = full ? (variant == 0 ? 3 : 2) : 1;
            for (int i = 0; i < bursts; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), -R(0.4f, 0.9f), Mathf.Sin(a));
                OpenZHitSparks.Burst(c + Vector3.up * R(-0.2f, 0.3f) * hs, dir, FxWeapon.Sniper, FxSurface.Metal, R(1.4f, 1.9f), false);
            }
            if (fx)
            {
                int ns = (int)(R(10f, 18f) * (full ? 1f : 0.4f)) + (variant == 0 ? 6 : 0);
                for (int i = 0; i < ns; i++)
                {
                    float a = R(0f, Mathf.PI * 2f), sp = R(2.5f, 7f) * sq;
                    var vel = new Vector3(Mathf.Cos(a) * sp, R(2.5f, 7.5f) * sq, Mathf.Sin(a) * sp) + away * R(0.5f, 2f);
                    var col = electric ? (i % 2 == 0 ? new Color(0.65f, 0.85f, 1f) : new Color(0.75f, 1f, 0.7f)) : (i % 3 == 0 ? new Color(1f, 0.92f, 0.6f) : new Color(1f, 0.62f, 0.25f));
                    fxp.EmitChannel(electric && i % 2 == 0 ? "m12_pop" : "leg_spark", c + Random.insideUnitSphere * 0.3f * hs, vel, R(0.45f, 0.9f), R(0.1f, 0.18f) * sq, col, 1f);
                }
                // ---- small metal chips (M12 rubble: gravity, floor, rubble shrink)
                int nc = (int)((variant == 2 ? R(10f, 15f) : R(5f, 9f)) * (full ? 1f : 0.5f));
                for (int i = 0; i < nc; i++)
                {
                    float a = R(0f, Mathf.PI * 2f), sp = R(1.5f, 5f) * sq;
                    fxp.EmitChannel("m12_metal", c + Random.insideUnitSphere * 0.4f * hs, new Vector3(Mathf.Cos(a) * sp, R(3f, 7f) * sq, Mathf.Sin(a) * sp) + away * R(0.3f, 1.5f),
                        R(2.4f, 3.6f), R(0.09f, 0.2f) * hs, Color.white, 1f);
                }
                // ---- smoke puff (billowing, heat glow at birth), darker for the scatter variant; a low dust ring for the ring variant
                int np = (int)R(3f, 6f);
                var smoke = variant == 2 ? new Color(0.33f, 0.32f, 0.31f) : new Color(0.5f, 0.48f, 0.45f);
                for (int i = 0; i < np; i++)
                {
                    float a = R(0f, Mathf.PI * 2f);
                    fxp.EmitChannel("blast_smoke", c + new Vector3(Mathf.Cos(a), R(-0.3f, 0.4f), Mathf.Sin(a)) * 0.35f * hs,
                        new Vector3(Mathf.Cos(a) * R(0.5f, 1.3f), R(0.8f, 1.7f), Mathf.Sin(a) * R(0.5f, 1.3f)) * sq, R(1.3f, 2.2f), R(1.1f, 1.8f) * hs, smoke, 0.85f);
                }
                if (variant == 3 && !wet)
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i * 0.628f + R(0f, 0.3f);
                        fxp.EmitChannel("m12_dust", new Vector3(c.x, gy + 0.2f, c.z), new Vector3(Mathf.Cos(a) * 3.2f, 0.35f, Mathf.Sin(a) * 3.2f) * sq, R(0.8f, 1.1f), R(0.9f, 1.3f) * hs, new Color(0.62f, 0.55f, 0.45f), 0.55f);
                    }
                if (wet) fxp.Splash(new Vector3(c.x, fxp.SurfaceY(c), c.z), 0.6f * hs);
            }
            if (!wet) ScorchDecals.Add(new Vector3(c.x, gy, c.z), R(0.85f, 1.3f) * hs, ScorchKind.Explosion);

            // ---- sound: a crunchy pop (3D, Destruction bus, random take + pitch), the electric variant from the fizz takes
            var au = CombatAudio.I;
            if (au) au.PlayPitched(SoundKey, c, Gain, 0f, view.Obj.Id, R(0.92f, 1.1f), electric ? Electric : Bank);

            // OpenZ/Deaths/Bake robot body parts) on their bones of this frame, then EVERY rigid part it carries (weapon, kit, chevrons)
            int real = ThrowBody(view, c, away, sq, full);
            // ---- robot bits: its own rigid parts (kit + gun) and chunky pieces in its colours
            int own = real > 0 ? (full ? parts.Count : Mathf.Min(parts.Count, 1)) : full ? Mathf.Min(parts.Count, (int)R(2f, 4.99f)) : Mathf.Min(parts.Count, 1);
            for (int i = 0; i < own; i++)
            {
                int k = (int)(R() * parts.Count); var src = (MeshRenderer)parts[k]; parts.RemoveAt(k);
                var p = Take(); if (p == null) break;
                p.mf.sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
                p.mr.sharedMaterials = src.sharedMaterials;
                copy ??= new MaterialPropertyBlock();
                for (int m = 0; m < src.sharedMaterials.Length; m++) { src.GetPropertyBlock(copy, m); p.mr.SetPropertyBlock(copy, m); } p.glow = OzGlowFade.HasGlow(p.mr) ? OzGlowFade.Steps + 2 : 0;
                OzDeadTint.PartBegin(p.mr);                                                                // item 228: a dead robot's part: its colour drains over OzDeadTint.Seconds (LateUpdate)
                p.mr.shadowCastingMode = ShadowCastingMode.On;
                p.t.SetPositionAndRotation(src.transform.position, src.transform.rotation); p.t.localScale = src.transform.lossyScale;
                var ext = src.bounds.extents; p.rad = Mathf.Clamp(Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) + 0.05f, 0.08f, 0.6f);
                Launch(p, c, away, sq, 1f);
                p.heavy = ext.magnitude > 0.35f; if (real > 0 && full) p.smoke = R(0.3f, 0.6f); p.dead = 0;   // PD1: the gun clangs down; item 228: the dead colour fades in
            }
            if (!EnsureKit(view)) return true;
            var teamC = OzDeadTint.PopPieceLive(Color.Lerp(TeamColors.Of(team), new Color(0.5f, 0.5f, 0.5f), 0.15f));   // item 228: the shoulder domes and team plates start live and drain to the dead colour with every other piece (OzDeadTint.cs)
            Color gun = new Color(0.42f, 0.44f, 0.47f), dark = new Color(0.2f, 0.21f, 0.23f), bone = new Color(0.86f, 0.82f, 0.7f), brass = new Color(0.8f, 0.62f, 0.3f), steel = new Color(0.66f, 0.68f, 0.7f);
            bool laser = view.Obj.Type == "laser";
            int n = real > 0 ? (full ? (variant == 2 ? (int)R(4f, 6.99f) : (int)R(2f, 4.99f)) : 0)          // PD1: the real parts fly, only a few loose plates / bolts
                  : full ? (variant == 2 ? (int)R(9f, 13f) : (int)R(5f, 8f)) : 2;
            for (int i = 0; i < n; i++)
            {
                var p = Take(); if (p == null) break;
                Mesh mesh; Color col; Vector3 s;
                int kind = real > 0 ? (int)R(2f, 6.99f) : i < 2 ? 0 : i < 4 ? 1 : (int)R(2f, 6.99f);
                switch (kind)
                {
                    case 0: mesh = dome; col = teamC; s = Vector3.one * R(0.38f, 0.48f); break;                                    // shoulder pad
                    case 1: mesh = laser ? box : boot; col = dark; s = new Vector3(R(0.36f, 0.44f), R(0.26f, 0.32f), R(0.5f, 0.62f)); break;   // boot
                    case 2: mesh = box; col = R() < 0.35f ? teamC : laser ? gun : (R() < 0.5f ? gun : bone); s = new Vector3(R(0.4f, 0.6f), R(0.08f, 0.13f), R(0.3f, 0.45f)); break;   // armour plate
                    case 3: mesh = gear; col = brass; s = new Vector3(R(0.3f, 0.42f), R(0.08f, 0.1f), R(0.3f, 0.42f)); break;       // chest gauge
                    case 4: mesh = rod; col = gun; s = new Vector3(R(0.12f, 0.16f), R(0.5f, 0.75f), R(0.12f, 0.16f)); break;        // leg rod
                    default: mesh = hex; col = steel; s = Vector3.one * R(0.13f, 0.2f); break;                                     // bolt / nut
                }
                p.mf.sharedMesh = mesh; p.mr.sharedMaterial = pieceMat;
                block ??= new MaterialPropertyBlock(); block.Clear(); block.SetColor(BaseColorId, col); p.mr.SetPropertyBlock(block);
                p.mr.shadowCastingMode = s.magnitude * hs > 0.35f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                p.t.SetPositionAndRotation(c + Random.insideUnitSphere * 0.45f * hs, Random.rotation); p.t.localScale = s * hs;
                p.rad = Mathf.Clamp(Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.5f * hs + 0.03f, 0.04f, 0.4f);
                Launch(p, c, away, sq, kind >= 5 ? 1.25f : 1f); p.dead = 0;                             // item 228: every piece of a pop is a dead robot's
            }
            return true;
        }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Launch(Piece p, Vector3 c, Vector3 away, float sq, float k)
        {
            float a = R(0f, Mathf.PI * 2f), sp = R(1.8f, 5.5f) * sq * k;
            var radial = p.t.position - c; radial.y = 0f;
            var dir = radial.sqrMagnitude > 0.01f ? (radial.normalized * 0.6f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.4f).normalized : new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            p.v = dir * sp + Vector3.up * R(4f, 8.5f) * sq * k + away * R(0.8f, 2.4f) * sq;
            p.w = Random.onUnitSphere * R(300f, 900f);
            p.age = 0f; p.life = R(PieceLifeMin, PieceLifeMax); p.rest = p.popping = p.wet = p.landed = false; p.bounces = 0; p.active = true;
            p.smoke = 0f; p.smokeT = 0f; p.heavy = p.concrete = false; p.dead = -1;
            p.go.SetActive(true);
        }

        // ------------------------------------------------------------------------------------------------ PD1: real body parts
        /// <summary>A robot type's baked body parts (Resources/OpenZ/RobotParts/&lt;type&gt;, mesh / file name "NN_&lt;bone&gt;@cx,cy,cz").</summary>
        public sealed class PartSet { public Mesh[] mesh; public string[] bone; public Vector3[] centre; }
        static readonly Dictionary<string, PartSet> partSets = new Dictionary<string, PartSet>();
        /// <summary>Body parts thrown so far (Play proof).</summary>
        public static int BodyPartsThrown { get; private set; }

        public static PartSet PartsOf(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            if (partSets.TryGetValue(type, out var ps)) return ps;
            var ms = Resources.LoadAll<Mesh>("OpenZ/RobotParts/" + type);
            if (ms != null && ms.Length > 0)
            {
                ps = new PartSet { mesh = ms, bone = new string[ms.Length], centre = new Vector3[ms.Length] };
                for (int i = 0; i < ms.Length; i++)
                {
                    var nm = ms[i].name; int at = nm.LastIndexOf('@');
                    ps.bone[i] = at > 0 ? nm.Substring(0, at) : nm;
                    if (ps.bone[i].Length > 3 && char.IsDigit(ps.bone[i][0]) && char.IsDigit(ps.bone[i][1]) && ps.bone[i][2] == '_') ps.bone[i] = ps.bone[i].Substring(3);   // "NN_" order prefix
                    if (at > 0) { var f = nm.Substring(at + 1).Split(','); if (f.Length == 3) ps.centre[i] = new Vector3(Pf(f[0]), Pf(f[1]), Pf(f[2])); }
                }
            }
            partSets[type] = ps;                                                                        // null cached too (no parts baked: old kit pieces)
            return ps;
        }
        static float Pf(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0f;

        int ThrowBody(UnitView view, Vector3 c, Vector3 away, float sq, bool full)
        {
            var ps = PartsOf(view.Obj.Type); if (ps == null) return 0;
            SkinnedMeshRenderer body = null;
            foreach (var s in view.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (s.enabled && s.sharedMesh && (!body || s.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = s;
            if (!body) return 0;
            var bones = body.bones; var mats = body.sharedMaterials; int n = 0;
            copy ??= new MaterialPropertyBlock();
            for (int i = 0; i < ps.mesh.Length; i++)
            {
                Transform b = null; foreach (var t in bones) if (t && t.name == ps.bone[i]) { b = t; break; }
                if (!b) continue;
                var p = Take(); if (p == null) break;
                p.mf.sharedMesh = ps.mesh[i]; p.mr.sharedMaterials = mats;
                for (int m = 0; m < mats.Length; m++) { body.GetPropertyBlock(copy, m); p.mr.SetPropertyBlock(copy, m); }
                OzDeadTint.PartBegin(p.mr);                                                                // item 228: a dead robot's chunk: its colour drains over OzDeadTint.Seconds (LateUpdate)
                p.glow = OzGlowFade.HasGlow(p.mr) ? OzGlowFade.Steps + 2 : 0;
                p.mr.shadowCastingMode = ShadowCastingMode.On;
                var sc = b.lossyScale;
                p.t.SetPositionAndRotation(b.TransformPoint(ps.centre[i]), b.rotation); p.t.localScale = sc;
                var ext = Vector3.Scale(ps.mesh[i].bounds.extents, new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z)));
                p.rad = Mathf.Clamp(Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) + 0.04f, 0.08f, 0.6f);
                Launch(p, c, away, sq, full ? 1f : 0.8f);
                p.smoke = full ? R(0.5f, 0.9f) : 0f; p.heavy = ext.magnitude > 0.3f; p.dead = 0;
                n++;
            }
            BodyPartsThrown += n;
            return n;
        }

        // ------------------------------------------------------------------------------------------------ HC1: parts knocked off by heavy hits
        static readonly Dictionary<int, int> knocked = new Dictionary<int, int>();
        static readonly Regex NoKnock = new Regex("wheel|track|tread|tyre|tire|hull|body|chassis|pedestal|barrel|muzzle|turret|model|shadow|label|glow|flame|decal|OZ_|gun|cannon|rack|launcher|pod|rocket|missile|weapon|recoil", RegexOptions.IgnoreCase);
        /// <summary></summary>
        static readonly Regex GunRig = new Regex("barrel|gun|cannon|launcher|rack|recoil|weapon|pitch", RegexOptions.IgnoreCase);
        static bool InGunRig(Transform t, Transform root)
        {
            for (var p = t.parent; p && p != root; p = p.parent) if (GunRig.IsMatch(p.name)) return true;
            return false;
        }
        /// <summary>HC1: at most this many real parts come off one vehicle / cannon (it stays readable, never stripped bare).</summary>
        public static int MaxKnockPerUnit = 3;
        public static int KnockOffs { get; private set; }
        public static int ChipsThrown { get; private set; }

        /// <summary>HC1: a heavy hit at `at` knocks the nearest small real part (antenna, box, light, hatch, rack, ...) off the model under
        /// `root`: it vanishes from the model and a copy is flung away from the blast (smoke trail, clang when it lands) with a metal
        /// tear at the hit point. False when no part is close enough or the unit already lost its share.</summary>
        public static bool KnockOff(Transform root, int id, Vector3 at, Vector3 blastFrom, uint seed)
        {
            if (!root || !ZGame.I) return false;
            knocked.TryGetValue(id, out var done); if (done >= MaxKnockPerUnit) return false;
            MeshRenderer best = null; float bestD = 2.2f;
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (!mr.enabled || !mr.TryGetComponent<MeshFilter>(out var mf) || !mf.sharedMesh || mr.transform.childCount > 0) continue;
                if (mr.sharedMaterial && mr.sharedMaterial.renderQueue >= 2450) continue;
                var b = mr.bounds; float size = b.size.magnitude;
                if (size < 0.25f || size > 1.8f || NoKnock.IsMatch(mr.name) || InGunRig(mr.transform, root)) continue;
                float d = Mathf.Sqrt(b.SqrDistance(at));
                if (d < bestD) { bestD = d; best = mr; }
            }
            return best && I.DoKnock(best, at, blastFrom, seed, id);
        }

        bool DoKnock(MeshRenderer src, Vector3 at, Vector3 from, uint seed, int id)
        {
            rs = seed | 1u; R();
            var p = Take(); if (p == null) return false;
            p.mf.sharedMesh = src.GetComponent<MeshFilter>().sharedMesh; p.mr.sharedMaterials = src.sharedMaterials;
            copy ??= new MaterialPropertyBlock();
            for (int m = 0; m < src.sharedMaterials.Length; m++) { src.GetPropertyBlock(copy, m); p.mr.SetPropertyBlock(copy, m); } p.glow = OzGlowFade.HasGlow(p.mr) ? OzGlowFade.Steps + 2 : 0;
            p.mr.shadowCastingMode = ShadowCastingMode.On;
            p.t.SetPositionAndRotation(src.transform.position, src.transform.rotation); p.t.localScale = src.transform.lossyScale;
            var bc = src.bounds.center; var ext = src.bounds.extents;
            p.rad = Mathf.Clamp(Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z)) + 0.05f, 0.08f, 0.6f);
            var away = bc - from; away.y = 0f; if (away.sqrMagnitude < 1e-3f) away = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)); away.Normalize();
            Launch(p, bc, away, 1f, 0.9f);
            p.v += away * R(2f, 4f); p.smoke = R(0.5f, 0.8f); p.heavy = true;
            src.gameObject.SetActive(false);                                                            // gone from the model for good (the wreck breaks without it)
            knocked[id] = KnockedCount(id) + 1; KnockOffs++;
            OpenZHitSparks.Burst(at, -away, FxWeapon.MachineGun, FxSurface.Metal, 1.5f, false);
            AudioDirector.Fire("dst_debris_metal", at, id, 0.85f, R(1.0f, 1.15f));                    // the tear / clang at the hit point
            return true;
        }
        static int KnockedCount(int id) => knocked.TryGetValue(id, out var k) ? k : 0;

        /// <summary>HC1: loose chips from a heavy hit: gunmetal plates / bolts in the unit's colours, or concrete chunks off a building
        /// (they clatter / crumble when they land).</summary>
        public static void Chips(Vector3 at, Vector3 away, Color col, int n, float size, uint seed, bool concrete)
        {
            if (!ZGame.I || n <= 0) return;
            I.DoChips(at, away, col, n, size, seed, concrete);
        }

        void DoChips(Vector3 at, Vector3 away, Color col, int n, float size, uint seed, bool concrete)
        {
            if (!EnsureKit(null)) return;
            rs = seed | 1u; R();
            away.y = 0f; if (away.sqrMagnitude < 1e-3f) away = Vector3.forward; away.Normalize();
            var gunC = new Color(0.42f, 0.44f, 0.47f);
            for (int i = 0; i < n; i++)
            {
                var p = Take(); if (p == null) break;
                Mesh mesh = concrete ? box : (i % 3 == 2 ? hex : box);
                var c = concrete ? Color.Lerp(col, new Color(0.55f, 0.53f, 0.5f), R(0f, 0.4f)) : (i % 2 == 0 ? col : gunC);
                var s = concrete ? new Vector3(R(0.25f, 0.45f), R(0.18f, 0.32f), R(0.22f, 0.4f)) : i % 3 == 2 ? Vector3.one * R(0.12f, 0.18f) : new Vector3(R(0.3f, 0.5f), R(0.06f, 0.1f), R(0.22f, 0.36f));
                s *= size;
                p.mf.sharedMesh = mesh; p.mr.sharedMaterial = pieceMat;
                block ??= new MaterialPropertyBlock(); block.Clear(); block.SetColor(BaseColorId, c); p.mr.SetPropertyBlock(block);
                p.mr.shadowCastingMode = s.magnitude > 0.3f ? ShadowCastingMode.On : ShadowCastingMode.Off;
                p.t.SetPositionAndRotation(at + Random.insideUnitSphere * 0.2f, Random.rotation); p.t.localScale = s;
                p.rad = Mathf.Clamp(Mathf.Min(s.x, Mathf.Min(s.y, s.z)) * 0.5f + 0.03f, 0.04f, 0.4f);
                Launch(p, at, away, 0.85f, 1f);
                p.v += away * R(1f, 3f); p.concrete = concrete;
                ChipsThrown++;
            }
        }

        /// <summary>A free piece; its glow is looked at on its first frame.</summary>
        Piece Take() { var p = TakeRaw(); if (p != null) p.glow = -1; return p; }

        Piece TakeRaw()
        {
            foreach (var p in pieces) if (!p.active) return p;
            if (pieces.Count < MaxPieces)
            {
                var go = new GameObject("OZ_PopPiece"); go.transform.SetParent(transform, false);
                var p = new Piece { go = go, t = go.transform, mf = go.AddComponent<MeshFilter>(), mr = go.AddComponent<MeshRenderer>() };
                p.mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
                p.mr.receiveShadows = true;
                pieces.Add(p); return p;
            }
            Piece best = null;                                                                          // full pool: the oldest resting piece
            foreach (var p in pieces) if (p.rest && !p.popping && (best == null || p.age > best.age)) best = p;
            if (best == null) return null;
            Retire(best); return best;
        }

        void Retire(Piece p) { p.active = false; p.popping = false; if (p.go) { p.go.SetActive(false); p.t.localScale = Vector3.one; } }

        void LateUpdate()
        {
            float dt = Time.deltaTime; if (dt <= 0f) return;
            var game = ZGame.I; if (!game) return;
            var fxp = OpenZFxPlayer.I; bool fx = fxp && fxp.isActiveAndEnabled;
            foreach (var p in pieces)
            {
                if (!p.active || p.popping) continue;
                p.age += dt;
                if (p.glow < 0) p.glow = OzGlowFade.HasGlow(p.mr) ? OzGlowFade.Steps + 2 : 0;
                if (p.glow > 0 && OzGlowFade.On)
                {   // a thrown part with a glow part or team paint: it goes dark as a body does
                    int gs = Mathf.CeilToInt((1f - Mathf.Clamp01(p.age / OzGlowFade.Seconds)) * OzGlowFade.Steps);
                    if (gs + 1 != p.glow) { p.glow = gs + 1; OzGlowFade.Dim(p.mr, gs / (float)OzGlowFade.Steps); if (gs == 0) p.glow = 0; }
                }
                if (p.dead >= 0 && OzDeadTint.On)
                {   // item 228: a dead robot's thrown part / chunk / piece: its colour drains to the dead colour over OzDeadTint.Seconds, as the body's does
                    int dn = Mathf.Min(OzDeadTint.Steps, Mathf.FloorToInt(Mathf.Clamp01(p.age / OzDeadTint.Seconds) * OzDeadTint.Steps));
                    if (dn != p.dead) { p.dead = dn; OzDeadTint.Set(p.mr, dn / (float)OzDeadTint.Steps); if (dn >= OzDeadTint.Steps) { OzDeadTint.PartsFaded++; p.dead = -1; } }
                }
                if (p.age >= p.life)
                {
                    p.popping = true; var pp = p;
                    PopOut.Begin(p.t, PopOut.Style.Debris, () => Retire(pp), false, false);
                    continue;
                }
                if (p.rest) continue;
                if (fx && p.smoke > 0f && p.age < p.smoke && (p.smokeT -= dt) <= 0f)
                {
                    p.smokeT = 0.07f;
                    fxp.EmitChannel("blast_smoke", p.t.position, Vector3.up * 0.4f - p.v * 0.05f, R(0.5f, 0.8f), R(0.22f, 0.38f) * (p.heavy ? 1.3f : 1f), new Color(0.3f, 0.29f, 0.28f), 0.6f);
                }
                p.v.y -= Gravity * dt;
                p.v *= 1f - Mathf.Min(0.3f * dt, 0.5f);
                var pos = p.t.position + p.v * dt;
                p.t.rotation = Quaternion.AngleAxis(p.w.magnitude * dt, p.w.sqrMagnitude > 1e-6f ? p.w.normalized : Vector3.up) * p.t.rotation;
                float gy = game.GroundY(pos.x, pos.z);
                if (!p.wet && fx && p.v.y < 0f && OzWaterLanding.OpenWaterAt(pos, gy, out float wy) && pos.y - p.rad <= wy)
                {   // b27, the shared water landing rule: the splash, the rings and the sound of its size (its smoke trail goes out). A big part
                    // then rests partly under the surface until its time is up; a small bit sinks slowly and pops out on the bed as before
                    p.wet = true; bool smoking = p.smoke > 0f && p.age < p.smoke; p.smoke = 0f;
                    if (OzWaterLanding.Enter(p.t, "robot part", p.v, smoking)) { p.v = Vector3.zero; p.w = Vector3.zero; p.rest = true; continue; }
                    p.v *= 0.15f; p.w *= 0.3f; p.life = Mathf.Min(p.life, p.age + 1.4f);
                }
                if (p.wet) { p.v.y = Mathf.Max(p.v.y, -0.8f); p.v.x *= 0.9f; p.v.z *= 0.9f; }
                if (pos.y - p.rad < gy)
                {
                    pos.y = gy + p.rad;
                    if (p.v.y < -1.6f && !p.wet && p.bounces < 3)
                    {
                        p.v.y = -p.v.y * R(0.3f, 0.45f); p.v.x *= 0.6f; p.v.z *= 0.6f; p.w *= 0.55f; p.bounces++;
                        if (!p.landed) { p.landed = true; Land(p, pos); }
                    }
                    else { p.v = Vector3.zero; p.w = Vector3.zero; p.rest = true; }
                }
                p.t.position = pos;
            }
        }

        /// <summary>
        /// chips crumble (dst_rock_chip), small metal bits tink.</summary>
        void Land(Piece p, Vector3 at)
        {
            if (p.concrete) { if (Time.time - lastClink >= 0.07f) { lastClink = Time.time; AudioDirector.Fire("dst_rock_chip", at, -1, 0.45f, R(0.9f, 1.2f)); } return; }
            if (p.heavy) { if (Time.time - lastClang >= 0.1f) { lastClang = Time.time; AudioDirector.Fire("dst_debris_metal", at, -1, 0.5f, R(0.95f, 1.2f)); } return; }
            Clink(at, p.rad);
        }
        float lastClang = -10f;

        void Clink(Vector3 at, float rad)
        {
            if (rad < 0.12f || Time.time - lastClink < 0.07f) return;
            lastClink = Time.time;
            var au = CombatAudio.I; if (!au) return;
            au.PlayPitched(OpenZHitSparks.SoundKey, at, 0.22f, 0f, -1, R(0.85f, 1.25f), OpenZHitSparks.Tink);   // bits clatter on the ground
        }

        // ------------------------------------------------------------------------------------------------ kit: meshes + material
        bool EnsureKit(UnitView view)
        {
            if (pieceMat && dome) return true;
            if (!pieceMat)
            {
                Material src = null;
                if (view)                                                                                // HC1 chips: no view
                    foreach (var r in view.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials) if (m && m.shader && m.shader.name == "OpenZ/TeamLit") { src = m; break; }
                if (src) pieceMat = new Material(src) { name = "OZ_PopPiece" };
                else
                {
                    var sh = Shader.Find("OpenZ/TeamLit"); if (!sh) sh = Shader.Find("Universal Render Pipeline/Lit");
                    if (!sh) return false;
                    pieceMat = new Material(sh) { name = "OZ_PopPiece" };
                }
                void F(string n, float v) { if (pieceMat.HasProperty(n)) pieceMat.SetFloat(n, v); }
                if (pieceMat.HasProperty("_BaseMap")) pieceMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (pieceMat.HasProperty("_BaseColor")) pieceMat.SetColor("_BaseColor", Color.white);
                if (pieceMat.HasProperty("_EmissionColor")) pieceMat.SetColor("_EmissionColor", Color.black);
                F("_TeamMode", 0f); F("_UseVertexColor", 1f); F("_VertexColorSRGB", 0f); F("_Accent", 0f); F("_CreamToTeam", 0f);
                F("_UseNormalMap", 0f); F("_UseMaskMap", 0f); F("_Triplanar", 0f); F("_TopBlend", 0f); F("_Smoothness", 0.45f); F("_Metallic", 0.15f);
                pieceMat.DisableKeyword("_NORMALMAP");
            }
            if (!dome)
            {
                dome = Dome(8, 3); box = Box(0.18f, 0f); boot = Box(0.22f, 0.25f); hex = Prism(6, 1f); gear = Prism(10, 1f); rod = Prism(6, 1f);
            }
            return true;
        }

        // Low-poly flat-shaded meshes, unit sized, centred; vertex colour = soft baked AO (darker underneath).
        static Mesh Build(List<Vector3> v, List<int> t, string name)
        {
            var mv = new List<Vector3>(); var mn = new List<Vector3>(); var mc = new List<Color>(); var mt = new List<int>();
            float lo = float.MaxValue, hi = float.MinValue; foreach (var p in v) { lo = Mathf.Min(lo, p.y); hi = Mathf.Max(hi, p.y); }
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]]; var n = Vector3.Cross(b - a, c - a).normalized;
                foreach (var p in new[] { a, b, c })
                {
                    float ao = Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(lo, hi, p.y));
                    mt.Add(mv.Count); mv.Add(p); mn.Add(n); mc.Add(new Color(ao, ao, ao, 1f));
                }
            }
            var m = new Mesh { name = name }; m.SetVertices(mv); m.SetNormals(mn); m.SetColors(mc); m.SetTriangles(mt, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Unit box (-.5..+.5) with its top edges chamfered by `ch` and the top narrowed by `taper` (boot / chunky plate).</summary>
        static Mesh Box(float ch, float taper)
        {
            float h = 0.5f, it = h * (1f - taper);
            var v = new List<Vector3>
            {
                new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h), new Vector3(-h, -h, h),                    // 0-3 bottom
                new Vector3(-h, h - ch, -h), new Vector3(h, h - ch, -h), new Vector3(h, h - ch, h), new Vector3(-h, h - ch, h),    // 4-7 shoulder
                new Vector3(-it + ch, h, -it + ch), new Vector3(it - ch, h, -it + ch), new Vector3(it - ch, h, it - ch), new Vector3(-it + ch, h, it - ch),   // 8-11 top
            };
            var t = new List<int>();
            void Q(int a, int b, int c, int d) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }
            Q(0, 1, 2, 3);                                                   // bottom (faces down)
            Q(0, 4, 5, 1); Q(1, 5, 6, 2); Q(2, 6, 7, 3); Q(3, 7, 4, 0);      // sides
            Q(4, 8, 9, 5); Q(5, 9, 10, 6); Q(6, 10, 11, 7); Q(7, 11, 8, 4);  // chamfer ring
            Q(8, 11, 10, 9);                                                 // top
            FixWinding(v, t);
            return Build(v, t, "OZ_PopBox");
        }

        /// <summary>n-sided prism (hex bolt, gauge wheel, rod), unit size.</summary>
        static Mesh Prism(int n, float h)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, -h * 0.5f, Mathf.Sin(a) * 0.5f)); }
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, h * 0.5f, Mathf.Sin(a) * 0.5f)); }
            int cb = v.Count; v.Add(new Vector3(0, -h * 0.5f, 0)); int ct = v.Count; v.Add(new Vector3(0, h * 0.5f, 0));
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                t.Add(i); t.Add(j); t.Add(n + j); t.Add(i); t.Add(n + j); t.Add(n + i);
                t.Add(cb); t.Add(j); t.Add(i); t.Add(ct); t.Add(n + i); t.Add(n + j);
            }
            FixWinding(v, t);
            return Build(v, t, "OZ_PopPrism" + n);
        }

        /// <summary>Half dome with a flat base (the BS-01 round shoulder pad), unit size.</summary>
        static Mesh Dome(int lon, int lat)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float el = j * (Mathf.PI * 0.5f) / (lat + 0.0001f), y = Mathf.Sin(el) * 0.5f - 0.2f, r = Mathf.Cos(el) * 0.5f;
                for (int i = 0; i < lon; i++) { float a = i * Mathf.PI * 2f / lon; v.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r)); }
            }
            int top = v.Count; v.Add(new Vector3(0, 0.3f, 0)); int bot = v.Count; v.Add(new Vector3(0, -0.2f, 0));
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * lon + i, b = j * lon + (i + 1) % lon, c = (j + 1) * lon + (i + 1) % lon, d = (j + 1) * lon + i;
                    t.Add(a); t.Add(d); t.Add(c); t.Add(a); t.Add(c); t.Add(b);
                }
            for (int i = 0; i < lon; i++) { t.Add(top); t.Add(lat * lon + (i + 1) % lon); t.Add(lat * lon + i); t.Add(bot); t.Add(i); t.Add((i + 1) % lon); }
            FixWinding(v, t);
            return Build(v, t, "OZ_PopDome");
        }

        /// <summary>Convex shapes centred near the origin: every triangle faces away from the centroid.</summary>
        static void FixWinding(List<Vector3> v, List<int> t)
        {
            var c = Vector3.zero; foreach (var p in v) c += p; c /= Mathf.Max(1, v.Count);
            for (int i = 0; i < t.Count; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, d - a), (a + b + d) / 3f - c) < 0f) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; }
            }
        }

        void OnDestroy() { if (inst == this) inst = null; if (pieceMat) Destroy(pieceMat); }
    }
}
