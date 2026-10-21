#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: the camera of the take and the per-frame hook (camera pose, listener, sound capture).
//  - Shots: a key with cut = true starts a new shot (a hard cut at its second). Inside a shot the camera and its aim run on one
//    Catmull-Rom spline through the keys AT THEIR SECONDS; the shot's ease (inout / smooth / in / out / linear) shapes its clock.
//    A shot whose last key comes before the next cut holds that key until the cut.
//  - look_sun keys aim at the level's own drawn sun (FlybySky.VisualSun of the held clock), the disc at sun_up of the half frame.
//  - The lens (vertical degrees) blends from key to key. A floor: never under floor_m over the ground (eased lift, snapped at a cut).
//  - The clearance: a living robot never comes closer than 3 m sideways to the lens (a vehicle or gun 4.5 m): the lens steps aside
//    to the edge of that ring and up while the unit stands under it, eased; the take log gives the real closest unit and the frames
//    the lens stepped aside (the capture kit's own 8 m / 13 m lift does not reach this pose).
//  - A soft handheld drift (drift x the capture kit's own amount) and the shakes of the scenario's cues.
//  - The eases (floor lift, clearance) move once per captured frame, never again while the capture kit holds for heat.
//  - The pose is set in Application.onBeforeRender, after the capture kit has placed its own cine camera: this file's path wins.
using System;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        sealed class Shot { public int a, b; public float t0, t1; public string ease; }
        readonly List<Shot> shots = new List<Shot>();
        Vector3[] kPos, kLook; float[] kT, kFov;
        Vector3 sunDir = new Vector3(0.9f, 0.02f, -0.45f).normalized;
        int curI = -1, lastShot = -1; float lift, liftVel;
        struct Shake { public float t0, dur, posM, rotDeg; }
        readonly List<Shake> shakes = new List<Shake>();

        /// <summary>The sun as the painted flyby sky draws it (the key light's azimuth, low over the horizon) for the held clock.</summary>
        static Vector3 DrawnSun()
        {
            var sun = RenderSettings.sun;
            return FlybySky.VisualSun(sun ? -sun.transform.forward : new Vector3(0.94f, 0.16f, -0.3f).normalized).normalized;
        }

        void BuildPath()
        {
            sunDir = DrawnSun();
            int nk = sc.camera.Length;
            kPos = new Vector3[nk]; kLook = new Vector3[nk]; kT = new float[nk]; kFov = new float[nk];
            for (int i = 0; i < nk; i++)
            {
                var k = sc.camera[i];
                kPos[i] = V(k.pos); kT[i] = k.t; kFov[i] = k.fov > 1f ? k.fov : 38f;
                if (k.look_sun)
                {   // aim below the disc by the angle that puts it at sun_up of the half frame
                    float down = Mathf.Atan(Mathf.Clamp(k.sun_up, -0.95f, 0.95f) * Mathf.Tan(kFov[i] * 0.5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
                    var right = Vector3.Cross(Vector3.up, sunDir); if (right.sqrMagnitude < 1e-6f) right = Vector3.right; right.Normalize();
                    kLook[i] = kPos[i] + (Quaternion.AngleAxis(down, right) * sunDir) * 200f;
                }
                else kLook[i] = V(k.look);
            }
            shots.Clear();
            for (int i = 0; i < nk; i++)
            {
                if (i == 0 || sc.camera[i].cut) shots.Add(new Shot { a = i, b = i, t0 = kT[i], ease = string.IsNullOrEmpty(sc.camera[i].ease) ? "inout" : sc.camera[i].ease });
                else shots[shots.Count - 1].b = i;
            }
            for (int s = 0; s < shots.Count; s++) shots[s].t1 = s + 1 < shots.Count ? shots[s + 1].t0 : Mathf.Max(sc.seconds, kT[shots[s].b]);
            var txt = new System.Text.StringBuilder();
            for (int s = 0; s < shots.Count; s++) txt.Append("shot ").Append(s + 1).Append(" ").Append(shots[s].t0.ToString("F1")).Append("-").Append(shots[s].t1.ToString("F1")).Append(" s, keys ").Append(shots[s].a).Append("-").Append(shots[s].b).Append(" ").Append(shots[s].ease).Append("; ");
            Note("camera: " + txt + "drawn sun heading " + (Mathf.Atan2(sunDir.x, sunDir.z) * Mathf.Rad2Deg).ToString("F1") + " deg, up " + (Mathf.Asin(sunDir.y) * Mathf.Rad2Deg).ToString("F1") + " deg");
        }

        /// <summary>Eight points of the take's path for the capture kit's own spline (it only places the camera before this file's
        /// pose replaces it every frame; the capture kit's min-distance numbers in its log come from it, not from the real lens: the
        /// take log's "lens clearance" line has the real one).</summary>
        Vector3[] SplineHint(bool camera)
        {
            var a = new Vector3[8];
            for (int i = 0; i < 8; i++) { Pose(sc.seconds * i / 7f, out var p, out var l, out _, out _); a[i] = camera ? p : l; }
            return a;
        }

        static float Ease(string e, float x)
        {
            x = Mathf.Clamp01(x);
            float sm = x * x * (3f - 2f * x);
            switch (e)
            {
                case "linear": return x;
                case "smooth": return sm;
                case "in": return Mathf.Lerp(x, x * x, 0.6f);
                case "out": return Mathf.Lerp(x, 1f - (1f - x) * (1f - x), 0.6f);
                default: return Mathf.Lerp(x, sm, 0.75f);                                              // inout: a gentle start and settle, never a dead stop
            }
        }

        static Vector3 CR(Vector3[] p, int a, int b, int j, float u)
        {
            Vector3 p1 = p[j], p2 = p[j + 1];
            Vector3 p0 = j > a ? p[j - 1] : p1 + (p1 - p2), p3 = j + 2 <= b ? p[j + 2] : p2 + (p2 - p1);
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// <summary>The path at clip second t: position, aim point, lens, shot index.</summary>
        void Pose(float t, out Vector3 pos, out Vector3 look, out float fov, out int shot)
        {
            shot = 0;
            for (int s = 0; s < shots.Count; s++) if (t >= shots[s].t0 - 1e-4f) shot = s;
            var sh = shots[shot];
            if (sh.b == sh.a) { pos = kPos[sh.a]; look = kLook[sh.a]; fov = kFov[sh.a]; return; }
            float tEnd = kT[sh.b], span = Mathf.Max(1e-3f, tEnd - sh.t0);
            float te = sh.t0 + span * Ease(sh.ease, (t - sh.t0) / span);
            int j = sh.a; while (j < sh.b - 1 && te > kT[j + 1]) j++;
            float u = Mathf.Clamp01((te - kT[j]) / Mathf.Max(1e-3f, kT[j + 1] - kT[j]));
            pos = CR(kPos, sh.a, sh.b, j, u); look = CR(kLook, sh.a, sh.b, j, u);
            fov = Mathf.Lerp(kFov[j], kFov[j + 1], u * u * (3f - 2f * u));
        }

        // ------------------------------------------------------------------ the hook
        UnityEngine.Events.UnityAction beforeRender; System.Action<int> frameHook, wrapped;

        void Hook()
        {
            frameHook = OnCaptureFrame;
            beforeRender = OnBeforeRender;
            Application.onBeforeRender += beforeRender;
            ch = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2; rate = AudioSettings.outputSampleRate; per = rate / fps;
            samples = new List<float>(n * per * ch + 4096);
            crocRead = W.CrocNoteCount; crocHunts0 = W.CrocHunts; crocKills0 = W.CrocKills;
            Kf4Hook();                                                                             // round 4 (OzEpicFlyby.Kf4.cs)
        }

        void Unhook()
        {
            if (beforeRender != null) Application.onBeforeRender -= beforeRender;
            beforeRender = null;
            if (ck && (ck.FrameHook == frameHook || ck.FrameHook == wrapped)) ck.FrameHook = null;
        }

        /// <summary>The capture kit calls this before each captured frame (frame i is drawn next).</summary>
        void OnCaptureFrame(int i)
        {
            curI = i; FrameNow = i;
            if (i == 0) { armed = true; if (frozen) { Time.timeScale = 1f; frozen = false; } LogFires("frame 0"); }
            RunCues(i / (float)fps, i);
            IslandFrame(i / (float)fps, i);                                                        // the loungers and the crocodile catches (OzEpicFlyby.Island.cs)
            ReadCrocNotes(i);
            if (i % 30 == 0) CountAlive(i);
        }

        void OnBeforeRender()
        {
            if (W != null && W.Bots.Count > 0) W.Bots.Clear();
            if (ck)
            {   // keep this take's frame hook in front of any other
                if (ck.FrameHook == null) ck.FrameHook = frameHook;
                else if (ck.FrameHook != frameHook && ck.FrameHook != wrapped) { var other = ck.FrameHook; wrapped = k => { frameHook(k); other(k); }; ck.FrameHook = wrapped; }
            }
            if (!cam || shots.Count == 0) return;
            float t = Mathf.Max(0, curI) / (float)fps;
            bool step = curI != lastStepI; lastStepI = curI;                                           // the eases move once per captured frame (not again in a heat hold)
            Pose(t, out var pos, out var look, out var fov, out int shot);
            // the floor: eased lift over the ground under the camera, snapped at a cut
            float gy = g ? g.GroundY(pos.x, pos.z) : 0f, need = Mathf.Max(0f, gy + sc.floor_m - pos.y);
            bool cut = shot != lastShot;
            if (cut) { lift = need; liftVel = 0f; lastShot = shot; }
            else if (step) lift = Mathf.SmoothDamp(lift, need, ref liftVel, 0.35f, Mathf.Infinity, 1f / fps);
            pos.y += Mathf.Max(lift, need);                                                            // up at once (never under the floor), down eased
            // the clearance: a living unit never walks through the lens (this pose replaces the capture kit's own 8 m / 13 m lift)
            if (cut || step)
            {
                var want = Clearance(pos);
                if (cut) { dodge = want; dodgeVel = Vector3.zero; }
                else dodge = Vector3.SmoothDamp(dodge, want, ref dodgeVel, 0.25f, Mathf.Infinity, 1f / fps);
                if (curI >= 0 && dodge.sqrMagnitude > 0.0025f) { dodgeFrames++; dodgeMax = Mathf.Max(dodgeMax, dodge.magnitude); }
            }
            pos += dodge;
            if (dodge.sqrMagnitude > 1e-6f && g) pos.y = Mathf.Max(pos.y, g.GroundY(pos.x, pos.z) + sc.floor_m);   // stepped aside onto higher ground: still over the floor
            // handheld drift and the shakes
            float tt = t + 17.3f, amp = sc.drift;
            pos += new Vector3(Mathf.PerlinNoise(tt * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(4.1f, tt * 0.17f) - 0.5f, Mathf.PerlinNoise(tt * 0.19f, 7.7f) - 0.5f) * 0.32f * amp;
            var fwd = look - pos; if (fwd.sqrMagnitude < 1e-4f) fwd = cam.transform.forward;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            rot *= Quaternion.Euler((Mathf.PerlinNoise(13.7f, tt * 0.23f) - 0.5f) * 0.5f * amp, (Mathf.PerlinNoise(tt * 0.27f, 11.1f) - 0.5f) * 0.6f * amp, (Mathf.PerlinNoise(tt * 0.2f, 19.5f) - 0.5f) * 0.4f * amp);
            foreach (var s in shakes)
            {
                if (t < s.t0 || t >= s.t0 + s.dur) continue;
                float u = (t - s.t0) / s.dur, env = (1f - u) * (1f - u) * Mathf.Clamp01((t - s.t0) / 0.08f), q = t * 9f;
                var j = new Vector3(Mathf.PerlinNoise(q, 3.7f) - 0.5f, Mathf.PerlinNoise(11.3f, q) - 0.5f, Mathf.PerlinNoise(q, 27.1f) - 0.5f) * 2f;
                var a = new Vector3(Mathf.PerlinNoise(q * 1.13f, 41.9f) - 0.5f, Mathf.PerlinNoise(53.3f, q * 1.07f) - 0.5f, Mathf.PerlinNoise(q * 0.91f, 67.7f) - 0.5f) * 2f;
                pos += rot * new Vector3(j.x, j.y, j.z * 0.3f) * (s.posM * env);
                rot *= Quaternion.Euler(a.x * s.rotDeg * env, a.y * s.rotDeg * env, a.z * s.rotDeg * 0.5f * env);
            }
            cam.fieldOfView = fov;
            cam.transform.SetPositionAndRotation(pos, rot);
            if (step && curI >= 0) { MeasureLens(pos); Kf4Frame(pos, rot, fov); }                // round 4: RTS distance, croc_show, lasers (OzEpicFlyby.Kf4.cs)
            // the listener rig (the combat sound reads the RTS focus and height) follows the point the camera looks at
            if (rts)
            {
                var f = cam.transform.forward; float d = f.y < -0.05f ? Mathf.Min(140f, (pos.y - gy) / -f.y) : 80f; var foc = pos + f * d;
                rts.focus = new Vector3(foc.x, 0f, foc.z); rts.height = Mathf.Clamp(d * 0.9f, rts.minHeight, 90f);
            }
            TickBirds(t, pos);
            TickLensBirds(t);                                                                      // parrots past the lens (OzEpicFlyby.LensBirds.cs)
            Silence();
            GrabSound();
        }

        // ------------------------------------------------------------------ the clearance of the lens from the units
        /// <summary>A robot keeps this far from the lens (sideways), a vehicle or gun ClearVehicleM; the lens steps aside to the edge of
        /// that ring (eased, 0.25 s), and up as well when the unit stands right under it.</summary>
        public const float ClearRobotM = 3f, ClearVehicleM = 4.5f;
        Vector3 dodge, dodgeVel; int lastStepI = int.MinValue, dodgeFrames; float dodgeMax;
        float minLens = float.MaxValue; int minLensFrame = -1; string minLensWho = "";

        static bool Living(UnitView v) => v && v.Obj != null && !v.Obj.Destroyed && v.Obj.KillAt < 0 && v.Obj.IsUnit && v.gameObject.activeInHierarchy;

        Vector3 Clearance(Vector3 pos)
        {
            if (!g) return Vector3.zero;
            Vector3 push = Vector3.zero; float up = 0f;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!Living(v)) continue;
                var p = v.transform.position; bool veh = v.Obj.Kind != ZKind.Robot;
                float r = veh ? ClearVehicleM : ClearRobotM, top = p.y + (veh ? 3.2f : 2.2f);
                if (pos.y > top + 2.5f) continue;                                                      // well above it
                var h = new Vector2(pos.x - p.x, pos.z - p.z); float dh = h.magnitude;
                if (dh >= r) continue;
                var dir = dh > 0.05f ? h / dh : new Vector2(1f, 0f);
                push += new Vector3(dir.x, 0f, dir.y) * (r - dh);                                      // aside to the edge of its ring
                up = Mathf.Max(up, (1f - dh / r) * Mathf.Max(0f, top + 1.2f - pos.y));                 // and up while it is under the lens
            }
            if (push.magnitude > ClearVehicleM) push = push.normalized * ClearVehicleM;
            return push + Vector3.up * up;
        }

        /// <summary>The real lens: the closest living unit (its upright body, ground to top) over the take, for the take log.</summary>
        void MeasureLens(Vector3 pos)
        {
            if (!g) return;
            foreach (var kv in g.Views)
            {
                var v = kv.Value; if (!Living(v)) continue;
                var p = v.transform.position; float top = p.y + (v.Obj.Kind != ZKind.Robot ? 3.2f : 2.2f);
                float d = Vector3.Distance(pos, new Vector3(p.x, Mathf.Clamp(pos.y, p.y, top), p.z));
                if (d < minLens) { minLens = d; minLensFrame = curI; minLensWho = v.Obj.Type + " #" + v.Obj.Id + " " + v.Obj.Owner; }
            }
        }

        // ------------------------------------------------------------------ the sound, rendered offline in step with the frames
        List<float> samples = new List<float>(); int ch = 2, rate = 48000, per = 800;
        bool armed, audioStarted; int rendered, lastFrame = -1, holdFrames, renderFails, dropped, countMismatch; float audioPeak;

        void GrabSound()
        {
            if (Time.frameCount == lastFrame || rendered >= n) return;
            if (armed && !audioStarted) { audioStarted = AudioRenderer.Start(); lastDrain = Time.unscaledTime; Note("sound capture start " + audioStarted + " at frame " + Time.frameCount + ", screen " + Screen.width + "x" + Screen.height); if (!audioStarted) { armed = false; return; } }
            if (!audioStarted) return;
            if (Time.captureFramerate <= 0) { holdFrames++; return; }                                     // a heat hold: no frame, no sound
            lastFrame = Time.frameCount;
            int avail = AudioRenderer.GetSampleCountForCaptureFrame();
            if (rendered > 0 && avail != per) { countMismatch++; if (countMismatch <= 8) Note("frame " + rendered + ": " + avail + " samples (hold frames before " + holdFrames + ")"); }
            using (var nb = new Unity.Collections.NativeArray<float>(Mathf.Max(0, avail) * ch, Unity.Collections.Allocator.Temp))
            {
                if (!AudioRenderer.Render(nb)) renderFails++;
                int from = avail > per + per / 2 && rendered > 0 ? (avail - per) * ch : 0;                     // after a heat hold: the frame's own share only
                if (from > 0) dropped += from / ch;
                for (int k = from; k < nb.Length; k++) { float s = nb[k]; samples.Add(s); if (Mathf.Abs(s) > audioPeak) audioPeak = Mathf.Abs(s); }
            }
            holdFrames = 0; rendered++;
            if (rendered % 20 == 0) Drain(false);
        }

        // the sound log of the captured span (every sound the game asked for, per source | key | outcome)
        readonly SortedDictionary<string, int> keyCount = new SortedDictionary<string, int>(); readonly Dictionary<string, float> keyVol = new Dictionary<string, float>();
        float lastDrain = -1f;
        void Drain(bool final)
        {
            if (lastDrain < 0f) return;
            float now = Time.unscaledTime;
            foreach (var e in OzSoundLog.Since(lastDrain))
            {
                if (!final && e.t >= now) continue;
                string k = e.src + " | " + e.key + " | " + e.outcome;
                keyCount.TryGetValue(k, out int c); keyCount[k] = c + 1;
                keyVol.TryGetValue(k, out float mv); if (e.vol > mv) mv = e.vol; keyVol[k] = mv;
                CountVoice(e);
            }
            lastDrain = now;
        }

        // the voice check of the captured span: what of the robots, the announcer and the commander could be heard
        int busVoices, poolVoices, poolStops;

        /// <summary>A voice-bus play that was heard (louder than 0.001: the muted buses log 0.000), or a robot voice line of the level's
        /// animal pool (the spoken crocodile lines vo_*, robot_croc_alarm, robot_eaten_cry, any clip of the silence list), or this take's
        /// stop of one (EFB | epic_flyby_silence | STOPPED).</summary>
        void CountVoice(OzSoundLog.Entry e)
        {
            string src = e.src ?? "", key = e.key ?? "", oc = e.outcome ?? "";
            if (src == "EFB" && key == "epic_flyby_silence" && oc.StartsWith("STOPPED", StringComparison.Ordinal)) { poolStops++; return; }
            if (!oc.StartsWith("PLAY", StringComparison.Ordinal)) return;
            if (src == "L16")
            {
                if (key.StartsWith("vo_", StringComparison.Ordinal) || key == "robot_croc_alarm" || key == "robot_eaten_cry" || IsRobotVoice(e.clip)) poolVoices++;
                return;
            }
            bool voice = oc.IndexOf("Voice", StringComparison.Ordinal) >= 0 || src == "VO" || oc.IndexOf("rachel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         oc.IndexOf("announcer", StringComparison.OrdinalIgnoreCase) >= 0;
            if (voice && e.vol > 0.001f) busVoices++;
        }

        /// <summary>Audible robot / announcer voices: the heard voice-bus plays plus the robot voice lines of the animal pool not stopped.</summary>
        int AudibleVoices() => busVoices + Mathf.Max(0, poolVoices - poolStops);
    }
}
#endif
