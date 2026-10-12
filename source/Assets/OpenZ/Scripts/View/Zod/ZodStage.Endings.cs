// OpenZ (GPL-3.0)
//  (22) During a victory transmission his head (and a little of his chest) turns to the
//       two red soldiers standing in front of him (GazeBoys), eased in / out, on top of the animation.
//  (38) On a loss his HQ burns behind him (explosions, shaky camera, signal static): HqFire(true) for a defeat transmission: flames + dark smoke along the back wall and the console, a
//       flickering fire light, and every 1.3-2.8 s a blast behind him (a flash light, sparks, dust from the ceiling, a camera kick,
//       a muffled boom); the field camera shakes harder. ZodBriefing adds the signal static on every blast (HqBlasted).
//       Calm screen (OzMotion.Reduced): no kicks, no extra shake, soft slow flashes (no strobing).
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        [Header("Endings audio")]
        [Tooltip("Victory: how far his head turns to the boys (share of the angle to them)")] [Range(0f, 1f)] public float gazeHead = 0.75f;
        [Tooltip("Victory: chest share of the turn")] [Range(0f, 1f)] public float gazeChest = 0.25f;
        [Tooltip("Largest head turn (deg)")] public float gazeMaxDeg = 48f;
        /// <summary>
        /// about the stage's up, this share of the angle to the boys), the head takes gazeHead of the rest; in every transmission that
        /// shows the boys (briefings, idle barb, nuke until the slam cut, win, loss). GazeCut snaps him back square (the slam close-up).</summary>
        [Range(0f, 1f)] public float gazeBody = 0.45f;
        [Tooltip("Defeat: hand-held amount while the HQ burns")] public float hqShake = 2.1f;

        /// <summary>Victory: Zod looks at Alan and Brad (eased).</summary>
        public bool GazeBoys { get; set; }
        float gazeW;
        public float GazeWeight => gazeW;

        void ApplyGaze(float dt)
        {
            if (dt > 0f) gazeW = Mathf.MoveTowards(gazeW, GazeBoys ? 1f : 0f, dt / (GazeBoys ? 0.6f : 0.4f));
            if (!rig) return;
            if (gazeW <= 0.001f || !model) { rig.gazeYaw = rig.gazePitch = 0f; if (model && bodyTurned) { model.localRotation = Quaternion.identity; bodyTurned = false; } BodyYawNow = 0f; return; }
            var head = headBone ? headBone : (headBone = Find(model, "mixamorigHead"));
            if (!head) return;
            Vector3 target = BoysGazePoint();
            Vector3 up = transform.up;
            Vector3 fwd = Vector3.ProjectOnPlane(model.parent ? model.parent.forward : transform.forward, up).normalized;   // the REST forward (the body turn below would feed back)
            Vector3 to = target - head.position;
            Vector3 toH = Vector3.ProjectOnPlane(to, up);
            if (toH.sqrMagnitude < 1e-4f || fwd.sqrMagnitude < 1e-4f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(fwd, toH, up), -gazeMaxDeg, gazeMaxDeg);
            float pitch = Mathf.Clamp(-Mathf.Atan2(Vector3.Dot(to, up), toH.magnitude) * Mathf.Rad2Deg, -5f, 22f);   // + = looks down at them
            float s = Smooth01(gazeW);
            // through the talk rig (it owns the head's base pose; a direct bone write here accumulated frame by frame: the head
            // tipped over until the hat crown faced the lens in the first Play check)
            float body = yaw * gazeBody * s;
            model.localRotation = Quaternion.AngleAxis(body, Vector3.up); bodyTurned = true; BodyYawNow = body;   // the model rests at identity (Build)
            rig.gazeYaw = (yaw * s - body) * Mathf.Max(gazeHead, 0.85f); rig.gazePitch = pitch * 0.6f * s;
            GazeYawNow = rig.gazeYaw; GazePitchNow = rig.gazePitch;
        }
        bool bodyTurned;
        public float BodyYawNow { get; private set; }
        /// <summary></summary>
        public void GazeCut()
        {
            GazeBoys = false; gazeW = 0f;
            if (rig) rig.gazeYaw = rig.gazePitch = 0f;
            if (model) model.localRotation = Quaternion.identity;
            bodyTurned = false; BodyYawNow = 0f;
        }
        public float GazeYawNow { get; private set; }
        public float GazePitchNow { get; private set; }
        Transform headBone;
        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>The middle of the two boys' heads (stage space when they are not built: in front of him, a little to his right).</summary>
        Vector3 BoysGazePoint()
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var b in boys)
            {
                if (b == null || !b.go) continue;
                sum += b.head ? b.head.position : b.t.position + transform.up * 1.35f; n++;
            }
            if (n > 0) return sum / n;
            return transform.TransformPoint(new Vector3(-0.7f, 1.45f, 2.1f));
        }

        // ------------------------------------------------------------------------------------------------ the HQ on fire (defeat)
        public bool HqFireOn { get; private set; }
        public int HqBlasts { get; private set; }
        /// <summary>ZodBriefing: a blast went off behind him (strength 0..1): it adds the signal static.</summary>
        public event Action<float> HqBlasted;
        GameObject hqRoot; ParticleSystem hqFlames, hqSmoke, hqSparks, hqDust; Light hqFireLight, hqFlash;
        float hqNext, hqFlashT = -10f, hqFlashK, handheldBase = -1f; AudioSource hqSfx; AudioClip[] hqBooms;
        static readonly Vector3[] FireSpots = { new Vector3(1.15f, 1.62f, -2.45f), new Vector3(-1.2f, 1.58f, -2.45f), new Vector3(0.55f, 2.5f, -2.55f), new Vector3(-2.35f, 1.5f, -2.42f), new Vector3(2.4f, 1.5f, -2.42f) };   // first Play check: at console height (1.28 m) the flames hid behind him: now on the wall where the frame sees it

        public void HqFire(bool on)
        {
            if (on == HqFireOn) return;
            HqFireOn = on;
            if (on)
            {
                BuildHqFire();
                if (!hqRoot) { HqFireOn = false; return; }
                hqRoot.SetActive(true);
                foreach (var sp in spots) if (sp) sp.Play(true);
                hqFlames.Play(false); hqSparks.Play(false); hqDust.Play(false);              // rate 0: they only emit the blasts
                hqNext = clock + 0.35f;
                if (handheldBase < 0f) handheldBase = handheldAmount;
                handheldAmount = OzMotion.Reduced ? handheldBase : Mathf.Max(handheldBase, hqShake);
                Debug.Log("[ZodCameo] HQ on fire (defeat): flames, smoke, blasts every 1.3-2.8 s" + (OzMotion.Reduced ? " (calm screen: no kicks, soft flashes)" : ""));
            }
            else
            {
                foreach (var sp in spots) if (sp) sp.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (hqFireLight) hqFireLight.intensity = 0f;
                if (hqFlash) hqFlash.intensity = 0f;
                if (handheldBase >= 0f) { handheldAmount = handheldBase; handheldBase = -1f; }
            }
        }

        void TickHqFire(float dt)
        {
            if (!hqRoot) return;
            if (!HqFireOn)
            {
                if (hqRoot.activeSelf && SpotCount(true) + SpotCount(false) == 0) hqRoot.SetActive(false);
                return;
            }
            if (dt <= 0f) return;
            float t = Time.unscaledTime;
            if (hqFireLight)
            {
                float fl = 0.72f + 0.28f * Mathf.PerlinNoise(t * (OzMotion.Reduced ? 2.2f : 7.5f), 1.7f) + 0.12f * Mathf.Sin(t * 13.1f) * (OzMotion.Reduced ? 0f : 1f);
                hqFireLight.intensity = 11f * fl;
            }
            if (hqFlash)
            {
                float age = clock - hqFlashT, len = OzMotion.Reduced ? 0.9f : 0.42f;
                float k = age < 0f || age > len ? 0f : (OzMotion.Reduced ? Mathf.Sin(Mathf.PI * age / len) : Mathf.Exp(-age * 8f));
                hqFlash.intensity = hqFlashK * k * (OzMotion.Reduced ? 0.3f : 1f);
            }
            if (clock >= hqNext) { HqBlast(UnityEngine.Random.Range(0.55f, 1f)); hqNext = clock + UnityEngine.Random.Range(1.3f, 2.8f); }
        }

        /// <summary>One blast behind Zod: flash, sparks, ceiling dust, camera kick, boom (2D on the stage, Zod's voice bus level).</summary>
        public void HqBlast(float strength)
        {
            if (!hqRoot) return;
            HqBlasts++;
            var at = transform.TransformPoint(new Vector3(UnityEngine.Random.Range(-3f, 3f), UnityEngine.Random.Range(1.0f, 2.6f), -2.3f));
            if (hqFlash) { hqFlash.transform.position = at + transform.forward * 0.4f; hqFlashK = 70f * strength; hqFlashT = clock; }
            if (hqSparks) { hqSparks.transform.position = at; hqSparks.Emit(Mathf.RoundToInt(26 * strength)); }
            if (hqDust) { hqDust.transform.position = transform.TransformPoint(new Vector3(UnityEngine.Random.Range(-1.4f, 1.4f), 4.3f, UnityEngine.Random.Range(-1.6f, 0.4f))); hqDust.Emit(Mathf.RoundToInt(22 * strength)); }
            if (hqFlames) { var ep = new ParticleSystem.EmitParams { position = at, applyShapeToPosition = true }; hqFlames.Emit(ep, 10); }
            Kick(0.45f + 0.45f * strength);                                                    // Kick is off under the calm screen
            if (hqSfx && hqBooms != null && hqBooms.Length > 0)
            {
                var c = hqBooms[UnityEngine.Random.Range(0, hqBooms.Length)];
                if (c) { hqSfx.pitch = UnityEngine.Random.Range(0.72f, 0.9f); hqSfx.PlayOneShot(c, Mathf.Clamp01(BoysSfxGain * 0.55f * (0.6f + 0.4f * strength))); }
            }
            HqBlasted?.Invoke(strength);
        }

        /// <summary>
        /// The big one on the facepalm frame: a strong flash, a heavy kick, a burst of
        /// sparks and falling ceiling dust, a louder boom AND a debris rattle on the same frame; the random blasts wait 1.8 s after it.</summary>
        public void HqBigBlast()
        {
            if (!hqRoot) return;
            HqBlast(1f);
            hqFlashK = 120f;
            if (hqSparks) hqSparks.Emit(34);
            if (hqDust) { hqDust.transform.position = transform.TransformPoint(new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), 4.3f, -0.3f)); hqDust.Emit(46); }
            Kick(1f);
            hqNext = clock + 1.8f;
            HqBigBlasts++;
            if (!hqSfx) return;
            float g = Mathf.Clamp01(BoysSfxGain * 0.95f);
            var boom = Resources.Load<AudioClip>("OpenZ/Audio/Sfx/explosion-v2-20260909");
            if (boom) { hqSfx.pitch = 0.66f; hqSfx.PlayOneShot(boom, g); OzSoundLog.Add("ZodCameo", "hq_facecover_blast", boom.name, "played", g); }
            else OzSoundLog.Add("ZodCameo", "hq_facecover_blast", "explosion-v2-20260909", "missing clip");
            var rattle = Resources.Load<AudioClip>(UnityEngine.Random.value < 0.5f ? "OpenZ/Audio/Sfx/sd_debris_rain_a" : "OpenZ/Audio/Sfx/sd_debris_rain_b");
            if (rattle) { hqSfx.PlayOneShot(rattle, g * 0.8f); OzSoundLog.Add("ZodCameo", "hq_debris_rattle", rattle.name, "played", g * 0.8f); }
            else OzSoundLog.Add("ZodCameo", "hq_debris_rattle", "sd_debris_rain_a", "missing clip");
            Debug.Log($"[ZodCameo] HQ big blast on the facepalm (frame {Time.frameCount}): flash 120, kick 1, sparks + dust, boom + debris rattle");
        }
        public int HqBigBlasts { get; private set; }

        void BuildHqFire()
        {
            if (hqRoot) return;
            hqRoot = new GameObject("HQFire") { layer = Layer };
            hqRoot.transform.SetParent(transform, false);
            var add = FireMaterial();
            var soft = SmokeMaterial();
            // flames along the console / back wall (one system, 4 spots by a mesh-less box shape per spot -> one emitter per spot is cheap enough)
            hqFlames = MakePs("Flames", Vector3.zero, add, 220);
            {
                var m = hqFlames.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.32f, 0.75f); m.gravityModifier = -0.25f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.16f, 0.9f), new Color(1f, 0.32f, 0.06f, 0.8f));
                var em = hqFlames.emission; em.rateOverTime = 0f;
                var bursts = new ParticleSystem.Burst[0]; em.SetBursts(bursts);
                var sh = hqFlames.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.7f, 0.1f, 0.15f);
                var col = hqFlames.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.42f, 0.1f), 0.45f), new GradientColorKey(new Color(0.5f, 0.12f, 0.04f), 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.12f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var size = hqFlames.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
                var noise = hqFlames.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.6f; noise.scrollSpeed = 1.2f;
            }
            hqSmoke = MakePs("Smoke", Vector3.zero, soft, 160);
            {
                var m = hqSmoke.main; m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.0f); m.gravityModifier = -0.04f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.09f, 0.08f, 0.075f, 0.55f), new Color(0.16f, 0.14f, 0.12f, 0.45f));
                var sh = hqSmoke.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.8f, 0.1f, 0.2f);
                var size = hqSmoke.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 2.6f)));
                var col = hqSmoke.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0.35f, 0.7f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }
            // the four fire spots: sub-emitters by position (each spot gets its own rate through a child system copy)
            foreach (var p in FireSpots) { AddSpot(hqFlames, p, 26f); AddSpot(hqSmoke, p + new Vector3(0f, 0.5f, 0f), 4.5f); }
            hqSparks = MakePs("Sparks", Vector3.zero, add, 200);
            {
                var m = hqSparks.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1.0f); m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f); m.gravityModifier = 1.2f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f, 1f), new Color(1f, 0.5f, 0.15f, 1f));
                var em = hqSparks.emission; em.rateOverTime = 0f;
                var sh = hqSparks.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.15f;
                var pr = hqSparks.GetComponent<ParticleSystemRenderer>(); pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.06f; pr.lengthScale = 1.5f;
            }
            hqDust = MakePs("CeilingDust", Vector3.zero, soft, 160);
            {
                var m = hqDust.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.15f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f); m.gravityModifier = 0.35f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.5f, 0.44f, 0.6f), new Color(0.4f, 0.37f, 0.33f, 0.5f));
                var em = hqDust.emission; em.rateOverTime = 0f;
                var sh = hqDust.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1.2f, 0.05f, 0.8f);
            }
            hqFireLight = AddLight("HQFireLight", LightType.Point, new Vector3(0.3f, 1.6f, -1.9f), new Vector3(0.3f, 1.6f, -1.9f), new Color(1f, 0.48f, 0.16f), 0f, 5.5f);
            hqFireLight.transform.SetParent(hqRoot.transform, true);
            hqFlash = AddLight("HQBlastFlash", LightType.Point, new Vector3(0f, 2f, -2f), new Vector3(0f, 2f, -2f), new Color(1f, 0.62f, 0.3f), 0f, 8f);
            hqFlash.transform.SetParent(hqRoot.transform, true);
            hqSfx = hqRoot.AddComponent<AudioSource>();                                         // 2D like Zod's voice: a muffled boom inside the transmission
            hqSfx.playOnAwake = false; hqSfx.spatialBlend = 0f; hqSfx.priority = 70; hqSfx.dopplerLevel = 0f; hqSfx.bypassReverbZones = true;
            var lp = hqRoot.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 1400f;     // through the walls and the radio
            hqBooms = new[] { Resources.Load<AudioClip>("OpenZ/Audio/Sfx/explosion-v1-20260909"), Resources.Load<AudioClip>("OpenZ/Audio/Sfx/explosion-v2-20260909"),
                              Resources.Load<AudioClip>("OpenZ/Audio/Sfx/explosion-v3-20260909"), Resources.Load<AudioClip>("OpenZ/Audio/Sfx/heavy") };
            hqRoot.SetActive(false);
        }

        void AddSpot(ParticleSystem master, Vector3 localPos, float rate)
        {
            var copy = Instantiate(master.gameObject, hqRoot.transform, false);
            copy.name = master.name + "_spot"; copy.layer = Layer;
            copy.transform.localPosition = localPos;
            var ps = copy.GetComponent<ParticleSystem>(); var em = ps.emission; em.rateOverTime = rate;
            spots.Add(ps);
        }
        readonly System.Collections.Generic.List<ParticleSystem> spots = new System.Collections.Generic.List<ParticleSystem>();

        ParticleSystem MakePs(string name, Vector3 local, Material mat, int max)
        {
            var go = new GameObject(name) { layer = Layer };
            go.transform.SetParent(hqRoot.transform, false); go.transform.localPosition = local;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main; m.duration = 5f; m.loop = true; m.playOnAwake = false; m.useUnscaledTime = true; m.maxParticles = max;
            m.simulationSpace = ParticleSystemSimulationSpace.World; m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission; em.rateOverTime = 0f;
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard; pr.sortMode = ParticleSystemSortMode.Distance;
            pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false; pr.renderingLayerMask = 1u << LightLayerBit;
            pr.sharedMaterial = mat;
            return ps;
        }

        static Material fireMat;
        static Material FireMaterial()
        {
            if (fireMat) return fireMat;
            var baseM = SmokeMaterial(); if (!baseM) return null;
            fireMat = new Material(baseM) { hideFlags = HideFlags.DontSave, name = "ZodStage_Fire" };
            fireMat.SetFloat("_Blend", 2f);                                                    // URP particles: additive
            fireMat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); fireMat.SetFloat("_DstBlend", (float)BlendMode.One);
            fireMat.renderQueue = 3010;
            return fireMat;
        }

        /// <summary>Play check (ZodBriefing.DebugHq): fire state.</summary>
        public string HqStatus() => $"HQ fire {(HqFireOn ? "ON" : "off")}, blasts {HqBlasts}, flame spots {spots.Count}, flames {(hqFlames ? SpotCount(true) : 0)} smoke {(hqSmoke ? SpotCount(false) : 0)} particles, shake {handheldAmount:F2}, gaze {gazeW:F2} (head yaw {GazeYawNow:F1}, pitch {GazePitchNow:F1} deg)";
        int SpotCount(bool flames) { int n = 0; foreach (var s in spots) if (s && (s.name.StartsWith("Flames") == flames)) n += s.particleCount; return n; }
    }
}
