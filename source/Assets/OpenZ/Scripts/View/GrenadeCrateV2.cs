using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// (the collector's grab, the crate pops on the grab frame). The view of a grenade crate map item, on Z_grenades:
    ///    riveted iron straps, handles, an open-slat lid showing six grenade.glb grenades in a rack), not code boxes;
    ///    halo pulsing .88 + sin(T .9) .12): it hovers HoverBase above its glow ring, bobs and spins, an amber halo blooms at night;
    ///  * collected: a pop-out (scale 1 -> 1.18 in 0.09 s, then to 0 in 0.24 s while it spins up and rises), an amber spark ring and a
    ///    for the collector's grab frame (UnitView.Grab), never before the sim's pickup;
    ///  * destroyed: the crate bursts into its 9 modelled break pieces (GrenadeCrate_Pieces.fbx, WreckPieces physics: thrown, tumble,
    ///    missile (ZWorld.DeathDebris: o.Grenades missiles, +-130 px, 3-4 s) from the crate's grenade sockets on the high ballistic arc to
    ///    that missile's x / y, landing exactly at its explodeTime, where the sim's own blast is drawn (grenade blast).
    /// View only: reads the sim (missile lists, events), never writes it.
    /// Crate/Use v2 | Revert to v1) sits on its block feet; collected, the crate stays put, the lid swings open to 105 deg in 0.30 s (ease
    /// out, sfx_crate_lid_open_1/2), the grenades inside lift 3.5 cm, and at 0.35 s (or the collector's grab frame, max 0.5 s) the ammo
    /// pickup sound + a scale punch 1 -> 1.12 -> 0 about the crate centre with a small dust / sparkle puff; never the burst on collect.
    /// </summary>
    [DisallowMultipleComponent]
    public class GrenadeCrateV2 : MonoBehaviour
    {
        public static bool Enabled = true;
        public static float HoverBase = 0.16f, BobAmp = 0.13f, BobRate = 1.5f, SpinRate = 0.9f;
        public static float HaloSize = 2.4f, HaloIntensity = 1.1f, HaloHeight = 0.75f;
        public static float PopUpT = 0.09f, PopDownT = 0.24f, PopPeak = 1.18f;
        /// <summary></summary>
        public static float PickupGain = 0.95f, BreakGain1 = 0.62f, BreakGain2 = 0.9f;
        /// <summary>Play check counters / log.</summary>
        public static int Pops, Bursts, GrenadesFlown, PiecesThrown, GrabbedPops;
        public static readonly List<string> Trace = new List<string>();

        /// <summary>
        /// On collect it settles (glides down onto its feet in V2SettleT while the spin eases out) as the lid
        /// opens, then pops as before. false = v2 sits still on its feet.</summary>
        public static bool V2Hover = true;
        public static float V2SettleT = 0.2f;
        Vector3 settlePos; Quaternion settleRot, popRot = Quaternion.identity; bool settling;
        public static float LidOpenDeg = 105f, LidOpenT = 0.30f, GrenLift = 0.035f, GrenLiftT0 = 0.05f, GrenLiftT = 0.25f, LidGain = 0.8f;
        public static float V2PopAt = 0.35f, V2PopMax = 0.50f, V2PopUpT = 0.05f, V2PopDownT = 0.12f, V2PopPeak = 1.12f, V2Centre = 0.54f, GlowFadeT = 0.15f;
        public static int LidOpens, V2Puffs; static int lidAlt;
        Transform lid, gren; Quaternion lidRot0; Vector3 grenPos0, grenLiftLocal; float lidSign = 1f, lidStart = -1f; bool puffDone; PickupGlow glow;
        public bool IsV2 => lid != null;

        Transform model, pieces; readonly List<Transform> sockets = new List<Transform>();
        Vector3 modelPos0, modelScale0; Quaternion modelRot0; float ph, popT, spinExtra; bool popping, gone, bound; int simId = -1;
        float popDue = -1f; bool picked;

        static void Note(string s) { Trace.Add($"{Time.time:F2} {s}"); if (Trace.Count > 200) Trace.RemoveAt(0); Debug.Log("[OpenZ Crate] " + s); }

        void Awake() { Bind(); }
        void OnEnable() { if (!bound) Bind(); }

        void Bind()
        {
            bound = true;
            model = transform.Find("Model"); pieces = transform.Find("Pieces");
            if (model) { modelPos0 = model.localPosition; modelRot0 = model.localRotation; modelScale0 = model.localScale; popRot = modelRot0; }
            sockets.Clear();
            if (model) foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("GRENADE_")) sockets.Add(t);
            sockets.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            ph = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.21f, 6.2831853f);
            lid = null; gren = null; glow = GetComponent<PickupGlow>();
            if (model) foreach (var t in model.GetComponentsInChildren<Transform>(true)) { if (t.name == "CRATE_lid") lid = t; else if (t.name == "CRATE_grenades") gren = t; }
            if (lid) { lidRot0 = lid.localRotation; lidSign = MeasureLidSign(); }
            if (gren) { grenPos0 = gren.localPosition; grenLiftLocal = gren.parent.InverseTransformVector(Vector3.up * GrenLift * model.lossyScale.y); }
        }

        /// <summary>The opening sign of the lid's local X turn (the FBX axis conversion can flip it): the sign that lifts the lid.</summary>
        float MeasureLidSign()
        {
            var rs = lid.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) return 1f;
            float up = LidY(rs, 30f), down = LidY(rs, -30f); lid.localRotation = lidRot0;
            return up >= down ? 1f : -1f;
        }
        float LidY(Renderer[] rs, float deg)
        {
            lid.localRotation = lidRot0 * Quaternion.AngleAxis(deg, Vector3.right);
            var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); return b.center.y;
        }

        void OpenLid()
        {
            if (!lid || lidStart >= 0f) return;
            lidStart = Time.time; LidOpens++;
            if (V2Hover && model) { settling = true; settlePos = model.localPosition; settleRot = model.localRotation; popRot = settleRot; }
            var au = CombatAudio.I; var c = model.position + Vector3.up * 0.8f;
            string clip = (lidAlt++ & 1) == 0 ? "sfx_crate_lid_open_1" : "sfx_crate_lid_open_2";
            if (au) au.Play("crate_lid", c, LidGain, 0f, SimId(), clip);
            Note("lid opens #" + SimId() + " (" + clip + ", sign " + lidSign.ToString("+0;-0") + ")");
        }

        void AnimateLid(float t)
        {
            float u = Mathf.Clamp01(t / LidOpenT), e = 1f - (1f - u) * (1f - u) * (1f - u);
            lid.localRotation = lidRot0 * Quaternion.AngleAxis(lidSign * LidOpenDeg * e, Vector3.right);
            if (gren) { float g = Mathf.Clamp01((t - GrenLiftT0) / GrenLiftT), ge = 1f - (1f - g) * (1f - g) * (1f - g); gren.localPosition = grenPos0 + grenLiftLocal * ge; }
        }

        /// <summary>The v2 pop: a scale punch 1 -> 1.12 (0.05 s, ease out) -> 0 (0.12 s, smoothstep) about the crate centre (not the ground
        /// pivot, so it pops in place), the glow ring fades over 0.15 s, a small dust / sparkle puff at 0.05 s.</summary>
        void V2Pop()
        {
            popT += Time.deltaTime;
            float s;
            if (popT < V2PopUpT) { float u = popT / V2PopUpT; s = Mathf.Lerp(1f, V2PopPeak, 1f - (1f - u) * (1f - u) * (1f - u)); }
            else { float u = Mathf.Clamp01((popT - V2PopUpT) / V2PopDownT); s = Mathf.Lerp(V2PopPeak, 0f, u * u * (3f - 2f * u)); }   // smoothstep: the crate visibly shrinks (u^3 read as a snap at 20 fps)
            float sc = Mathf.Max(0.001f, s), ws = (model.parent ? model.parent.lossyScale.y : 1f) * modelScale0.y;
            model.localScale = modelScale0 * sc;
            model.localPosition = modelPos0 + transform.InverseTransformVector(Vector3.up * (V2Centre * ws * (1f - sc)));
            model.localRotation = popRot;
            if (glow) glow.fade = 1f - Mathf.Clamp01(popT / GlowFadeT);
            if (!puffDone && popT >= V2PopUpT) { puffDone = true; V2Puff(transform.position + Vector3.up * (V2Centre * ws)); }
            if (popT >= V2PopUpT + V2PopDownT) { gone = true; gameObject.SetActive(false); }
        }

        void V2Puff(Vector3 c)
        {
            var fx = OpenZFxPlayer.I; if (!fx || !fx.isActiveAndEnabled) return;
            V2Puffs++;
            int nd = Random.Range(8, 13);
            for (int i = 0; i < nd; i++)
            {   // olive-tan dust motes (the crate's wood dust), about 0.6 m out
                var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y) * 0.6f; d.Normalize();
                fx.EmitChannel("m12_dust", c + d * Random.Range(0.12f, 0.35f), d * Random.Range(0.8f, 1.4f) + Vector3.up * 0.25f,
                    Random.Range(0.32f, 0.45f), Random.Range(0.22f, 0.34f), new Color(0.74f, 0.66f, 0.46f), 0.45f);
            }
            int ns = Random.Range(4, 7);
            for (int i = 0; i < ns; i++)
            {   // a few yellow-white sparkles
                var d = Random.onUnitSphere; d.y = Mathf.Abs(d.y);
                fx.EmitChannel("leg_spark", c + d * 0.15f, d * Random.Range(1.1f, 1.7f), Random.Range(0.3f, 0.42f), Random.Range(0.04f, 0.06f), new Color(1f, 0.95f, 0.7f), 1f);
            }
        }

        int SimId()
        {
            if (simId >= 0 || ZGame.I == null) return simId;
            foreach (var kv in ZGame.I.StaticViews) if (kv.Value == gameObject) { simId = kv.Key; break; }
            return simId;
        }

        void LateUpdate()
        {
            if (!model || gone) return;
            float T = Time.time;
            if (popDue >= 0f && T >= popDue) { popDue = -1f; StartPop(); }
            if (lid && lidStart >= 0f) AnimateLid(T - lidStart);
            if (popping && IsV2) { V2Pop(); return; }
            if (!popping && IsV2 && (!V2Hover || lidStart >= 0f))
            {   // v2 sits on its block feet; the halo pulses over it (the crate stays put while the lid opens)
                if (lidStart < 0f) { model.localPosition = modelPos0; model.localRotation = modelRot0; }
                else if (settling)
                {
                    float st = Mathf.Min(T - lidStart, V2SettleT), u = st / Mathf.Max(1e-3f, V2SettleT), e = 1f - (1f - u) * (1f - u) * (1f - u);
                    model.localPosition = Vector3.Lerp(settlePos, modelPos0, e);
                    float extra = SpinRate * Mathf.Rad2Deg * (st - st * st / (2f * Mathf.Max(1e-3f, V2SettleT)));
                    model.localRotation = popRot = Quaternion.Euler(0f, extra, 0f) * settleRot;
                    if (u >= 1f) settling = false;
                }
                var fx2 = OpenZFxPlayer.I;
                if (Enabled && fx2 && fx2.isActiveAndEnabled && lidStart < 0f)
                {
                    float k = 0.88f + Mathf.Sin(T * 0.9f + ph) * 0.12f;
                    fx2.DrawHalo(model.position + Vector3.up * HaloHeight, HaloSize, new Color(1f, 0.66f, 0.26f), HaloIntensity * k * NightBoost());
                }
                return;
            }
            if (!popping)
            {
                float h = Enabled ? HoverBase + BobAmp * Mathf.Sin(T * BobRate + ph) : 0f;
                model.localPosition = modelPos0 + transform.InverseTransformVector(Vector3.up * h);
                model.localRotation = Quaternion.Euler(0f, Enabled ? (T * SpinRate + ph) * Mathf.Rad2Deg : 0f, 0f) * modelRot0;
                var fx = OpenZFxPlayer.I;
                if (Enabled && fx && fx.isActiveAndEnabled)
                {
                    float k = 0.88f + Mathf.Sin(T * 0.9f + ph) * 0.12f;
                    fx.DrawHalo(model.position + Vector3.up * HaloHeight, HaloSize, new Color(1f, 0.66f, 0.26f), HaloIntensity * k * NightBoost());
                }
                return;
            }
            popT += Time.deltaTime;
            float s, rise;
            if (popT < PopUpT) { float u = popT / PopUpT; s = Mathf.Lerp(1f, PopPeak, 1f - (1f - u) * (1f - u) * (1f - u)); rise = 0.12f * u; }
            else { float u = Mathf.Clamp01((popT - PopUpT) / PopDownT); s = Mathf.Lerp(PopPeak, 0f, u * u * u); rise = 0.12f + 0.4f * u; }
            spinExtra += Time.deltaTime * (6f + 14f * Mathf.Clamp01(popT / (PopUpT + PopDownT)));
            model.localScale = modelScale0 * Mathf.Max(0.001f, s);
            model.localPosition = modelPos0 + transform.InverseTransformVector(Vector3.up * (HoverBase + rise));
            model.localRotation = Quaternion.Euler(0f, (T * SpinRate + ph + spinExtra) * Mathf.Rad2Deg, 0f) * modelRot0;
            if (popT >= PopUpT + PopDownT) { gone = true; gameObject.SetActive(false); }
        }

        static float NightBoost()
        {
            var sun = RenderSettings.sun;
            if (!sun) return 1f;
            float up = -sun.transform.forward.y;                                                   // > 0 = sun above the horizon
            return Mathf.Lerp(1.9f, 1f, Mathf.Clamp01(up * 4f));                                    // the halo reads at night (bloom)
        }

        // ------------------------------------------------------------------ pickup
        /// <summary>CombatView GrenadesPicked. True = handled (the crate pops itself and hides at the end; the caller must not hide it).</summary>
        public static bool Picked(ZGame game, ZEvent e, ZObject collector, CombatAudio au)
        {
            if (!Enabled || game == null || !game.StaticViews.TryGetValue(e.otherId, out var go) || !go || !go.activeInHierarchy) return false;
            var cv = go.GetComponent<GrenadeCrateV2>(); if (!cv || !cv.model || cv.gone || cv.popping) return false;
            cv.simId = e.otherId; cv.picked = true;
            if (cv.IsV2) cv.OpenLid();
            float wait = 0f;
            UnitView uv = null;
            if (collector != null && game.Views.TryGetValue(collector.Id, out uv) && uv)
            {
                float left = uv.GrabFrameIn(e.otherId);
                if (left < 0f) left = uv.StartGrab(cv.HandleFacing(uv.transform.position), cv.LidPoint, e.otherId);
                if (left > 0f) { wait = Mathf.Min(left, 0.45f); GrabbedPops++; }
            }
            Note($"picked #{e.otherId} by {(collector != null ? collector.Type + " #" + collector.Id + " " + collector.Owner : "?")}, grab frame in {wait:F2} s");
            if (cv.IsV2) { cv.popDue = Time.time + Mathf.Clamp(wait, V2PopAt, V2PopMax); Note("v2: pop in " + Mathf.Clamp(wait, V2PopAt, V2PopMax).ToString("F2") + " s"); }   // v2: the pop waits for the open lid (0.35 s) or the grab frame
            else if (wait > 0.01f) cv.popDue = Time.time + wait; else cv.StartPop();
            return true;
        }

        /// <summary>The handle on the side facing `from` (world): where the free hand closes (HANDLE_L / HANDLE_R; null = none).</summary>
        public Transform HandleFacing(Vector3 from)
        {
            Transform best = null; float bd = float.MaxValue;
            if (model) foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("HANDLE_")) { float d = (t.position - from).sqrMagnitude; if (d < bd) { bd = d; best = t; } }
            return best;
        }
        public Vector3 LidPoint => (model ? model.position : transform.position) + Vector3.up * 0.6f;

        void StartPop()
        {
            if (popping || gone) return;
            if (IsV2)
            {   // v2: the ammo pickup sound on the grab frame; the punch + puff run in V2Pop (no spark ring, no flash light, no shake)
                popping = true; popT = 0f; Pops++; puffDone = false;
                var c2 = transform.position + Vector3.up * V2Centre;
                var au2 = CombatAudio.I;
                if (au2) au2.Play("crate_pickup", c2, PickupGain, 0f, SimId(), "sfx_ammo_pickup");
                Note("pop #" + SimId() + " (v2), lid open " + (lidStart >= 0f ? Time.time - lidStart : -1f).ToString("F2") + " s");
                return;
            }
            popping = true; popT = 0f; Pops++;
            var c = model.position + Vector3.up * 0.6f;
            var fx = OpenZFxPlayer.I;
            if (fx && fx.isActiveAndEnabled)
                for (int i = 0; i < 20; i++)
                {   // the pickup spark ring: amber sparks fanning out flat from the crate, a few up
                    float a = i / 20f * 6.2831853f; var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    fx.EmitChannel("leg_spark", c + d * 0.45f, d * Random.Range(2.4f, 3.6f) + Vector3.up * Random.Range(0.4f, 1.8f),
                        Random.Range(0.28f, 0.5f), Random.Range(0.05f, 0.085f), new Color(1f, 0.8f, 0.4f), 1f);
                }
            FxManager.I?.AddLight(c, new Color(1f, 0.72f, 0.36f), 1.4f, 4.5f, 0.3f);
            var au = CombatAudio.I;
            if (au) au.Play("crate_pickup", c, PickupGain, 0f, SimId(), "sfx_ammo_pickup");
            Note($"pop #{SimId()} at {c}");
        }

        // ------------------------------------------------------------------ burst
        /// <summary>CombatView Death of a grenade crate: 0 = not this view (old path), 1 = collected (Zod deletes a picked-up box the same
        /// tick: the pop-out owns it, no blast, no pieces), 2 = destroyed: pieces, one flying grenade per sim debris missile, the break
        /// sound (the caller adds LegacyMatch.CrateDeath's small blast).</summary>
        public static int OnDeath(ZGame game, ZObject o, CombatAudio au)
        {
            if (!Enabled || game == null || o == null || !game.StaticViews.TryGetValue(o.Id, out var go) || !go) return 0;
            var cv = go.GetComponent<GrenadeCrateV2>(); if (!cv || !cv.model) return 0;
            cv.simId = o.Id;
            if (cv.picked || cv.popping || cv.popDue >= 0f) { PickDeaths++; return 1; }
            cv.DoBurst(game, o, au);
            return 2;
        }
        public static int PickDeaths;

        void DoBurst(ZGame game, ZObject o, CombatAudio au)
        {
            Bursts++;
            var centre = model.gameObject.activeInHierarchy ? model.position : transform.position;
            bool visible = gameObject.activeInHierarchy && !gone;
            // 1) the grenades: one per sim debris missile of this crate, from the in-crate grenades' sockets, landing on the sim's blast
            int k = 0; var W = game.World;
            if (W != null)
                foreach (var list in new[] { W.QueuedMissiles, W.PendingMissiles })
                    foreach (var m in list)
                    {
                        if (!m.debris || m.attackerId != o.Id) continue;
                        Vector3 from = sockets.Count > 0 ? sockets[k % sockets.Count].position : centre + Vector3.up * 0.8f;
                        if (!visible) from = centre + Vector3.up * 0.8f;
                        var to = ZGame.PxToWorld(m.x, m.y); to.y = game.GroundY(to.x, to.z);
                        float flight = Mathf.Max(0.3f, (float)(m.explodeTime - W.Time));
                        if (GrenadeFx.Throw(null, from, to, flight)) { GrenadesFlown++; k++; }
                        Note($"burst #{o.Id}: grenade {k} -> sim missile ({m.x},{m.y}) = {to} in {flight:F2} s sim");
                    }
            // 2) the crate breaks into its modelled pieces
            int n = 0;
            if (visible && pieces)
            {
                pieces.SetPositionAndRotation(model.position, model.rotation);
                pieces.localScale = model.localScale;
                pieces.gameObject.SetActive(true);
                var parts = new List<Transform>(); foreach (Transform t in pieces) parts.Add(t);
                foreach (var t in parts)
                    if (WreckPieces.Single(t, centre - Vector3.up * 0.25f, 1f, o.Id * 37 + n)) n++;
                PiecesThrown += n;
            }
            model.gameObject.SetActive(false);
            if (au) { bool one = (o.Id & 1) == 0; au.Play("crate_break", centre, one ? BreakGain1 : BreakGain2, 0f, o.Id, one ? "sfx_crate_break_1" : "sfx_crate_break_2"); }
            Note($"burst #{o.Id}: {k} grenades flying (o.Grenades before death {o.Grenades}), {n} pieces, visible {visible}");
            gone = true;
            gameObject.SetActive(false);
        }

        /// <summary>Every frame: a leader robot that can pick up grenades and walks toward this crate starts its scoop so that the
        /// grab frame lands about when the sim picks the crate up (its centre inside the crate's box). View only.</summary>
        public static float ApproachLead = 0.12f;
        void Update()
        {
            if (!Enabled || gone || popping || popDue >= 0f || !UnitView.GrabEnabled) return;
            var g = ZGame.I; if (g == null || g.World == null) return;
            int id = SimId(); if (id < 0) return;
            var crate = g.World.GetAny(id); if (crate == null || crate.Destroyed || crate.Grenades <= 0) return;
            var c = g.CenterWorld(crate);
            foreach (var o in g.World.Objects)
            {
                if (o.Kind != ZKind.Robot || o.IsMinion || o.Destroyed || o.KillAt >= 0 || !o.IsMoving || !o.CanPickupGrenades()) continue;
                if (!g.Views.TryGetValue(o.Id, out var uv) || !uv || !uv.CanGrab || uv.Grabbing) continue;
                var p = uv.transform.position; var d = new Vector3(c.x - p.x, 0f, c.z - p.z);
                float dist = d.magnitude; if (dist > 3.2f) continue;
                var v = new Vector3(o.Dx, 0f, -o.Dy) / 8f; float sp = v.magnitude; if (sp < 0.2f) continue;
                if (Vector3.Dot(v / sp, d / Mathf.Max(0.01f, dist)) < 0.8f) continue;                   // heading at the crate
                float eta = Mathf.Max(0f, dist - 1.0f) / sp;                                           // the sim picks up ~1 m out (centre in the 2 m box)
                if (eta > uv.GrabLead(true) + ApproachLead) continue;
                uv.StartGrab(HandleFacing(p), LidPoint, id);
            }
        }
    }
}
