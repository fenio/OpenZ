using UnityEngine;

namespace OpenZ
{
    // ============================================================================================================================
    //  - SessionMusicOff: `-take` switches the music off for its own run only (MusicEnabled reads it; nothing is written, so a
    //    killed take can no longer leave oz_music 0 in the player's prefs: the root cause, see OzSettings).
    //  - The main-menu music sits under the UI: MenuMusicDb below the track's own level (the victory -> menu path already used
    //    -6 dB), and every start is logged ("[AudioDirector] menu music: ..." in Player.log) and put in OzSoundLog as a
    //    "PLAY Music (menu)" row, so the exe smoke test can prove it without ears.
    //  - HudKeyTick (review fix): PollInput's HUD toggle cue follows the bound HIDE THE HUD key (it read kb.f10Key, so a rebound
    //    key hid the HUD in silence and F10 clicked with nothing happening). Same test as OpenZHud's SetVisible line.
    // ============================================================================================================================
    public partial class AudioDirector
    {
        /// <summary>menu_exe_v1: -take run: music off for this session only (never saved).</summary>
        public static bool SessionMusicOff;
        /// <summary>menu_exe_v1: the main-menu music level against its own mix (dB): under the UI, not loud.</summary>
        public const float MenuMusicDb = -6f;

        /// <summary>menu_exe_v1 review fix: PollInput calls this where it read kb.f10Key: ui_hud_toggle on the bound HIDE THE HUD key
        /// (OzBindings, the same key OpenZHud hides the HUD with). True when the cue was asked for (MenuExeProof).</summary>
        public static bool HudKeyTick()
        {
            if (!OzBindings.Pressed(OzAction.HideHud)) return false;
            UI("ui_hud_toggle");
            return true;
        }

        /// <summary>After StartMenuMusic's PlayMusic: log what plays (or why nothing does).</summary>
        void MenuMusicStarted(SoundEventDef d)
        {
            var v = musicOnA ? musicA : musicB;
            bool playing = musicDef == d && v != null && v.src && v.src.clip;
            string clip = playing ? v.src.clip.name : "-";
            if (playing) OzSoundLog.Add("AD", d.id, clip, "PLAY Music (menu)", v.baseLin, null);
            else OzSoundLog.Add("AD", d.id, null, MusicOn ? "DROP menu music did not start" : "DROP music off (oz_music)", 0f, null);
            Debug.Log(playing
                ? $"[AudioDirector] menu music: {clip} ({d.loaded.Count} menu tracks), {MenuMusicDb:0} dB under its mix, music volume {Mathf.RoundToInt(PlayerPrefs.GetFloat("oz_vol_music", 1f) * 100)} %, oz_music {PlayerPrefs.GetInt(MusicPref, 1)}"
                : $"[AudioDirector] menu music NOT started (music {(MusicOn ? "on: no clip" : "off")})");
        }

        /// <summary>AudioDirector.Start in the menu with the music off: say so (else-if chain: always false).</summary>
        bool MenuMusicOffNote()
        {
            Debug.Log($"[AudioDirector] menu music OFF ({(SessionMusicOff ? "-take session" : "oz_music 0")}): {(menuAmbience ? "the dusk bed plays" : "silence")}");
            OzSoundLog.Add("AD", "mus_menu", null, "DROP music off (oz_music)", 0f, null);
            return false;
        }
    }
}
