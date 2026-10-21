using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Item 225: the hero squad's rifle shot is ONE sound event, "hero_shot" (a CombatAudio key on the weapons bus with the grunt's bolt
    /// profile), whose takes are EVERY audio clip in ONE folder: Assets/OpenZ/Resources/OpenZ/Audio/Sfx/HeroSquad (the Resources path
    /// editor import them; nothing else to edit. Any number of takes, any names; they play in random order, never the same twice in a
    /// row (WeaponsV2.Pick), pitch-varied like every shot.
    /// Levelling: a take listed in the clip level table keeps its measured loudness; any other take is measured from its own samples
    /// when it loads (RMS over the clip as an integrated loudness estimate), so it enters the shot leveller like the grunt's bolts.
    /// Never silent: when the folder yields no clip (a build without the files), the grunt's own bolt bank plays for the heroes and the
    /// log says so once. The Player.log carries one line when the first hero view of a level is made: what was loaded from where.
    /// </summary>
    public static class OzHeroShotSound
    {
        /// <summary>The one folder, under Assets/OpenZ/Resources.</summary>
        public const string Folder = "OpenZ/Audio/Sfx/HeroSquad";
        /// <summary>The one event (CombatAudio key; the sound log shows "CA hero_shot &lt;take&gt; PLAY Weapons").</summary>
        public const string Key = "hero_shot";
        /// <summary>Gain of a hero shot as a multiple of the grunt's shot gain (LegacyMatch.GainBolt).</summary>
        public static float Gain = 1f;
        /// <summary>The takes as CombatAudio.Clip names ("HeroSquad/&lt;clip&gt;", loaded under OpenZ/Audio/Sfx); null until Load, null when the folder is empty.</summary>
        public static string[] Bank { get; private set; }
        /// <summary>What Load found (the log line).</summary>
        public static string Status { get; private set; } = "not loaded";
        public static int Loaded, Measured, Played, Fallbacks;
        static bool tried;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { tried = false; Bank = null; Status = "not loaded"; Loaded = Measured = Played = Fallbacks = 0; }

        /// <summary>Loads the folder once per run (the first hero view, or the first shot) and logs what it found.</summary>
        public static void Load()
        {
            if (tried) return;
            tried = true;
            var clips = Resources.LoadAll<AudioClip>(Folder);
            var names = new List<string>();
            var sb = new System.Text.StringBuilder();
            if (clips != null) System.Array.Sort(clips, (a, b) => string.CompareOrdinal(a ? a.name : "", b ? b.name : ""));
            string sub = Folder.Substring(Folder.LastIndexOf('/') + 1);
            if (clips != null)
                foreach (var c in clips)
                {
                    if (!c) continue;
                    names.Add(sub + "/" + c.name);
                    string how = "level table";
                    if (!CombatAudio.ClipLufs.ContainsKey(c.name)) { CombatAudio.ClipLufs[c.name] = Measure(c, out how); Measured++; }
                    sb.Append(sb.Length > 0 ? ", " : "").Append(c.name).Append(' ').Append(c.length.ToString("0.00")).Append(" s ")
                      .Append(CombatAudio.ClipLufs[c.name].ToString("0.0")).Append(" LUFS (").Append(how).Append(')');
                }
            Loaded = names.Count;
            Bank = Loaded > 0 ? names.ToArray() : null;
            Status = Loaded > 0 ? $"{Loaded} take(s) from Resources/{Folder}: {sb}" : $"NO clip in Resources/{Folder}: the grunt's bolts play for the heroes";
            Debug.Log("[OpenZ HeroSquad] shot sound \"" + Key + "\": " + Status);
        }

        /// <summary>Integrated loudness estimate of a clip from its samples (RMS over the whole clip, as dB of a full-scale sine), for takes the
        /// level table does not list; the table's own figures (looped x4, gated) stay where they exist.</summary>
        static float Measure(AudioClip c, out string how)
        {
            how = "measured";
            try
            {
                int n = c.samples * c.channels;
                if (n <= 0 || c.loadType != AudioClipLoadType.DecompressOnLoad) { how = "not measurable, -16 assumed"; return -16f; }
                var data = new float[n];
                if (!c.GetData(data, 0)) { how = "not readable, -16 assumed"; return -16f; }
                double sum = 0; for (int i = 0; i < n; i++) sum += (double)data[i] * data[i];
                double rms = System.Math.Sqrt(sum / n);
                if (rms < 1e-6) { how = "silent"; return -40f; }
                return (float)(20.0 * System.Math.Log10(rms) - 0.691);
            }
            catch (System.Exception) { how = "not readable, -16 assumed"; return -16f; }
        }

        /// <summary>A hero fired (the grunt-class shot path): its own event, or the grunt's bolts when the folder gave no take. Always a sound.</summary>
        public static void Play(int shooterId, Vector3 from, CombatAudio au)
        {
            if (!au) return;
            Load();
            if (Bank != null) { au.PlayPitched(Key, from, LegacyMatch.GainBolt * Gain, 0.2f, shooterId, 1f, WeaponsV2.Pick(Bank)); Played++; }
            else { au.PlayPitched("bolt", from, LegacyMatch.GainBolt, 0.2f, shooterId, 1f, WeaponsV2.Pick(LegacyMatch.BankBolt)); Fallbacks++; }
        }
    }
}
