#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// legacy_match Play-mode check (README_IMPORT "Check list"). Editor / development builds only, boots itself with ZGame.
    ///   F6  mix overlay: duck, zoom gain, range scale, voices per bus, engine loops, compressor gain reduction, listener rig
    ///   F7  explosion line-up at the camera focus, smallest to largest (the LEGACY_MATCH.md size table), label on screen
    ///       shell, grenade) flying 1.2 s from 22 m left of the focus, landing with its table blast and sound
    /// </summary>
    public class LegacyMatchCheck : MonoBehaviour
    {
        bool overlay; string label = ""; float labelUntil;
        RtsCamera rts;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (FindFirstObjectByType<ZGame>() && !FindFirstObjectByType<LegacyMatchCheck>()) new GameObject("LegacyMatchCheck").AddComponent<LegacyMatchCheck>(); }

        Vector3 Focus()
        {
            if (!rts) rts = FindFirstObjectByType<RtsCamera>();
            var f = rts ? rts.focus : Vector3.zero; f.y = ZGame.I ? ZGame.I.GroundY(f.x, f.z) : 0f; return f;
        }

        void Update()
        {
            var kb = Keyboard.current; if (kb == null) return;
            if (kb.f6Key.wasPressedThisFrame) overlay = !overlay;
            if (kb.f7Key.wasPressedThisFrame) { StopAllCoroutines(); StartCoroutine(Blasts(Focus())); }
            if (kb.f8Key.wasPressedThisFrame) { StopAllCoroutines(); StartCoroutine(Weapons(Focus())); }
        }

        void Say(string s) { label = s; labelUntil = Time.unscaledTime + 1.6f; Debug.Log("[legacy_match] " + s); }

        IEnumerator Blasts(Vector3 f)
        {
            var list = new List<LegacyMatch.BlastSpec> { LegacyMatch.DeathCrate, LegacyMatch.Grenade, LegacyMatch.LightShell, LegacyMatch.Bazooka, LegacyMatch.GunShell,
                LegacyMatch.MediumShell, LegacyMatch.MissileCannonRocket, LegacyMatch.HowitzerShell, LegacyMatch.MlrsRocket, LegacyMatch.HeavyShell,
                LegacyMatch.DeathJeep, LegacyMatch.DeathCannon, LegacyMatch.DeathApc, LegacyMatch.DeathLight, LegacyMatch.DeathMlrs, LegacyMatch.DeathMedium,
                LegacyMatch.DeathHeavy, LegacyMatch.DeathRadar, LegacyMatch.DeathFactory };
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i]; var at = f + new Vector3((i % 5 - 2) * 7f, 0.6f, (1 - i / 5) * 7f);
                at.y = (ZGame.I ? ZGame.I.GroundY(at.x, at.z) : 0f) + 0.6f;
                LegacyMatch.DoBlast(at, d, 9000 + i, FxManager.I);
                if (d.clips != null && CombatAudio.I) CombatAudio.I.PlayPitched(d.key, at, d.gain, 0f, -1, d.pitch, d.clips);
                Say($"{i + 1}/{list.Count} {d.label}: s {d.s:0.00}, LEG r {d.legRadius:0.0} m, {d.kind}, sound {d.key ?? "(RulesView collapse)"}");
                yield return new WaitForSeconds(1.4f);
            }
            Say("explosion line-up done");
        }

        IEnumerator Weapons(Vector3 f)
        {
            var zfx = OpenZFxPlayer.I; var au = CombatAudio.I;
            var from = f + new Vector3(-22f, 1.2f, 0f); var target = f + Vector3.up * 0.9f;
            string[] shooters = { "grunt", "grunt", "psycho", "jeep", "sniper", "gatling" };
            foreach (var type in shooters)
            {
                var w = LegacyMatch.ShotWeapon(type);
                for (int k = 0; k < 3; k++)
                {
                    if (zfx) zfx.Shot(from + Vector3.forward * k * 0.3f, target, w, FxSurface.Metal);
                    if (au) au.Play(w == FxWeapon.Bolt ? "bolt" : w == FxWeapon.Sniper ? "sniper" : "burst", from, w == FxWeapon.Bolt ? LegacyMatch.GainBolt : 0.6f, 0f, 9100 + k,
                        w == FxWeapon.Bolt ? LegacyMatch.BankBolt : w == FxWeapon.Sniper ? LegacyMatch.BankSniper : LegacyMatch.BankRifleBurst);
                    yield return new WaitForSeconds(0.18f);
                }
                Say($"{type}: {w}");
                yield return new WaitForSeconds(0.5f);
            }
            (string type, LegacyMatch.BlastSpec blast)[] shots = { ("tough", LegacyMatch.Bazooka), ("missile_launcher", LegacyMatch.MlrsRocket), ("missile_cannon", LegacyMatch.MissileCannonRocket),
                ("light", LegacyMatch.LightShell), ("medium", LegacyMatch.MediumShell), ("heavy", LegacyMatch.HeavyShell), ("gun", LegacyMatch.GunShell), ("grenade", LegacyMatch.Grenade) };
            foreach (var (type, blast) in shots)
            {
                var land = new Vector3(f.x, (ZGame.I ? ZGame.I.GroundY(f.x, f.z) : 0f) + 0.25f, f.z);
                float flight = 1.2f * (ZGame.I ? Mathf.Max(0.05f, ZGame.I.gameSpeed) : 1f);                // Launch divides by gameSpeed
                if (type == "grenade") { LegacyMatch.GrenadeFlight(from, land, flight, FxManager.I); LegacyMatch.GrenadeSound(from, 9200, au); }
                else
                {
                    if (zfx) zfx.Launch(from, land, flight, LegacyMatch.MissileWeapon(null, type));
                    LegacyMatch.LaunchSound(type, from, 9200, au);
                }
                Say($"{type}: {(type == "grenade" ? FxWeapon.Grenade : LegacyMatch.MissileWeapon(null, type))} -> {blast.label}");
                yield return new WaitForSeconds(1.2f);
                LegacyMatch.DoBlast(land, blast, 9300, FxManager.I);
                if (au && blast.clips != null) au.PlayPitched(blast.key, land, blast.gain, 0f, -1, blast.pitch, blast.clips);
                yield return new WaitForSeconds(1.0f);
            }
            Say("weapon line-up done");
        }

        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 18 }; st.normal.textColor = Color.white;
            if (Time.unscaledTime < labelUntil) GUI.Label(new Rect(20, Screen.height * 0.18f, Screen.width - 40, 30), label, st);
            if (!overlay) return;
            var au = CombatAudio.I; var eng = LegacyEngineAudio.I; var comp = FindFirstObjectByType<LegacyMixCompressor>();
            var buses = new int[6];
            if (au) foreach (var s in au.GetComponentsInChildren<AudioSource>()) if (s.isPlaying && s.name.StartsWith("Sfx") && !s.name.StartsWith("Sfx2D")) buses[0]++;
            string txt = au ? $"duck {20f * Mathf.Log10(Mathf.Max(1e-4f, au.Duck)):0.0} dB   zoom {20f * Mathf.Log10(Mathf.Max(1e-4f, au.ZoomGain)):0.0} dB   range x{au.RangeScale:0.00}   3D voices {buses[0]}/24" : "no CombatAudio";
            txt += eng ? $"   engines {eng.ActiveLoops}/{eng.maxAudible}" : "   no engines";
            txt += comp ? $"   comp GR {comp.GainReductionDb:0.0} dB" : "   no compressor";
            txt += au && au.ListenerRig ? $"   listener y {au.ListenerRig.position.y:0.0}" : "   listener on camera";
            GUI.Label(new Rect(20, Screen.height * 0.18f + 30, Screen.width - 40, 30), txt, st);
        }
    }
}
#endif
