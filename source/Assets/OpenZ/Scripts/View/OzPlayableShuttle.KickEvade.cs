using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K436, the kickflip as a real manoeuvre (the view's side; the rules: DV129, Sim/ZWorld.ShuttleKick.cs; the plain part:
    /// View/OzShuttleKickflip.Evade.cs).
    /// <list type="bullet">
    /// <item>THE DOUBLE CLICK'S FLIP asks the rules on the frame it pops (ZWorld.ShuttleKickOrder): the rules' window opens with it, so the
    /// ship dashes forward through the roll (the spiral), is a harder target, and no second flip can start. The steady test also asks
    /// the rules first (the way for the dash, no flip on).</item>
    /// <item>THE SHIP'S OWN FLIP: when the rules start the evasive self-flip, the same flip pops here at once (the same curve, arc,
    /// trails and sounds; off screen nothing is heard or seen). On screen AND selected, the pilot says one of the evasive lines
    /// (vo_shuttle_evade: "Buckle up!", "It's gonna hurt!", ...), never over another voice line (the robot channel's own gap, the shuttle
    /// lines' wait for Rachel, and not while any voice speaks).</item>
    /// <item>THE FRAME: the selection frame, the health bar and the name chip stay the level hull's (never turned with the roll) and move
    /// with the drawn hull's middle through the arc (its offset from the level ship: the rise, the swing, the lead), so the hull never
    /// leaves its frame and the frame never jumps or changes size. The route line starts there too (it used to start at the rolled
    /// root, which circled the hull's middle with the roll).</item>
    /// <item>THE WHOOSH IN THE MIX: under the long whoosh the music and the ambience bed step back 6 dB and the combat buses 3 dB for
    /// 2 s (the flip's rows also carry on the broad distance curve: AudioDirector.K436.cs); the roll's swish plays at the fastest turn.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>K436 knobs: the mix under the whoosh (dB, s), the roll's swish at the fastest turn, the frame following the drawn
        /// hull's middle, the voice lines of the self-flip.</summary>
        public static float KickMixMusicDb = -6f, KickMixAmbDb = -6f, KickMixCombatDb = -3f, KickMixSec = 2.0f;
        public static bool KickRollSwishOn = true, KickFrameFollow = true, KickEvadeVoiceOn = true;
        public const string KickEvadeLine = "vo_shuttle_evade";

        /// <summary>K436: the drawn hull's middle minus the level hull's middle in the last drawn frame (world; 0 when not flipping).</summary>
        public Vector3 KickFrameOffset { get; private set; }
        /// <summary>K436 proof counts: the rules accepted / refused the double click's flip, the ship's own flips started here, the evasive
        /// lines said, skipped (not selected, off screen, a voice speaking, no row), the largest frame offset (m), the last skip's reason.</summary>
        public int KickSimOrders { get; private set; }
        public int KickSimRefused { get; private set; }
        public int KickSelfFlips { get; private set; }
        public int KickEvadeLines { get; private set; }
        public int KickEvadeLineSkips { get; private set; }
        public float KickFrameOffsetMax { get; private set; }
        public string KickEvadeWhy { get; private set; } = "";

        int kickSimSeen = -1, kickPopCloses0, kickExtFrame = -1; bool kickSimTied;
        static PlayerInput kickInput;

        /// <summary>KickflipBeforeJets, before the idle test: a flip the rules started by themselves pops here now.</summary>
        void KickEvadeWatch(ZObject o)
        {
            if (kickSimSeen < 0) { kickSimSeen = o.ShuttleKickSeq; return; }
            if (o.ShuttleKickSeq == kickSimSeen) return;
            kickSimSeen = o.ShuttleKickSeq;
            if (!o.ShuttleKickBusy) return;
            if (!flip.StartExternal(o.ShuttleKickT, KickArcBlocked(), o.ShuttleKickSelf, o.ShuttleGoalX, o.ShuttleGoalY)) return;
            kickExtFrame = Time.frameCount; kickSimTied = true; kickPopCloses0 = o.ShuttleKickPopCloses;
            if (o.ShuttleKickSelf) KickSelfFlips++;
            OzSoundLog.Add("FLIP", "shuttle_kickflip", null, $"#{o.Id} the rules' {(o.ShuttleKickSelf ? "evasive self-flip" : "flip")} at {game.World.Time:F2} s ({o.ShuttleKickWayLeftPx / 8.0:F1} m to go, under fire since {(o.ShuttleKickUnderFire ? (game.World.Time - o.ShuttleKickFireSince).ToString("F1") + " s" : "-")})", 0f, transform.position);
            if (o.ShuttleKickSelf) KickEvadeVoice(o);
        }

        /// <summary>The rules' side of this frame's flip input (the steady test's word, the pop's way out).</summary>
        void KickEvadeIn(ZObject o, ref OzKickflipIn i)
        {
            bool armed = flip.Phase == OzKickflipPhase.Armed;
            i.SimKnown = ZWorld.ShuttleKickDeviation && (armed || kickSimTied);
            i.SimCan = armed && o.ShuttleKickCan(out _);
            i.SimAbort = kickSimTied && o.ShuttleKickPopCloses > kickPopCloses0;
        }

        /// <summary>After this frame's step: the double click's flip popped now = the rules' window opens with it.</summary>
        void KickEvadeAfter(ZObject o, in OzKickflipOut k)
        {
            if ((k.Events & OzKickflipState.EvEnd) != 0) kickSimTied = false;
            if ((k.Events & OzKickflipState.EvStart) == 0 || kickExtFrame == Time.frameCount) return;
            bool ok = game.World.ShuttleKickOrder(o.Owner, o);
            kickSimSeen = o.ShuttleKickSeq; kickSimTied = ok; kickPopCloses0 = o.ShuttleKickPopCloses;
            if (ok) KickSimOrders++; else KickSimRefused++;
            OzSoundLog.Add("FLIP", "shuttle_kickflip", null, ok ? $"#{o.Id} the rules' window open: the dash to x{ZWorld.ShuttleKickPeak:F1}, a harder target for {ZWorld.ShuttleKickSec:F1} s"
                                                                : $"#{o.Id} the rules said no ({o.ShuttleKickWhy}): a picture only", 0f, transform.position);
        }

        /// <summary>The self-flip's line: only with the ship on screen and in the player's selection, and no voice speaking.</summary>
        void KickEvadeVoice(ZObject o)
        {
            string why = null;
            if (!KickEvadeVoiceOn) why = "off";
            else if (!AudioDirector.Has(KickEvadeLine)) why = "no row";
            else
            {
                if (!kickInput) kickInput = FindFirstObjectByType<PlayerInput>();
                var cam = Camera.main;
                if (!kickInput || !kickInput.Selection.Contains(o)) why = "not selected";
                else if (!cam || !DetailKit.OnScreen(cam, KickCentre, 0f)) why = "off screen";
                else if (AudioDirector.VoiceBusy) why = "a voice speaks";
            }
            KickEvadeWhy = why ?? "";
            if (why != null) { KickEvadeLineSkips++; OzSoundLog.Add("VO", KickEvadeLine, null, "not said: " + why, 0f, transform.position); return; }
            KickEvadeLines++;
            Voice(KickEvadeLine);
        }

        /// <summary>KickflipDraw, the picture's pose on: the drawn hull's middle against the level one's (the frame's offset).</summary>
        void KickFrameNote()
        {
            if (!kickOn) { KickFrameOffset = Vector3.zero; return; }
            var off = transform.TransformPoint(KickPivot) - (kickLevelPos + kickLevelRot * KickPivot);
            if (float.IsNaN(off.x) || float.IsNaN(off.y) || float.IsNaN(off.z)) off = Vector3.zero;
            KickFrameOffset = off;
            if (off.magnitude > KickFrameOffsetMax) KickFrameOffsetMax = off.magnitude;
        }

        /// <summary>PlayerInput.MeshRect (the frame, the click box, the health bar's and name chip's place): the ship's frame matrix. While
        /// it flips: the LEVEL pose (never the rolled one, whatever moment of the frame asks) moved by the drawn hull's offset.</summary>
        public static Matrix4x4 FrameL2W(ZObject o, Transform t)
        {
            if (!t) return Matrix4x4.identity;
            var s = o != null && o.IsShuttle ? Of(o) : null;
            if (!s || !KickFrameFollow) return t.localToWorldMatrix;
            bool rolled = s.kickOn && t == s.transform;
            if (!rolled && s.KickFrameOffset == Vector3.zero) return t.localToWorldMatrix;
            var level = rolled ? Matrix4x4.TRS(s.kickLevelPos, s.kickLevelRot, t.lossyScale) : t.localToWorldMatrix;
            return Matrix4x4.Translate(s.KickFrameOffset) * level;
        }

        /// <summary>SelectionOverlay.ShuttleRoute: where a flying ship's route line starts: the level root moved by the drawn hull's offset
        /// (never the rolled root).</summary>
        public Vector3 KickRouteRoot => (kickOn ? kickLevelPos : transform.position) + KickFrameOffset;

        /// <summary>KickSoundsFor, the roll's start (with the long whoosh): the mix makes room under it.</summary>
        void KickWhooshMix()
        {
            if (AudioDirector.I) AudioDirector.I.AddDuck(KickMixMusicDb, 0f, KickMixAmbDb, 0f, KickMixSec, 0.2f, 0.8f);
            if (CombatAudio.I && KickMixCombatDb < 0f) CombatAudio.I.DuckFor(KickMixSec, KickMixCombatDb, 0f);
        }
    }
}
