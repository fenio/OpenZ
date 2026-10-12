using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// <list type="number">
    /// <item>
    /// (AudioDirector.Nuke), the camera frames the fort (RulesView). The fort model keeps standing until the impact.</item>
    /// <item>
    /// (v3-aircraft-engine-a), a fly-by whoosh when it passes the fort, casting its shadow on the ground.</item>
    /// <item>
    /// glow halo) with the nuke-descent whistle.</item>
    /// <item>Impact: a slow-motion beat (time scale SlowMo for SlowMoHold real seconds, eased back), the exposure + bloom flash
    /// (OpenZBloomDirector.Flash), the queued building collapse + NukeFx (mushroom, shock ring, side explosions), a ring of
    /// secondary blasts running out with the shock wave (Zod bfort.cpp 12-17 side explosions, spread wider), burning debris,
    /// </item>
    /// </list>
    /// RulesView.BuildingDeath (fort branch, fx_v2 patch) calls Begin(); both fort halves die together and are merged into one
    /// strike. Impacted is raised for OpenZVictory (voices, celebration, end screen). Pause-safe: the slow motion never writes the
    /// time scale while the pause menu holds it at 0, and always restores 1.
    /// </summary>
    public sealed class OpenZNukeStrike : MonoBehaviour
    {
        public static bool Enabled = true;
        // flight (m, s, real time)
        public static float PlaneScale = 1.6f, PlaneAltitude = 33f, PlaneSpeed = 45f, PlaneLeadIn = 75f, PlaneExit = 95f, PlaneYawTowardCamera = 18f;
        public static float PlaneYawOffset = 0f;                  // the glb nose is -Z in the imported prefab (glTFast: x -> -x); tweak if it flies sideways
        public static float EnginePreRoll = 0.9f;
        public static float FallSeconds = 2.3f, ReleaseLead = 14f, MissileScale = 2.6f, ExhaustScale = 3.2f, DescentPitch = 1.11f;
        /// <summary>
        /// the old screen height at the default RTS camera (70 m, 55 deg, fov 32: 35 m over the fort alone = viewport y 1.12, off the top;
        /// 33 m + 8 m = 0.90, the 26 m line was 0.90); the bomb still eases onto the same spot.</summary>
        public static float PlaneCameraOffset = 8f;
        // impact
        public static float SlowMo = 0.3f, SlowMoHold = 0.85f, SlowMoRelease = 0.6f;
        public static float RumbleStrength = 4f, RumbleSeconds = 8.5f;
        public static int SecondaryBlasts = 10;

        public static bool Active => current != null;
        /// <summary>Game seconds until the impact (-1 without a strike in flight).</summary>
        public static float SecondsToImpact => current && !current.impacted ? Mathf.Max(0f, current.impactAt - Time.time) : -1f;
        /// <summary>Game seconds (time scale 1) from Begin() to the impact with the current tunables (AudioDirector times the ear ring with it).</summary>
        public static float ExpectedImpactDelay => Mathf.Max(0.3f, PlaneLeadIn - ReleaseLead) / PlaneSpeed + FallSeconds;
        public static float LastImpactTime { get; private set; } = -100f;
        public static Vector3 LastImpactPoint { get; private set; }
        /// <summary>(impact point, the player's own fort?) at the moment of the impact.</summary>
        public static event Action<Vector3, bool> Impacted;

        static OpenZNukeStrike current;

        // (OpenZVoiceV3.PlanNuke gives the longest wait; LaunchNow ends it early). While held the bomber is hidden and silent.
        float hold, heldFor, beganAt; bool launchFired;
        /// <summary>voice_lines_v3: seconds the current strike's launch is (or was) held for the voices; 0 without a strike in flight.</summary>
        public static float HoldSeconds => current && !current.impacted ? (current.launchFired ? current.heldFor : current.hold) : 0f;
        /// <summary>voice_lines_v3: launch the held strike now (Zod slammed the button, or the voices are done).</summary>
        public static void LaunchNow() { if (current && !current.impacted && !current.launchFired) current.Launch(); }

        /// <summary>
        /// The bomber flies in the ATTACKER's shown team colour. The shark bomber's own paint has red
        /// accent panels (= the red team); a blue attacker gets airstrike_plane_blue.png (the same texture, its red accents
        /// recoloured to team blue 47,79,168 with their shading, khaki body untouched). Swap-aware through TeamColors.Key.</summary>
        public static string PlaneColour { get; private set; } = "red";
        void PaintPlane(GameObject model)
        {
            var g = ZGame.I; Team player = g ? g.player : Team.Red;
            Team attacker = ours ? (player == Team.Red ? Team.Blue : Team.Red) : player;           // ours = the player's fort falls: the enemy flies
            PlaneColour = TeamColors.Key(attacker);
            if (PlaneColour != "blue") return;                                                     // the model's own paint is red
            var blue = Resources.Load<Texture2D>("OpenZ/FX/airstrike_plane_blue");
            if (!blue) { Debug.LogWarning("[OpenZ] nuke strike: Resources/OpenZ/FX/airstrike_plane_blue.png missing: the bomber stays red"); return; }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;                                                            // instances: the shared glb material stays red
                foreach (var m in mats) if (m && m.HasProperty("baseColorTexture")) m.SetTexture("baseColorTexture", blue);
                r.materials = mats;
            }
            Debug.Log($"[OpenZ] nuke strike: bomber painted {PlaneColour} (attacker {attacker}, player's fort {ours})");
        }

        /// <summary>
        /// (its 1024 atlas with the red / orange fins, nose and dark maroon recoloured to team blue with their shading; the cream body
        /// and the yellow radiation sign untouched). PlaneColour is set by PaintPlane first (same attacker rule).</summary>
        public static string BombColour { get; private set; } = "red";
        void PaintBomb(GameObject model)
        {
            var g = ZGame.I; Team player = g ? g.player : Team.Red;
            Team attacker = ours ? (player == Team.Red ? Team.Blue : Team.Red) : player;
            BombColour = TeamColors.Key(attacker);
            if (BombColour != "blue") return;                                                      // the model's own paint is red / orange
            var blue = Resources.Load<Texture2D>("OpenZ/FX/legacy_nuke_blue");
            if (!blue) { Debug.LogWarning("[OpenZ] nuke strike: Resources/OpenZ/FX/legacy_nuke_blue.png missing: the bomb stays red"); BombColour = "red"; return; }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;
                foreach (var m in mats) if (m && m.HasProperty("baseColorTexture")) m.SetTexture("baseColorTexture", blue);
                r.materials = mats;
            }
            Debug.Log($"[OpenZ] nuke strike: bomb painted {BombColour} (attacker {attacker}, player's fort {ours}, plane {PlaneColour})");
        }

        void Launch()
        {
            launchFired = true;
            float now = Time.time;
            if (t0 > now)
            {   // launched before the hold ran out: the flight starts now
                t0 = now;
                releaseAt = t0 + Mathf.Max(0.3f, PlaneLeadIn - ReleaseLead) / PlaneSpeed;
                impactAt = releaseAt + FallSeconds;
            }
            heldFor = Mathf.Max(0f, t0 - beganAt);
            CombatAudio.I?.PlayWide("fortboom", target, 0.9f, "legacy-nuclear-launch");
            Place(now - t0);
        }

        Vector3 target, dir, planeFrom; bool ours, impacted; float t0, releaseAt, impactAt, impactTime = -100f;
        float impactY;                                                                                  // combat_nb_v1 NB29: the fort's roof / gun top under the bomb
        Action onImpact;
        Transform plane, missile; AudioSource engine; Light missileLight;
        Vector3 releasePos, missilePrev; float smokeRemainder; bool released, whooshed;

        /// <summary>Starts (or joins) the strike on a fort. onImpact runs at the impact (collapse the model + NukeFx). Returns false
        /// when strikes are off (the caller then does the old immediate nuke).</summary>
        public static bool Begin(Vector3 fortCentre, bool ours, Action onImpact)
        {
            if (!Enabled) return false;
            if (current && !current.impacted && (current.target - fortCentre).sqrMagnitude < 60f * 60f)
            {   // fort_front + fort_back die in the same tick: one bomber, one nuke, both halves collapse at the impact
                current.onImpact += onImpact;
                return true;
            }
            var go = new GameObject("OpenZNukeStrike");
            var s = go.AddComponent<OpenZNukeStrike>();
            s.target = fortCentre; s.ours = ours; s.onImpact = onImpact;
            s.hold = Mathf.Max(0f, OpenZVoiceV3.PlanNuke(ours));                                     // voice_lines_v3: 0 = launch at once (as before)
            current = s;
            s.Build();
            return true;
        }

        void Build()
        {
            beganAt = Time.time; t0 = Time.time + hold;                                                              // game time: the pause menu freezes the strike
            var cam = Camera.main;
            var right = cam ? cam.transform.right : Vector3.right; right.y = 0f; right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            var toCam = cam ? cam.transform.position - target : Vector3.back; toCam.y = 0f; toCam = toCam.sqrMagnitude > 1e-4f ? toCam.normalized : Vector3.back;
            dir = right * Mathf.Cos(PlaneYawTowardCamera * Mathf.Deg2Rad) + toCam * Mathf.Sin(PlaneYawTowardCamera * Mathf.Deg2Rad);   // left to right, a little toward the viewer
            dir.y = 0f; dir.Normalize();
            float gy = ZGame.I ? ZGame.I.GroundY(target.x, target.z) : target.y;
            target.y = gy;
            impactY = Mathf.Max(gy, FortTopAt(target, gy));                                          // combat_nb_v1 NB29: the bomb meets the roof, not the ground inside
            planeFrom = target - dir * PlaneLeadIn + Vector3.up * PlaneAltitude + toCam * PlaneCameraOffset;
            releaseAt = t0 + Mathf.Max(0.3f, PlaneLeadIn - ReleaseLead) / PlaneSpeed;                  // dropped a little before the fort: a curved dive
            impactAt = releaseAt + FallSeconds;

            var planeLib = Resources.Load<GameObject>("OpenZ/FX/airstrike_plane");
            if (planeLib)
            {
                var root = new GameObject("StrikePlane").transform; root.SetParent(transform, false);
                var model = Instantiate(planeLib, root, false); model.name = "airstrike_plane";
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
                PaintPlane(model);
                root.localScale = Vector3.one * PlaneScale;
                plane = root;
                var ca = CombatAudio.I; var clip = ca ? ca.Clip("v3-aircraft-engine-a") : null;
                if (clip)
                {
                    engine = root.gameObject.AddComponent<AudioSource>();
                    engine.clip = clip; engine.loop = true; engine.playOnAwake = false; engine.dopplerLevel = 0.6f; engine.priority = 40;
                    ca.Configure(engine, null, CombatAudio.Profile.Broad, 240f);
                    engine.volume = 0f; engine.Play();
                    OzSoundLog.Add("NukeStrike", "plane_engine", clip.name, "played", 0.5f);
                }
            }
            else Debug.LogWarning("[OpenZ] nuke strike: Resources/OpenZ/FX/airstrike_plane missing (fx_v2 README step 1): the nuke falls without a bomber");
            var nukeLib = Resources.Load<GameObject>("OpenZ/FX/legacy_nuke");
            if (nukeLib)
            {
                var root = new GameObject("StrikeMissile").transform; root.SetParent(transform, false);
                var model = Instantiate(nukeLib, root, false); model.name = "legacy_nuke";
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = false; }
                PaintBomb(model);
                root.localScale = Vector3.one * MissileScale;
                var lg = new GameObject("ExhaustLight"); lg.transform.SetParent(root, false); lg.transform.localPosition = new Vector3(0f, 0f, -1.4f);
                missileLight = lg.AddComponent<Light>(); missileLight.type = LightType.Point; missileLight.shadows = LightShadows.None;
                missileLight.renderMode = LightRenderMode.ForcePixel; missileLight.color = new Color(1f, 0.55f, 0.25f); missileLight.range = 18f; missileLight.intensity = 6f;
                root.gameObject.SetActive(false);
                missile = root;
            }
            if (hold <= 0f) Launch(); else Place(-hold);                                     // voice_lines_v3: alarm + flight at the launch
        }

        /// <summary>
        /// bomb fell to the GROUND height under the fort centre (target.y), 9-12 m inside the model. The height where it meets the
        /// fort: the fort mesh's own top there (TowerMounts.FortSurface, FortBase read / write) and the top of any part of the fort view
        /// standing over the point (the gun turret on the roof, renderer bounds, never the whole-model shell), capped 16 m above ground.</summary>
        static float FortTopAt(Vector3 p, float ground)
        {
            float top = ground; var g = ZGame.I;
            if (!g || g.World == null) return top;
            var px = ZGame.WorldToPx(p);
            foreach (var f in g.World.Objects)
            {
                if (!f.IsFort || px.x < f.LocX || px.y < f.LocY || px.x >= f.LocX + f.WidthPix || px.y >= f.LocY + f.HeightPix) continue;
                if (!g.StaticViews.TryGetValue(f.Id, out var go) || !go) continue;
                if (TowerMounts.FortSurface(go.transform, p, ground + 40f, out float y)) top = Mathf.Max(top, y);
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    var b = r.bounds;
                    if (b.size.x > 8f || b.size.z > 8f) continue;                                               // the fort shell itself: sampled above
                    if (p.x < b.min.x || p.x > b.max.x || p.z < b.min.z || p.z > b.max.z) continue;
                    top = Mathf.Max(top, b.max.y);
                }
            }
            return Mathf.Min(top, ground + 16f);
        }

        void Place(float age)
        {
            if (!plane) return;
            var pos = planeFrom + dir * (PlaneSpeed * age) + Vector3.up * (Mathf.Sin(age * 0.8f) * 0.7f + Mathf.Max(0f, age - PlaneLeadIn / PlaneSpeed) * 1.6f);
            plane.position = pos;
            var look = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(0f, PlaneYawOffset, 0f);   // nose = local -Z
            plane.rotation = look * Quaternion.Euler(0.05f * Mathf.Sin(age * 1.3f) * Mathf.Rad2Deg, 0f, 0.075f * Mathf.Sin(age * 0.8f) * Mathf.Rad2Deg);
            // engine: fade in over .6 s, out over the last 1.2 s of the flight
            float total = (PlaneLeadIn + PlaneExit) / PlaneSpeed;
            // otherwise it starts loud, far out on the lead-in), then out over the last 1.2 s of the flight
            float fade = Mathf.Clamp01((age + EnginePreRoll) / 0.7f) * Mathf.Clamp01((total - age) / 1.2f);
            if (engine)
            {
                var ca = CombatAudio.I;
                engine.volume = 0.5f * fade * (ca ? ca.LoopBusGain : 0.8f);
            }
            if (!whooshed && age >= PlaneLeadIn / PlaneSpeed - 0.35f)
            {
                whooshed = true;
                CombatAudio.I?.PlayWide("collapse", pos, 0.85f, "demo-flyby-whoosh");
            }
            plane.gameObject.SetActive(age >= 0f && age < total);                                    // voice_lines_v3: hidden while the launch is held
        }

        void Update()
        {
            float now = Time.time;
            if (!launchFired && now >= t0) Launch();                                         // voice_lines_v3: the hold ran out (watchdog)
            float age = now - t0;
            if (plane) Place(age);
            if (!released && now >= releaseAt) Release();
            if (released && !impacted) StepMissile(now);
            if (!impacted && now >= impactAt) Impact();
            if (impacted && (!plane || !plane.gameObject.activeSelf) && !slowing && now - impactTime > RumbleSeconds + 2.5f) Destroy(gameObject);
        }

        void Release()
        {
            released = true;
            if (plane) releasePos = plane.position + Vector3.down * (1.2f * PlaneScale);
            else releasePos = target - dir * 8f + Vector3.up * (PlaneAltitude + 10f);
            missilePrev = releasePos;
            if (missile) { missile.gameObject.SetActive(true); missile.position = releasePos; }
            CombatAudio.I?.Play("launch", releasePos, 0.8f, 0f, -1, "v3-bomb-release-a");
            CombatAudio.I?.PlayPitched("nuke", releasePos, 1.0f, 0f, -1, DescentPitch, "nuke-descent-20260909");
            OpenZVoiceV3.OnBombReleased(ours);                                            // voice_lines_v3: "Here comes the delivery!"
        }

        /// <summary></summary>
        void StepMissile(float now)
        {
            float u = Mathf.Clamp01((now - releaseAt) / FallSeconds);
            float fall = 0.35f * u + 0.65f * u * u;
            float ease = 1f - (1f - u) * (1f - u);                                   // keeps the plane's forward speed at first
            var p = Vector3.Lerp(releasePos, new Vector3(target.x, releasePos.y, target.z), ease);
            p.y = Mathf.Lerp(releasePos.y, impactY + 0.5f, fall);                                        // combat_nb_v1 NB29: to the fort's top (was the ground inside the fort)
            float dt = Time.deltaTime;
            if (dt <= 0f) return;                                                       // paused
            var v = (p - missilePrev) / dt;
            var heading = v.sqrMagnitude > 1e-3f ? v.normalized : Vector3.down;
            if (missile)
            {
                missile.position = p;
                missile.rotation = Quaternion.LookRotation(heading, Vector3.up);
                if (missileLight) missileLight.intensity = 6f * (0.85f + 0.15f * Mathf.Sin((now - releaseAt) * 47f));   // exhaust flicker on the ground below
            }
            // M12 exhaust (rocketExhaust, nuke exhaustScale 3.2) + smoke every .05 s + LEG halo
            var fx = OpenZFxPlayer.I;
            if (fx)
            {
                float len = 1.9f * MissileScale;                                          // tail = nose - body length
                var tail = p - heading * (len * 0.5f + OpenZFxCatalog.ExhaustDistance * ExhaustScale * 0.5f);
                fx.DrawExhaust(tail, heading, ExhaustScale, now - releaseAt);
                fx.DrawHalo(tail, 3.2f, new Color(1f, 0.36f, 0.12f), 1.2f);
                smokeRemainder += dt;
                while (smokeRemainder >= OpenZFxCatalog.RocketSmokeEvery)
                {
                    smokeRemainder -= OpenZFxCatalog.RocketSmokeEvery;
                    fx.EmitChannel("m12_smoke", tail - heading * 0.6f, OpenZFxCatalog.RocketSmokeVelocity, OpenZFxCatalog.RocketSmokeLife * 1.6f,
                        OpenZFxCatalog.RocketSmokeScale * ExhaustScale * 2f, OpenZFxCatalog.M12Smoke.color, OpenZFxCatalog.M12Smoke.alpha);
                }
            }
            missilePrev = p;
        }

        void Impact()
        {
            impacted = true;
            impactTime = LastImpactTime = Time.time; LastImpactPoint = target;
            if (missile) missile.gameObject.SetActive(false);
            if (impactY > target.y + 1f && OpenZFxPlayer.I)                                           // combat_nb_v1 NB29: the bomb bursts where it hits the roof
                OpenZFxPlayer.I.Blast(new Vector3(target.x, impactY + 0.5f, target.z), 9f, FxBlastKind.Missile, 98000 + (int)(t0 * 10f) % 1000, 2.2f);
            NukeFx.StrikeDelivered = true;                                             // NukeFx: no descent whistle at the blast
            var act = onImpact; onImpact = null;
            try { act?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            OpenZBloomDirector.Flash(1f);
            StartCoroutine(SlowMotion());
            StartCoroutine(Aftermath());
            try { Impacted?.Invoke(target, ours); } catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"[OpenZ] nuke strike impact at {target} ({(ours ? "our fort" : "enemy fort")}), {Time.time - t0:F1} s after the fort fell");
        }

        bool slowing; float scaleWritten = -1f;

        IEnumerator SlowMotion()
        {
            slowing = true;
            float start = Time.unscaledTime, total = SlowMoHold + SlowMoRelease;
            while (true)
            {
                float k = Time.unscaledTime - start;
                if (k >= total) break;
                if (Time.timeScale > 0f)                                                      // the pause menu owns 0
                {
                    float s = k < SlowMoHold ? SlowMo : Mathf.Lerp(SlowMo, 1f, Mathf.SmoothStep(0f, 1f, (k - SlowMoHold) / SlowMoRelease));
                    Time.timeScale = s; scaleWritten = s;
                }
                else start += Time.unscaledDeltaTime;                                         // paused: the beat waits
                yield return null;
            }
            if (Time.timeScale > 0f) Time.timeScale = 1f;
            scaleWritten = -1f; slowing = false;
        }

        IEnumerator Aftermath()
        {
            var fx = OpenZFxPlayer.I; var at = target;
            AudioDirector.Fire("nuke_shockwave", at);
            StartCoroutine(Rumble(at));
            yield return null; yield return null;                                                    // nuke_fx_v3: not in the impact frame (fort-fall hitch)
            // Z-style "more parts": secondary blasts running out with the shock ring, burning debris thrown high
            float gy = at.y;
            for (int i = 0; i < SecondaryBlasts; i++)
            {
                float a = i * 2.399f + 0.7f, r = 8f + (i % 4) * 2.2f;
                var p = at + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                p.y = (ZGame.I ? ZGame.I.GroundY(p.x, p.z) : gy) + 0.8f;
                if (fx) { fx.Blast(p, 0f, i % 3 == 0 ? FxBlastKind.Barrel : FxBlastKind.Rocket, 7000 + i); if (i % 2 == 0) fx.DemolitionDust(p, 3f, 3f, 0.8f); }
                else FxManager.I?.Explosion(p, 1.1f);
                if (i % 3 == 0) CombatAudio.I?.Play("fortboom", p, 0.7f, 0.1f, -1, "explosion-v1-20260909", "explosion-v2-20260909", "explosion-v3-20260909");
                yield return new WaitForSeconds(0.07f + (i % 3) * 0.04f);
            }
            yield return new WaitForSeconds(1.2f);
            AudioDirector.Fire("nuke_rumble_tail", at);
        }

        IEnumerator Rumble(Vector3 at)
        {
            // 2.6 s rumble through OzShakeGate (RumbleSeconds still times the object's clean-up)
            var shake = OpenZFxPlayer.I ? OpenZFxPlayer.I.Shake : null;
            if (shake == null) yield break;
            // (ShakeNukeRumble, ~1 s at full strength, eased out by ShakeNukeRumbleSeconds), above the fort fall without a strike
            shake.TimedRumble(at, OpenZFxCatalog.ShakeNukeRumble, OpenZFxCatalog.ShakeNukeRumbleSeconds, "nuke");
            OzEndingVignette.Begin(ours ? 1 : 0);
        }

        void OnDestroy()
        {
            if (scaleWritten > 0f && Time.timeScale > 0f) Time.timeScale = 1f;               // never leave the game in slow motion
            if (current == this) current = null;
        }
    }
}
