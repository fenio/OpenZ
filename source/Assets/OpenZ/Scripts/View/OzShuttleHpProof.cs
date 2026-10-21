using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K443 Play proof (rule DV131, Sim/ZWorld.ShuttleHp.cs: every shuttle starts with the light tank's hit points x 1.2), on level 16 in
    /// Play right after the start (menu OpenZ/V1.1/Shuttle: K443 More HP Proof, Editor/OzShuttleHpMenu.cs). Counted PASS / FAIL lines and
    /// two full-screen pictures from the normal RTS camera at 70 m. The other teams hold their fire while it runs (about 15 s); health is
    /// set straight in the rules (test only) and given back at the end.
    /// <list type="bullet">
    /// <item>P1 level 16's own ship has 4054 hit points (the light tank's 3378 x 1.2).</item>
    /// <item>P2 the testing panel's drops (OzCheatShuttle.Spawn, the panel's own path): a red and an empty one by the red fort, a blue one
    /// by the blue fort, each with the same 4054 (they stay on the map: test only, start the level again afterwards).</item>
    /// <item>P3 the damage smoke and fire (K393, View/FxV3/OpenZDamageStates) read a share of the NEW maximum: 2100 HP no smoke (52 %),
    /// 1800 HP smoke (44 %; on the old 3378 ship it would have been none), 1000 HP fire (25 %; old: smoke), 1700 HP the fire out, smoke
    /// (42 %, past 40 %), full health clean.</item>
    /// <item>P4 the hover bar over the ship is as long as before (Zod's length for its maximum is under the shortest bar: both 26 u).</item>
    /// </list>
    /// Pictures K443_smoke_1800hp_70m (selected: the unit panel reads 45 %) and K443_fire_1000hp_70m (25 %). Output: <see cref="Folder"/>
    /// </summary>
    public sealed class OzShuttleHpProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_more_hp_k443");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_more_hp_k443");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleHpProof").AddComponent<OzShuttleHpProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; OpenZHud hud; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); hud = FindFirstObjectByType<OpenZHud>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleHpProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        void Hp(ZObject o, int hp) { if (o != null && !o.Destroyed) { o.SetHealth(Mathf.Clamp(hp, 1, o.MaxHealth)); W.UpdateObjectHealth(o); } }
        static int Percent(int hp, int max) => max > 0 ? Mathf.CeilToInt(hp / (float)max * 100) : 0;   // the unit panel's own rounding (UI/OpenZHud.cs)

        /// <summary>The testing panel's drop for `team` at the first of the points (px) where it finds room (the panel's own path).</summary>
        ZObject Drop(Team team, IEnumerable<Vector2Int> at)
        {
            foreach (var p in at) { var o = OzCheatShuttle.Spawn(g, hud, team, p); if (o != null) return o; }
            return null;
        }

        IEnumerator All()
        {
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the SHUTTLE box ON)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship || !OpenZDamageStates.I) { Log("the shuttle has no view, or no damage states"); fails++; Finish(); yield break; }
            OzProofHoldFire.Attach(gameObject, W, s, me, false, true, true, Log);                                      // the other teams hold their fire, the forts' big guns quiet
            int light = W.S.Unit("light")?.MaxHealth ?? ZWorld.ShuttleBaseHp, want = W.ShuttleMaxHp;
            Log($"K443 shuttle hit points proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, game time {W.Time:0.0} s; the rule {(ZWorld.ShuttleMoreHpOn ? "on" : "OFF")}, the light tank {light} HP x {ZWorld.ShuttleHpFactor:0.0#} = {want}");

            // P1 level 16's own ship
            Check($"P1 level 16's own ship #{s.Id}: {s.Health}/{s.MaxHealth} HP (wanted a maximum of {want} = the light tank's {light} x 1.2)", s.MaxHealth == want && want == 4054 && s.Health <= s.MaxHealth && s.Health > 0);

            // P2 the testing panel's drops, both teams and an empty one
            var fort = (ZObject)null; foreach (var o in W.Objects) if (o != null && o.IsFort && o.Owner != me && o.Owner != Team.Neutral && !o.Destroyed) { fort = o; break; }
            var near = new List<Vector2Int>(); foreach (int dx in new[] { 200, -200, 280, -280, 360, -360, 440, -440 }) foreach (int dy in new[] { 160, -160, 0, 320, -320 }) near.Add(new Vector2Int(s.CenterX + dx, s.CenterY + dy));
            var far = new List<Vector2Int>(); if (fort != null) foreach (int dx in new[] { 0, 260, -260, 400, -400 }) foreach (int dy in new[] { 300, -300, 380, -380, 460, -460 }) far.Add(new Vector2Int(fort.CenterX + dx, fort.CenterY + dy));
            var red = Drop(me, near); var empty = Drop(Team.Neutral, near); var blue = fort != null ? Drop(fort.Owner, far) : null;
            string Row(string n, ZObject o) => o != null ? $"{n} #{o.Id} {o.Owner} {o.Health}/{o.MaxHealth} HP" : $"{n} NOT DROPPED ({OzCheatShuttle.LastWhy})";
            Log("P2 drops: " + Row("red", red) + "; " + Row("empty", empty) + "; " + Row("blue (by the enemy fort)", blue));
            Check($"P2 the testing panel's red, empty and blue drops all start with {s.MaxHealth} HP, the level's own ship's", red != null && empty != null && blue != null
                  && red.MaxHealth == s.MaxHealth && empty.MaxHealth == s.MaxHealth && blue.MaxHealth == s.MaxHealth && red.Health == red.MaxHealth && empty.Health == empty.MaxHealth && blue.Health == blue.MaxHealth);

            // P3 the damage stages read a share of the new maximum
            input.Selection.Clear(); input.Selection.Add(s);
            Cam(ship.transform.position, 70f);
            yield return Wait(1.0f);
            var steps = new (int hp, int want, string pic)[] { (2100, 0, null), (1800, 1, "K443_smoke_1800hp_70m"), (1000, 2, "K443_fire_1000hp_70m"), (1700, 1, null), (s.MaxHealth, 0, null) };
            var row = new StringBuilder(); bool asWanted = true;
            foreach (var st in steps)
            {
                Hp(s, st.hp); yield return Wait(0.6f);
                int got = OpenZDamageStates.StageOf(s.Id);
                float k = st.hp / (float)s.MaxHealth, kOld = st.hp / (float)light;
                int old = kOld <= OpenZDamageStates.BurnAt ? 2 : kOld <= OpenZDamageStates.SmokeAt ? 1 : 0;
                row.Append($" {st.hp} HP = {k * 100f:0.0} % (panel {Percent(st.hp, s.MaxHealth)} %): stage {got}, wanted {st.want} (on the old {light} ship {kOld * 100f:0.0} %, about {old});");
                asWanted &= got == st.want;
                if (st.pic != null) { Cam(ship.transform.position, 70f); yield return Wait(0.8f); Shot(st.pic); yield return Wait(0.3f); }
            }
            Log("P3 stages:" + row);
            Check($"P3 the smoke and fire read a share of the new maximum (smoke at {OpenZDamageStates.SmokeAt * 100f:0} % = {Mathf.FloorToInt(s.MaxHealth * OpenZDamageStates.SmokeAt)} HP, fire at {OpenZDamageStates.BurnAt * 100f:0} % = {Mathf.FloorToInt(s.MaxHealth * OpenZDamageStates.BurnAt)} HP, out past {OpenZDamageStates.OutAt * 100f:0} % = {Mathf.FloorToInt(s.MaxHealth * OpenZDamageStates.OutAt)} HP): every step as wanted", asWanted);

            // P4 the hover bar's length (UI/OpenZUnitOverlay.cs: Zod's 64 u per 10000 HP, between its shortest and longest)
            var ov = FindFirstObjectByType<OpenZUnitOverlay>();
            if (ov)
            {
                float was = Mathf.Clamp(ov.barPer10k * light / (float)ZConst.MAX_UNIT_HEALTH, ov.minBar, ov.maxBar), now = Mathf.Clamp(ov.barPer10k * s.MaxHealth / (float)ZConst.MAX_UNIT_HEALTH, ov.minBar, ov.maxBar);
                Check($"P4 the hover bar over the ship: {now:0.0} u long ({light} HP: {was:0.0} u; Zod's {ov.barPer10k:0} u per 10000 HP, shortest {ov.minBar:0} u)", Mathf.Abs(now - was) < 0.01f);
            }
            else Check("P4 the hover bar: no unit overlay found", false);
            Finish();
        }

        void Finish()
        {
            if (s != null && !s.Destroyed) Hp(s, s.MaxHealth);
            Log($"K443 shuttle hit points proof: {passes} PASS, {fails} FAIL (expected 4 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "shuttle_more_hp_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleHpProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
