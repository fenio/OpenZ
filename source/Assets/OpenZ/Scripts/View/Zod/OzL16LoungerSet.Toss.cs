// OpenZ (GPL-3.0) - Level 16, the LOSE ending: the bigger robot tosses his can away (the beat "can_toss" of the endings' cue sheet).
// The beat plays his clip "can_toss" (data file, clips row): he draws the can back beside his shoulder, flicks it forward and away,
// the empty hand drops back into his lap, he looks back at the smaller robot and holds (hold_freeze). At the row's release second the
// can leaves his hand: it is parented to his lounger and follows the data file's "tosses" row, keyed every 1/60 s in the lounger's own
// space (measured off-line from the clip itself: the can's place and speed in the hand at the release): thrown, tumbling, a contact with
// the ground, a small bounce, it skids and rolls a little on its side and stops on the grass in front of them.
//  - this runs in Update BEFORE the robot's graph is evaluated: the hand (and the can in it) read at the release is LAST frame's pose.
//    The can's place and turn are therefore set against the path at that moment (its first segment carried back by that frame's
//    time), not against its first key: the can goes on from where it really is, it is not left a frame behind in his palm;
//  - what is left of that offset (place and turn) fades out before the first contact, so the can lands and lies with the path's own
//    turn (flat on its side, not on one rim);
//  - the fingers stay closed until the can is clear of them (TossOpenAfter s after the release), then open in 0.12 s: the open
//    hand's straight fingers would run through the can while it is still at the palm;
//  - a key's "ground" share follows the level's own ground under it (1 = lying on it), and the can's lowest point (from its turn, its
//    radius and half length) is never under the ground (TossClearance: how near it came before it was held on it);
//  - the contacts play the row's sound event at the can, on their frame (written into the sound log as "end_can_toss_hit");
//  - the hand that threw it stays open (SetHand: a hand whose can has left it does not close again in this ending);
//  - Play check TossSteps: the largest turn of the thrower's neck and head from one frame to the next (per 1/60 s) from 0.26 s before
//    the sheet's toss beat to 0.34 s after it (no snap when the beat cuts into the fade before it).
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using UnityEngine;

namespace OpenZ
{
    /// <summary>A thrown can: who / hand / the beat that starts it, the second of his clip at which the can leaves the hand, the can's
    /// radius and half length (m), the keys (seconds from the beat, place in his lounger's space, turn, share of the level's ground),
    /// the contacts (seconds from the beat, volume) and their sound event.</summary>
    [System.Serializable]
    public class OzLoungerToss
    {
        public string who, hand, beat, ev; public float release, radius, halfLength;
        public float[] t, x, y, z, qx, qy, qz, qw, ground, hitT, hitVol;
        public string[] hitEv;                                                                        // item 229: each contact's own event (OzL16LoungerSet.TossSound.cs; empty / missing = ev)
    }

    public sealed partial class OzL16LoungerSet
    {
        [System.Serializable] class OzLoungerTossFile { public OzLoungerToss[] tosses; }
        static OzLoungerToss[] tossRows; static bool tossRowsTried;
        OzLoungerToss toss; Robot tossRobot; Transform tossCan; float tossT; int tossHit, tossK; bool tossOut; Vector3 tossOff; Quaternion tossTurn;
        Robot stepRobot; Transform[] stepBones; readonly Quaternion[] stepPrev = new Quaternion[2]; float stepFrom = -1f, stepTo = -1f, stepDt, stepAt; int stepFrames; bool stepDone;
        /// <summary>Tunable: seconds after the release at which the throwing hand opens (fully in 0.12 s): by then the can is clear of
        /// the closed fingers (measured off-line: 7 cm from its place in the hand 0.057 s after the release).</summary>
        public static float TossOpenAfter = 0.05f;
        /// <summary>Play check: the toss of this ending, the contact sounds played, how near the can's lowest point came to the level's
        /// ground before it was held on it (m; 9 = no toss yet).</summary>
        public static string TossStatus = "no toss yet";
        public static int TossHits;
        public static float TossClearance = 9f;
        /// <summary>Play check: the thrower's neck and head, the largest turn from one frame to the next (degrees per 1/60 s) around
        /// the toss beat (LOSE: 0.26 s before it to 0.34 s after it), and the text of it.</summary>
        public static float TossStepMost;
        public static string TossSteps = "not measured";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void TossStatics() { tossRows = null; tossRowsTried = false; TossStatus = "no toss yet"; TossHits = 0; TossClearance = 9f; TossStepMost = 0f; TossSteps = "not measured"; }

