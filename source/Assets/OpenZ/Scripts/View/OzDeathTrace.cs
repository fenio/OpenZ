using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// While armed (OpenZ/Deaths/Trace deaths, Play) every frame logs: the sim's MissileExploded blasts CombatView draws, the
    /// Death events, the frame UnitView.Die runs and the death it picked (ragdoll + Zod flight / airborne / melt / pop), the robot's
    /// hips every frame for 6 s (first frame it moved 5 cm and 25 cm, apex, landing time and distance), the death / pop sound
    /// plays counted per frame (CombatAudio.PlayCounts), and blast reactions. On disarm it writes
    /// Die -> first motion in frames and ms, flight time, landing distance, sound frame). View only, off unless armed.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class OzDeathTrace : MonoBehaviour
    {
        static OzDeathTrace inst;
        static float until = -1f;
        public static bool Armed => until > 0f && Time.unscaledTime < until;

        sealed class Track
        {
            public int id, dieFrame, blastFrame = -1, eventFrame = -1, move5 = -1, move25 = -1, soundFrame = -1, landFrame = -1; public float dieT, blastT = -1f, move5T = -1f, move25T = -1f, landT = -1f, apex, landDist;
            public string mode = "?", type; public Transform hips; public Vector3 start; public bool fire, missile, climbed; public UnitView view;
        }
        readonly List<Track> tracks = new List<Track>(), done = new List<Track>();
        readonly StringBuilder csv = new StringBuilder();
        readonly List<(int frame, float t, Vector3 at, float r)> blasts = new List<(int, float, Vector3, float)>();
        readonly Dictionary<string, int> lastCounts = new Dictionary<string, int>();
        int reactions;

        public static void Arm(float seconds)
        {
            if (!inst) inst = new GameObject("OzDeathTrace").AddComponent<OzDeathTrace>();
            inst.tracks.Clear(); inst.done.Clear(); inst.blasts.Clear(); inst.csv.Clear(); inst.reactions = 0;
            inst.csv.AppendLine("frame,time_s,sim_s,event,id,kind,x,y,z,value,note");
            until = Time.unscaledTime + seconds;
            Debug.Log($"[OpenZ] death trace armed for {seconds:0} s (real time)");
        }

        static void Row(string ev, int id, string kind, Vector3 p, float value, string note = "")
        {
            if (!inst) return;
            var g = ZGame.I; double sim = g && g.World != null ? g.World.Time : 0;
            inst.csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.0000},{2:0.000},{3},{4},{5},{6:0.00},{7:0.00},{8:0.00},{9:0.000},{10}",
                Time.frameCount, Time.unscaledTime, sim, ev, id, kind, p.x, p.y, p.z, value, note));
        }

        /// <summary>Proof hook (harness): every blast CombatView draws for a sim MissileExploded, with its drawn point.</summary>
        public static System.Action<ZEvent, Vector3> BlastDrawn;

        /// <summary>CombatView, MissileExploded (after the blast is drawn).</summary>
        public static void Blast(ZEvent e, Vector3 at)
        {
            BlastDrawn?.Invoke(e, at);                                                              // proof hook (CombatFeelProof E1), view only
            if (!Armed) return;
            inst.blasts.Add((Time.frameCount, Time.unscaledTime, at, e.radius / 8f));
            Row("blast_drawn", e.objId, e.flag ? "grenade" : e.flag2 ? "debris" : "missile", at, e.radius / 8f);
        }

        /// <summary>CombatView, Death of a robot (before UnitView.Die).</summary>
        public static void DeathEvent(ZEvent e, ZObject o, Vector3 from)
        {
            if (!Armed || o == null) return;
            Row("death_event", o.Id, (e.flag ? "fire " : "") + (e.flag2 ? "missile" : "direct"), from, 0f, o.Type);
        }

        public static void Died(UnitView v, bool fire, bool missile, Vector3 blast)
        {
            if (!Armed || !v || v.Obj == null) return;
            var t = new Track { id = v.Obj.Id, type = v.Obj.Type, dieFrame = Time.frameCount, dieT = Time.unscaledTime, fire = fire, missile = missile, view = v };
            var an = v.GetComponentInChildren<Animator>();
            if (an && an.isHuman) t.hips = an.GetBoneTransform(HumanBodyBones.Hips);
            if (!t.hips) foreach (var x in v.GetComponentsInChildren<Transform>()) if (x.name == "mixamorigHips") { t.hips = x; break; }
            if (!t.hips) t.hips = v.transform;
            t.start = t.hips.position;
            float best = float.MaxValue;
            foreach (var b in inst.blasts)
            {
                if (Time.frameCount - b.frame > 120) continue;
                var d = b.at - t.start; d.y = 0; float m = d.magnitude;
                if (m <= b.r + 2f && m < best) { best = m; t.blastFrame = b.frame; t.blastT = b.t; }
            }
            inst.tracks.Add(t);
            Row("die_called", t.id, missile ? "missile" : fire ? "fire" : "direct", t.start, best < float.MaxValue ? best : -1f, "dist to blast");
        }

        public static void Mode(UnitView v, string mode)
        {
            if (!Armed || !v || v.Obj == null) return;
            foreach (var t in inst.tracks) if (t.id == v.Obj.Id) t.mode = mode;
            Row("death_mode", v.Obj.Id, mode, v.transform.position, 0f);
        }

        public static void Reacted(UnitView v, int kind)
        {
            if (!Armed || !v || v.Obj == null) return;
            inst.reactions++;
            Row("reaction", v.Obj.Id, kind == 3 ? "knockdown" : kind == 2 ? "stagger" : "flinch", v.transform.position, kind);
        }

        void LateUpdate()
        {
            if (until < 0f) return;
            // sounds that started this frame (CombatAudio counts every play per key)
            var au = CombatAudio.I; int deathSounds = 0;
            if (au)
                foreach (var key in new[] { "death", RobotPopDeath.SoundKey })
                {
                    au.PlayCounts.TryGetValue(key, out int n); lastCounts.TryGetValue(key, out int was); lastCounts[key] = n;
                    if (n > was) { deathSounds += n - was; Row("sound", -1, key, Vector3.zero, n - was); }
                }
            var g = ZGame.I;
            for (int i = tracks.Count - 1; i >= 0; i--)
            {
                var t = tracks[i];
                if (deathSounds > 0 && t.soundFrame < 0) t.soundFrame = Time.frameCount;
                if (!t.hips || !t.hips.gameObject.activeInHierarchy || Time.unscaledTime - t.dieT > 6f) { done.Add(t); tracks.RemoveAt(i); continue; }
                var p = t.hips.position; float moved = (p - t.start).magnitude;
                if (t.move5 < 0 && moved > 0.05f) { t.move5 = Time.frameCount; t.move5T = Time.unscaledTime; }
                if (t.move25 < 0 && moved > 0.25f) { t.move25 = Time.frameCount; t.move25T = Time.unscaledTime; }
                float gy = g ? g.GroundY(p.x, p.z) : 0f, above = p.y - gy;
                t.apex = Mathf.Max(t.apex, above);
                if (above > 1.2f) t.climbed = true;
                if (t.climbed && t.landFrame < 0 && above < 0.9f) { t.landFrame = Time.frameCount; t.landT = Time.unscaledTime; var d = p - t.start; d.y = 0; t.landDist = d.magnitude; }
                if ((Time.frameCount - t.dieFrame) % 2 == 0) Row("hips", t.id, t.mode, p, moved);
            }
            if (!Armed) { Write(); until = -1f; }
        }

        void Write()
        {
            done.AddRange(tracks); tracks.Clear();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/qc"));
            try { Directory.CreateDirectory(dir); } catch { dir = Application.temporaryCachePath; }
            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var md = new StringBuilder();
            md.AppendLine($"# Death frame trace {stamp}");
            md.AppendLine();
            md.AppendLine($"{done.Count} robot deaths, {blasts.Count} blasts drawn, {reactions} blast reactions. Frames at the Play frame rate; ms = real time.");
            md.AppendLine();
            md.AppendLine("| robot | type | death | picked | blast -> Die | Die -> 5 cm | Die -> 25 cm | death sound | flight (Die -> landing) | apex | landed from start |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            string F(int a, int b, float ta, float tb) => a < 0 || b < 0 ? "-" : $"{b - a} f / {(tb - ta) * 1000f:0} ms";
            foreach (var t in done)
                md.AppendLine($"| {t.id} | {t.type} | {(t.missile ? "missile" : t.fire ? "fire" : "direct")} | {t.mode} | {F(t.blastFrame, t.dieFrame, t.blastT, t.dieT)} | " +
                              $"{F(t.dieFrame, t.move5, t.dieT, t.move5T)} | {F(t.dieFrame, t.move25, t.dieT, t.move25T)} | " +
                              $"{(t.soundFrame < 0 ? "-" : (t.soundFrame - t.dieFrame) + " f")} | {F(t.dieFrame, t.landFrame, t.dieT, t.landT)} | {t.apex:0.0} m | {t.landDist:0.0} m |");
            md.AppendLine();
            md.AppendLine("Expected (deaths_mech_v1): blast -> Die 0 f (same frame), Die -> 5 cm 1-2 f, death sound 0 f, missile deaths fly 3.5-4.4 s (Zod ERobotTurrent), landing <= 6 m + ragdoll roll.");
            File.WriteAllText(Path.Combine(dir, $"death_trace_{stamp}.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(dir, $"death_trace_{stamp}.md"), md.ToString());
            Debug.Log($"[OpenZ] death trace written: {Path.Combine(dir, $"death_trace_{stamp}.md")}\n{md}");
        }
    }
}
