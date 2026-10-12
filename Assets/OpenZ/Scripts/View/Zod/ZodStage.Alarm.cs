// OpenZ (GPL-3.0)
// beacons on arms off the HQ wall behind Zod (between him and the tactical board, between him and the banner, far right), on for
// the whole nuke transmission (ZodBriefing: Kind.Nuke, from the window opening to its close). Each has a red glass dome that flares
// whenever its reflector faces the camera, and a red spot light that sweeps across the room (soft, not blinding: the light's
// source sits 0.3 m behind the reflector, so the beam never burns a hot spot into the wall right behind it); a faint red wash
// pulses with them. A low klaxon wail (sd_alarm_klaxon, pitched 0.8, through the radio low-pass) loops under Zod's voice.
// CALM SCREEN (OzMotion.Reduced): no sweep, no flare; the beacons pulse slowly and softly instead.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        /// <summary></summary>
        public static bool AlarmBeacons = true;
        [Header("HQ alarm beacons (nuke transmission)")]
        [Tooltip("Spot intensity of each beacon")] public float alarmSpotIntensity = 2.4f;
        [Tooltip("Red room wash peak intensity")] public float alarmWashIntensity = 0.9f;
        [Tooltip("Beacon turns per second")] public float alarmTurns = 0.8f;
        [Tooltip("Klaxon wail, dB on Zod's voice level")] public float alarmKlaxonDb = -8f;
        [Tooltip("The klaxon ducks this much (dB) under his words and swells back in the pauses")] public float alarmKlaxonWordDuckDb = -5f;
        /// <summary>ZodBriefing, every frame: a subtitle cue of his is up (he is speaking).</summary>
        public bool KlaxonUnderWords { get; set; }
        public float alarmKlaxonPitch = 0.8f;

        static readonly Vector3[] BeaconSpots = { new Vector3(-1.85f, 3.05f, -2.34f), new Vector3(1.75f, 3.08f, -2.34f), new Vector3(-3.05f, 2.62f, -2.34f) };
        GameObject alarmRoot; Light alarmWash; AudioSource klaxonSrc; float alarmW, alarmClock;
        readonly List<(Transform spin, Light spot, Material dome, float phase)> beacons = new List<(Transform, Light, Material, float)>();
        public bool AlarmOn { get; private set; }

        /// <summary>Starts / stops the HQ alarm (beacons fade in 0.3 s, out 0.8 s with the klaxon).</summary>
        public void Alarm(bool on)
        {
            if (on && !AlarmBeacons) return;
            if (on == AlarmOn) return;
            AlarmOn = on;
            if (on)
            {
                BuildAlarm();
                if (!alarmRoot) { AlarmOn = false; return; }
                alarmRoot.SetActive(true);
                if (klaxonSrc && klaxonSrc.clip)
                {
                    klaxonSrc.volume = 0f; klaxonSrc.pitch = alarmKlaxonPitch; klaxonSrc.Play();
                    OzSoundLog.Add("ZodCameo", "hq_alarm_klaxon", klaxonSrc.clip.name, "played", KlaxonGain());
                }
                else OzSoundLog.Add("ZodCameo", "hq_alarm_klaxon", "sd_alarm_klaxon", "missing clip");
                Debug.Log("[ZodCameo] HQ alarm ON (nuke): " + beacons.Count + " beacons" + (OzMotion.Reduced ? " (calm screen: slow pulse, no sweep)" : " sweeping") + ", klaxon wail under the voice");
            }
            else Debug.Log("[ZodCameo] HQ alarm off (beacons + klaxon fade out)");
        }

        float KlaxonGain() => Mathf.Clamp01(BoysSfxGain * Mathf.Pow(10f, alarmKlaxonDb / 20f));

        void BuildAlarm()
        {
            if (alarmRoot) return;
            alarmRoot = new GameObject("HqAlarm") { layer = Layer };
            alarmRoot.transform.SetParent(transform, false);
            var steel = Lit(new Color32(52, 56, 54, 255), 0.45f, 0.7f);
            var dk = Lit(new Color32(22, 24, 23, 255), 0.4f, 0.5f);
            for (int i = 0; i < BeaconSpots.Length; i++)
            {
                var p = BeaconSpots[i];
                var b = new GameObject("AlarmBeacon" + i) { layer = Layer }; b.transform.SetParent(alarmRoot.transform, false); b.transform.localPosition = p;
                var bt = b.transform;
                // the wall arm (wall front z -2.70) and the round base
                RBox("Arm", bt, new Vector3(0f, -0.075f, -0.18f), new Vector3(0.05f, 0.04f, 0.36f), 0.012f, steel);
                RBox("ArmPlate", bt, new Vector3(0f, -0.075f, -0.35f), new Vector3(0.14f, 0.14f, 0.02f), 0.006f, dk);
                Part(PrimitiveType.Cylinder, "Base", bt, new Vector3(0f, -0.04f, 0f), new Vector3(0.15f, 0.025f, 0.15f), dk);
                var dome = Glow(new Color32(150, 14, 10, 255), new Color(1f, 0.08f, 0.04f), 0.8f);
                if (dome) { dome.SetFloat("_Smoothness", 0.85f); }
                Part(PrimitiveType.Sphere, "Dome", bt, new Vector3(0f, 0.035f, 0f), new Vector3(0.13f, 0.16f, 0.13f), dome);
                Part(PrimitiveType.Cylinder, "Cap", bt, new Vector3(0f, 0.118f, 0f), new Vector3(0.05f, 0.008f, 0.05f), dk);
                var spin = new GameObject("Spin") { layer = Layer }; spin.transform.SetParent(bt, false); spin.transform.localPosition = new Vector3(0f, 0.035f, 0f);
                var sp = AddLight("BeaconSpot" + i, LightType.Spot, Vector3.zero, Vector3.zero, new Color(1f, 0.10f, 0.05f), 0f, 7.5f, 38f);
                sp.transform.SetParent(spin.transform, false); NoBezel(sp);
                sp.transform.localPosition = new Vector3(0f, 0f, -0.3f);                                    // the virtual source behind the reflector
                sp.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);                                  // a little down into the room
                beacons.Add((spin.transform, sp, dome, i * 2.1f));
            }
            alarmWash = AddLight("AlarmWash", LightType.Point, new Vector3(0f, 2.6f, -1.2f), new Vector3(0f, 2.6f, -1.2f), new Color(1f, 0.12f, 0.06f), 0f, 5.5f);
            alarmWash.transform.SetParent(alarmRoot.transform, true); NoBezel(alarmWash);
            klaxonSrc = alarmRoot.AddComponent<AudioSource>();                                               // 2D like Zod's voice: the HQ alarm inside the transmission
            klaxonSrc.playOnAwake = false; klaxonSrc.loop = true; klaxonSrc.spatialBlend = 0f; klaxonSrc.priority = 64; klaxonSrc.dopplerLevel = 0f; klaxonSrc.bypassReverbZones = true;
            klaxonSrc.clip = Resources.Load<AudioClip>("OpenZ/Audio/Sfx/sd_alarm_klaxon");
            var lp = alarmRoot.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 2400f;              // through the radio
            alarmRoot.SetActive(false);
        }

        /// <summary>LateUpdate: spin, flare, wash, klaxon fades.</summary>
        void TickAlarm(float dt)
        {
            if (!alarmRoot || !alarmRoot.activeSelf) return;
            alarmW = Mathf.MoveTowards(alarmW, AlarmOn ? 1f : 0f, dt / (AlarmOn ? 0.3f : 0.8f));
            if (!AlarmOn && alarmW <= 0f && (!klaxonSrc || klaxonSrc.volume <= 0.001f))
            {
                if (klaxonSrc) klaxonSrc.Stop();
                alarmRoot.SetActive(false); return;
            }
            alarmClock += dt;
            bool calm = OzMotion.Reduced;
            Vector3 camPos = cam ? cam.transform.position : transform.position + transform.forward * 3f;
            foreach (var (spin, spot, dome, phase) in beacons)
            {
                if (!spin) continue;
                float k;
                if (calm)
                {   // no sweep: the beam rests pointing into the room, a slow soft pulse
                    spin.localRotation = Quaternion.Euler(0f, 180f + phase * 10f, 0f);
                    k = 0.35f + 0.25f * (0.5f + 0.5f * Mathf.Sin(alarmClock * 2.2f + phase));
                    if (spot) spot.intensity = alarmSpotIntensity * 0.45f * k * alarmW;
                }
                else
                {
                    float ang = (alarmClock * alarmTurns * 360f + phase * 57.3f) % 360f;
                    spin.localRotation = Quaternion.Euler(0f, ang, 0f);
                    Vector3 toCam = Vector3.ProjectOnPlane(camPos - spin.position, transform.up).normalized;
                    float face = Mathf.Max(0f, Vector3.Dot(spin.forward, toCam));
                    k = 0.45f + 3.2f * Mathf.Pow(face, 6f);
                    if (spot) spot.intensity = alarmSpotIntensity * alarmW;
                }
                if (dome) dome.SetVector("_EmissionColor", (Vector4)(new Color(1f, 0.08f, 0.04f) * (0.5f + 2.2f * k) * alarmW));
            }
            if (alarmWash) alarmWash.intensity = alarmWashIntensity * alarmW * (calm ? 0.5f + 0.2f * Mathf.Sin(alarmClock * 2.2f) : 0.65f + 0.35f * Mathf.Sin(alarmClock * alarmTurns * Mathf.PI * 2f * 1.5f));
            if (klaxonSrc && klaxonSrc.isPlaying)
            {
                float want = AlarmOn ? KlaxonGain() * (KlaxonUnderWords ? Mathf.Pow(10f, alarmKlaxonWordDuckDb / 20f) : 1f) : 0f;
                klaxonSrc.volume = Mathf.MoveTowards(klaxonSrc.volume, want, dt / (AlarmOn ? 0.5f : 0.8f) * Mathf.Max(0.02f, KlaxonGain()));
            }
        }

        /// <summary>Proof: the alarm state in one line.</summary>
        public string AlarmStatus() => $"HQ alarm {(AlarmOn ? "ON" : "off")}, w {alarmW:F2}, beacons {beacons.Count}, spot {(beacons.Count > 0 && beacons[0].spot ? beacons[0].spot.intensity : 0f):F2}, " +
                                       $"klaxon {(klaxonSrc && klaxonSrc.isPlaying ? "playing vol " + klaxonSrc.volume.ToString("F3") : "off")}, calm {OzMotion.Reduced}";
    }
}