        static OzLoungerToss TossRow(string who, string beat)
        {
            if (!tossRowsTried)
            {
                tossRowsTried = true;
                var ta = Resources.Load<TextAsset>(DataPath);
                if (ta) { try { var f = JsonUtility.FromJson<OzLoungerTossFile>(ta.text); tossRows = f != null ? f.tosses : null; } catch (System.Exception) { tossRows = null; } }
            }
            if (tossRows != null) foreach (var d in tossRows) if (d != null && d.who == who && d.beat == beat) return d;
            return null;
        }

        /// <summary>Begin of an ending: no toss yet; the step watch is set on the sheet's toss beat (if this ending has one).</summary>
        void TossReset()
        {
            TossClear();
            stepRobot = null; stepBones = null; stepFrom = stepTo = -1f; stepDt = 0f; stepAt = 0f; stepFrames = 0; stepDone = false; TossStepMost = 0f; TossSteps = "no toss beat in this ending";
            var sc = EndSheet(endWon);
            if (sc == null || sc.beats == null) return;
            foreach (var b in sc.beats)
                if (b != null && b.beat == "can_toss" && Find(b.who) != null) { stepRobot = Find(b.who); stepFrom = b.t - 0.26f; stepTo = b.t + 0.34f; }
            if (stepRobot == null) return;
            stepBones = new[] { FindDeep(stepRobot.root, "neck"), FindDeep(stepRobot.root, "Head") };
            TossSteps = stepBones[0] && stepBones[1] ? "waiting for the toss beat" : "no neck / head bone found";
            if (!stepBones[0] || !stepBones[1]) stepRobot = null;
        }

        void TossClear() { toss = null; tossRobot = null; tossCan = null; tossOut = false; tossT = 0f; tossHit = 0; tossK = 0; }

        void StartToss(Robot r, string beat)
        {
            TossClear();
            if (r == null) return;
            var d = TossRow(r.who, beat); int k = d != null && d.hand == "L" ? 0 : 1;
            bool ok = d != null && d.t != null && d.t.Length >= 2 && d.x != null && d.y != null && d.z != null && d.qx != null && d.qy != null && d.qz != null && d.qw != null && d.ground != null
                      && d.x.Length == d.t.Length && d.y.Length == d.t.Length && d.z.Length == d.t.Length && d.qx.Length == d.t.Length && d.qy.Length == d.t.Length
                      && d.qz.Length == d.t.Length && d.qw.Length == d.t.Length && d.ground.Length == d.t.Length;
            if (!ok || !r.can[k]) { Warn(r.who + ": no usable toss path for " + beat + " in the data file: the can stays in his hand"); return; }
            toss = d; tossRobot = r; tossCan = r.can[k];
            TossHits = 0; TossClearance = 9f; TossStatus = r.who + " " + beat + ": the can in his hand";
        }

        Quaternion TossKey(int i) => new Quaternion(toss.qx[i], toss.qy[i], toss.qz[i], toss.qw[i]);

        /// <summary>The step watch: the pose read here is the one the robot's graph wrote last frame (this runs before its Evaluate),
        /// so each change is divided by that frame's own time.</summary>
        void TossWatch(float dt)
        {
            if (stepRobot == null || stepDone || endClock < stepFrom) return;
            if (endClock > stepTo)
            {
                stepDone = true;
                TossSteps = $"{stepRobot.who}: neck and head at most {TossStepMost:F1} deg per 1/60 s from one frame to the next (at {stepAt:F2} s), LOSE {stepFrom:F2} to {stepTo:F2} s, {stepFrames} frames";
                Debug.Log("[OpenZ L16 loungers] toss steps: " + TossSteps);
                return;
            }
            for (int i = 0; i < 2; i++)
            {
                Quaternion q = stepBones[i].localRotation;
                if (stepFrames > 0 && stepDt > 1e-4f)
                {
                    float a = Quaternion.Angle(stepPrev[i], q) / (stepDt * 60f);
                    if (a > TossStepMost) { TossStepMost = a; stepAt = endClock; }
                }
                stepPrev[i] = q;
            }
            stepFrames++; stepDt = dt;
        }

