using System.Collections;
using System.Reflection;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// clips, none celebrate), so the cheer is built from its jump clips: 2-4 hops (jump_takeoff -> jump_air -> landing, the
    /// root lifted on a parabola), a full twirl on one hop, and between hops the rifle types (grunt, psycho, sniper) raise the gun
    /// delay, hop count and height (seeded by its id), so a squad never cheers in lockstep. It runs after UnitView (order 200):
    /// UnitView writes the pose every frame, the cheer adds the hop and twirl on top and moves the team-colour band with the
    /// body. It ends early (hand-back to UnitView's own animation state) when the robot moves, shoots or dies.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class OpenZCheer : MonoBehaviour
    {
        public static float HopHeightMin = 0.9f, HopHeightMax = 1.4f, HopTime = 0.62f, SaluteShotsMin = 3, SaluteShotsMax = 5;

        UnitView view; Animator anim; SkinnedMeshRenderer skin; MaterialPropertyBlock mpb;
        bool shooter, running; int seed; string now;
        float lift, twirl;
        static FieldInfo stateField;
        static readonly int AccentLoId = Shader.PropertyToID("_AccentLo"), AccentHiId = Shader.PropertyToID("_AccentHi");

        public void Begin(UnitView v, bool fireIntoSky, int id)
        {
            if (running) return;
            view = v; seed = id; shooter = fireIntoSky;
            anim = GetComponentInChildren<Animator>();
            skin = GetComponentInChildren<SkinnedMeshRenderer>();
            running = true;
            StartCoroutine(Run());
        }

        bool Valid => view && view.Obj != null && !view.Dying && !view.Obj.Destroyed && !view.Obj.IsMoving
                      && (view.Obj.AttackObject == null || view.Obj.AttackObject.Destroyed);

        float R(int k) { float x = Mathf.Sin((seed + 1) * 12.9898f + k * 78.233f) * 43758.5453f; return x - Mathf.Floor(x); }

        IEnumerator Run()
        {
            yield return new WaitForSeconds(R(1) * 0.6f);
            int hops = 2 + (int)(R(2) * 2.99f);
            int twirlHop = (int)(R(3) * hops);
            string type = view && view.Obj != null ? view.Obj.Type : "";
            bool rifle = type == "grunt" || type == "psycho" || type == "sniper";
            for (int i = 0; i < hops && Valid; i++)
            {
                float h = Mathf.Lerp(HopHeightMin, HopHeightMax, R(10 + i)), dur = HopTime * (0.9f + 0.2f * R(20 + i));
                Play("jump_takeoff", 0.06f);
                float t = 0f;
                while (t < dur && Valid)
                {
                    t += Time.deltaTime; float k = Mathf.Clamp01(t / dur);
                    lift = 4f * h * k * (1f - k);
                    twirl = i == twirlHop ? 360f * Mathf.SmoothStep(0f, 1f, k) : 0f;
                    if (k > 0.18f && k < 0.8f) Play("jump_air", 0.08f);
                    else if (k >= 0.8f) Play("landing", 0.06f);
                    yield return null;
                }
                lift = 0f; twirl = 0f;
                Play("idle", 0.15f);
                yield return new WaitForSeconds(0.12f + 0.2f * R(30 + i));
                if (shooter && rifle && i < hops - 1 && Valid) yield return Salute(type);
            }
            Finish();
        }

        IEnumerator Salute(string type)
        {
            Play("aim_up", 0.12f);
            yield return new WaitForSeconds(0.18f);
            int shots = Mathf.RoundToInt(Mathf.Lerp(SaluteShotsMin, SaluteShotsMax, R(40)));
            var w = type == "psycho" ? FxWeapon.MachineGun : type == "sniper" ? FxWeapon.Sniper : FxWeapon.Rifle;
            for (int s = 0; s < shots && Valid; s++)
            {
                var fx = OpenZFxPlayer.I;
                var from = view.MuzzlePos();
                var dir = (Vector3.up + new Vector3(R(50 + s) - 0.5f, 0f, R(60 + s) - 0.5f) * 0.35f).normalized;
                if (fx)
                {
                    fx.Muzzle(from, dir, w);
                    float speed = Mathf.Max(OpenZFxCatalog.TracerMinSpeed, 90f), dist = 30f, life = dist / speed;
                    fx.EmitChannel("leg_tracer", from + dir * 0.4f, dir * speed, life, OpenZFxCatalog.TracerWidth, OpenZFxCatalog.TracerColor, OpenZFxCatalog.TracerAlpha);
                    fx.EmitChannel("leg_tracer_sleeve", from + dir * 0.4f, dir * speed, life, OpenZFxCatalog.TracerSleeveWidth, OpenZFxCatalog.TracerSleeveColor, OpenZFxCatalog.TracerSleeveAlpha);
                }
                CombatAudio.I?.Play("rifle", from, 0.32f, 0.05f, seed, LegacyMatch.BankRifleSingle);
                yield return new WaitForSeconds(w == FxWeapon.MachineGun ? 0.09f : 0.16f + 0.08f * R(70 + s));
            }
            yield return new WaitForSeconds(0.15f);
            Play("idle", 0.2f);
        }

        void Play(string clip, float fade)
        {
            if (!anim || !anim.isActiveAndEnabled || clip == now) return;
            string c = Resolve(clip);
            if (c != null) anim.CrossFadeInFixedTime(c, fade, 0);
            now = clip;
        }

        string Resolve(string want)
        {
            string[] chain;
            bool tough = view && view.Obj != null && view.Obj.Type == "tough";
            switch (want)
            {
                case "jump_takeoff": chain = new[] { "jump_takeoff", "jump_air" }; break;
                case "jump_air": chain = new[] { "jump_air", "jump_takeoff" }; break;
                case "landing": chain = new[] { "landing", tough ? "bazooka_idle" : "idle" }; break;
                case "aim_up": chain = new[] { "aim_up", "shooting" }; break;
                case "idle": chain = tough ? new[] { "bazooka_idle", "idle" } : new[] { "idle" }; break;
                default: chain = new[] { want }; break;
            }
            foreach (var c in chain)
                if (anim.HasState(0, Animator.StringToHash(c)) || anim.HasState(0, Animator.StringToHash("Base Layer." + c))) return c;
            return null;
        }

        void Finish()
        {
            running = false; lift = 0f; twirl = 0f;
            if (!anim || !view) return;
            stateField ??= typeof(UnitView).GetField("state", BindingFlags.NonPublic | BindingFlags.Instance);
            string back = stateField != null ? stateField.GetValue(view) as string : null;   // UnitView only re-issues on a change: hand its state back
            if (string.IsNullOrEmpty(back)) back = Resolve("idle");
            if (back != null && (anim.HasState(0, Animator.StringToHash(back)) || anim.HasState(0, Animator.StringToHash("Base Layer." + back))))
                anim.CrossFadeInFixedTime(back, 0.25f, 0);
            now = null;
        }

        void LateUpdate()
        {
            if (!running) return;
            if (!Valid) { StopAllCoroutines(); Finish(); return; }
            if (twirl != 0f) transform.rotation = Quaternion.AngleAxis(twirl, Vector3.up) * transform.rotation;
            if (lift > 0f)
            {
                transform.position += Vector3.up * lift;
                if (skin)
                {   // UnitView set the team band from the ground-level root this frame: lift it with the body
                    mpb ??= new MaterialPropertyBlock();
                    skin.GetPropertyBlock(mpb, 0);
                    float lo = mpb.GetFloat(AccentLoId), hi = mpb.GetFloat(AccentHiId);
                    if (hi > lo) { mpb.SetFloat(AccentLoId, lo + lift); mpb.SetFloat(AccentHiId, hi + lift); skin.SetPropertyBlock(mpb, 0); }
                }
            }
        }

        void OnDisable() { if (running) { StopAllCoroutines(); Finish(); } }
    }
}
