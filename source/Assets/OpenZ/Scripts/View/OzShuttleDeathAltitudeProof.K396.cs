using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256h Play proof's lines for the shuttle's big blast (K396, test only; OzShuttleBreakup.BigBlast.cs): every air kill plays the
    /// big second-type explosion (exp2 Big, air form) at the hull's middle, its boom and its deep rumble, the camera kick (within the
    /// SCREEN SHAKE setting), the burning pockets and the smoke column; the pieces leave the blast fast (their launch out of it, relative
    /// to the ship's own velocity) and land in a field 1.5-2 x as wide as before (the kit's twin) but within a sensible range; the
    /// ground death plays the big explosion in its ground form.
    /// </summary>
    public sealed partial class OzShuttleDeathAltitudeProof
    {
        /// <summary>The pieces' launch out of the blast per break: the median and the slowest at least (m/s).</summary>
        public static float LaunchMedianBurst = 5.5f, LaunchMedianSplit = 6.5f, LaunchMedianCrack = 3.8f, LaunchSlowestBurst = 1.8f, LaunchSlowestCrack = 1.0f;
        /// <summary>The landing field at rest: its width at least (m) per break, the farthest piece at most (m, + the ship's own way in 2.5 s).</summary>
        public static float FieldWideBurst = 30f, FieldWideSplit = 40f, FieldWideCrack = 18f, FieldFarMax = 50f;
        int rumbles0, secs0, puffs0K396, kicks0;

        /// <summary>BreakChecks, the frame after the break: the blast and the launch.</summary>
        void BigBlastLines(Kill k, string tag, bool air)
        {
            rumbles0 = OzShuttleBreakup.BigRumbles - (OzShuttleBreakup.LastRumble ? 1 : 0);
            secs0 = OzShuttleBreakup.BigSecondaries; puffs0K396 = OzShuttleBreakup.BigColumnPuffs; kicks0 = OzShuttleBreakup.BigKicks;
            bool thisBreak = Mathf.Abs(OzShuttleBreakup.LastBigTime - OzShuttleBreakup.LastBreakAt) < 0.01f;
            float d = Vector3.Distance(OzShuttleBreakup.LastBigAt, OzShuttleBreakup.LastCentre);
            Log($"{tag} K396 the big blast: {OzShuttleBreakup.LastBigKind} U {OzShuttleBreakup.LastBigU:F1} m at {Fmt(OzShuttleBreakup.LastBigAt)} ({d:F2} m from the blast's middle); boom heard {OzShuttleBreakup.LastBoom}; kick {OzShuttleBreakup.LastKick} (SCREEN SHAKE {(OzShakeGate.SettingOn ? "on" : "off / calm")}); air exp2 so far {OpenZFxPlayer.Exp2AirTotal}; launch {OzShuttleBreakup.LastLaunchText}; crew throw x{OzShuttleBreakup.LastCrewK:F2}");
            if (!air)
            {
                Check($"{tag} K396 the ground death plays the big var2 explosion every time (exp2 Big, ground form: {OzShuttleBreakup.LastBigKind}) with the heavy boom ({OzShuttleBreakup.LastBoom})", thisBreak && OzShuttleBreakup.LastBigKind == "Big ground" && OzShuttleBreakup.LastBoom);
                return;
            }
            Check($"{tag} K396 the big var2 explosion plays up there: exp2 Big in its air form ({OzShuttleBreakup.LastBigKind}), U {OzShuttleBreakup.LastBigU:F1} m (>= 5.5), {d:F2} m from the hull's middle (0.3 allowed)", thisBreak && OzShuttleBreakup.LastBigKind == "Big air" && OzShuttleBreakup.LastBigU >= 5.5f && d <= 0.3f);
            Check($"{tag} K396 its boom: the heavy vehicle death ('vdeath') heard at the blast ({OzShuttleBreakup.LastBoom}, sound log +{Played("vdeath")})", OzShuttleBreakup.LastBoom || Played("vdeath") > 0);
            Check($"{tag} K396 the camera kick: one short kick, only with SCREEN SHAKE on (kicked {OzShuttleBreakup.LastKick}, the setting {(OzShakeGate.SettingOn ? "on" : "off / calm")})", OzShuttleBreakup.LastKick == OzShakeGate.SettingOn || (!OzShuttleBreakup.LastKick && OzShakeGate.SettingOn && !OzShakeGate.OnScreen(OzShuttleBreakup.LastBigAt)));
            float med = k.expect == OzShuttleDeathPlan.Split ? LaunchMedianSplit : k.expect == OzShuttleDeathPlan.Crack ? LaunchMedianCrack : LaunchMedianBurst;
            float slow = k.expect == OzShuttleDeathPlan.Crack ? LaunchSlowestCrack : LaunchSlowestBurst;
            Check($"{tag} K396 the pieces start with the blast's impact: launch out of it {OzShuttleBreakup.LastLaunchMedian:F1} m/s median (>= {med:F1}), the slowest {OzShuttleBreakup.LastLaunchMin:F1} m/s (>= {slow:F1}), spin median {OzShuttleBreakup.LastSpinMedian:F1} rad/s", OzShuttleBreakup.LastLaunchMedian >= med && OzShuttleBreakup.LastLaunchMin >= slow);
        }

        /// <summary>BreakChecks' end, everything at rest: the rumble, the pockets and the column, the landing field.</summary>
        void BigLandLines(Kill k, string tag, bool air)
        {
            Flush();
            Log($"{tag} K396 after the blast: rumble heard {OzShuttleBreakup.LastRumble} (+{OzShuttleBreakup.BigRumbles - rumbles0}, sound log 'heavy' +{Played("heavy")}), burning pockets +{OzShuttleBreakup.LastSecondariesFired} of {OzShuttleBreakup.LastSecondaries} (counted by the blast itself; +{OzShuttleBreakup.BigSecondaries - secs0} since this proof's snapshot), column puffs +{OzShuttleBreakup.LastColumnPuffs}, kicks +{OzShuttleBreakup.BigKicks - kicks0}");
            Check($"{tag} K396 the deep rumble follows the boom ({OzShuttleBreakup.LastRumble}, 'heavy' +{Played("heavy")})", OzShuttleBreakup.LastRumble || Played("heavy") > 0);
            if (!air) return;
            Check($"{tag} K396 the burning pockets burst round the fireball (+{OzShuttleBreakup.LastSecondariesFired} of {OzShuttleBreakup.LastSecondaries}) and the smoke column rises (+{OzShuttleBreakup.LastColumnPuffs} puffs, >= 30)", OzShuttleBreakup.LastSecondariesFired >= OzShuttleBreakup.LastSecondaries && OzShuttleBreakup.LastColumnPuffs >= 30);   // K419a: the blast's own counts (the snapshot above was taken after the first pocket)
            var pts = new List<Vector3>(); foreach (var pc in OzShuttleBreakup.LastPieces) if (pc) pts.Add(pc.transform.position);
            float wide = 0f, far = 0f; var g0 = OzShuttleBreakup.LastBreakGround;
            foreach (var a in pts) { far = Mathf.Max(far, Flat(a, g0)); foreach (var b in pts) wide = Mathf.Max(wide, Flat(a, b)); }
            float drift = new Vector2(OzShuttleBreakup.LastBreakVel.x, OzShuttleBreakup.LastBreakVel.z).magnitude * 2.5f;
            float want = k.expect == OzShuttleDeathPlan.Split ? FieldWideSplit : k.expect == OzShuttleDeathPlan.Crack ? FieldWideCrack : FieldWideBurst;
            OzShuttleBreakup.BodyField(out float bodyWide, out float bodyFar);
            Log($"{tag} K396 the landing field at rest: {pts.Count} pieces, {wide:F1} m wide, the farthest {far:F1} m from the ground under the blast (the ship's own way in 2.5 s {drift:F1} m); the body panels' field {bodyWide:F1} m wide, the farthest panel {bodyFar:F1} m");
            Check($"{tag} K396 the pieces are thrown wide: the field {wide:F1} m wide (>= {want:F0}), the farthest piece {far:F1} m (<= {FieldFarMax + drift:F0}: a sensible range)", wide >= want && far <= FieldFarMax + drift);
        }
    }
}
