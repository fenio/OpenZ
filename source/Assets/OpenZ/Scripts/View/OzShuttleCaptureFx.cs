using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K389 (view only): the shuttle's capture is shown with the game's own capture pieces instead of a snap to the team colour.
    /// When the squad that took an EMPTY shuttle sits inside (OzShuttleEmptyLook: dark to lit, CaptureHold released):
    /// <list type="bullet">
    /// <item>the FLASH of a captured building (FlagView.BuildingCaptureFlash: the team's flash light, 9 power, 13 m, 0.9 s, white-hot
    /// first 60 ms), over the ship's roof (outside the hull, so the hull and the ground round it catch it), and the bloom halo of a
    /// placed gun (PlacementPop.Halo: the camera-facing glow, gain 1.8, fading k squared) in the team's flash colour, sized to the ship
    /// as the gun's halo is to the gun (1.5 x its width; 0.24 s, twice the gun's 0.12 s for a ship about twice its size);</item>
    /// <item>its lamps and neon start as every captured vehicle's lights start (VehicleLights.Step: the power rises over 0.2 s and
    /// stutters for 0.8 s, Perlin noise at 24 Hz over 0.42 = on, else 12 %);</item>
    /// <item>its team panels ease from the empty grey to the team colour over <see cref="EaseSeconds"/> (TeamPaint.SetEase,
    /// smoothstep), starting under the flash;</item>
    /// <item>its sound: the vehicles' capture cue CombatView.EnterSounds already plays for the shuttle at this moment ("capture" +
    /// "engine_start" 0.15 s later); nothing is added.</item>
    /// </list>
    /// When the crew leaves (lit to dark: a hero pilot got out with everyone), the lights fade out over 0.4 s as every vehicle's do
    /// when its crew is gone (VehicleLights.Step), then go dark (mode 2); the paint turns grey at once, as every vehicle's does.
    /// The first look of a ship (a level start, a panel drop) is set at once, as VehicleLights lights a crewed vehicle at load.
    /// A destroyed ship is left to its own death (its lights and paint are set there).
    /// </summary>
    [DefaultExecutionOrder(-5)]   // before OzShuttleLights (0) reads Power in its Update
    public sealed class OzShuttleCaptureFx : MonoBehaviour
    {
        /// <summary>Off = the snap of before (the lights switched at once, no flash, no ease).</summary>
        public static bool On = true;
        // VehicleLights.Step's numbers (the engine-start flicker of a boarded / captured vehicle and its power ramps)
        public const float StartFlicker = 0.8f, FlickerHz = 24f, FlickerOn = 0.42f, FlickerLow = 0.12f, PowerUp = 0.2f, PowerDown = 0.4f;
        /// <summary>Seconds over which the team panels ease from the empty grey to the team colour (smoothstep).</summary>
        public const float EaseSeconds = 0.6f;
        // the halo: PlacementPop's placement flash (halo 3.0 m for a gun 2 m wide, gain 1.8, 0.12 s) sized to the ship
        public const float HaloPerWidth = 1.5f, HaloGain = 1.8f, HaloLife = 0.24f;
        /// <summary>The halo never wider than this (m): a ship with its legs spread still gets the ship-sized glow, never a screen-filling one.</summary>
        public const float HaloMax = 12f;
        /// <summary>The flash light this far over the roof (the building flash stands 3.5 m over a building's middle).</summary>
        public const float LightOverRoof = 1.0f;

        /// <summary>Proof counters: capture flashes shown, fades to dark, eases started; the last flash (unscaled time, team, halo size,
        /// light point).</summary>
        public static int Flashes, Fades, Eases;
        public static float LastFlashAt = -100f, LastHaloSize;
        public static Team LastTeam;
        public static Vector3 LastLightAt;

        OzPlayableShuttle ship; OzShuttleLights lights; TeamPaint paint; ZObject obj;
        float power = 1f, startAt = -100f, easeAt = -100f, seed; bool want = true, easing;

        /// <summary>The lights' level now (0 dark .. 1 full, with the start flicker).</summary>
        public float Shown => lights ? lights.Power : power;
        /// <summary>The paint ease now (1 = done or none).</summary>
        public float Ease => paint ? paint.EaseK : 1f;
        public bool Easing => easing;

        /// <summary>OzShuttleEmptyLook: the ship's look changed (dark = no pilot or the capture is still held; was = the look before:
        /// -1 the first look, 0 lit, 1 dark).</summary>
        public static void Switch(OzShuttleEmptyLook l, ZObject o, bool dark, int was)
        {
            if (!l) return;
            var fx = l.GetComponent<OzShuttleCaptureFx>();
            if (!fx)
            {
                fx = l.gameObject.AddComponent<OzShuttleCaptureFx>();
                fx.ship = l.GetComponent<OzPlayableShuttle>(); fx.lights = l.Lights; fx.paint = l.GetComponent<TeamPaint>();
                fx.seed = Hash((o != null ? o.Id : 0) * 7919 + 13);
            }
            fx.Set(o, dark, was);
        }

        void Set(ZObject o, bool dark, int was)
        {
            obj = o; want = !dark;
            if (was < 0)
            {   // the first look: at once, no flicker (a crewed vehicle at load is lit at once)
                power = want ? 1f : 0f; startAt = -100f;
                if (lights) { lights.Power = power; lights.SetMode(dark ? 2 : 0); }
                return;
            }
            if (dark) { Fades++; return; }                                                       // the lights fade out in Update, then mode 2
            if (lights) lights.SetMode(0);
            startAt = Time.time;
            if (lights) lights.Power = Level(Time.time);                                          // this frame already starts low
            Flash(o);
            if (paint) { paint.SetEase(Team.Neutral, 0f); easeAt = Time.time; easing = true; Eases++; }
        }

        void Flash(ZObject o)
        {
            var team = CaptureHold.Shown(o);
            if (team == Team.Neutral && o != null) team = o.Owner;
            var b = HullBounds();
            var top = new Vector3(b.center.x, b.max.y, b.center.z);
            LastLightAt = top + Vector3.up * LightOverRoof;
            FlagView.BuildingCaptureFlash(LastLightAt, team);
            LastHaloSize = Mathf.Min(HaloPerWidth * Mathf.Max(b.size.x, b.size.z), HaloMax);
            PlacementPop.Halo(top, LastHaloSize, HaloGain, HaloLife, FlagView.CaptureLightColor(team));
            Flashes++; LastFlashAt = Time.unscaledTime; LastTeam = team;
            Debug.Log($"[OpenZ] shuttle #{(o != null ? o.Id : -1)} captured by {team}: the capture flash (light over the roof at {LastLightAt.y:F1} m, halo {LastHaloSize:F1} m), lamps start, paint eases {EaseSeconds:F1} s");
        }

        /// <summary>The ship's drawn body (the model's own SH_ parts below this object: hull, legs, lamps), or a 6.4 x 5.1 m box round
        /// its root. The night-light heads, beams, lenses and halos hung under the ship (OZ_Fx..., Beam, Lens, Halo) are not its body:
        /// a lit beam cone would make the halo many metres too wide at night.</summary>
        Bounds HullBounds()
        {
            bool any = false; var b = new Bounds(transform.position + Vector3.up * 2.57f, new Vector3(6.4f, 5.14f, 6.7f));
            foreach (var r in GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer) || !r.name.StartsWith("SH_")) continue;
                if (any) b.Encapsulate(r.bounds); else { b = r.bounds; any = true; }
            }
            return b;
        }

        float Level(float now)
        {
            float p = power;
            if (now - startAt < StartFlicker) p *= Mathf.PerlinNoise(seed * 13f, now * FlickerHz) > FlickerOn ? 1f : FlickerLow;
            return p;
        }

        void Update()
        {
            if (obj == null || obj.Destroyed || (ship && ship.BrokenUp))
            {   // destroyed: its death sets the lights and the paint; nothing of this stays behind
                if (lights) lights.Power = 1f;
                if (easing && paint) paint.SetEase(Team.Neutral, 1f);
                easing = false; enabled = false; return;
            }
            float now = Time.time, dt = Time.deltaTime;
            float target = want ? 1f : 0f;
            power = Mathf.MoveTowards(power, target, dt / (target > power ? PowerUp : PowerDown));
            if (lights)
            {
                lights.Power = Level(now);
                if (!want && power <= 0f && lights.Mode != 2) lights.SetMode(2);                   // faded out: dark
            }
            if (easing)
            {
                float k = Mathf.Clamp01((now - easeAt) / EaseSeconds);
                if (!want) k = 1f;                                                                   // emptied again before the ease ended
                if (paint) paint.SetEase(Team.Neutral, k * k * (3f - 2f * k));
                if (k >= 1f) easing = false;
            }
        }

        static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u; return ((x >> 22) ^ x) / 4294967295f; } }   // VehicleLights.Hash

        /// <summary>A new level: the counters start again.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Flashes = Fades = Eases = 0; LastFlashAt = -100f; LastHaloSize = 0f; LastTeam = Team.Neutral; LastLightAt = Vector3.zero; }
    }
}
