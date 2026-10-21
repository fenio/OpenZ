using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// VIEW ONLY: the sim's missile (damage, radius, the
    /// explode time) is untouched; the shell flies the same ballistic arc over the same real time the old projectile used
    /// (OpenZFxPlayer.RealFlight(delay, gameSpeed), g = OpenZFxCatalog.GravityOf(Cannon) = 9.8, v0 = (to - from) / T +
    /// g T / 2 up: OpenZFxPlayer.Launch's formula), so it lands on the sim's blast frame.
    /// <list type="bullet">
    /// <item>Who: the howitzer cannon (LegacyMatch.Launch, type "howitzer"; before this it drew OpenZFxCatalog's Cannon shell at
    /// bodyScale 1 = a 0.45 m FX_Shell) and the fort's big gun (FortGunView.LaunchShell: a 0.92 m capsule). Both impacts:
    /// LegacyMatch.MissileBlast (the sim's MissileExploded, howitzer / DV7 fort gun) and FortGunView.Impact (cosmetic shells).</item>
    /// <item><see cref="HowitzerLength"/><see cref="FortLength"/>
    /// shell), dark gunmetal with a hot rim (FxLit), a white-hot base glow (FxAdditive flame mesh, HDR), a LEG halo, a small
    /// orange point light that lights the ground under it, a heat-shimmer quad behind it (OpenZ/HeatDistort) and a thick
    /// continuous smoke trail emitted by DISTANCE (overlapping puffs every .4 x their size, like the rocket trail).</item>
    /// <item>Whistle: sd_shell_whistle_a / _b (1.33 s, Resources/OpenZ/Audio/Sfx) on a 3D source that rides the shell from the
    /// apex, timed so the whistle ENDS on the impact (started part-way for short flights), pitch falling 1.05 -> .9.</item>
    /// <item>Muzzle: OpenZFxPlayer muzzle + a big launch flash with ground light, a ring of bore smoke, a forward blast cloud,
    /// pitch), camera shake by distance.</item>
    /// <item>Impact (on top of the table blast, LegacyMatch.HowitzerShell unchanged): crater (ScorchDecals explosion + contact
    /// mark), stone debris (FxManager.Breakup), dust ring + dark column, sparks, flash light + ground light, a deep thump
    /// On water: a big splash instead of crater and dust.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class OpenZHeavyShells : MonoBehaviour
    {
        public static bool Enabled = true;
        public static OpenZHeavyShells I { get; private set; }

        // shell look
        public static float HowitzerLength = 2.4f, FortLength = 2.2f;                 // m (FX_Shell is 0.45 m long, 0.19 m wide)
        public static float GlowGain = 1.4f, HaloSize = 1.3f, LightIntensity = 3.2f, LightRange = 9f;
        public static float ShimmerStrength = 0.012f, ShimmerSize = 3.2f;
        public static float SmokeSize = 1.65f, SmokeLife = 3.2f, SmokeAlpha = 0.6f, SmokeSpacing = 0.28f;   // wide, soft puffs that overlap into one column
        public static Color SmokeDark = new Color(0.26f, 0.255f, 0.25f), SmokeGrey = new Color(0.44f, 0.43f, 0.42f);   // trail puffs: dark to mid grey, never pale, still seen at night
        /// <summary>Modelled shell bodies (OpenZShellMesh). False = the old baked FX_Shell body, for a side-by-side comparison only.</summary>
        public static bool Modelled = true;
        public static float WhistleVolume = 0.9f, WhistleMinFlight = 0.45f;
        // muzzle
        public static float MuzzleFlashK = 2.4f, MuzzleGroundLight = 1.8f, MuzzleRingRadius = 9f, MuzzleRingSeconds = 0.38f, MuzzleShake = 0.3f;
        // impact
        public static float CraterRadius = 3.4f, ImpactShake = 1.0f, ImpactShakeRange = 75f, ImpactLight = 26f;

        sealed class Shell
        {
            public bool active, fort, whistled; public Vector3 p0, v0, pos, prev; public float g, T, age, smokeRem, emberT, whistleAt = -1f;
            public Action<Vector3> onLand; public AudioSource src; public Light light;
        }
        sealed class Ring { public bool active; public Vector3 c; public float age, life, r1; public Material mat; public GameObject go; }

        readonly List<Shell> shells = new List<Shell>();
        readonly List<Ring> rings = new List<Ring>();
        readonly List<AudioSource> whistles = new List<AudioSource>();
        readonly List<Light> lights = new List<Light>();
        Mesh shellMesh, sphere, ringMesh, fortMesh, howitzerMesh; Vector3 nose = Vector3.forward; float shellLen = 0.45f;
        Material bodyMat, bandMat, baseMat, hotMat, shimmerMat, ringShaderMat; AudioClip[] whistleClips;
        /// <summary>
        /// and the howitzer, 4-6 variations each picked without repeats): recorded takes (Resources/OpenZ/Audio/Sfx, a crescendo that ends on
        /// </summary>
        public static readonly string[] BankIncomingFort = { "oz_incoming_fort_a", "oz_incoming_fort_b", "oz_incoming_fort_c", "oz_incoming_fort_d", "oz_incoming_fort_e" };
        public static readonly string[] BankIncomingHowitzer = { "oz_incoming_howitzer_a", "oz_incoming_howitzer_b", "oz_incoming_howitzer_c", "oz_incoming_howitzer_d", "oz_incoming_howitzer_e" };
        AudioClip[] fortWhistles, howitzerWhistles; readonly List<int> fortBag = new List<int>(), howBag = new List<int>(); int fortLast = -1, howLast = -1;
        /// <summary>Stats (Play proof): heavy whistles started per bank.</summary>
        public static int FortWhistles, HowitzerWhistles;
        static AudioClip[] LoadBank(string[] names)
        {
            var l = new List<AudioClip>();
            foreach (var n in names) { var c = Resources.Load<AudioClip>("OpenZ/Audio/Sfx/" + n); if (c) l.Add(c); }
            if (l.Count == 0) Debug.LogWarning("[OpenZ] heavy shells: " + names[0] + ".. missing in Resources/OpenZ/Audio/Sfx (old whistles)");
            return l.ToArray();
        }
        /// <summary>Shuffle bag: every take once per round, never the last take of the previous round first.</summary>
        AudioClip NextWhistle(bool fort)
        {
            var bank = fort ? fortWhistles : howitzerWhistles; var bag = fort ? fortBag : howBag;
            if (bank == null || bank.Length == 0) return null;
            if (bag.Count == 0)
            {
                for (int i = 0; i < bank.Length; i++) bag.Add(i);
                for (int i = bag.Count - 1; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); int t = bag[i]; bag[i] = bag[j]; bag[j] = t; }
                int last = fort ? fortLast : howLast;
                if (bag.Count > 1 && bag[0] == last) { int t = bag[0]; bag[0] = bag[bag.Count - 1]; bag[bag.Count - 1] = t; }
            }
            int k = bag[0]; bag.RemoveAt(0);
            if (fort) { fortLast = k; FortWhistles++; } else { howLast = k; HowitzerWhistles++; }
            return bank[k];
        }
        RtsCamera rts; float nextRts;

        public static OpenZHeavyShells Ensure()
        {
            if (I) return I;
            if (!Application.isPlaying) return null;
            I = new GameObject("OpenZHeavyShells").AddComponent<OpenZHeavyShells>();
            return I;
        }

        void Awake()
        {
            if (I && I != this) { Destroy(gameObject); return; }
            I = this;
            BakeShell();
            fortMesh = OpenZShellMesh.Fort(); howitzerMesh = OpenZShellMesh.Howitzer();
            sphere = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
            var lit = Resources.Load<Material>("OpenZ/FX/M_FxLit");
            if (lit)
            {
                bodyMat = new Material(lit) { name = "HeavyShellBody (runtime)", enableInstancing = true };
                bodyMat.SetColor("_BaseColor", new Color(0.15f, 0.16f, 0.18f));                         // ZF5: dark gunmetal shell (was .30/.33/.35 + a hot rim), never black
                bodyMat.SetColor("_EmissionColor", Hdr(new Color(0.5f, 0.55f, 0.62f), 0.02f));               // a floor: never pure black at night
                bodyMat.SetColor("_RimColor", Hdr(new Color(0.55f, 0.6f, 0.68f), 0.1f)); bodyMat.SetFloat("_RimPower", 0.6f);   // ZF5: a faint metal edge, no glowing rim
                bodyMat.SetFloat("_Unlit", 0f); bodyMat.SetFloat("_Alpha", 1f);
                bandMat = new Material(lit) { name = "HeavyShellBand (runtime)", enableInstancing = true };          // driving band(s) and fuze: copper
                bandMat.SetColor("_BaseColor", new Color(0.50f, 0.27f, 0.11f)); bandMat.SetColor("_EmissionColor", Hdr(new Color(0.50f, 0.27f, 0.11f), 0.05f));
                bandMat.SetFloat("_RimPower", 0f); bandMat.SetFloat("_Unlit", 0f); bandMat.SetFloat("_Alpha", 1f);
                baseMat = new Material(lit) { name = "HeavyShellBase (runtime)", enableInstancing = true };          // base cup: hot orange face-on, deep red towards its edge
                baseMat.SetColor("_BaseColor", new Color(0.12f, 0.05f, 0.03f)); baseMat.SetColor("_EmissionColor", Hdr(new Color(1f, 0.42f, 0.10f), 1.8f));
                baseMat.SetColor("_RimColor", Hdr(new Color(1f, 0.2f, 0.04f), 1.2f)); baseMat.SetFloat("_RimPower", 2f);
                baseMat.SetFloat("_Unlit", 0f); baseMat.SetFloat("_Alpha", 1f);
            }
            var add = Resources.Load<Material>("OpenZ/FX/M_FxAdditive");
            if (add)
            {
                hotMat = new Material(add) { name = "HeavyShellHot (runtime)", renderQueue = 3043 };
                hotMat.SetFloat("_Shape", 8f); hotMat.SetFloat("_UseVertexColor", 0f); hotMat.SetFloat("_Opaqueness", 0f); hotMat.SetFloat("_Intensity", 1f);
                hotMat.SetVector("_Color", new Vector4(1f, 0.36f, 0.06f, 1f) * GlowGain); hotMat.SetVector("_CoreColor", new Vector4(1f, 0.52f, 0.16f, 1f) * GlowGain);   // ZF5: an orange ember at the base, no white core
                hotMat.SetFloat("_CoreMix", 0.3f); hotMat.SetFloat("_RimGain", 0.25f); hotMat.SetFloat("_RingAlpha", 1f); hotMat.SetFloat("_SoftFade", 0f);
                ringShaderMat = add;
            }
            var dm = Resources.Load<Material>("OpenZ/FX/M_HeatDistort");
            if (dm && dm.shader && dm.shader.isSupported)
            {
                shimmerMat = new Material(dm) { name = "HeavyShellShimmer (runtime)" };
                shimmerMat.SetFloat("_Mode", 0f); shimmerMat.SetFloat("_Strength", ShimmerStrength); shimmerMat.SetFloat("_Inner", 0.1f); shimmerMat.SetFloat("_NoiseScale", 5f); shimmerMat.SetFloat("_Speed", 3.2f);
            }
            whistleClips = new[] { Resources.Load<AudioClip>("OpenZ/Audio/Sfx/sd_shell_whistle_a"), Resources.Load<AudioClip>("OpenZ/Audio/Sfx/sd_shell_whistle_b") };
            if (!whistleClips[0] && !whistleClips[1]) Debug.LogWarning("[OpenZ] heavy shells: sd_shell_whistle_a/_b missing in Resources/OpenZ/Audio/Sfx (no whistle)");
            fortWhistles = LoadBank(BankIncomingFort); howitzerWhistles = LoadBank(BankIncomingHowitzer);
            ringMesh = FlatRing(64, 0.7f);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var m in new[] { bodyMat, bandMat, baseMat, hotMat, shimmerMat }) if (m) Destroy(m);
            if (fortMesh) Destroy(fortMesh); if (howitzerMesh) Destroy(howitzerMesh);
            foreach (var r in rings) if (r.mat) Destroy(r.mat);
        }

        static Color Hdr(Color c, float gain) { var l = c.linear * gain; var g = l.gamma; g.a = 1f; return g; }   // gain in linear space (SetColor gamma-decodes)

        /// <summary>FX_Shell from combat_fx.glb baked into library space (as NukeFx.Part / OpenZFxPlayer.BakeInto); nose = the
        /// thinner end of the longest axis.</summary>
        void BakeShell()
        {
            var lib = Resources.Load<GameObject>("OpenZ/FX/combat_fx");
            MeshFilter src = null;
            if (lib) foreach (var mf in lib.GetComponentsInChildren<MeshFilter>(true)) if (mf.name == "FX_Shell") { src = mf; break; }
            if (!src || !src.sharedMesh) { shellMesh = Resources.GetBuiltinResource<Mesh>("New-Capsule.fbx"); nose = Vector3.up; shellLen = 2f; return; }
            var m = Instantiate(src.sharedMesh); m.name = "HeavyShell (baked)";
            var M = lib.transform.worldToLocalMatrix * src.transform.localToWorldMatrix;
            var v = m.vertices; var n = m.normals;
            for (int i = 0; i < v.Length; i++) { v[i] = M.MultiplyPoint3x4(v[i]); if (i < n.Length) n[i] = M.MultiplyVector(n[i]).normalized; }
            m.vertices = v; if (n.Length == v.Length) m.normals = n; else m.RecalculateNormals(); m.RecalculateBounds();
            var b = m.bounds; var e = b.extents;
            int ax = e.x >= e.y && e.x >= e.z ? 0 : e.y >= e.z ? 1 : 2;
            var axis = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            // centre the mesh on its bounds, then pick the thinner end as the nose
            for (int i = 0; i < v.Length; i++) v[i] -= b.center;
            m.vertices = v; m.RecalculateBounds();
            float half = e[ax], wPlus = 0f, wMinus = 0f; int cPlus = 0, cMinus = 0;
            foreach (var p in v)
            {
                float along = Vector3.Dot(p, axis), rad = (p - axis * along).magnitude;
                if (along > half * 0.6f) { wPlus += rad; cPlus++; } else if (along < -half * 0.6f) { wMinus += rad; cMinus++; }
            }
            float avgPlus = cPlus > 0 ? wPlus / cPlus : 0f, avgMinus = cMinus > 0 ? wMinus / cMinus : 0f;
            nose = avgPlus <= avgMinus ? axis : -axis;
            shellLen = Mathf.Max(0.05f, half * 2f);
            shellMesh = m;
        }

        // ================================================================================================ API
        /// <summary>A howitzer shot through LegacyMatch.Launch (CombatView MissileFired): muzzle blast + the heavy shell on the
        /// sim's delay. Returns false when off (the caller then draws the old projectile).</summary>
        public static bool Launch(Vector3 from, Vector3 to, float simDelay, bool fort)
        {
            if (!Enabled || !Ensure()) return false;
            float T = OpenZFxPlayer.RealFlight(simDelay, ZGame.I ? ZGame.I.gameSpeed : 1f);
            float g = OpenZFxCatalog.GravityOf(FxWeapon.Cannon);
            g = ShotClearance.LiftGravity(from, to, T, g, 0.6f);
            g = OzAirShots.LobGravity(from, to, T, g);                                                // K337: a shell at the flying shuttle: its lob under the ceiling (View/OzAirShots.cs)
            var v0 = (to - from) / T; v0.y += g * T * 0.5f;
            MuzzleBlast(from, v0, fort, true);
            return Fly(from, v0, g, T, fort, null);
        }

        /// <summary>Flies a heavy shell p0 + v0 t - g t^2 / 2 for T real seconds (game-paused aware); onLand(p) at the end.</summary>
        public static bool Fly(Vector3 from, Vector3 v0, float g, float T, bool fort, Action<Vector3> onLand)
        {
            if (!Enabled || !Ensure() || I.shellMesh == null) return false;
            Shell s = null;
            foreach (var x in I.shells) if (!x.active) { s = x; break; }
            if (s == null) { if (I.shells.Count >= 12) return false; s = new Shell(); I.shells.Add(s); }
            s.active = true; s.fort = fort; s.p0 = s.pos = s.prev = from; s.v0 = v0; s.g = g; s.T = Mathf.Max(0.05f, T); s.age = 0f;
            s.smokeRem = 0f; s.emberT = 0f; s.whistled = false; s.whistleAt = -1f; s.onLand = onLand; s.src = null;
            s.light = I.TakeLight();
            return true;
        }

        /// <summary>Muzzle blast: flash (howitzer only: the fort gun draws its own), bore smoke ring, forward cloud, shock ring,
        /// dust kick, deep sound layer, camera shake by distance.</summary>
        public static void MuzzleBlast(Vector3 muzzle, Vector3 dirOrVel, bool fort, bool flash)
        {
            if (!Enabled || !Ensure()) return;
            var z = OpenZFxPlayer.I;
            var dir = dirOrVel.sqrMagnitude > 1e-6f ? dirOrVel.normalized : Vector3.forward;
            if (z)
            {
                if (flash) { z.Muzzle(muzzle, dir, FxWeapon.Cannon); z.LaunchFlash(muzzle + dir * 0.45f, dir, MuzzleFlashK, MuzzleGroundLight); }
                var right = Vector3.Cross(Vector3.up, dir); right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
                var up = Vector3.Cross(dir, right).normalized; var smoke = OpenZFxCatalog.M12Smoke.color;
                var c = muzzle + dir * 0.7f;
                for (int i = 0; i < 18; i++)
                {
                    float a = i * Mathf.PI * 2f / 18 + UnityEngine.Random.Range(-0.1f, 0.1f);
                    var radial = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                    z.EmitChannel("leg_smoke", c + radial * 0.3f, radial * UnityEngine.Random.Range(3.4f, 4.6f) + dir * UnityEngine.Random.Range(0.8f, 1.6f),
                        UnityEngine.Random.Range(1.2f, 1.7f), UnityEngine.Random.Range(0.7f, 0.95f), smoke, 0.78f);
                }
                for (int j = 0; j < 6; j++)
                    z.EmitChannel("m12_smoke", muzzle + dir * (0.9f + j * 0.6f), dir * UnityEngine.Random.Range(3f, 6f) + UnityEngine.Random.insideUnitSphere * 0.5f,
                        UnityEngine.Random.Range(0.9f, 1.4f), UnityEngine.Random.Range(0.9f, 1.3f), smoke, OpenZFxCatalog.M12Smoke.alpha);
                // shock ring + dust kick: on the ground under a field gun, on the roof plate under the fort gun
                float gy = z.SurfaceY(muzzle);
                float baseY = fort ? muzzle.y - 1.3f : gy;
                bool nearGround = muzzle.y - gy < 4f;
                I.AddRing(new Vector3(muzzle.x + dir.x * 1.2f, baseY + 0.25f, muzzle.z + dir.z * 1.2f), MuzzleRingRadius * (fort ? 1.2f : 1f), MuzzleRingSeconds);
                if (nearGround && !z.WetAt(muzzle))
                {
                    var dust = ZGame.I != null && ZGame.I.level == "L17" ? new Color(0.62f, 0.57f, 0.52f) : OpenZFxCatalog.M12Dust.color;
                    for (int k = 0; k < 16; k++)
                    {
                        float a = k * Mathf.PI * 2f / 16 + UnityEngine.Random.Range(-0.12f, 0.12f);
                        var rd = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        z.EmitChannel("m12_smoke", new Vector3(muzzle.x, gy + 0.4f, muzzle.z) + rd * 1.2f, rd * UnityEngine.Random.Range(4.5f, 7f) + Vector3.up * UnityEngine.Random.Range(0.4f, 1.1f),
                            UnityEngine.Random.Range(1.1f, 1.6f), UnityEngine.Random.Range(1.1f, 1.7f), dust, 0.55f);
                    }
                }
            }
            var au = CombatAudio.I;
            au?.PlayPitched("heavyshot", muzzle, 0.85f, 0.1f, -1, 0.8f, LegacyMatch.BankHeavyTankShot);
            // the shot of a howitzer / the fort's big cannon in view: a small kick (the landing kicks more, Impact)
            if (z) z.Shake?.Kick(muzzle, fort ? OpenZFxCatalog.ShakeFortShotPx : OpenZFxCatalog.ShakeHowitzerShotPx, fort ? "fort_shot" : "howitzer_shot");
        }

        /// <summary>Heavy impact extras on top of the table blast (never a second damage source: view only).</summary>
        public static void Impact(Vector3 at, bool fort)
        {
            if (!Enabled || !Ensure()) return;
            var z = OpenZFxPlayer.I; var fx = FxManager.I; var au = CombatAudio.I;
            float k = fort ? FortImpactK : 1f;
            int kick = 0;                                                                            // 1 = the landing kicked the camera, -1 = refused (not in view, the pause, the switches), 0 = no effect shake to ask
            float gy = z ? z.SurfaceY(at) : (ZGame.I ? ZGame.I.GroundY(at.x, at.z) : at.y);
            var ground = new Vector3(at.x, gy, at.z);
            bool wet = z && z.WetAt(ground);
            if (wet)
            {
                z.Splash(ground, 1.8f * k); z.Ripple(ground, 1.4f * k);
            }
            else
            {
                CraterField.OnBlast(ground, 1.65f * k, FxBlastKind.HeavyShell);
                ScorchDecals.Add(ground, CraterRadius * k, ScorchKind.Explosion);
                ScorchDecals.Add(ground, CraterRadius * 0.45f * k, ScorchKind.Contact);
                fx?.Breakup(ground + Vector3.up * 0.4f, "stone", 2.6f * k, 14, 1.15f);
                if (z)
                {
                    var dust = ZGame.I != null && ZGame.I.level == "L17" ? new Color(0.62f, 0.57f, 0.52f) : OpenZFxCatalog.M12Dust.color;
                    for (int i = 0; i < 18; i++)
                    {
                        float a = i * Mathf.PI * 2f / 18 + UnityEngine.Random.Range(-0.1f, 0.1f);
                        var rd = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        z.EmitChannel("m12_smoke", ground + rd * 1.4f + Vector3.up * 0.6f, rd * UnityEngine.Random.Range(5f, 8f) * k + Vector3.up * UnityEngine.Random.Range(0.8f, 1.6f),
                            UnityEngine.Random.Range(1.6f, 2.2f), UnityEngine.Random.Range(1.6f, 2.4f) * k, dust, 0.6f);
                    }
                    for (int j = 0; j < 5; j++)
                        z.EmitChannel("m12_smoke", ground + Vector3.up * (1f + j * 0.8f), Vector3.up * UnityEngine.Random.Range(3f, 5f) + UnityEngine.Random.insideUnitSphere * 0.6f,
                            UnityEngine.Random.Range(2.0f, 2.8f), UnityEngine.Random.Range(1.8f, 2.6f) * k, new Color(0.36f, 0.33f, 0.30f), 0.7f);
                }
            }
            if (z)
            {
                for (int s = 0; s < 14; s++)
                {
                    var v = UnityEngine.Random.onUnitSphere; v.y = Mathf.Abs(v.y) + 0.4f;
                    z.EmitChannel("m12_spark", ground + Vector3.up * 0.5f, v.normalized * UnityEngine.Random.Range(7f, 12f), UnityEngine.Random.Range(0.45f, 0.8f), 0.12f, new Color(1f, 0.62f, 0.28f), 1f);
                }
                z.AddLight(ground + Vector3.up * 2f, new Color(1f, 0.6f, 0.3f), ImpactLight * k, 24f * k, 0.42f, 2f, 0.2f, true);
                z.GroundLight(ground, 1.8f * k, false, true);
                if (z.Shake != null) kick = z.Shake.Kick(ground, fort ? OpenZFxCatalog.ShakeFortImpactPx : OpenZFxCatalog.ShakeHowitzerImpactPx, fort ? "fort_impact" : "howitzer_impact") ? 1 : -1;   // one kick per landing, in view
            }
            au?.PlayPitched("heavy", ground, LegacyMatch.GainHeavy * 0.8f, 0.08f, -1, 0.72f, LegacyMatch.BankHeavy);
            I.ShakeAt(ground, ImpactShake * (fort ? FortShakeK : HowitzerShakeK), kick);
        }
        /// <summary>
        /// </summary>
        public static float FortImpactK = 1.45f, FortShakeK = 1.9f, HowitzerShakeK = 1.9f, OffScreenShake = 0.2f;

        static bool OnScreenNow(Vector3 p)
        {
            var c = Camera.main; if (!c) return false;
            var v = c.WorldToViewportPoint(p);
            return v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
        }
        /// <summary>The camera's own short jitter for a landing. kick 1 = the landing's kick went through: the jitter is part of it
        /// (not asked a second time against the pause); -1 = the kick was refused: in view nothing, off screen the subtle one as
        /// before; 0 = no effect shake was asked: the old rule (asked on its own).</summary>
        void ShakeAt(Vector3 p, float amount, int kick = 0)
        {
            if (OpenZFortShockwave.ShakeScale <= 0f) return;
            if (!rts && Time.unscaledTime >= nextRts) { nextRts = Time.unscaledTime + 2f; rts = FindFirstObjectByType<RtsCamera>(); }
            if (!rts) return;
            float d = new Vector2(p.x - rts.focus.x, p.z - rts.focus.z).magnitude;
            float f = Mathf.Pow(Mathf.Clamp01(1f - d / ImpactShakeRange), 1.5f);
            if (f * amount <= 0.02f) return;
            if (kick > 0) { rts.Shake(amount * f, p, "heavy_shell", true, true); return; }           // the second part of the landing's kick
            if ((kick < 0 || !rts.Shake(amount * f, p, "heavy_shell")) && !OnScreenNow(p))
                rts.Shake(amount * f * OffScreenShake, p, "heavy_shell_far", false);
        }

        // ================================================================================================ frame
        void Update()
        {
            bool paused = (ZGame.I && ZGame.I.paused) || Time.timeScale <= 0f;
            float dt = paused ? 0f : Time.deltaTime;
            var cam = Camera.main; var camRot = cam ? cam.transform.rotation : Quaternion.identity;
            var z = OpenZFxPlayer.I;
            foreach (var s in shells)
            {
                if (!s.active) continue;
                if (s.src) { if (paused && s.src.isPlaying) s.src.Pause(); else if (!paused && !s.src.isPlaying && s.src.time > 0f) s.src.UnPause(); }
                s.age = Mathf.Min(s.age + dt, s.T);
                s.prev = s.pos;
                s.pos = s.p0 + s.v0 * s.age + 0.5f * s.age * s.age * s.g * Vector3.down;
                var vel = s.v0 + Vector3.down * (s.g * s.age);
                var dir = vel.sqrMagnitude > 1e-6f ? vel.normalized : Vector3.down;
                if (s.age >= s.T) { Land(s); continue; }
                Draw(s, dir, camRot);
                if (dt > 0f && z) Trail(s, dir, dt, z);
                Whistle(s, vel);
            }
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i]; if (!r.active) continue;
                r.age += dt; float k = r.age / r.life;
                if (k >= 1f) { r.active = false; r.go.SetActive(false); continue; }
                float rad = r.r1 * (1f - (1f - k) * (1f - k));
                r.go.transform.position = r.c; r.go.transform.localScale = new Vector3(rad * 1.05f, 1f, rad * 1.05f);
                r.mat.SetVector("_RingCenter", r.c); r.mat.SetFloat("_RingRadius", rad);
                r.mat.SetVector("_Color", new Vector4(1f, 0.8f, 0.55f, 1f) * (3.5f * (1f - k) * (1f - k)));
            }
        }

        void Draw(Shell s, Vector3 dir, Quaternion camRot)
        {
            float len = s.fort ? FortLength : HowitzerLength, scale = len / shellLen;
            var rot = Quaternion.FromToRotation(nose, dir);
            var model = Modelled ? (s.fort ? fortMesh : howitzerMesh) : null;
            if (model && bodyMat)
            {   // modelled body: unit length along +Z; sub-mesh 0 steel, 1 copper band(s) and fuze, 2 the hot base cup
                var trs = Matrix4x4.TRS(s.pos, Quaternion.FromToRotation(Vector3.forward, dir), Vector3.one * len);
                Graphics.DrawMesh(model, trs, bodyMat, 0, null, OpenZShellMesh.Steel, null, ShadowCastingMode.On, true);
                if (bandMat) Graphics.DrawMesh(model, trs, bandMat, 0, null, OpenZShellMesh.Band, null, ShadowCastingMode.On, true);
                if (baseMat) Graphics.DrawMesh(model, trs, baseMat, 0, null, OpenZShellMesh.Hot, null, ShadowCastingMode.Off, false);
            }
            else if (bodyMat) Graphics.DrawMesh(shellMesh, Matrix4x4.TRS(s.pos, rot, Vector3.one * scale), bodyMat, 0, null, 0, null, ShadowCastingMode.On, true);
            var tail = s.pos - dir * (len * 0.5f);
            if (hotMat && sphere) Graphics.DrawMesh(sphere, Matrix4x4.TRS(tail - dir * (0.36f * (len / 2.1f)), Quaternion.LookRotation(dir), new Vector3(0.3f, 0.3f, 1f) * (len / 2.1f)), hotMat, 0, null, 0, null, ShadowCastingMode.Off, false);
            var z = OpenZFxPlayer.I;
            if (z) z.DrawHalo(tail - dir * (0.3f * (len / 2.1f)), HaloSize * (len / 2.1f), new Color(1f, 0.4f, 0.1f), 0.5f);   // ZF5: small warm glow (was 1.4)
            var c = Camera.main;
            if (z && OzFxSmooth.On)
            {   // MOTION BLUR option: a soft tail along the flight in the body's colour and a glowing one from the hot base
                var bv = s.v0 + Vector3.down * (s.g * s.age);
                float bw = len * OzFxSmooth.HeavyShellWide;
                z.DrawBlurTail(s.pos, bv, bw, OpenZFxCatalog.ShellBody.linear, 1f, false);
                z.DrawBlurTail(tail, bv, bw * 0.8f, new Color(1f, 0.4f, 0.1f).linear, OzFxSmooth.TailTracerGain * 2f, true);
            }
            if (shimmerMat && c)
            {
                var sp = tail - dir * 1.2f;
                Graphics.DrawMesh(OpenZFortShockwave.Quad(), Matrix4x4.TRS(sp, Quaternion.LookRotation(sp - c.transform.position, c.transform.up), Vector3.one * ShimmerSize), shimmerMat, 0, null, 0, null, ShadowCastingMode.Off, false);
            }
            if (s.light)
            {
                s.light.transform.position = tail;
                s.light.intensity = LightIntensity * (0.9f + 0.1f * Mathf.Sin(s.age * 40f));
                s.light.enabled = true;
            }
        }

        void Trail(Shell s, Vector3 dir, float dt, OpenZFxPlayer z)
        {
            float len = s.fort ? FortLength : HowitzerLength;
            float size = SmokeSize * (s.fort ? 1.1f : 1f);
            float every = Mathf.Max(0.12f, size * SmokeSpacing);
            float seg = Vector3.Distance(s.prev, s.pos);
            s.smokeRem += seg;
            int guard = 0;
            float back = len * 0.5f + size * 0.55f;                                                  // behind the base: the hot core stays clear of the smoke
            while (s.smokeRem >= every && guard++ < 16)
            {
                s.smokeRem -= every;
                float k = seg > 1e-5f ? Mathf.Clamp01(1f - s.smokeRem / seg) : 1f;
                var p = Vector3.Lerp(s.prev, s.pos, k) - dir * back + UnityEngine.Random.insideUnitSphere * size * 0.15f;
                var col = Color.Lerp(SmokeDark, SmokeGrey, UnityEngine.Random.value);
                z.EmitChannel("m12_smoke", p, new Vector3(0.1f, 0.45f, 0.06f) + UnityEngine.Random.insideUnitSphere * 0.35f, SmokeLife * UnityEngine.Random.Range(0.85f, 1.15f), size * UnityEngine.Random.Range(0.85f, 1.2f), col, SmokeAlpha);
            }
            if (guard >= 16) s.smokeRem = 0f;
            s.emberT += dt;
            if (s.emberT >= 0.05f)
            {
                s.emberT = 0f;
                z.EmitChannel("leg_ember", s.pos - dir * (len * 0.55f), -dir * 0.4f + UnityEngine.Random.insideUnitSphere * 0.3f, 0.22f, 0.24f, new Color(1f, 0.42f, 0.12f), 0.6f);
            }
        }

        void Whistle(Shell s, Vector3 vel)
        {
            if (s.whistled || s.T < WhistleMinFlight) return;
            var ca = CombatAudio.I; if (!ca) return;
            float remaining = s.T - s.age;
            if (s.whistleAt < 0f)
            {
                if (vel.y > 0f && remaining > 0.5f * s.T) return;                               // still climbing: the whistle starts at the apex
                var clip = NextWhistle(s.fort) ?? whistleClips[UnityEngine.Random.Range(0, 2)] ?? whistleClips[0] ?? whistleClips[1];
                if (!clip) { s.whistled = true; return; }
                s.src = TakeWhistle(); if (!s.src) { s.whistled = true; return; }
                s.src.clip = clip;
                s.whistleAt = Mathf.Max(s.age, s.T - clip.length);                                    // ends on the impact
            }
            if (s.age < s.whistleAt || !s.src) return;
            s.whistled = true;
            float L = s.src.clip.length, left = s.T - s.age;
            ca.Configure(s.src, null, CombatAudio.Profile.Field, 130f * Mathf.Max(0.6f, ca.RangeScale));
            s.src.transform.position = s.pos;
            s.src.time = Mathf.Clamp(L - left, 0f, Mathf.Max(0f, L - 0.05f));
            s.src.pitch = 1.05f;
            float take = CombatAudio.TakeGain.TryGetValue(s.src.clip.name, out var tg) ? tg : 1f;
            s.src.volume = Mathf.Clamp01(WhistleVolume * ca.volume * CombatAudio.BusGain(CombatAudio.Bus.Weapons) * ca.ZoomGain * ca.DuckOf(CombatAudio.Bus.Weapons) * 2.6f * ca.LevelTrim(s.src.clip.name) * take);
            OzSoundLog.Add("CA", s.fort ? "fort_incoming" : "howitzer_incoming", s.src.clip.name, "PLAY whistle", s.src.volume, s.pos);
            s.src.Play();
        }

        void LateUpdate()
        {
            foreach (var s in shells)
            {
                if (!s.active || !s.src || !s.whistled || !s.src.isPlaying) continue;
                s.src.transform.position = s.pos;                                                   // rides the shell
                s.src.pitch = Mathf.Lerp(1.05f, 0.9f, Mathf.Clamp01(s.age / Mathf.Max(0.01f, s.T)));
            }
        }

        void Land(Shell s)
        {
            s.active = false;
            if (s.light) { s.light.enabled = false; s.light = null; }
            if (s.src) { var src = s.src; s.src = null; StartCoroutine(StopAfter(src, 0.12f)); }
            var land = s.onLand; s.onLand = null;
            try { land?.Invoke(s.pos); } catch (Exception e) { Debug.LogException(e); }
        }

        System.Collections.IEnumerator StopAfter(AudioSource a, float t) { yield return new WaitForSeconds(t); if (a) a.Stop(); }

        AudioSource TakeWhistle()
        {
            foreach (var a in whistles) if (a && !a.isPlaying && !InUse(a)) return a;
            if (whistles.Count >= 6) return null;
            var go = new GameObject("HeavyShellWhistle"); go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>(); src.playOnAwake = false; src.loop = false; src.dopplerLevel = 0.4f; src.spread = 40f; src.priority = 60;
            whistles.Add(src);
            return src;
        }

        bool InUse(AudioSource a) { foreach (var s in shells) if (s.active && s.src == a) return true; return false; }

        Light TakeLight()
        {
            foreach (var l in lights) if (l && !l.enabled) return l;
            if (lights.Count >= 4) return null;
            var go = new GameObject("HeavyShellLight"); go.transform.SetParent(transform, false);
            var l2 = go.AddComponent<Light>(); l2.type = LightType.Point; l2.range = LightRange; l2.color = new Color(1f, 0.55f, 0.25f); l2.shadows = LightShadows.None;
            l2.renderMode = LightRenderMode.ForcePixel; l2.enabled = false;
            lights.Add(l2);
            return l2;
        }

        void AddRing(Vector3 c, float r1, float life)
        {
            if (!ringShaderMat) return;
            Ring r = null;
            foreach (var x in rings) if (!x.active) { r = x; break; }
            if (r == null)
            {
                if (rings.Count >= 6) return;
                r = new Ring { mat = new Material(ringShaderMat) { name = "HeavyShellRing (runtime)", renderQueue = 3071 } };
                r.mat.SetFloat("_Shape", 4f); r.mat.SetFloat("_UseVertexColor", 0f); r.mat.SetFloat("_Opaqueness", 0f); r.mat.SetFloat("_Intensity", 1f);
                r.mat.SetFloat("_BandInner", 0.82f); r.mat.SetFloat("_BandSides", 0f); r.mat.SetFloat("_BandProfile", 1f); r.mat.SetFloat("_RingAlpha", 1f);
                r.go = new GameObject("HeavyShellRing"); r.go.transform.SetParent(transform, false);
                r.go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
                var mr = r.go.AddComponent<MeshRenderer>(); mr.sharedMaterial = r.mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                rings.Add(r);
            }
            r.active = true; r.c = c; r.age = 0f; r.life = life; r.r1 = r1; r.go.SetActive(true);
        }

        /// <summary>Flat annulus (unit outer radius) in XZ, inner fraction `inner`.</summary>
        static Mesh FlatRing(int seg, float inner)
        {
            var v = new List<Vector3>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                v.Add(new Vector3(Mathf.Cos(a) * inner, 0f, Mathf.Sin(a) * inner)); v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            for (int i = 0; i < seg; i++) { int a = i * 2; tri.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            var m = new Mesh { name = "HeavyShellRing" }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>an editor script: OpenZ.OpenZHeavyShells.Test() fires a howitzer-style shell 40 m in front of the camera focus.</summary>
        public static string Test(bool fort = false)
        {
            var r = FindFirstObjectByType<RtsCamera>(); var g = ZGame.I;
            if (!r || g == null) return "no camera / game";
            var to = r.focus + new Vector3(12f, 0f, 6f); to.y = g.GroundY(to.x, to.z);
            var from = r.focus + new Vector3(-24f, 0f, -10f); from.y = g.GroundY(from.x, from.z) + (fort ? 8f : 1.6f);
            float delay = 1.6f;
            bool ok = Launch(from, to, delay, fort);
            if (ok) I.StartCoroutine(I.ImpactLater(to, OpenZFxPlayer.RealFlight(delay, g.gameSpeed), fort));
            return ok ? $"heavy shell fired ({(fort ? "fort gun" : "howitzer")}), lands in {delay:F1} s at {to}" : "heavy shells disabled";
        }

        System.Collections.IEnumerator ImpactLater(Vector3 at, float t, bool fort)
        {
            yield return new WaitForSeconds(t);
            LegacyMatch.DoBlast(at + Vector3.up * 0.2f, LegacyMatch.HowitzerShell, -1, FxManager.I);
            Impact(at, fort);
        }

        public static string Status()
        {
            if (!I) return "heavy shells: not created yet (first howitzer / fort gun shot creates them)";
            int n = 0; foreach (var s in I.shells) if (s.active) n++;
            string md = Modelled && I.fortMesh && I.howitzerMesh ? I.fortMesh.vertexCount + " / " + I.howitzerMesh.vertexCount + " verts [" + OpenZShellMesh.FortInfo + "] [" + OpenZShellMesh.HowitzerInfo + "]" : "off";
            var st = $"heavy shells: {n} in flight, pool {I.shells.Count}, rings {I.rings.Count}, whistles {I.whistles.Count}, lights {I.lights.Count}, mesh {(I.shellMesh ? I.shellMesh.name : "none")} len {I.shellLen:F2} m, nose {I.nose}, modelled bodies (fort / howitzer) {md}, fort {FortLength:F2} m, howitzer {HowitzerLength:F2} m";
            Debug.Log("[OpenZ] " + st);
            return st;
        }
    }
}
