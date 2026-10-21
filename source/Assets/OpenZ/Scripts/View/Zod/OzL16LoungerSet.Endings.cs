// OpenZ (GPL-3.0) - Level 16: the lounger set as the picture of the two endings (IOzEndingSet of ZodBriefing.EndingScene.cs).
// THIS FILE GOES INTO THE GAME ONLY TOGETHER WITH THE ENDINGS' OWN CODE (it names its interface and its cue classes).
//  - Ready / Begin / Beat / Jaw / Point: exactly what the endings' code calls. The set binds itself to ZodBriefing.EndingSet when it is built.
//  - Begin: the set is stood up at its place, turned with ZodBriefing.EndingSetRotation (the fallen fort over the smaller robot's
//    shoulder), the two loungers closer together (the data's spacingEnd: over 3.2 m the two cans cannot meet) and as one stiff group on
//    their common ground plane; the bigger robot's can goes into his RIGHT hand (the hand nearest the other robot).
//    WIN: both lying. LOSE: the bigger robot lies with the can at his mouth (the sheet's first beat is his sip, with its sound on the beat).
//  - Beat: the cue sheet's names -> the clips of the data file. Two clips end in a contact that the NEXT beat must meet (the cans touch
//    on "clink"): their speed is set from the cue sheet's own seconds between the two beats, so another wording keeps the contact.
//  - blast_glow_on: one warm light toward the fort, fading with the fireball (8.5 s); boom_shiver: parasol and cans shake 0.4 s and the
//    glow doubles. Under the calm-screen option: no flicker, no jump.
//  - can_drop: the can slides down out of the closed fingers, tips onto its side and comes to lie where it can lie, touching what
//    carries it (the path and the way it lies were measured on the pose: data "drops"); then the hand relaxes.
// WRITTEN BY READING ONLY: compiled off-line with the project's code, never run in the engine. Remove this line once it runs.
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzL16LoungerSet : IOzEndingSet
    {
        /// <summary>Tunables (Play): the glow's strength at the fireball's hottest, its reach (m).</summary>
        public static float GlowIntensity = 5.5f, GlowRange = 16f;
        static OzEndCueFile endCues; static bool endCuesTried;
        bool endOn, endWon; Vector3 endFort; Light glow; float glowAt = -100f, shiverAt = -100f, endClock;
        OzLoungerDrop drop; Transform dropFrame; float dropT; Quaternion dropRot0; Vector3 dropStart; Robot dropRobot;
        readonly Dictionary<Transform, Vector3> shiverRest = new Dictionary<Transform, Vector3>();

        // from where his chest faces (turn and nod), and his chest from his hips about the body's long line, against the model's own
        // straight stance (the data file's restFacing: the prefab's own stance is not straight, it reads 5 and 6 degrees by this
        // measure). Limits: the data file's headMostDeg / chestMostDeg (75 / 30). The clip files hold 66.9 / 24.0 at the most.
        float endHeadMax, endChestMax; int endTurnFrames, endTurnOver;
        readonly Dictionary<Robot, Transform[]> turnBones = new Dictionary<Robot, Transform[]>();
        /// <summary>Play check: the largest head and chest turn of this ending so far, the frames counted (two robots a frame), the frames over a limit.</summary>
        public static string EndTurns => I ? $"head at most {I.endHeadMax:F1} deg from the chest, chest at most {I.endChestMax:F1} deg from the hips, {I.endTurnOver} of {I.endTurnFrames} robot frames over" : "no set";

        /// <summary>The two turns of one robot right now (degrees). False = the data file has no straight stance for him.</summary>
        public bool Turns(Robot r, out float head, out float chest)
        {
            head = chest = 0f;
            if (r == null || data == null || data.restFacing == null) return false;
            OzLoungerRest q = null; foreach (var x in data.restFacing) if (x.who == r.who) q = x;
            if (q == null) return false;
            if (!turnBones.TryGetValue(r, out var tb)) turnBones[r] = tb = new[] { FindDeep(r.root, "Hips"), FindDeep(r.root, "Spine"), FindDeep(r.root, "Head") };
            if (!tb[0] || !tb[1] || !tb[2]) return false;
            Quaternion inv = Quaternion.Inverse(r.root.rotation);
            Quaternion dp = inv * tb[0].rotation * Quaternion.Inverse(new Quaternion(q.hipsX, q.hipsY, q.hipsZ, q.hipsW));
            Quaternion dc = inv * tb[1].rotation * Quaternion.Inverse(new Quaternion(q.chestX, q.chestY, q.chestZ, q.chestW));
            Quaternion dh = inv * tb[2].rotation * Quaternion.Inverse(new Quaternion(q.headX, q.headY, q.headZ, q.headW));
            Vector3 front = new Vector3(q.frontX, 0f, q.frontZ);
            head = Vector3.Angle(dh * front, dc * front);
            Quaternion t = Quaternion.Inverse(dp) * dc;
            chest = Mathf.Abs(Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(t.y, t.w) * Mathf.Rad2Deg));
            return true;
        }

        partial void LateEnding()
        {
            if (!endOn || scene == "play" || scene == "brief") return;
            float hm = data.headMostDeg > 0f ? data.headMostDeg : 75f, cm = data.chestMostDeg > 0f ? data.chestMostDeg : 30f;
            foreach (var r in robots)
            {
                if (!Turns(r, out float h, out float c)) continue;
                endHeadMax = Mathf.Max(endHeadMax, h); endChestMax = Mathf.Max(endChestMax, c); endTurnFrames++;
                if (h > hm || c > cm) endTurnOver++;
            }
        }

        partial void BindEnding() { ZodBriefing.EndingSet = this; }
        partial void UnbindEnding() { if (ReferenceEquals(ZodBriefing.EndingSet, this)) ZodBriefing.EndingSet = null; }

        static OzEndScene EndSheet(bool won)
        {
            if (!endCuesTried)
            {
                endCuesTried = true;
                var ta = Resources.Load<TextAsset>(ZodBriefing.EndingCuesPath);
                if (ta) { try { endCues = JsonUtility.FromJson<OzEndCueFile>(ta.text); } catch (System.Exception) { endCues = null; } }
            }
            return endCues == null ? null : won ? endCues.win : endCues.lose;
        }

        /// <summary>Seconds on the cue sheet from this robot's beat to the next beat named `next` (0 = not found).</summary>
        static float SheetGap(bool won, string who, string beat, string next)
        {
            var sc = EndSheet(won); if (sc == null || sc.beats == null) return 0f;
            float t0 = -1f;
            foreach (var b in sc.beats)
            {
                if (t0 < 0f) { if ((b.who == who || b.who == "both") && b.beat == beat) t0 = b.t; continue; }
                if (b.beat == next && (b.who == who || b.who == "both")) return b.t - t0;
            }
            return 0f;
        }

        public Transform Begin(Vector3 fort, bool won)
        {
            if (!EnsureBuilt()) return null;
            if (briefOn) BriefEnd();
            endOn = true; endWon = won; endFort = fort; scene = won ? "win" : "lose"; endClock = 0f; BeatLog.Clear();
            // robots' SIDE (nobody looks back over a shoulder), the scene camera across both loungers, the far cloud's size in the
            // scene's shots. Written into the endings' own statics BEFORE the set is turned; the endings' code reads them every frame.
            var es = won ? data.endWin : data.endLose;
            if (es != null && es.camFov > 1f)
            {
                ZodBriefing.EndingFortLeftDeg = es.fortLeftDeg; ZodBriefing.EndingCamPos = new Vector3(es.camX, es.camY, es.camZ);
                ZodBriefing.EndingCamLeftDeg = es.camLeftDeg; ZodBriefing.EndingCamUpDeg = es.camUpDeg; ZodBriefing.EndingCamFov = es.camFov;
                if (won) ZodBriefing.EndingPushWin = es.push; else ZodBriefing.EndingPushLose = es.push;
                if (es.mushroomLife > 0f) ZodBriefing.EndingMushroomLife = es.mushroomLife;
                ZodBriefing.EndingMushroomScale = es.mushroomScale > 0f ? es.mushroomScale : 1f;
                Debug.Log($"[OpenZ L16 loungers] ending {(won ? "win" : "lose")} staging: fort {es.fortLeftDeg:F0} deg left of the set's long line, camera ({es.camX:F1}, {es.camY:F1}, {es.camZ:F1}) left {es.camLeftDeg:F1} up {es.camUpDeg:F1} lens {es.camFov:F0}, far cloud x {ZodBriefing.EndingMushroomScale:F2}");
            }
            endHeadMax = endChestMax = 0f; endTurnFrames = endTurnOver = 0;
            Vector3 place = new Vector3(root.position.x, 0f, root.position.z);
            Arrange(data.spacingEnd, true, place, ZodBriefing.EndingSetRotation(place, fort));
            droppingCan = null; drop = null; TossReset();                                                // round 3d: the toss of the LOSE scene (OzL16LoungerSet.Toss.cs)
            foreach (var r in robots) { r.cues.Clear(); r.jawExternal = 0f; r.dentTo = ""; }
            StartLoops(scene);
            SceneExtrasBegin(won, es);
            Debug.Log($"[OpenZ L16 loungers] ending {(won ? "win" : "lose")}: the set turned to the fort at ({fort.x:F0}, {fort.z:F0}), loungers {data.spacingEnd:F2} m apart on one plane");
            return root;
        }

        public void Beat(string who, string beat)
        {
            if (!built) return;
            switch (beat)
            {
                case "blast_glow_on": GlowOn(); ZodBriefing.EndingShake(1f, 0.5f); return;                // the far flash: a short rumble of the frame
                case "boom_shiver": shiverAt = Time.unscaledTime; ZodBriefing.EndingShake(0.7f, 0.85f); return;   // the far boom: softer, a little longer
                case "cut_two_shot": return;                                                          // the camera's own mark
                case "end_screen": Debug.Log($"[OpenZ L16 loungers] ending {scene} turns: {EndTurns}"); return;   // the camera's own mark; round 2: the count of the scene
            }
            float speed = 1f;
            foreach (var w in who == "both" ? new[] { "alan", "brad" } : new[] { who })
            {
                var bd = BeatDef(scene, w, beat); var cd = bd != null && !string.IsNullOrEmpty(bd.clip) ? Clip(w, bd.clip) : null;
                if (cd == null) continue;
                speed = 1f;
                if (cd.lead > 0f)
                {   // this clip ends in a contact on a later beat: the cans touch on "clink", the can is at the mouth on its drink sound
                    float gap = cd.name == "drink" ? 0f : SheetGap(endWon, w, beat, "clink");
                    if (gap > 0.05f) speed = cd.lead / gap;
                }
                BeatIn(scene, w, beat, speed, endClock);
                if (beat == "can_drop") StartDrop(Find(w));
                else if (beat == "can_toss") StartToss(Find(w), beat);                                   // round 3d: he throws his can away
            }
        }

        void GlowOn()
        {
            if (!glow)
            {
                var go = new GameObject("L16LoungerBlastGlow"); go.transform.SetParent(root, false);
                glow = go.AddComponent<Light>(); glow.type = LightType.Point; glow.color = new Color(1f, 0.55f, 0.2f); glow.shadows = LightShadows.None; glow.range = GlowRange;
            }
            Vector3 to = endFort - root.position; to.y = 0f; to = to.sqrMagnitude > 1f ? to.normalized : root.forward;
            glow.transform.position = root.position + to * 6f + Vector3.up * 3f;
            glowAt = Time.unscaledTime; glow.enabled = true;
        }

        void StartDrop(Robot r)
        {
            if (r == null || data.drops == null) return;
            foreach (var d in data.drops) if (d.who == r.who) drop = d;
            int k = r.hand == "R" ? 1 : 0;
            if (drop == null || !r.can[k] || drop.t == null || drop.t.Length < 2) { drop = null; return; }
            droppingCan = r.can[k]; dropRobot = r; dropFrame = r.piece; dropT = 0f;
            droppingCan.SetParent(dropFrame, true);                                                      // it has left the hand: it moves in the lounger's own space now
            dropRot0 = droppingCan.localRotation; dropStart = droppingCan.localPosition;
            r.gripTo[k] = 100f;                                                                          // the fingers stay closed while it slides out; they relax when it is gone
        }

        partial void TickEnding(float dt)
        {
            if (!endOn) return;
            endClock += dt;
            SceneExtrasTick(dt);
            TickToss(dt);                                                                             // round 3d: the thrown can
            float now = Time.unscaledTime; bool calm = OzMotion.Reduced;
            if (glow && glow.enabled)
            {   // the fireball's own life: full for 2 s, gone by 8.5 s; a slow flicker; double for 0.4 s on the far boom
                float age = now - glowAt, k = Mathf.Clamp01(1f - (age - 2f) / 6.5f);
                float flick = calm ? 1f : 0.9f + 0.1f * Mathf.PerlinNoise(now * 1.7f, 3.1f);
                float jump = !calm && now - shiverAt < 0.4f ? 2f : 1f;
                glow.intensity = GlowIntensity * k * flick * jump; glow.range = GlowRange;
                if (k <= 0f) glow.enabled = false;
            }
            // the shiver: parasol and cans shake for 0.4 s
            float sh = !calm && now - shiverAt < 0.4f ? 1f - (now - shiverAt) / 0.4f : 0f;
            var par = Piece("parasol");
            if (par) par.localRotation = Quaternion.Euler(Mathf.Sin(now * 61f) * 1.2f * sh, 0f, Mathf.Sin(now * 47f) * 1.2f * sh);
            foreach (var r in robots)
                for (int k = 0; k < 2; k++)
                {
                    var c = r.can[k]; if (!c || !c.gameObject.activeSelf || c == droppingCan) continue;
                    if (!shiverRest.ContainsKey(c)) shiverRest[c] = c.localPosition;
                    c.localPosition = shiverRest[c] + new Vector3(Mathf.Sin(now * 83f), 0f, Mathf.Sin(now * 71f)) * 0.004f * sh;
                }
            // the can that slipped
            if (drop != null && droppingCan)
            {
                dropT += dt; int n = drop.t.Length; int i = 0;
                while (i < n - 2 && dropT > drop.t[i + 1]) i++;
                float u = Mathf.Clamp01((dropT - drop.t[i]) / Mathf.Max(0.001f, drop.t[i + 1] - drop.t[i]));
                // the path is the can's middle in the lounger's own space (its first point is where the can stood in the hand)
                Vector3 p0 = new Vector3(drop.x[i], drop.y[i], drop.z[i]), p1 = new Vector3(drop.x[i + 1], drop.y[i + 1], drop.z[i + 1]), first = new Vector3(drop.x[0], drop.y[0], drop.z[0]);
                Vector3 p = Vector3.Lerp(p0, p1, u) + (dropStart - first);
                float lying = Mathf.Lerp(drop.lying[i], drop.lying[i + 1], u), rolled = Mathf.Max(0f, first.z - Mathf.Lerp(p0.z, p1.z, u));
                droppingCan.localPosition = p;
                // upright as it was, then on its side: its long line across the lounger (yaw 0, turning as it rolls) or along it with
                // its top toward the feet (yaw 90: it stays where it comes to lie), as the path was measured
                Quaternion side = Quaternion.Euler(0f, drop.yaw, 0f) * Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(0f, 0f, -Mathf.Rad2Deg * rolled / Mathf.Max(0.01f, drop.radius));
                droppingCan.localRotation = Quaternion.Slerp(dropRot0, side, lying);
                if (dropT > 0.14f && dropRobot != null) { dropRobot.gripTo[0] = 0f; dropRobot.gripTo[1] = 0f; }   // the can is out: the hand relaxes
                if (dropT > drop.t[n - 1]) drop = null;
            }
        }
    }
}
