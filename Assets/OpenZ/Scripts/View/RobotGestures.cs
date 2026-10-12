using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// There are no such clips on the BS-01 rigs, so both are procedural left-arm gestures laid
    /// over the playing clip in LateUpdate (after UnitView 60, RobotLegIK 70 and WeaponHold 80: the weapon stays in the right hand,
    /// the left hand leaves the foregrip for the gesture and eases back): analytic two-bone IK on the humanoid left arm toward a
    /// body-space hand goal, blended in and out, plus a small chest lean back / chin up for the celebrations.
    ///  * Salute (any type, on a new selection, only idle robots: no waypoint, not moving, not fighting, not stunned): hand to the
    ///    brow, elbow out, held, snapped down (1.1 s). Sound: a servo whirr at the robot.
    ///  * Celebration (the squad that took the flag, when no enemy unit is within 12 tiles of it): grunt + Electrobot = fist pumps,
    ///    psycho + pyro = a wave over the head, sniper + laser = the arm thrown up and held, Tough = one slow big pump; every member
    ///    starts 0-0.3 s apart at +-8 % speed (never lockstep). Sounds: servo whirr per robot + (own team) one Alan "yeah!".
    /// Any gesture stops at once (0.15 s blend out) when the robot starts moving, fights or dies. View only.
    /// </summary>
    [DefaultExecutionOrder(95)]
    public class RobotGestures : MonoBehaviour
    {
        public enum Kind { None, Salute, FistPump, Wave, ArmUp, BigPump }
        /// <summary>Proof counters.</summary>
        public static int Salutes, Celebrations, SkippedEnemyNear, SkippedBusy, Peaks;
        /// <summary>Largest hand lift seen (m above the shoulder) during the last gesture (proof).</summary>
        public static float LastLift;

        UnitView view; Animator anim; Transform lArm, lFore, lHand, rArm, head, chest;
        Kind kind; float t0, dur, speed = 1f, stopAt = -1f, peakLift, waitUntil; bool counted;

        public static bool Begin(UnitView v, Kind k, float delay)
        {
            if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Dying || !v.gameObject.activeInHierarchy) return false;
            var g = v.GetComponent<RobotGestures>(); if (!g) g = v.gameObject.AddComponent<RobotGestures>();
            if (!g.Bind(v)) return false;
            g.kind = k; g.speed = Random.Range(0.92f, 1.08f); g.t0 = Time.time + delay; g.stopAt = -1f; g.peakLift = 0f; g.counted = false;
            g.waitUntil = g.t0 + 4f;                                                            // a celebration waits (at most 4 s) until the robot stands
            g.dur = k == Kind.Salute ? 1.1f : k == Kind.BigPump ? 1.5f : k == Kind.Wave ? 1.5f : k == Kind.ArmUp ? 1.25f : 1.4f;
            return true;
        }

        public static Kind CelebrationOf(string type)
        {
            switch (type)
            {
                case "psycho": case "pyro": return Kind.Wave;
                case "sniper": case "laser": return Kind.ArmUp;
                case "tough": return Kind.BigPump;
                default: return Kind.FistPump;                                                      // grunt, electrobot
            }
        }

        bool Bind(UnitView v)
        {
            view = v;
            if (anim && lArm) return true;
            anim = v.GetComponentInChildren<Animator>();
            if (!anim || !anim.isHuman) return false;
            lArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm); lFore = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand); rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            head = anim.GetBoneTransform(HumanBodyBones.Head);
            chest = anim.GetBoneTransform(HumanBodyBones.UpperChest); if (!chest) chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            return lArm && lFore && lHand && rArm && head;
        }

        /// <summary>A real order or a fight (formation shuffles of an idle squad do not count).</summary>
        public static bool Busy(ZObject o)
        {
            if (o == null || o.Destroyed || o.AttackObject != null || o.Stunned) return true;
            var lead = o.IsMinion && o.Leader != null ? o.Leader : o;
            return lead.Waypoints.Count > 0;
        }

        void LateUpdate()
        {
            if (kind == Kind.None || !view || view.Obj == null) return;
            if (!counted)
            {
                if (Time.time < t0) return;
                if (kind != Kind.Salute && (view.Obj.IsMoving || view.Obj.Destroyed))
                {   // the squad usually still walks onto the flag when it flips: celebrate once it stands
                    if (Time.time > waitUntil || view.Dying || view.Obj.AttackObject != null) { kind = Kind.None; SkippedBusy++; }
                    else t0 = Time.time;
                    return;
                }
                counted = true; t0 = Time.time;
                if (kind == Kind.Salute) Salutes++; else Celebrations++;
                CombatAudio.I?.PlayPitched("rob_gesture", view.transform.position + Vector3.up * 2f, kind == Kind.Salute ? 0.2f : 0.26f, 0.25f, view.Obj.Id, Random.Range(0.95f, 1.12f),
                                           "sd_robot_servo_a", "sd_robot_servo_b", "sd_robot_servo_c");
                OzSoundLog.Add("GESTURE", "rob_gesture", "sd_robot_servo", kind.ToString(), 0.22f, view.transform.position);
            }
            float t = (Time.time - t0) * speed;
            bool busy = view.Dying || (kind == Kind.Salute ? Busy(view.Obj) : view.Obj.IsMoving || view.Obj.AttackObject != null || view.Obj.Destroyed);
            if (busy && stopAt < 0f) stopAt = t;
            float w = Mathf.Clamp01(t / 0.22f) * Mathf.Clamp01((dur - t) / 0.3f);
            if (stopAt >= 0f) w *= Mathf.Clamp01(1f - (t - stopAt) / 0.15f);
            if (t >= dur || w <= 0f && stopAt >= 0f) { kind = Kind.None; LastLift = peakLift; Peaks++; return; }
            w = w * w * (3f - 2f * w);
            var right = rArm.position - lArm.position; right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) return;
            float s = right.magnitude; right /= s;
            var up = Vector3.up; var fwd = Vector3.Cross(right, up);
            float L = (lFore.position - lArm.position).magnitude + (lHand.position - lFore.position).magnitude;
            var sh = lArm.position;
            Vector3 goal, hint;
            float ph = t * Mathf.PI * 2f;
            switch (kind)
            {
                case Kind.Salute:
                    goal = head.position + fwd * (0.16f * L) - right * (0.10f * L) + up * (0.04f * L);
                    hint = sh - right * L + fwd * (0.25f * L) + up * (0.05f * L);
                    break;
                case Kind.ArmUp:
                    goal = sh + up * (0.96f * L) - right * (0.14f * L) + fwd * (0.06f * L);
                    hint = sh - right * (0.6f * L) + fwd * (0.3f * L);
                    break;
                case Kind.Wave:
                    goal = sh + up * (0.88f * L) - right * ((0.18f + 0.26f * Mathf.Sin(ph * 1.6f)) * L) + fwd * (0.1f * L);
                    hint = sh - right * (0.8f * L) + fwd * (0.2f * L);
                    break;
                case Kind.BigPump:
                case Kind.FistPump:
                {
                    float hz = kind == Kind.BigPump ? 0.9f : 2.0f;
                    float p = 0.5f - 0.5f * Mathf.Cos(Mathf.Max(0f, t - 0.15f) * Mathf.PI * 2f * hz);
                    var low = sh + up * (0.42f * L) - right * (0.25f * L) + fwd * (0.30f * L);
                    var high = sh + up * (0.95f * L) - right * (0.12f * L) + fwd * (0.08f * L);
                    goal = Vector3.Lerp(low, high, p);
                    hint = sh - right * (0.7f * L) + fwd * (0.25f * L);
                    break;
                }
                default: return;
            }
            if (kind != Kind.Salute && chest)
            {   // celebrations: a little lean back and the chin up
                chest.rotation = Quaternion.AngleAxis(-7f * w, right) * chest.rotation;
                head.rotation = Quaternion.AngleAxis(-9f * w, right) * head.rotation;
            }
            TwoBone(lArm, lFore, lHand, Vector3.Lerp(lHand.position, goal, w), Vector3.Lerp(lFore.position, hint, w));
            peakLift = Mathf.Max(peakLift, (lHand.position.y - sh.y) / Mathf.Max(0.01f, L));
        }

        /// <summary>Analytic two-bone IK: upper a, lower b, end c to target, elbow toward the hint.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint)
        {
            float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
            var at = target - a.position; float d = Mathf.Clamp(at.magnitude, 0.01f, la + lb - 0.001f);
            var dir = at.normalized;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f), sinA = Mathf.Sqrt(1f - cosA * cosA);
            var hp = hint - a.position; var perp = hp - dir * Vector3.Dot(hp, dir);
            perp = perp.sqrMagnitude < 1e-6f ? Vector3.down : perp.normalized;
            var elbow = a.position + dir * (la * cosA) + perp * (la * sinA);
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + dir * d - b.position) * b.rotation;
        }

        // ------------------------------------------------------------------ triggers
        /// <summary>RulesView, FlagCaptured: the capturing squad celebrates when no enemy unit is within 12 tiles.</summary>
        public static void OnFlagCaptured(ZGame game, ZObject by)
        {
            if (game == null || by == null || by.Kind != ZKind.Robot) return;
            var lead = by.IsMinion && by.Leader != null ? by.Leader : by;
            var W = game.World; const int R = 12 * 16;
            foreach (var o in W.Objects)
            {
                if (!(o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) || o.Destroyed || o.KillAt >= 0) continue;
                if (o.Owner == lead.Owner || o.Owner == Team.Neutral) continue;
                double dx = o.CenterX - lead.CenterX, dy = o.CenterY - lead.CenterY;
                if (dx * dx + dy * dy <= (double)R * R) { SkippedEnemyNear++; Debug.Log($"[OpenZ] gestures: {lead.Type}#{lead.Id} took a flag, enemy {o.Type}#{o.Id} near: no celebration"); return; }
            }
            var k = CelebrationOf(lead.Type); int n = 0;
            var squad = new List<ZObject> { lead }; squad.AddRange(lead.Minions);
            foreach (var m in squad)
                if (game.Views.TryGetValue(m.Id, out var v) && Begin(v, k, 0.15f + Random.Range(0f, 0.3f))) n++;
            if (n > 0 && lead.Owner == game.player) CombatAudio.I?.Voice("celebrate", new[] { "alan_emo_yeah_1", "alan_emo_yeah_2" }, 0.75f, 6f, true);
            Debug.Log($"[OpenZ] gestures: {lead.Type}#{lead.Id} ({lead.Owner}) took a flag: {k} x{n}");
        }
    }

    /// <summary>
    /// every newly selected idle robot salute (at most 6 squads per selection).</summary>
    public class RobotGestureDirector : MonoBehaviour
    {
        ZGame game; PlayerInput input; float lastSel = -10f;
        void Update()
        {
            if (!game) game = ZGame.I; if (!game || game.World == null) return;
            if (!input) input = FindFirstObjectByType<PlayerInput>(); if (!input) return;
            if (input.SelectTime == lastSel || input.SelectTime <= 0f) return;
            lastSel = input.SelectTime;
            if (game.World.GameOver) return;
            int squads = 0;
            foreach (var u in input.Selection)
            {
                if (u == null || u.Kind != ZKind.Robot || u.Owner != game.player || u.IsMinion) continue;
                if (squads >= 6) break;
                var squad = new List<ZObject> { u }; squad.AddRange(u.Minions);
                bool any = false;
                foreach (var m in squad)
                {
                    if (RobotGestures.Busy(m)) { RobotGestures.SkippedBusy++; continue; }
                    if (game.Views.TryGetValue(m.Id, out var v) && RobotGestures.Begin(v, RobotGestures.Kind.Salute, Random.Range(0.05f, 0.22f))) any = true;
                }
                if (any) squads++;
            }
        }
    }
}
