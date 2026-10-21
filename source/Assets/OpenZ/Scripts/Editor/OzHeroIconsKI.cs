using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The ALAN &amp; BRAD unit icons, planned by hand (no automatic pick, no bone edits): for each picture ONE frame of the map's own
    /// idle (RobotBS01.controller state "idle" = idle_calm_humanoid, unchanged, through the humanoid retarget), the rifle held the
    /// way the game holds it on the map in that frame (the game's own WeaponHold component, one LateUpdate: the grip in the right
    /// palm socket, the barrel along the robot's front, the left fist toward the fore-grip), the whole robot turned by its root so
    /// that, like the BS-01 unit portraits, his front and face turn a little toward the picture's LEFT, the camera from the front
    /// robot's Head bone with fixed offsets, the lens and lamps of the other unit portraits (the lamps mirrored with the facing).
    /// Red and blue = the same pose, the team's paint only.
    /// Menu OpenZ/Hero Squad/2 renders the six pictures (Resources/OpenZ/UI/PortraitsHero/portrait_{alanbrad,alan,brad}_{red,blue}.png
    /// and copies for the owner) ONLY when every check line passes; nothing is written on a FAIL. Menu 2b prints the check lines
    /// only; menu 2c renders the two solo pictures only (Alan alone, Brad alone), with the same rule.
    /// </summary>
    public static class OzHeroIconsKI
    {
        public const string ClipName = "idle_calm_humanoid";
        const string Ctl = "Assets/OpenZ/Animation/RobotBS01.controller", Res = "Assets/OpenZ/Resources/OpenZ/HeroSquad",
                     PortraitDir = "Assets/OpenZ/Resources/OpenZ/UI/PortraitsHero", PortraitRef = "Assets/OpenZ/Resources/OpenZ/UI/PortraitsBS01/portrait_grunt_red.png";

        /// <summary>One robot in a picture: the second of the idle clip, the root's Euler y (deg; the camera looks along -z from +z, so
        /// 180 = his front toward the lens), the root's place (m), and the planned values of his check line (chest-to-lens: plus =
        /// his front turned toward the picture's left, like the BS-01 portraits).</summary>
        public sealed class Member
        {
            public string who; public float t, rootYaw; public Vector3 pos;
            public float planChestToLens, planFistCm;
            public Member(string who, float t, float rootYaw, Vector3 pos, float planChestToLens, float planFistCm)
            { this.who = who; this.t = t; this.rootYaw = rootYaw; this.pos = pos; this.planChestToLens = planChestToLens; this.planFistCm = planFistCm; }
        }

        /// <summary>One picture: its robots, the robot whose Head bone the camera is set from, the camera and its aim point as offsets
        /// (m, world) from that Head bone, the vertical lens (deg).</summary>
        public sealed class Icon
        {
            public string key, front; public Member[] members; public Vector3 cam, aim; public float fov;
        }

        public static readonly Icon[] Icons =
        {
            new Icon { key = "alanbrad", front = "brad", fov = 24f, cam = new Vector3(0.520f, 0.071f, 4.005f), aim = new Vector3(0.520f, -0.110f, -0.243f),   // the pair close: Alan just behind and beside Brad's shoulder, heads in the upper part, cut at the chest
                       members = new[] { new Member("brad", 0.5f, 172.3f, new Vector3(-0.60f, 0f, 0.30f), 7.0f, 13.1f),
                                         new Member("alan", 1.0f, 157.1f, new Vector3(0.45f, 0f, -0.30f), 4.0f, 15.6f) } },
            new Icon { key = "alan", front = "alan", fov = 24f, cam = new Vector3(0f, 0.032f, 3.376f), aim = new Vector3(0f, -0.111f, 0f),
                       members = new[] { new Member("alan", 1.0f, 164.5f, Vector3.zero, 4.0f, 15.6f) } },
            new Icon { key = "brad", front = "brad", fov = 24f, cam = new Vector3(0f, -0.022f, 3.604f), aim = new Vector3(0f, -0.175f, 0f),
                       members = new[] { new Member("brad", 0.5f, 167.7f, Vector3.zero, 7.0f, 13.1f) } },
        };

        /// <summary>Check limits: head turned from the chest at most (deg), the face turned from the lens at most (deg), chest-to-lens off
        /// its plan at most (deg), the fist at most this far over its planned distance from the fore-grip (cm), the palm socket on the
        /// grip (cm), a face covered by a rifle (share); the left hand: a fist this close to the fore-grip holds it (cm) and may be seen,
        /// a fist further off must not be seen from the lens (share of the picture); the close look that proves the hand check works
        /// must see the hand change on at least this share of its picture.</summary>
        public static float MaxHeadChest = 15f, MaxHeadLens = 13f, MaxPlanOff = 4f, MaxFistOver = 3f, MaxPalmCm = 1f, MaxFaceCover = 0.02f,
                            FistOnGripCm = 3f, MaxHandSeen = 0.001f, MinProbeSeen = 0.005f;

        [MenuItem("OpenZ/Hero Squad/2b Icons: check the planned poses (no files)", false, 11)]
        static void MenuCheck() => Run(false, null);

        [MenuItem("OpenZ/Hero Squad/2c Icons: render Alan alone and Brad alone only (the pair waits)", false, 11)]
        static void MenuSolos() => Run(true, new[] { "alan", "brad" });

        /// <summary>Menu 2 (OzHeroSquadSetup.MenuPortraits) comes here: the six pictures with the planned numbers.</summary>
        public static string Render() => Run(true, null);

        static string Run(bool write, string[] only)
        {
            if (Application.isPlaying) return Say("leave Play mode first");
            var log = new List<string>(); bool allOk = true; var outs = new List<(string name, byte[] png)>();
            foreach (var ic in Icons)
            {
                if (only != null && !only.Contains(ic.key)) continue;
                foreach (var team in new[] { Team.Red, Team.Blue })
                {
                    string name = $"portrait_{ic.key}_{(team == Team.Red ? "red" : "blue")}.png";
                    var lines = new List<string>();
                    var png = One(ic, team, write, lines, out bool ok);
                    allOk &= ok && (!write || png != null);
                    log.Add(name + (write && png == null ? ": NOT rendered" : ""));
                    foreach (var l in lines) log.Add("   " + l);
                    if (png != null) outs.Add((name, png));
                }
            }
            int pass = log.Count(l => l.TrimStart().StartsWith("PASS")), fail = log.Count(l => l.TrimStart().StartsWith("FAIL"));
            log.Insert(0, $"hero icons (planned poses): {pass} check line(s) PASS, {fail} FAIL");
            if (!allOk)
            {
                log.Add("SOME CHECKS FAIL: nothing was written, the existing pictures are left alone. Report the FAIL lines; do not change the numbers.");
                return Say(string.Join("\n", log));
            }
            if (!write) { log.Add("ALL CHECKS PASS"); return Say(string.Join("\n", log)); }
            string own = OzHeroSquadSetup.OwnerDir;
            Directory.CreateDirectory(own); Directory.CreateDirectory(PortraitDir);
            string moved = MoveEarlier(own);
            foreach (var (name, png) in outs)
            {
                string p = $"{PortraitDir}/{name}";
                File.WriteAllBytes(p, png);
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                CopyTextureSettings(PortraitRef, p);
                File.WriteAllBytes(Path.Combine(own, name), png);
                log.Add($"written {p} ({png.Length / 1024} KB) and the owner's copy in for_owner/hero_icons");
            }
            log.Add($"ALL CHECKS PASS: {outs.Count} picture(s) written");
            if (only != null) log.Add("only " + string.Join(", ", only) + " rendered: the pair picture in " + PortraitDir + " is still the earlier one and the owner's folder has none");
            if (moved != null) log.Add("the earlier pictures, panel pictures and logs of for_owner/hero_icons were moved to its folder " + Path.GetFileName(moved));
            log.Add("next: in Play, the unit panel with each picture (panel_alanbrad.png, panel_alan.png, panel_brad.png) into for_owner/hero_icons, then menu 4");
            string text = string.Join("\n", log) + "\n";
            File.WriteAllText(Path.Combine(own, "icons_KI_checks.txt"), text);
            File.WriteAllText(Path.Combine(own, "icons_pick_log.txt"), text);       // the name menu 4's marker points to
            return Say(string.Join("\n", log));
        }

        /// <summary>Moves every earlier portrait_*.png, panel_*.png and icons_*.txt of the owner's folder into a subfolder (rejected_v2
        /// the first time, previous_KI after a run of this menu), so menu 4 can only pass on the new pictures and new panel pictures.</summary>
        static string MoveEarlier(string own)
        {
            var old = Directory.GetFiles(own).Where(f =>
            {
                string n = Path.GetFileName(f);
                return ((n.StartsWith("portrait_") || n.StartsWith("panel_")) && n.EndsWith(".png")) || (n.StartsWith("icons_") && n.EndsWith(".txt"));
            }).ToArray();
            if (old.Length == 0) return null;
            string sub = File.Exists(Path.Combine(own, "icons_KI_checks.txt")) ? "previous_KI" : "rejected_v2";
            string dir = Path.Combine(own, sub); int k = 1;
            while (Directory.Exists(dir)) dir = Path.Combine(own, sub + "_" + (++k));
            Directory.CreateDirectory(dir);
            foreach (var f in old) File.Move(f, Path.Combine(dir, Path.GetFileName(f)));
            return dir;
        }

        sealed class Robot { public Member m; public GameObject go; public Animator an; public Renderer[] rifle; public Transform grip, fore, palm, fist, headFront; }

        static byte[] One(Icon ic, Team team, bool write, List<string> lines, out bool ok)
        {
            ok = true;
            var scene = EditorSceneManager.NewPreviewScene();
            var made = new List<GameObject>(); var temp = new List<Object>(); var khaki = new Dictionary<Texture, Texture2D>();
            RenderTexture rt = null; Texture2D tex = null;
            try
            {
                var ctl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Ctl);
                var clip = ctl ? ctl.animationClips.FirstOrDefault(c => c.name == ClipName) : null;
                if (!clip) { lines.Add($"FAIL  {ClipName} not in {Ctl}"); ok = false; return null; }
                var rs = new List<Robot>();
                foreach (var m in ic.members)
                {
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Res}/Z_hero_{m.who}.prefab");
                    if (!pf) { lines.Add($"FAIL  {Res}/Z_hero_{m.who}.prefab missing (menu 1 first)"); ok = false; return null; }
                    var go = Object.Instantiate(pf); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene); made.Add(go);
                    go.transform.SetPositionAndRotation(m.pos, Quaternion.Euler(0f, m.rootYaw, 0f));
                    var look = go.GetComponent<OzHeroLook>(); if (look) look.Apply(team);
                    var all = go.GetComponentsInChildren<Transform>(true);
                    Transform F(string n) => all.FirstOrDefault(x => x.name == n);
                    var r = new Robot { m = m, go = go, an = go.GetComponentInChildren<Animator>(), grip = F("Grip"), fore = F("Foregrip"), palm = F("RightHandWeapon"), fist = F("LeftHandGrip"), headFront = F("headfront") };
                    var rw = F("RightHandWeapon"); r.rifle = rw ? rw.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
                    foreach (var rr in r.rifle) Khaki(rr, khaki, temp);
                    rs.Add(r);
                }
                // ONE frame of the map's idle, unchanged, on every robot of the picture (one sampling pass for all)
                if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                foreach (var r in rs) if (r.an) AnimationMode.SampleAnimationClip(r.an.gameObject, clip, r.m.t);
                AnimationMode.EndSampling();
                // the rifle as the game holds it in that frame: the game's own WeaponHold, one LateUpdate
                var late = typeof(WeaponHold).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (var r in rs)
                {
                    var h = WeaponHold.Attach(r.go);
                    if (!h || late == null) { lines.Add($"FAIL  {r.m.who}: WeaponHold could not hold the rifle"); ok = false; continue; }
                    late.Invoke(h, null);
                }
                // the camera from the front robot's Head bone; the lamps of the unit portraits mirrored with the facing (key from the
                // front left, rim from behind right, the soft fill from the front right)
                var front = rs.First(r => r.m.who == ic.front);
                var head = front.an ? front.an.GetBoneTransform(HumanBodyBones.Head) : null;
                if (!head) { lines.Add("FAIL  no Head bone on " + ic.front); ok = false; return null; }
                var camGo = new GameObject("IconCam"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene); made.Add(camGo);
                var cam = camGo.AddComponent<Camera>(); cam.scene = scene; cam.fieldOfView = ic.fov; cam.nearClipPlane = 0.05f; cam.farClipPlane = 80f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.10f, 0.24f, 1f);
                cam.transform.position = head.position + ic.cam; cam.transform.LookAt(head.position + ic.aim, Vector3.up);
                void Lamp(string n, Color c, float intensity, Vector3 euler)
                {
                    var l = new GameObject(n); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(l, scene); made.Add(l);
                    var li = l.AddComponent<Light>(); li.type = LightType.Directional; li.color = c; li.intensity = intensity; li.shadows = LightShadows.None;
                    l.transform.rotation = Quaternion.Euler(euler);
                }
                Lamp("Key", new Color(1f, 0.92f, 0.82f), 1.5f, new Vector3(28f, 210f, 0f));
                Lamp("Rim", new Color(0.55f, 0.72f, 1f), 1.4f, new Vector3(18f, 30f, 0f));
                Lamp("Fill", new Color(0.75f, 0.80f, 0.95f), 0.35f, new Vector3(5f, 160f, 0f));
                // check lines
                const int R = 256;
                var full = Shot(cam, R, null);
                foreach (var r in rs)
                {
                    float fistCm = r.fist && r.fore ? Vector3.Distance(r.fist.position, r.fore.position) * 100f : -1f;
                    float palmCm = r.palm && r.grip ? Vector3.Distance(r.palm.position, r.grip.position) * 100f : -1f;
                    var la = r.an.GetBoneTransform(HumanBodyBones.LeftUpperArm); var ra = r.an.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var hb = r.an.GetBoneTransform(HumanBodyBones.Head);
                    var ch = r.an.GetBoneTransform(HumanBodyBones.UpperChest) ? r.an.GetBoneTransform(HumanBodyBones.UpperChest) : r.an.GetBoneTransform(HumanBodyBones.Chest);
                    float chest = Yaw(Vector3.Cross(ra.position - la.position, Vector3.up));
                    float headYaw = r.headFront ? Yaw(r.headFront.position - hb.position) : chest;
                    float lens = Yaw(cam.transform.position - ch.position);
                    float hc = Mathf.DeltaAngle(chest, headYaw), cl = Mathf.DeltaAngle(lens, chest), hl = Mathf.DeltaAngle(lens, headYaw);
                    var no = Shot(cam, R, r.rifle);
                    float cover = 0f; foreach (var q in rs) cover = Mathf.Max(cover, FaceCover(cam, q, full, no, R));
                    float seen = HandSeen(cam, r, full, R, out float probe);
                    bool fistOn = fistCm >= 0f && fistCm <= FistOnGripCm;
                    bool handOk = probe >= MinProbeSeen && seen >= 0f && (fistOn || seen <= MaxHandSeen);
                    bool pass = fistCm >= 0f && fistCm <= r.m.planFistCm + MaxFistOver && palmCm >= 0f && palmCm <= MaxPalmCm && Mathf.Abs(hc) <= MaxHeadChest
                                && Mathf.Abs(hl) <= MaxHeadLens && Mathf.Abs(cl - r.m.planChestToLens) <= MaxPlanOff && cover <= MaxFaceCover && handOk;
                    ok &= pass;
                    string handTxt = probe < MinProbeSeen ? $"left hand check NOT working (the close look saw {probe * 100f:0.0} % change)"
                                   : fistOn ? $"left hand on the fore-grip, seen {seen * 100f:0.00} % of the picture"
                                   : $"left hand seen from the lens {seen * 100f:0.00} % of the picture (at most {MaxHandSeen * 100f:0.0} % while the fist is off the fore-grip)";
                    lines.Add($"{(pass ? "PASS" : "FAIL")}  {r.m.who} ({ClipName} @{r.m.t:0.0} s, root {r.m.rootYaw:0.0} deg): left fist {fistCm:0.0} cm from the fore-grip (plan {r.m.planFistCm:0.0}, the game's own hold), " +
                              $"palm socket {palmCm:0.0} cm from the grip; head turned {hc:+0.0;-0.0} deg from the chest (at most {MaxHeadChest:0}); chest {cl:+0.0;-0.0} deg from the lens (plan {r.m.planChestToLens:+0.0;-0.0}, + = toward the picture's left), " +
                              $"face {hl:+0.0;-0.0} deg from the lens (at most {MaxHeadLens:0}); rifle over a face {cover * 100f:0.0} %; {handTxt}");
                }
                if (!write) return null;
                rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt; cam.Render();
                var prev = RenderTexture.active; RenderTexture.active = rt;
                tex = new Texture2D(1024, 1024, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1024, 1024), 0, 0); tex.Apply();
                RenderTexture.active = prev; cam.targetTexture = null;
                Backdrop(tex, cam.backgroundColor);
                return tex.EncodeToPNG();
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                foreach (var g in made) if (g) Object.DestroyImmediate(g);
                foreach (var o in temp) if (o) Object.DestroyImmediate(o);
                if (rt) { rt.Release(); Object.DestroyImmediate(rt); }
                if (tex) Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>What the lens sees of a robot's left hand (share of the picture): the picture with the hand folded away (its bone
        /// scaled to almost nothing, put back at once) against the full picture. probe = the same fold seen by a close look at the hand
        /// with his rifle hidden: it proves the fold reaches the picture (else the check line says the hand check is not working).</summary>
        static float HandSeen(Camera cam, Robot r, Color32[] full, int res, out float probe)
        {
            probe = 0f;
            var lh = r.an ? r.an.GetBoneTransform(HumanBodyBones.LeftHand) : null; if (!lh) return -1f;
            var s0 = lh.localScale;
            lh.localScale = s0 * 0.0001f; var folded = Shot(cam, res, null); lh.localScale = s0;
            float seen = Changed(full, folded) / (float)(res * res);
            var p0 = cam.transform.position; var q0 = cam.transform.rotation; float f0 = cam.fieldOfView; const int P = 128;
            var d = p0 - lh.position; d.y = 0f; d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            cam.transform.position = lh.position + d * 1.0f + Vector3.up * 0.3f; cam.transform.LookAt(lh.position, Vector3.up); cam.fieldOfView = 30f;
            var a = Shot(cam, P, r.rifle);
            lh.localScale = s0 * 0.0001f; var b = Shot(cam, P, r.rifle); lh.localScale = s0;
            cam.transform.SetPositionAndRotation(p0, q0); cam.fieldOfView = f0;
            probe = Changed(a, b) / (float)(P * P);
            return seen;
        }

        static int Changed(Color32[] a, Color32[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length && i < b.Length; i++)
                if (Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b) > 24) n++;
            return n;
        }

        static float Yaw(Vector3 d) { d.y = 0f; return d.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }

        /// <summary>A small picture of the camera (res x res, no anti-aliasing), with the given renderers hidden for it and put back as
        /// they were (a renderer that was off stays off).</summary>
        static Color32[] Shot(Camera cam, int res, Renderer[] off)
        {
            var was = off != null ? off.Select(r => r.enabled).ToArray() : null;
            if (off != null) foreach (var r in off) r.enabled = false;
            var t2 = RenderTexture.GetTemporary(res, res, 24, RenderTextureFormat.ARGB32); t2.antiAliasing = 1;
            cam.targetTexture = t2; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = t2;
            var tx = new Texture2D(res, res, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, res, res), 0, 0); tx.Apply();
            RenderTexture.active = prev; cam.targetTexture = null; RenderTexture.ReleaseTemporary(t2);
            var px = tx.GetPixels32(); Object.DestroyImmediate(tx);
            if (off != null) for (int i = 0; i < off.Length; i++) off[i].enabled = was[i];
            return px;
        }

        /// <summary>The share of a robot's face (a disc round his head, in front of the Head bone) that his own rifle's pixels cover.</summary>
        static float FaceCover(Camera cam, Robot q, Color32[] full, Color32[] no, int res)
        {
            var hb = q.an ? q.an.GetBoneTransform(HumanBodyBones.Head) : null; if (!hb) return 0f;
            float k = Mathf.Abs(q.an.transform.lossyScale.y);
            var c = q.headFront ? q.headFront.position : hb.position + Vector3.up * 0.2f * k;
            var sc = cam.WorldToViewportPoint(c); var se = cam.WorldToViewportPoint(c + cam.transform.right * 0.22f * k);
            float rad = Mathf.Abs(se.x - sc.x) * res; if (rad < 1f || sc.z < 0f) return 0f;
            int cx = Mathf.RoundToInt(sc.x * res), cy = Mathf.RoundToInt(sc.y * res), all = 0, hit = 0;
            for (int y = cy - (int)rad; y <= cy + (int)rad; y++)
                for (int x = cx - (int)rad; x <= cx + (int)rad; x++)
                {
                    if (x < 0 || y < 0 || x >= res || y >= res || (x - cx) * (x - cx) + (y - cy) * (y - cy) > rad * rad) continue;
                    all++; int i = y * res + x;
                    if (Mathf.Abs(full[i].r - no[i].r) + Mathf.Abs(full[i].g - no[i].g) + Mathf.Abs(full[i].b - no[i].b) > 24) hit++;
                }
            return all > 0 ? hit / (float)all : 0f;
        }

        /// <summary>The rifle in the game's khaki in the picture only (as the earlier portrait menu did: its own texture, the orange
        /// paint texels turned to OzHeroSquadSetup.RifleKhaki keeping their luminance), on a material copy of this preview instance.</summary>
        static void Khaki(Renderer r, Dictionary<Texture, Texture2D> cache, List<Object> temp)
        {
            if (!r || !r.sharedMaterial) return;
            var src = r.sharedMaterial.HasProperty("_BaseMap") ? r.sharedMaterial.GetTexture("_BaseMap") : r.sharedMaterial.mainTexture;
            if (!src) return;
            if (!cache.TryGetValue(src, out var k)) { k = KhakiCopy(src); cache[src] = k; if (k) temp.Add(k); }
            if (!k) return;
            var mc = new Material(r.sharedMaterial) { name = r.sharedMaterial.name + "_khaki" }; temp.Add(mc);
            if (mc.HasProperty("_BaseMap")) mc.SetTexture("_BaseMap", k);
            if (mc.HasProperty("_MainTex")) mc.SetTexture("_MainTex", k);
            if (mc.HasProperty("_BaseColor")) mc.SetColor("_BaseColor", Color.white);
            r.sharedMaterial = mc;
        }

        static Texture2D KhakiCopy(Texture src)
        {
            string p = AssetDatabase.GetAssetPath(src);
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, false);
            if (!t.LoadImage(File.ReadAllBytes(p), false)) { Object.DestroyImmediate(t); return null; }
            var px = t.GetPixels32(); var target = ((Color)OzHeroSquadSetup.RifleKhaki).linear;
            float lo = OzHeroSquadSetup.KhakiHueLo, hi = OzHeroSquadSetup.KhakiHueHi;
            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i]; Color.RGBToHSV(c, out float h, out float s, out float v); h *= 360f;
                float band = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo - 6f, lo, h)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(hi, hi + 6f, h)));
                float w = band * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.38f, s)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.14f, v));
                if (w <= 0f) continue;
                var l = c.linear; float y = 0.2126f * l.r + 0.7152f * l.g + 0.0722f * l.b; float kk = Mathf.Min(1f, y / PaintKhaki.RefLum);
                var o = Color.Lerp(l, new Color(Mathf.Clamp01(target.r * kk), Mathf.Clamp01(target.g * kk), Mathf.Clamp01(target.b * kk), 1f), w).gamma; o.a = c.a;
                px[i] = o;
            }
            t.SetPixels32(px); t.Apply(true); t.wrapMode = src.wrapMode; t.filterMode = src.filterMode; t.anisoLevel = src.anisoLevel;
            return t;
        }

        /// <summary>The BS-01 portraits' backdrop: deep blue, lighter behind the heads, darker to the corners.</summary>
        static void Backdrop(Texture2D t, Color bg)
        {
            var px = t.GetPixels(); int w = t.width, h = t.height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    float dx = (x - w * 0.5f) / w, dy = (y - h * 0.62f) / h, r = Mathf.Sqrt(dx * dx + dy * dy), k = Mathf.Clamp01(1.25f - r * 1.6f);
                    bool isBg = Mathf.Abs(c.r - bg.r) < 0.01f && Mathf.Abs(c.g - bg.g) < 0.01f && Mathf.Abs(c.b - bg.b) < 0.01f;
                    c = isBg ? Color.Lerp(new Color(0.01f, 0.05f, 0.14f), new Color(0.10f, 0.30f, 0.62f), k) : c * Mathf.Lerp(0.82f, 1f, k);
                    c.a = 1f; px[y * w + x] = c;
                }
            t.SetPixels(px); t.Apply();
        }

        static void CopyTextureSettings(string from, string to)
        {
            if (!(AssetImporter.GetAtPath(from) is TextureImporter a) || !(AssetImporter.GetAtPath(to) is TextureImporter b)) return;
            var s = new TextureImporterSettings(); a.ReadTextureSettings(s); b.SetTextureSettings(s);
            b.maxTextureSize = a.maxTextureSize; b.textureCompression = a.textureCompression; b.crunchedCompression = a.crunchedCompression;
            b.SaveAndReimport();
        }

        static string Say(string s) { Debug.Log("[OpenZ HeroIcons] " + s); return s; }
    }
}
