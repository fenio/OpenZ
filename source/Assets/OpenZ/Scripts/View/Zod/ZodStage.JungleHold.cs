// OpenZ (GPL-3.0) - Level 16, round 4: the commander's window shows ONE background for the whole of a Jungle game.
// What flickered: parts of the base room that the stage builds only when they are first needed (the far wall behind the two robots
// with its ribs, wainscot and lamp strip, built at the first cut to them; the alarm beacons with their wall arms, built with the
// first alarm) were drawn until the jungle setting's next pass, which ran four times a second: up to a quarter of a second of the
// base room in the jungle, at each such first time.
// Now: (1) while the level being played is level 16, the stage keeps the jungle whatever level it is told (it is told one only at
// its own moments); (2) the moment the jungle setting is on and the stage is complete, those late parts are built at once, before
// any window opens, and go into the jungle setting like the rest (room parts not drawn, the beacons on posts, the floor in its grass
// tone); (3) while the window shows, the room parts are looked for every frame, not four times a second.
// Play check: return OpenZ.ZodStage.JungleHoldStatus();
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodStage
    {
        /// <summary>Counts (Play check): room parts found drawn while the window showed after the jungle was set (must stay 0), times the
        /// stage was told another level while level 16 was played (kept the jungle), settings switched while the window showed (must stay 0).</summary>
        public static int JungleLateParts, JungleToldOther, JungleSwitchedLive, JunglePrebuilds;
        bool junglePrebuilt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetJungleHold() { JungleLateParts = JungleToldOther = JungleSwitchedLive = JunglePrebuilds = 0; }

        /// <summary>JungleSetting: the level whose setting the stage shows. While level 16 is played, always level 16.</summary>
        string JungleLevelFor(string level)
        {
            var g = ZGame.I;
            if (g && g.level == JungleLevel)
            {
                if (level != JungleLevel) JungleToldOther++;
                level = JungleLevel;
            }
            if ((level == JungleLevel) != jungleOn && Live) JungleSwitchedLive++;
            return level;
        }

        /// <summary>JungleTick, every frame while the jungle is on: the late parts built at once, the room parts looked for every frame while
        /// the window shows.</summary>
        void JungleEveryFrame()
        {
            if (!jungleOn) return;
            if (!junglePrebuilt && cam)
            {   // the stage is complete (its camera exists): the parts it would build later are built now and set like the rest
                junglePrebuilt = true; JunglePrebuilds++;
                BuildFrontSet(); BuildAlarm();
                jungleNext = 0f;                                                                     // the regular pass of this same call hides them, posts the beacons, greens the floor
                return;
            }
            if (!Live) return;
            int n0 = jungleHid.Count;
            JungleHide(transform);
            var fs = transform.Find("FrontSet"); if (fs) JungleHide(fs);
            if (junglePrebuilt && jungleHid.Count > n0) JungleLateParts += jungleHid.Count - n0;
        }

        /// <summary>Play check of the jungle setting in one line.</summary>
        public static string JungleHoldStatus()
        {
            var b = ZodBriefing.I; var st = FindFirstObjectByType<ZodStage>();
            string s = st ? $"jungle {(st.JungleSettingOn ? "on" : "off")}, room parts not drawn {st.JungleHiddenParts}, posts {st.JunglePostCount}, prebuilt {st.junglePrebuilt}, window live {st.Live}" : "no stage";
            return $"jungle hold: {s}; late room parts found while the window showed {JungleLateParts}, told another level while level 16 played {JungleToldOther}, switched while the window showed {JungleSwitchedLive}, prebuilds {JunglePrebuilds}" + (b ? "" : " (no briefing director)");
        }
    }
}
