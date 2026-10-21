using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// DV108 hero squads, KH2: the Play measures of items 196 to 198 (made by the editor menus OpenZ/Hero Squad/Play: KH2 ...; nothing
    /// in the game makes it). View only, read only: it looks at the drawn bones after every pass of the frame.
    /// <list type="bullet">
    /// <item><b>lie</b>: each hero lying on his lounger, every frame: the feet on their own sides of the body's middle (not crossed), the
    /// ankles' gap, the two shins parallel (angle between them), each leg along the lounger (angle to the robot's front), each knee
    /// above its hip-ankle line (bending up), the toes up.</item>
    /// <item><b>drink</b>: each robot in a drink antic (heroes judged; grunts beside them printed as REF, not judged), every frame: each
    /// foot's and each knee's step in the robot's own frame (cm); frames over 1 cm, split into the start (the step into the drink's first
    /// frame and its first <see cref="DrinkStartSpan"/> s: the fade in), the drink, and the return to the standing idle (after the drink
    /// ended: the cross-fade back and the held feet handed back, RobotLegIK.HeroHoldTail + HeroHoldOut; measured for at least
    /// <see cref="DrinkTailMin"/> s and on while the hold's weight is above 0 or the animator still cross-fades). With StopWhenDrinksEnd
    /// it finishes when no hero drinks any more and every hero's return to idle is measured.</item>
    /// <item><b>aim</b>: each hero aiming (the torso aim has a target), every frame: the angle between his face and the target.</item>
    /// </list>
    /// </summary>
    [DefaultExecutionOrder(95)]
    public sealed class OzHeroKh2Probe : MonoBehaviour
    {
        public string Mode = "lie"; public float Seconds = 5f; public bool StopWhenDrinksEnd;
        float t0; bool seenDrink;
        public static readonly List<string> Last = new List<string>();
        /// <summary>The toe bone's up share (ankle -> toe bone, normalised) a lying foot needs to count as "toes up" (the boots' toe bones sit low in
        /// the boot: Alan's straight-up boot reads about 0.4, Brad's 0.7; a boot turned sideways reads about 0).</summary>
        public static float ToesUpMin = 0.25f;
        /// <summary>Drink: the first seconds of a drink counted as its start (the fade into it); after the drink a robot is measured for at
        /// least DrinkTailMin s and on while his held feet are handed back or the animator cross-fades, at most DrinkTailMax s.</summary>
        public static float DrinkStartSpan = 0.5f, DrinkTailMin = 1.0f, DrinkTailMax = 3.0f;

        sealed class Rec
        {
            public string who; public bool hero; public int frames;
            public int crossed, notParallel, notAlong, kneeDown, toesDown; public float minGap = 99f, maxShin, maxAlong, minToe = 1f;
            public int footOver, kneeOver; public float footMax, kneeMax; public bool prevOk; public readonly Vector3[] prev = new Vector3[4];
            public bool sawDrink, tailDone; public float endAt = -1f;                                   // drink: his drink seen, its return to idle measured, when it ended
            public readonly int[] phFrames = new int[3], phFoot = new int[3], phKnee = new int[3];      // drink: per part (0 start, 1 the drink, 2 back to idle)
            public double errSum; public float errMax; public readonly List<float> errs = new List<float>();
        }
        readonly Dictionary<int, Rec> recs = new Dictionary<int, Rec>();

        public static OzHeroKh2Probe Begin(string mode, float seconds, bool stopWhenDrinksEnd = false)
        {
            var go = new GameObject("OzHeroKh2Probe_" + mode);
            var p = go.AddComponent<OzHeroKh2Probe>(); p.Mode = mode; p.Seconds = seconds; p.StopWhenDrinksEnd = stopWhenDrinksEnd; p.t0 = Time.time;
            return p;
        }

        void LateUpdate()
        {
            var g = ZGame.I; if (!g || g.World == null) return;
            foreach (var v in g.Views.Values)
            {
                if (!v || v.Obj == null || v.Obj.Kind != OpenZ.Sim.ZKind.Robot || v.Dying) continue;
                var an = v.GetComponentInChildren<Animator>(); if (!an || !an.isHuman) continue;
                if (Mode == "lie") Lie(v, an);
                else if (Mode == "drink") Drink(v, an);
                else if (Mode == "aim") Aim(v);
            }
            bool drinking = false, returning = false;
            if (Mode == "drink")
            {
                foreach (var v in g.Views.Values) if (v && v.Obj != null && v.Obj.IsHero && v.Antic != null && v.Antic.Kind == AnticKind.Drink) drinking = true;
                foreach (var r in recs.Values) if (r.hero && r.sawDrink && !r.tailDone) returning = true;   // a hero's return to idle still being measured
            }
            seenDrink |= drinking;
            if (Time.time - t0 >= Seconds || (StopWhenDrinksEnd && seenDrink && !drinking && !returning)) { Finish(); Destroy(gameObject); }
        }

        Rec R(UnitView v) { if (!recs.TryGetValue(v.Obj.Id, out var r)) recs[v.Obj.Id] = r = new Rec { who = (v.Obj.IsHero ? v.Obj.HeroMemberName : v.Obj.Type) + " #" + v.Obj.Id, hero = v.Obj.IsHero }; return r; }

        void Lie(UnitView v, Animator an)
        {
            if (!v.Obj.IsHero) return;
            var a = v.Antic; if (a == null || a.Kind != AnticKind.Sunbed || a.Step != "sunbed_lie") return;
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
            Transform hL = B(HumanBodyBones.LeftUpperLeg), hR = B(HumanBodyBones.RightUpperLeg), kL = B(HumanBodyBones.LeftLowerLeg), kR = B(HumanBodyBones.RightLowerLeg),
                      fL = B(HumanBodyBones.LeftFoot), fR = B(HumanBodyBones.RightFoot), tL = B(HumanBodyBones.LeftToes), tR = B(HumanBodyBones.RightToes);
            if (!hL || !hR || !kL || !kR || !fL || !fR) return;
            var r = R(v); r.frames++;
            var front = -v.transform.forward; front.y = 0f; front.Normalize();
            var mid = 0.5f * (hL.position + hR.position);
            var cm = 0.5f * (fL.position + fR.position) - mid; cm.y = 0f;
            var along = Vector3.Dot(cm, front) >= 0f ? front : -front; var right = Vector3.Cross(Vector3.up, along);
            float l = -Vector3.Dot(fL.position - mid, right), rr = Vector3.Dot(fR.position - mid, right);       // + = on its own side
            if (l <= 0f || rr <= 0f) r.crossed++;
            r.minGap = Mathf.Min(r.minGap, Vector3.Dot(fR.position - fL.position, right));
            float shin = Vector3.Angle(fL.position - kL.position, fR.position - kR.position); r.maxShin = Mathf.Max(r.maxShin, shin); if (shin > 12f) r.notParallel++;
            for (int i = 0; i < 2; i++)
            {
                var h = i == 0 ? hL : hR; var k = i == 0 ? kL : kR; var f = i == 0 ? fL : fR; var t = i == 0 ? tL : tR;
                var leg = f.position - h.position; leg.y = 0f;
                float al = leg.sqrMagnitude > 1e-6f ? Vector3.Angle(leg, along) : 90f; r.maxAlong = Mathf.Max(r.maxAlong, al); if (al > 15f) r.notAlong++;
                var line = Vector3.Lerp(h.position, f.position, Vector3.Dot(k.position - h.position, f.position - h.position) / Mathf.Max(1e-6f, (f.position - h.position).sqrMagnitude));
                if (k.position.y < line.y - 0.005f) r.kneeDown++;
                if (t) { float up = Vector3.Dot((t.position - f.position).normalized, Vector3.up); r.minToe = Mathf.Min(r.minToe, up); if (up < ToesUpMin) r.toesDown++; }
            }
        }

        void Drink(UnitView v, Animator an)
        {
            var a = v.Antic; bool inDrink = a != null && a.Kind == AnticKind.Drink && !a.Finished;
            recs.TryGetValue(v.Obj.Id, out var r);
            int part;                                                                                    // -1 = only remembered (the frame before the drink)
            if (inDrink)
            {
                if (r != null && r.tailDone) return;                                                     // a later drink: not this count
                if (r == null) r = R(v);
                r.sawDrink = true; r.endAt = -1f; part = a.Age < DrinkStartSpan ? 0 : 1;
            }
            else if (r != null && r.sawDrink)
            {   // the drink ended: the return to the standing idle (the cross-fade back, the held feet handed back)
                if (r.tailDone) return;
                if (r.endAt < 0f) r.endAt = Time.time;
                var ik = an.GetComponent<RobotLegIK>(); float since = Time.time - r.endAt;
                bool open = since < DrinkTailMin || (since < DrinkTailMax && ((ik && ik.HeroHoldWeight > 0f) || an.IsInTransition(0)));
                if (!open) { r.tailDone = true; r.prevOk = false; return; }
                part = 2;
            }
            else if (v.Obj.IsHero) { if (r == null) r = R(v); part = -1; }                               // a hero before his drink: his last standing frame, so the step into the drink's first frame counts
            else return;
            var bones = new[] { an.GetBoneTransform(HumanBodyBones.LeftFoot), an.GetBoneTransform(HumanBodyBones.RightFoot), an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg) };
            foreach (var b in bones) if (!b) return;
            if (part >= 0) { r.frames++; r.phFrames[part]++; }
            var rt = v.transform; var inv = Quaternion.Inverse(rt.rotation);
            for (int i = 0; i < 4; i++)
            {
                var p = inv * (bones[i].position - rt.position);
                if (part >= 0 && r.prevOk)
                {
                    float s = (p - r.prev[i]).magnitude * 100f;
                    if (i < 2) { r.footMax = Mathf.Max(r.footMax, s); if (s > 1f) { r.footOver++; r.phFoot[part]++; } }
                    else { r.kneeMax = Mathf.Max(r.kneeMax, s); if (s > 1f) { r.kneeOver++; r.phKnee[part]++; } }
                }
                r.prev[i] = p;
            }
            r.prevOk = true;
        }

        void Aim(UnitView v)
        {
            if (!v.Obj.IsHero) return;
            var ua = v.GetComponent<UnitAim>(); if (!ua || ua.HeroFaceError < 0f) return;
            var r = R(v); r.frames++; r.errSum += ua.HeroFaceError; r.errMax = Mathf.Max(r.errMax, ua.HeroFaceError); r.errs.Add(ua.HeroFaceError);
        }

        void Finish()
        {
            var sb = new StringBuilder(); Last.Clear();
            sb.Append($"[OpenZ HeroSquad KH2] Play numbers ({Mode}, {Time.time - t0:0.0} s): ");
            if (recs.Count == 0) sb.Append(Mode == "lie" ? "no hero lying on his lounger (start Level 16 from its scene and run it in the first seconds)" : Mode == "drink" ? "no hero found (the pair standing idle in Level 16)" : "no hero aimed");
            foreach (var r in recs.Values)
            {
                string line;
                if (Mode == "lie")
                {
                    bool ok = r.frames > 0 && r.crossed == 0 && r.notParallel == 0 && r.notAlong == 0 && r.kneeDown == 0 && r.toesDown == 0;
                    line = $"{(ok ? "PASS" : "FAIL")} {r.who}: {r.frames} lying frames, feet crossed {r.crossed}, shins not parallel (> 12 deg) {r.notParallel} (widest {r.maxShin:0} deg), " +
                           $"a leg off the lounger's line (> 15 deg) {r.notAlong} (at most {r.maxAlong:0} deg), a knee bending down {r.kneeDown}, toes not up {r.toesDown} (lowest toe-up {r.minToe:0.00}), narrowest ankle gap {r.minGap * 100:0} cm";
                }
                else if (Mode == "drink")
                {
                    string nums = $"{r.frames} frames (start {r.phFrames[0]}, drink {r.phFrames[1]}, back to idle {r.phFrames[2]}), foot steps over 1 cm {r.footOver} " +
                                  $"(start / drink / back {r.phFoot[0]} / {r.phFoot[1]} / {r.phFoot[2]}, largest {r.footMax:0.00} cm), knee steps over 1 cm {r.kneeOver} " +
                                  $"(start / drink / back {r.phKnee[0]} / {r.phKnee[1]} / {r.phKnee[2]}, largest {r.kneeMax:0.00} cm)";
                    if (!r.hero) line = $"REF {r.who} (a grunt, not judged): {nums}";
                    else if (!r.sawDrink) line = $"FAIL {r.who}: never drank (see the 'drink forced' line)";
                    else
                    {
                        bool ok = r.phFrames[1] > 30 && r.tailDone && r.phFrames[2] > 0 && r.footOver == 0 && r.kneeOver == 0;
                        line = $"{(ok ? "PASS" : "FAIL")} {r.who}: {nums}" + (r.tailDone ? "" : "; the return to idle was NOT measured to its end (the probe timed out)");
                    }
                }
                else
                {
                    r.errs.Sort(); float med = r.errs.Count > 0 ? r.errs[r.errs.Count / 2] : -1f;
                    bool ok = r.frames > 10 && med <= 15f;
                    line = $"{(ok ? "PASS" : "FAIL")} {r.who}: {r.frames} aiming frames, face to target median {med:0} deg (wanted 15 or less), at most {r.errMax:0} deg";
                }
                Last.Add(line); sb.Append("\n  " + line);
            }
            sb.Append($"\n  switches: RobotLegIK.HeroLie {RobotLegIK.HeroLie}, RobotLegIK.HeroAnticFeet {RobotLegIK.HeroAnticFeet}, UnitAim.HeroTorso {UnitAim.HeroTorso}, UnitAim.HeroHeadLook {UnitAim.HeroHeadLook}");
            Debug.Log(sb.ToString());
        }
    }
}