        void TickToss(float dt)
        {
            TossWatch(dt);
            if (toss == null || !tossCan || tossRobot == null) return;
            tossT += dt;
            int k = toss.hand == "L" ? 0 : 1;
            if (!tossOut)
            {
                if (tossT < toss.release) return;
                // it leaves his hand: from now on it moves in his lounger's own space (the hand stays open once it has opened)
                droppingCan = tossCan; tossCan.SetParent(tossRobot.piece, true); tossOut = true; tossK = 0;
                // what we read is last frame's pose (tossT - dt on the toss's clock): set it against the path at that moment
                float back = (tossT - dt - toss.t[0]) / Mathf.Max(1e-4f, toss.t[1] - toss.t[0]);
                Vector3 seen = Vector3.LerpUnclamped(new Vector3(toss.x[0], toss.y[0], toss.z[0]), new Vector3(toss.x[1], toss.y[1], toss.z[1]), back);
                tossOff = tossCan.localPosition - seen;
                tossTurn = Quaternion.Inverse(Quaternion.SlerpUnclamped(TossKey(0), TossKey(1), back)) * tossCan.localRotation;
                TossStatus = $"{tossRobot.who}: the can thrown {tossT:F2} s after the beat";
            }
            int n = toss.t.Length; float u = tossT;
            while (tossK < n - 2 && u > toss.t[tossK + 1]) tossK++;
            int i = tossK; float a = Mathf.Clamp01((u - toss.t[i]) / Mathf.Max(1e-4f, toss.t[i + 1] - toss.t[i]));
            Vector3 p0 = new Vector3(toss.x[i], toss.y[i], toss.z[i]), p1 = new Vector3(toss.x[i + 1], toss.y[i + 1], toss.z[i + 1]);
            float land = toss.hitT != null && toss.hitT.Length > 0 ? toss.hitT[0] : toss.t[n - 1];
            float f = Mathf.Clamp01((u - toss.t[0]) / Mathf.Max(0.05f, land - toss.t[0])); f = f * f * (3f - 2f * f);
            Vector3 p = Vector3.Lerp(p0, p1, a) + tossOff * (1f - f);                         // from the can's real place at the release onto the path
            tossCan.localRotation = Quaternion.Slerp(TossKey(i), TossKey(i + 1), a) * Quaternion.Slerp(tossTurn, Quaternion.identity, f);   // and from its real turn onto the path's
            // on the level's own ground: a key's ground share follows it; the can's lowest point never under it
            var fr = tossRobot.piece; Vector3 w = fr.TransformPoint(p);
            float g = Mathf.Lerp(toss.ground[i], toss.ground[i + 1], a), plane = fr.TransformPoint(new Vector3(p.x, 0f, p.z)).y, gy = Ground(w);
            w.y += g * (gy - plane);
            float c = Mathf.Abs(Vector3.Dot(tossCan.rotation * Vector3.forward, Vector3.up));
            float low = toss.halfLength * c + toss.radius * Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
            TossClearance = Mathf.Min(TossClearance, w.y - low - gy);
            if (w.y - low < gy) w.y = gy + low;
            tossCan.position = w;
            tossRobot.gripTo[k] = tossT >= toss.release + TossOpenAfter ? 0f : 100f;                     // closed until the can is clear of the fingers, then open
            // the contacts, on their frame
            while (toss.hitT != null && tossHit < toss.hitT.Length && u >= toss.hitT[tossHit])
            {
                float vol = toss.hitVol != null && tossHit < toss.hitVol.Length ? toss.hitVol[tossHit] : 1f; string ev = TossHitEvent(toss, tossHit); tossHit++; TossHits++;   // item 229: the contact's own event
                if (string.IsNullOrEmpty(ev) || !AudioDirector.Has(ev)) { OzSoundLog.Add("L16Loungers", "end_can_toss_hit", ev ?? "", "missing event"); continue; }
                bool started = AudioDirector.I && AudioDirector.I.Play(ev, w, -1621, vol); TossHitHeard(ev, started);
                OzSoundLog.Add("L16Loungers", "end_can_toss_hit", ev, started ? "sent, started" : "sent, not started", vol, w);
            }
            if (u > toss.t[n - 1])
            {
                TossStatus = $"{tossRobot.who}: the can lies on the ground, {TossHits} contact sounds, its lowest point came {TossClearance * 100f:F1} cm to the ground before it was held on it; {TossSteps}";
                Debug.Log("[OpenZ L16 loungers] toss: " + TossStatus);
                toss = null;
            }
        }
    }
}
