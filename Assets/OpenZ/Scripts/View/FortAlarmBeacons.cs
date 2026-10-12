using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// View only, the Sim is never read for anything but state and never written.
    /// Auto-boots in every level scene (ZGame), like NightLights / VehicleLights.
    /// <list type="bullet">
    /// <item><b>Where</b>: 4 small chunky toy beacons on OUR fort only (the player's fort, like the klaxon; never on the enemy fort):
    /// two on the front parapet walkway flanking the gate (fort-local x +-3.6, z 5.70) and two at the front corners on the side
    /// walkways just behind the front towers (x +-6.75, z 1.60), all on the 7.045 m deck. Measured on the live FortBase (fort_v2:
    /// their gunners' ledge, the searchlight brackets, the gate / side floods and the defenders' roof nests. Seated at run time on
    /// TowerMounts.FortSurface (2 cm sunk: never floating, never co-planar); the measured height is the fallback. Fort palette: a
    /// cream foot like the fort walls, gunmetal collar and cap, a glossy red dome.</item>
    /// <item><b>When</b>: ON the frame AudioDirector starts the klaxon (AlarmStarts: StartLoop "fortalarm", its ALARM START line);
    /// on while AudioDirector.FortDanger (Zod danger level FORT, debounced like the music) or the klaxon still sounds. Within the same
    /// attack (the klaxon's own AlarmCalm re-arm window) a new FORT danger lights them again without a new klaxon. When the attack
    /// ends the reflectors spin slower and slower and the light fades out over fadeTime (2 s), then off. Never stuck on: game over
    /// (ZWorld.GameOver: a victory inside the klaxon burst too) = the same 2 s spin-down at once and never on again (AudioDirector
    /// OnGameOver also stops the klaxon); our fort destroyed or hidden = off at once.</item>
    /// <item><b>Light</b>: per beacon a rotating reflector = one real red spot light (no shadows) offered to the night package's
    /// budget through NightBudgetShare (the same cap, heat and frame-time scaling as every night light; without the night package a
    /// small own budget), a faint haze cone at night, the dome flashing each time the reflector faces the camera (lit red dome +
    /// additive bulb + small halo, HDR kept low: no bloom blobs), and a soft red NightPool glow on the nearby walls and deck (no
    /// real light). Readable by day (the dome flash) and at night (+ the sweeping red spot on walls and sand).</item>
    /// <item><b>Sound</b> (every visible change has one): ON with the klaxon (bld_fort_alarm), in sync with it; a relight within the
    /// same attack without a new klaxon = a quiet relay clunk (ngt_searchlight_on, pitched up); the spin-down (attack over or game
    /// over) = the quiet radar spin-down whine (bld_radar_dish_stop, sd_radar_spindown 2.4 s: it runs down with the reflectors; the
    /// klaxon burst is long over by then). Our fort destroyed = its own destruction sound. Every state change goes to OzSoundLog
    /// under bld_fort_alarm (src FAB: BEACONS ON / SPIN DOWN / OFF, naming the sound it plays, next to AudioDirector's ALARM START /
    /// STOP lines; never a PLAY line itself, so no sound counter changes), the sound's own AD PLAY line follows in the same frame,
    /// and the Console gets "[OpenZ] FortAlarmBeacons".</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(85)]   // Update before NightLights (90) ticks the budget; LateUpdate after it (and VehicleLights' fallback budget, 80)
    public class FortAlarmBeacons : MonoBehaviour
    {
        public static FortAlarmBeacons I { get; private set; }
        /// <summary>Set false (e.g. in ZGame.Awake) to add FortAlarmBeacons by hand.</summary>
        public static bool AutoBoot = true;
        public enum Phase { Off, On, SpinDown }

        [Header("Beacons")]
        [Range(0.6f, 2f)] public float size = 1.2f;                       // 0.37 m tall x 1.2 = 0.45 m: small, chunky, readable at the RTS height
        [Range(0.4f, 2f)] public float spinPeriod = 0.9f;                 // one turn of the reflector (s)
        [Range(0.5f, 4f)] public float fadeTime = 2f;                     // attack over: spin down + fade out
        [Range(0.05f, 1f)] public float spinUp = 0.3f;
        [Header("Light")]
        [Range(0f, 80f)] public float intensity = 30f;                    // x lightGain (0.6); the factories' work beacons are 24
        [Range(4f, 30f)] public float range = 16f;
        [Range(10f, 80f)] public float spotAngle = 38f;
        [Range(0f, 40f)] public float pitch = 24f;                        // reflector tilt down: sweeps the walls, the deck and the sand in front
        [Tooltip("The real spot's source sits this far above the dome (m): a soft sweep on the deck and the sand instead of a hot red disc at the foot (no bloom blobs)")]
        [Range(0f, 3f)] public float sourceLift = 1.0f;
        [Range(0f, 8f)] public float priority = 3.5f;
        [Range(0f, 2f)] public float beamGain = 0.12f, poolGain = 0.9f, haloGain = 0.6f, domeGain = 1f;
        [Header("Sound (lights_tail_v1 review fix: every visible change has one)")]
        public string spinDownSound = "bld_radar_dish_stop";              // SFX/World, sd_radar_spindown 2.4 s: runs down with the reflectors
        [Range(0f, 1f)] public float spinDownVol = 0.5f;
        public string relightSound = "ngt_searchlight_on";                // SFX/World relay clunk: a relight within the same attack (no klaxon)
        [Range(0f, 1f)] public float relightVol = 0.4f;
        [Range(0.5f, 2f)] public float relightPitch = 1.25f;
        public Color red = new Color(1f, 0.09f, 0.04f);
        public bool debugLog = true;

        /// <summary>Fort-local seats (Z_fort prefab root, metres) and names; see the summary.</summary>
        public static readonly Vector3[] Anchors =
        {
            new Vector3(-3.6f, 7.045f, 5.70f), new Vector3(3.6f, 7.045f, 5.70f),
            new Vector3(-6.75f, 7.045f, 1.60f), new Vector3(6.75f, 7.045f, 1.60f),
        };
        public static readonly string[] Names = { "gate_l", "gate_r", "corner_l", "corner_r" };
        const float DomeY = 0.262f;                                       // dome centre above the foot (model units, x size)

        public Phase State { get; private set; }
        public float Level { get; private set; }
        public float Spin { get; private set; }
        public float OnAt { get; private set; } = -1f;
        public float SpinDownAt { get; private set; } = -1f;
        public float OffAt { get; private set; } = -1f;
        public int TimesOn { get; private set; }
        /// <summary>lights_tail_v1 review fix: the game is over (ZWorld.GameOver): the beacons spun down and never come on again.</summary>
        public bool Over { get; private set; }
        public int RealLit { get; private set; }
        public int Count => beacons.Count;
        public string Seats { get; private set; } = "";
        public readonly List<int> FortIds = new List<int>();

        sealed class Beacon
        {
            public string Name; public ZObject Fort; public Transform FortT, Root, SpinT, PitchT; public Vector3 Local;
            public Renderer Dome, Bulb, Halo, Beam, Pool; public Light Light; public NightFixture Fix;
            public float Phase, Seed, DomeShown = -1f, BulbShown = -1f, HaloShown = -1f, BeamShown = -1f, PoolShown = -1f; public bool Shown = true;
        }

        readonly List<Beacon> beacons = new List<Beacon>();
        ZGame game; Camera cam; MaterialPropertyBlock mpb;
        Material cream, dome; float nextBuild, calmSince = -1f, s0, l0; int seenStarts; bool armed, built;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ------------------------------------------------------------------ boot
        static bool hooked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Early() { if (!hooked) { hooked = true; SceneManager.sceneLoaded += (s, m) => TryAttach(); } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => TryAttach();

        /// <summary>Level scenes only (a ZGame is present).</summary>
        public static void TryAttach()
        {
            if (!AutoBoot) return;
            var g = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            if (!g || FindFirstObjectByType<FortAlarmBeacons>()) return;
            new GameObject("FortAlarmBeacons").AddComponent<FortAlarmBeacons>();
        }

        void Awake()
        {
            if (I && I != this) { Destroy(this); return; }
            I = this;
            mpb = new MaterialPropertyBlock();
            game = ZGame.I ? ZGame.I : FindFirstObjectByType<ZGame>();
            cam = Camera.main;
            seenStarts = AudioDirector.AlarmStarts;                                    // a klaxon of an earlier level never lights this one
            if (!game) { Debug.LogWarning("[OpenZ] FortAlarmBeacons: no ZGame"); enabled = false; }
        }

        void OnDestroy()
        {
            foreach (var b in beacons) if (b.Fix != null) NightBudgetShare.Fixtures.Remove(b.Fix);
            if (cream) Destroy(cream); if (dome) Destroy(dome);
            if (I == this) I = null;
        }

        // ------------------------------------------------------------------ build
        void Build()
        {
            if (game == null || game.World == null) return;
            int forts = 0, waiting = 0;
            foreach (var o in game.World.Objects)
            {
                if (!o.IsFort || o.Owner != game.player || o.Destroyed) continue;          // OUR fort only (like the klaxon)
                if (!game.StaticViews.TryGetValue(o.Id, out var go) || !go) { waiting++; continue; }
                forts++; FortIds.Add(o.Id);
                var T = go.transform; var sb = new System.Text.StringBuilder();
                for (int i = 0; i < Anchors.Length; i++)
                {
                    var b = MakeBeacon(o, T, i, out string seat);
                    beacons.Add(b); sb.Append(sb.Length > 0 ? ", " : "").Append(Names[i]).Append(' ').Append(seat);
                }
                Seats += (Seats.Length > 0 ? "; " : "") + $"fort #{o.Id} {o.Type}: " + sb;
            }
            if (waiting > 0 && forts == 0) return;                                        // the fort view is not bound yet: next second
            built = true;
            Debug.Log($"[OpenZ] FortAlarmBeacons {game.level}: {beacons.Count} beacons on {forts} {game.player} fort(s) " +
                      $"(on with the klaxon, off {fadeTime:F1} s after the attack; lights via {(NightLights.I ? "the night budget" : "their own budget")}). {Seats}");
        }

        Beacon MakeBeacon(ZObject fort, Transform T, int i, out string seat)
        {
            var a = Anchors[i]; float y = a.y; seat = $"y {a.y:F3} (measured)";
            var probe = T.TransformPoint(new Vector3(a.x, a.y + 0.6f, a.z));
            if (TowerMounts.FortSurface(T, probe, probe.y, out float wy))
            {
                float ly = T.InverseTransformPoint(new Vector3(probe.x, wy, probe.z)).y;
                if (Mathf.Abs(ly - a.y) <= 0.35f) { y = ly; seat = $"y {ly:F3} (surface, measured {a.y:F3})"; }
                else seat = $"y {a.y:F3} (measured; surface {ly:F3} rejected)";
            }
            var b = new Beacon { Name = Names[i], Fort = fort, FortT = T, Local = new Vector3(a.x, y - 0.02f, a.z), Seed = Hash(fort.Id * 131 + i * 17 + 5) };
            b.Phase = b.Seed * 360f;
            b.Root = new GameObject($"FAB_{fort.Id}_{b.Name}").transform; b.Root.SetParent(transform, false);
            b.Root.SetPositionAndRotation(T.TransformPoint(b.Local), T.rotation);
            var model = new GameObject("Model").transform; model.SetParent(b.Root, false); model.localScale = Vector3.one * size;
            if (!cream) cream = Lit("FAB_Cream", new Color(0.84f, 0.79f, 0.69f), 0.28f, 0f);        // the fort walls' cream
            if (!dome) { dome = Lit("FAB_Dome", new Color(0.66f, 0.06f, 0.04f), 0.82f, 0f); dome.EnableKeyword("_EMISSION"); }
            NightMeshes.Part("Foot", model, FootMesh(), cream, true, Vector3.zero, Quaternion.identity, Vector3.one);
            NightMeshes.Part("Collar", model, CollarMesh(), NightMeshes.Housing(), true, Vector3.zero, Quaternion.identity, Vector3.one);
            b.Dome = NightMeshes.Part("Dome", model, DomeMesh(), dome, true, Vector3.zero, Quaternion.identity, Vector3.one).GetComponent<Renderer>();
            b.Bulb = NightMeshes.Part("Bulb", model, NightMeshes.Sphere(), NightMeshes.Bulb(), false, new Vector3(0f, DomeY, 0f), Quaternion.identity, new Vector3(0.125f, 0.14f, 0.125f)).GetComponent<Renderer>();
            b.Halo = NightMeshes.Part("Halo", model, NightMeshes.Quad(), NightMeshes.Halo(), false, new Vector3(0f, DomeY, 0f), Quaternion.identity, Vector3.one * 0.5f).GetComponent<Renderer>();
            b.Bulb.enabled = false; b.Halo.enabled = false;
            // the rotating reflector: spot light + a faint haze cone, pitched down
            b.SpinT = new GameObject("Spin").transform; b.SpinT.SetParent(b.Root, false); b.SpinT.localPosition = new Vector3(0f, DomeY * size, 0f);
            b.PitchT = new GameObject("Reflector").transform; b.PitchT.SetParent(b.SpinT, false); b.PitchT.localRotation = Quaternion.Euler(pitch * 0.5f, 0f, 0f);   // the haze cone leaves the dome flatter than the lifted spot
            var lgo = new GameObject("Light"); lgo.transform.SetParent(b.SpinT, false);                 // a virtual source above the dome, turning with the reflector
            lgo.transform.localPosition = new Vector3(0f, sourceLift, 0f); lgo.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = spotAngle; l.innerSpotAngle = spotAngle * 0.35f; l.range = range; l.color = red;
            l.intensity = 0f; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel; l.enabled = false;
            b.Light = l;
            float L = 4.5f, ang = Mathf.Tan(spotAngle * 0.5f * Mathf.Deg2Rad) * 0.85f;
            b.Beam = NightMeshes.Part("Beam", b.PitchT, NightMeshes.Cone(), NightMeshes.Beam(), false, new Vector3(0f, 0f, 0.05f), Quaternion.identity, new Vector3(L * ang, L * ang, L)).GetComponent<Renderer>();
            b.Beam.enabled = false;
            // soft red glow on the walls and the deck around it (projected impostor, no real light)
            b.Pool = NightMeshes.Part("Glow", b.Root, NightMeshes.Cube(), NightMeshes.Pool(), false, new Vector3(0f, -0.45f, 0f), Quaternion.identity, new Vector3(4.4f, 3.0f, 4.4f)).GetComponent<Renderer>();
            b.Pool.enabled = false;
            b.Fix = new NightFixture
            {
                Kind = NightKind.WorkBeacon, Id = $"fortalarm_{fort.Id}_{b.Name}", Light = l, Col = red, ColLinear = red.linear,
                Intensity = intensity, Range = range, Angle = spotAngle, Priority = priority, ShadowCandidate = false, Root = b.Root, Seed = b.Seed,
            };
            NightBudgetShare.Fixtures.Add(b.Fix);
            SetEmission(b, Color.black);
            return b;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (!game) return;
            float now = Time.time, dt = Time.deltaTime;
            if (!built && now >= nextBuild) { nextBuild = now + 1f; Build(); }
            int starts = AudioDirector.AlarmStarts; bool klaxonStart = starts != seenStarts; seenStarts = starts;
            var ad = AudioDirector.I;
            // lights_tail_v1 review fix: after the game over AudioDirector.UpdateMusic no longer runs (a klaxon started just before a
            // victory was never stopped there): the game over alone ends the alarm, whatever the klaxon / danger say
            bool over = game.World != null && game.World.GameOver;
            if (over && !Over) { Over = true; armed = false; }
            bool danger = !over && AudioDirector.FortDanger, klaxon = !over && ad && ad.LoopActive("fortalarm");
            bool fortOk = FortOk();
            if (klaxonStart && !over) armed = true;
            if (danger || klaxon) calmSince = -1f; else if (calmSince < 0f) calmSince = now;
            if (armed && calmSince >= 0f && now - calmSince >= AudioDirector.AlarmCalm) armed = false;   // the klaxon's own re-arm: a new attack = a new ALARM START
            bool want = !over && armed && fortOk && beacons.Count > 0 && (danger || klaxon);

            if (want && State != Phase.On)
            {
                string why = klaxonStart ? $"with the klaxon (ALARM START #{starts})"
                           : State == Phase.SpinDown ? "back on (danger FORT again during the spin-down, same attack)"
                           : "again (same attack: danger FORT again, the klaxon already sounded)";
                State = Phase.On; OnAt = now; TimesOn++;
                if (klaxonStart) Note($"BEACONS ON {beacons.Count} {why}");                                // the klaxon is the sound
                else { Note($"BEACONS ON {beacons.Count} {why}, sound {relightSound}"); BeaconSound(relightSound, relightVol, relightPitch); }   // review fix: a relight is never silent
            }
            else if (klaxonStart && State == Phase.On) Note("klaxon again (a much bigger wave): beacons already on");
            else if (!want && State == Phase.On && fortOk)
            {   // (our fort gone: OFF at once below, its destruction is the sound)
                State = Phase.SpinDown; SpinDownAt = now; s0 = Spin; l0 = Level;
                Note("BEACONS SPIN DOWN (" + (over ? "game over" : !armed ? "re-armed" : "attack over: danger " + (danger ? "FORT" : "left FORT") + ", klaxon " + (klaxon ? "on" : "off")) + $"), fade {fadeTime:F1} s, sound {spinDownSound}");
                BeaconSound(spinDownSound, spinDownVol, 1f);                                                // review fix: the spin-down whine runs down with the reflectors
            }

            if (!fortOk && State != Phase.Off) { State = Phase.Off; Level = Spin = 0f; OffAt = now; Note("BEACONS OFF at once (our fort destroyed / hidden)"); }
            switch (State)
            {
                case Phase.On:
                    Level = Mathf.MoveTowards(Level, 1f, dt / 0.25f);
                    Spin = Mathf.MoveTowards(Spin, 1f, dt / Mathf.Max(0.05f, spinUp));
                    break;
                case Phase.SpinDown:
                {
                    float t = Mathf.Clamp01((now - SpinDownAt) / Mathf.Max(0.1f, fadeTime));
                    Spin = s0 * (1f - t);                                                   // the reflector slows down to a stop
                    Level = l0 * Mathf.Pow(1f - t, 1.5f);                                   // and the lamp fades with it
                    if (t >= 1f) { State = Phase.Off; Level = Spin = 0f; OffAt = now; Note($"BEACONS OFF after a {now - SpinDownAt:F1} s spin-down / fade"); }
                    break;
                }
                default: Level = 0f; Spin = 0f; break;
            }

            for (int i = 0; i < beacons.Count; i++)
            {
                var b = beacons[i];
                b.Phase = Mathf.Repeat(b.Phase + Spin * 360f / (spinPeriod * (0.95f + 0.1f * b.Seed)) * dt, 360f);
                b.Fix.Priority = priority; b.Fix.Intensity = intensity; b.Fix.Range = range;
                b.Fix.LightOut = b.Shown ? Level : 0f;                                     // the budget ranks it this frame (NightLights, order 90)
            }
        }

        bool FortOk()
        {
            if (beacons.Count == 0) return false;
            foreach (var b in beacons) if (b.Fort != null && !b.Fort.Destroyed && b.FortT && b.FortT.gameObject.activeInHierarchy) return true;
            return false;
        }

        void LateUpdate()
        {
            if (!game || beacons.Count == 0) return;
            float now = Time.time, dt = Time.deltaTime, night = NightGrade.NightK, dusk = Mathf.Lerp(0.25f, 1f, night);
            if (!cam) cam = Camera.main;
            var camPos = cam ? cam.transform.position : Vector3.zero;
            var nl = NightLights.I;
            bool budgetLive = NightBudgetShare.HookFrame >= Time.frameCount - 2 || (VehicleLights.I && VehicleLights.I.isActiveAndEnabled);
            float gain = nl ? nl.lightGain : 0.6f;
            bool beamsOn = !nl || nl.beams, poolsOn = !nl || nl.pools, halosOn = !nl || nl.halos;
            var redLin = red.linear; int lit = 0;
            foreach (var b in beacons)
            {
                bool vis = b.Fort != null && !b.Fort.Destroyed && b.FortT && b.FortT.gameObject.activeInHierarchy;
                if (vis != b.Shown) { b.Shown = vis; b.Root.gameObject.SetActive(vis); }
                if (!vis) continue;
                var wp = b.FortT.TransformPoint(b.Local);                                  // follows the fort (the game-over fall, a shake)
                if ((wp - b.Root.position).sqrMagnitude > 1e-6f || Quaternion.Angle(b.Root.rotation, b.FortT.rotation) > 0.05f) b.Root.SetPositionAndRotation(wp, b.FortT.rotation);
                b.SpinT.localRotation = Quaternion.Euler(0f, b.Phase, 0f);
                float L = Level, flash = 0f;
                if (L > 0.001f)
                {   // the reflector faces the camera: the dome flashes
                    var dir = b.PitchT.forward; dir.y = 0f;
                    var to = camPos - b.SpinT.position; to.y = 0f;
                    if (dir.sqrMagnitude > 1e-4f && to.sqrMagnitude > 1e-4f) flash = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(dir.normalized, to.normalized)), 6f);
                }
                if (!budgetLive) b.Fix.RealK = Mathf.MoveTowards(b.Fix.RealK, b.Fix.LightOut > 0.001f ? 1f : 0f, dt / 0.35f);   // no night package: our own 4 lights
                float realK = b.Fix.RealK;
                float domeK = domeGain * L * Mathf.Lerp(1.6f, 1.1f, night) * (0.55f + 2.2f * flash);
                if (Mathf.Abs(domeK - b.DomeShown) > Mathf.Max(0.01f, b.DomeShown * 0.02f)) { b.DomeShown = domeK; SetEmission(b, redLin * domeK); }
                SetColor(b.Bulb, redLin * (L * (0.35f + 2.0f * flash) * Mathf.Lerp(0.45f, 1f, night)), ref b.BulbShown);
                SetColor(b.Halo, halosOn ? redLin * (haloGain * dusk * L * (0.25f + 1.4f * flash)) : Color.black, ref b.HaloShown);
                SetColor(b.Beam, beamsOn ? redLin * (beamGain * L * Mathf.Pow(night, 0.8f)) : Color.black, ref b.BeamShown);
                SetColor(b.Pool, poolsOn ? redLin * (poolGain * L * Mathf.Lerp(0.3f, 1f, night) * (0.8f + 0.2f * flash) * (1f - 0.4f * realK)) : Color.black, ref b.PoolShown);
                // the real sweeping spot (budget: RealK)
                float k = b.Fix.LightOut * realK;
                bool on = k > 0.002f && b.Light.gameObject.activeInHierarchy;
                if (b.Light.enabled != on) b.Light.enabled = on;
                if (on) { b.Light.intensity = intensity * k * gain * Mathf.Lerp(1.4f, 1f, night); lit++; }
            }
            RealLit = lit;
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>lights_tail_v1 review fix: the one-shot of a beacon change (relight / spin-down), at the middle of the first fort's
        /// beacons; its AD PLAY (or DROP) line follows the FAB line in the same frame.</summary>
        void BeaconSound(string key, float vol, float pitch)
        {
            if (beacons.Count == 0 || string.IsNullOrEmpty(key)) return;
            var fort = beacons[0].Fort; var c = Vector3.zero; int n = 0;
            foreach (var b in beacons) if (b.Fort == fort && b.Root && b.Shown) { c += b.Root.position; n++; }
            if (n == 0) return;                                                                              // hidden: nothing to see or hear
            c /= n;
            if (AudioDirector.Has(key)) AudioDirector.Fire(key, c, fort != null ? fort.Id : -1, vol, pitch);
            else OzSoundLog.Add("FAB", key, null, "DROP no AudioDirector event (sound table not imported?)", 0f, c);
        }

        void Note(string what)
        {
            var at = beacons.Count > 0 && beacons[0].Root ? beacons[0].Root.position : (Vector3?)null;
            OzSoundLog.Add("FAB", "bld_fort_alarm", null, what, 0f, at);
            if (debugLog) Debug.Log($"[OpenZ] FortAlarmBeacons {(game ? game.level : "?")} t {Time.time:F2}: {what}");
        }

        void SetEmission(Beacon b, Color c)
        {
            if (!b.Dome) return;
            b.Dome.GetPropertyBlock(mpb); mpb.SetColor(EmissionId, c); b.Dome.SetPropertyBlock(mpb);
        }

        void SetColor(Renderer r, Color c, ref float shown)
        {
            if (!r) return;
            float key = c.r + c.g * 1.7f + c.b * 0.6f;
            if (shown >= 0f && Mathf.Abs(key - shown) <= Mathf.Max(0.004f, shown * 0.012f)) return;
            shown = key;
            bool on = key > 0.0015f;
            if (r.enabled != on) r.enabled = on;
            if (!on) return;
            mpb.Clear(); mpb.SetColor(NightMeshes.ColorId, c); r.SetPropertyBlock(mpb);
        }

        static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; } }

        static Material Lit(string name, Color baseCol, float smooth, float metal)
        {   // the night package's housing recipe (NightMeshes.Lit): OpenZ/TeamLit, neutral (no team paint), night rim like the buildings
            var s = Shader.Find("OpenZ/TeamLit");
            if (!s) s = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(s) { name = name, hideFlags = HideFlags.DontSave, enableInstancing = true };
            m.SetColor("_BaseColor", baseCol);
            if (m.HasProperty("_TeamMode")) m.SetFloat("_TeamMode", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (m.HasProperty("_Accent")) m.SetFloat("_Accent", 1f);
            if (m.HasProperty("_NightClass")) m.SetFloat("_NightClass", 2f);
            if (m.HasProperty("_Degreen")) m.SetFloat("_Degreen", 0f);
            return m;
        }

        // procedural chunky toy beacon (model units; x size): cream foot 0..0.14, gunmetal collar 0.14..0.18 + cap, red dome 0.18..0.35
        static Mesh foot, collar, domeMesh;
        const int Seg = 16;
        static Mesh FootMesh() => foot ? foot : foot = Build("FAB_Foot", (v, n, t) =>
        {
            Band(v, n, t, 0.17f, 0f, 0.155f, 0.06f); Disc(v, n, t, 0.155f, 0.06f, 0.13f);   // chamfered plate, its top ring
            Band(v, n, t, 0.13f, 0.06f, 0.13f, 0.14f); Disc(v, n, t, 0.13f, 0.14f, 0f);
        });
        static Mesh CollarMesh() => collar ? collar : collar = Build("FAB_Collar", (v, n, t) =>
        {
            Band(v, n, t, 0.142f, 0.135f, 0.142f, 0.18f); Disc(v, n, t, 0.142f, 0.18f, 0.113f);
            Band(v, n, t, 0.035f, 0.335f, 0.035f, 0.372f); Disc(v, n, t, 0.035f, 0.372f, 0f);   // the cap on the dome
        });
        static Mesh DomeMesh() => domeMesh ? domeMesh : domeMesh = Build("FAB_Dome", (v, n, t) =>
        {
            Band(v, n, t, 0.115f, 0.176f, 0.115f, 0.235f);
            const int R = 6;                                                                 // hemisphere on top, smooth
            for (int r = 0; r < R; r++)
            {
                float a0 = r * Mathf.PI * 0.5f / R, a1 = (r + 1) * Mathf.PI * 0.5f / R;
                int o = v.Count;
                for (int i = 0; i <= Seg; i++)
                {
                    float p = i * Mathf.PI * 2f / Seg; var d = new Vector3(Mathf.Sin(p), 0f, Mathf.Cos(p));
                    foreach (float a in new[] { a0, a1 })
                    {
                        var nn = d * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                        v.Add(nn * 0.115f + Vector3.up * 0.235f); n.Add(nn);
                    }
                }
                for (int i = 0; i < Seg; i++) { int q = o + i * 2; t.AddRange(new[] { q, q + 3, q + 1, q, q + 2, q + 3 }); }   // clockwise seen from outside
            }
        });

        static Mesh Build(string name, System.Action<List<Vector3>, List<Vector3>, List<int>> make)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            make(v, n, t);
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>Side band from (r0, y0) to (r1, y1), outward normals (a frustum when r0 != r1), smooth around.</summary>
        static void Band(List<Vector3> v, List<Vector3> n, List<int> t, float r0, float y0, float r1, float y1)
        {
            int o = v.Count;
            var slope = new Vector2(y1 - y0, r0 - r1).normalized;                           // (radial, up) of the side normal
            for (int i = 0; i <= Seg; i++)
            {
                float p = i * Mathf.PI * 2f / Seg; var d = new Vector3(Mathf.Sin(p), 0f, Mathf.Cos(p));
                var nn = (d * slope.x + Vector3.up * slope.y).normalized;
                v.Add(d * r0 + Vector3.up * y0); n.Add(nn);
                v.Add(d * r1 + Vector3.up * y1); n.Add(nn);
            }
            for (int i = 0; i < Seg; i++) { int q = o + i * 2; t.AddRange(new[] { q, q + 3, q + 1, q, q + 2, q + 3 }); }   // clockwise seen from outside
        }

        /// <summary>Flat upward ring (outer r, inner ri; ri 0 = a disc) at height y.</summary>
        static void Disc(List<Vector3> v, List<Vector3> n, List<int> t, float r, float y, float ri)
        {
            int o = v.Count;
            for (int i = 0; i <= Seg; i++)
            {
                float p = i * Mathf.PI * 2f / Seg; var d = new Vector3(Mathf.Sin(p), 0f, Mathf.Cos(p));
                v.Add(d * r + Vector3.up * y); n.Add(Vector3.up);
                v.Add(d * ri + Vector3.up * y); n.Add(Vector3.up);
            }
            for (int i = 0; i < Seg; i++) { int q = o + i * 2; t.AddRange(new[] { q, q + 3, q + 1, q, q + 2, q + 3 }); }
        }

        // ------------------------------------------------------------------ debug
        public string Status()
        {
            return $"[OpenZ] FortAlarmBeacons {(game ? game.level : "?")}: {beacons.Count} beacons (forts {string.Join(",", FortIds)}), state {State}, level {Level:F2}, spin {Spin:F2}, " +
                   $"armed {armed}, game over {Over}, danger FORT {AudioDirector.FortDanger}, klaxon {(AudioDirector.I && AudioDirector.I.LoopActive("fortalarm"))}, real lights {RealLit}, " +
                   $"on at {OnAt:F2}, spin-down at {SpinDownAt:F2}, off at {OffAt:F2}, times on {TimesOn}";
        }
    }
}
