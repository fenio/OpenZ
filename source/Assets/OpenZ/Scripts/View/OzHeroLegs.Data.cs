using System;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Hero legs (item 209), the data side: the baked leg programs of a hero squad member, read from the TextAsset
    /// Resources/OpenZ/HeroSquad/Legs/&lt;member&gt;_legs.json (JsonUtility, flat arrays). A program is a list of frames; each frame holds
    /// the Unity LOCAL rotations of the written bones (Hips, the 8 leg bones, and for the lie and its clips the 3 spine bones) and the
    /// Hips' local position, all under Model/Armature at Model scale 1. Kinds: "gait" (walk, run, back, strafes: the phase advances with
    /// the distance the body travels), "static" (one frame: the standing pose, the lie), "clip" (the phase follows the Animator's
    /// normalizedTime: lie-down, get-up, knockdown, deaths, jumps, climb, seats). The "states" table maps the grunt controller's state
    /// names to programs. Loaded once per member and shared.
    /// </summary>
    public sealed partial class OzHeroLegs
    {
        public const string DataDir = "OpenZ/HeroSquad/Legs/";

        [Serializable] public sealed class ProgramJson
        {
            public string name; public string kind; public string dir; public int fps; public int frames; public int nbones; public bool loop;
            public float stride; public float period; public float duty; public float entryPhase; public float[] stanceL; public float[] stanceR; public float[] q; public float[] p;
            public float rollRangeL; public float rollRangeR; public float rollStepL; public float rollStepR;
        }
        [Serializable] public sealed class StateJson { public string state; public string program; }
        [Serializable] public sealed class LegsJson
        {
            public string hero; public float modelScale; public float soleLift; public float soleY; public float legLenL; public float legLenR; public float bootWidthL; public float bootWidthR;
            public float ankleRestY; public float toeRestY; public float walkHipsY; public float standHipsY; public float standTop; public float lieHipsY; public float lieUnderside; public float lieBootLow;
            public float lieSeat; public float lieHipsBack; public float lieToeY; public float lieAnkleY;
            public string[] bones; public string[] paths; public StateJson[] states; public ProgramJson[] programs;
        }

        /// <summary>One baked program, ready to sample.</summary>
        public sealed class Program
        {
            public string Name, Kind, Dir; public int Fps, Frames, NBones; public bool Loop; public float Stride, Period, Duty, EntryPhase;
            public float[] StanceL, StanceR; public float RollRangeL, RollRangeR, RollStepL, RollStepR;
            /// <summary>Frames x NBones local rotations; Frames Hips local positions.</summary>
            public Quaternion[] Q; public Vector3[] P;
            public bool IsGait => Kind == "gait";
            public bool IsClip => Kind == "clip";
            public bool IsStatic => Kind == "static";

            /// <summary>The pose at phase u (a loop wraps, a one-shot clamps; frames are slerped): the first NBones of q and the Hips' local position.</summary>
            public void Sample(float u, Quaternion[] q, out Vector3 p)
            {
                int n = Frames;
                if (n <= 1) { for (int b = 0; b < NBones; b++) q[b] = Q[b]; p = P[0]; return; }
                float t; int i0, i1;
                if (Loop) { u = Mathf.Repeat(u, 1f); t = u * n; i0 = Mathf.Min((int)t, n - 1); i1 = (i0 + 1) % n; }
                else { u = Mathf.Clamp01(u); t = u * (n - 1); i0 = Mathf.Min((int)t, n - 1); i1 = Mathf.Min(i0 + 1, n - 1); }
                float f = Mathf.Clamp01(t - i0);
                int a = i0 * NBones, c = i1 * NBones;
                if (f <= 0f || i0 == i1) { for (int b = 0; b < NBones; b++) q[b] = Q[a + b]; p = P[i0]; return; }
                for (int b = 0; b < NBones; b++) q[b] = Quaternion.Slerp(Q[a + b], Q[c + b], f);
                p = Vector3.Lerp(P[i0], P[i1], f);
            }

            /// <summary>Gait: is this foot (0 left, 1 right) planted at phase u (the bake's stance window, wrapping)?</summary>
            public bool Planted(int foot, float u)
            {
                var w = foot == 0 ? StanceL : StanceR;
                if (w == null || w.Length < 2) return false;
                float s0 = w[0], s1 = w[1];
                if (s0 <= 0f && s1 >= 1f) return true;
                if (Mathf.Abs(s0 - s1) < 1e-6f) return false;
                u = Mathf.Repeat(u, 1f);
                return s0 < s1 ? (u >= s0 && u < s1) : (u >= s0 || u < s1);
            }
        }

        /// <summary>A member's whole data set.</summary>
        public sealed class LegData
        {
            public LegsJson J; public Program[] Programs = new Program[0]; public string[] Bones; public string Error;
            public readonly Dictionary<int, Program> ByState = new Dictionary<int, Program>();
            public readonly Dictionary<string, Program> ByName = new Dictionary<string, Program>();
            public readonly List<string> StateNames = new List<string>();
            public Program Find(string name) => name != null && ByName.TryGetValue(name, out var p) ? p : null;
            public Program ForState(int shortNameHash) => ByState.TryGetValue(shortNameHash, out var p) ? p : null;
        }

        static readonly Dictionary<string, LegData> cache = new Dictionary<string, LegData>();

        /// <summary>Forgets the cached data of every member (the editor's check after a re-import).</summary>
        public static void ForgetData() => cache.Clear();

        /// <summary>The member's data (cached; Error set when something is missing or malformed, Programs then holds what could be read).</summary>
        public static LegData Load(string member)
        {
            if (string.IsNullOrEmpty(member)) return null;
            if (cache.TryGetValue(member, out var d)) return d;
            d = new LegData();
            var ta = Resources.Load<TextAsset>(DataDir + member + "_legs");
            if (!ta) { d.Error = "no TextAsset Resources/" + DataDir + member + "_legs"; cache[member] = d; return d; }
            try { d.J = JsonUtility.FromJson<LegsJson>(ta.text); }
            catch (Exception e) { d.Error = "legs data unreadable: " + e.Message; }
            if (d.J == null || d.J.programs == null || d.J.bones == null) { if (d.Error == null) d.Error = "legs data empty"; cache[member] = d; return d; }
            d.Bones = d.J.bones;
            var list = new List<Program>();
            foreach (var pj in d.J.programs)
            {
                if (pj == null || pj.q == null || pj.p == null || pj.frames <= 0 || pj.nbones <= 0 || pj.nbones > d.Bones.Length
                    || pj.q.Length != pj.frames * pj.nbones * 4 || pj.p.Length != pj.frames * 3)
                { d.Error = "program " + (pj != null && pj.name != null ? pj.name : "?") + ": wrong array sizes"; continue; }
                var pr = new Program
                {
                    Name = pj.name, Kind = pj.kind, Dir = pj.dir, Fps = pj.fps, Frames = pj.frames, NBones = pj.nbones, Loop = pj.loop, Stride = pj.stride, Period = pj.period, Duty = pj.duty,
                    EntryPhase = pj.entryPhase, StanceL = pj.stanceL, StanceR = pj.stanceR, RollRangeL = pj.rollRangeL, RollRangeR = pj.rollRangeR, RollStepL = pj.rollStepL, RollStepR = pj.rollStepR,
                    Q = new Quaternion[pj.frames * pj.nbones], P = new Vector3[pj.frames]
                };
                for (int i = 0; i < pr.Q.Length; i++) { int k = i * 4; pr.Q[i] = new Quaternion(pj.q[k], pj.q[k + 1], pj.q[k + 2], pj.q[k + 3]); }
                for (int i = 0; i < pr.P.Length; i++) { int k = i * 3; pr.P[i] = new Vector3(pj.p[k], pj.p[k + 1], pj.p[k + 2]); }
                list.Add(pr); d.ByName[pr.Name] = pr;
            }
            d.Programs = list.ToArray();
            if (d.J.states != null)
                foreach (var s in d.J.states)
                    if (s != null && s.state != null && d.ByName.TryGetValue(s.program, out var pr)) { d.ByState[Animator.StringToHash(s.state)] = pr; d.StateNames.Add(s.state); }
            cache[member] = d;
            return d;
        }
    }
}
