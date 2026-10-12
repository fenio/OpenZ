using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    /// <summary>electrobot_v1: OpenZ > FX > Electro FX menus (the look of the tesla bolt, the stun and the Electrobot's idle
    /// electricity; OpenZElectroFx / ElectrobotView). Play mode for Show / Report.</summary>
    public static class OpenZElectroFxMenu
    {
        const string Root = "OpenZ/FX/Electro FX/";

        [MenuItem(Root + "Toggle A-B (OpenZElectroFx.Enabled)")]
        static void Toggle()
        {
            OpenZElectroFx.Enabled = !OpenZElectroFx.Enabled;
            Debug.Log($"[OpenZElectroFx] Enabled = {OpenZElectroFx.Enabled} (off = no tesla FX at all; the sim is unchanged)");
        }

        /// <summary>From the selected unit (or the first Electrobot, else the first robot) onto the nearest unit of another team: 3 full
        /// cycles (windup, bolt, stun FX, recharge). View only: no sim event, no damage, no real stun (the FX run as a 15 s jolt).</summary>
        [MenuItem(Root + "Show (Play): 3 bolts onto the nearest enemy")]
        static void Show()
        {
            if (!Application.isPlaying || !ZGame.I) { Debug.LogWarning("[OpenZElectroFx] Show: enter Play mode on a level first"); return; }
            if (!OpenZElectroFx.I) new GameObject("OpenZElectroFx").AddComponent<OpenZElectroFx>();
            UnitView from = Selection.activeGameObject ? Selection.activeGameObject.GetComponentInParent<UnitView>() : null;
            if (!from) foreach (var v in ZGame.I.Views.Values) if (v && v.Obj != null && v.Obj.Kind == ZKind.Robot && v.Obj.Type == ZElectro.Type) { from = v; break; }
            if (!from) foreach (var v in ZGame.I.Views.Values) if (v && v.Obj != null && v.Obj.Kind == ZKind.Robot && !v.Dying) { from = v; break; }
            if (!from) { Debug.LogWarning("[OpenZElectroFx] Show: no unit to fire from"); return; }
            UnitView to = null; float best = float.MaxValue;
            foreach (var v in ZGame.I.Views.Values)
            {
                if (!v || v == from || v.Obj == null || v.Dying || v.Obj.Owner == from.Obj.Owner || v.Obj.Owner == Team.Neutral) continue;
                float d = (v.transform.position - from.transform.position).sqrMagnitude;
                if (d < best) { best = d; to = v; }
            }
            if (!to) { Debug.LogWarning("[OpenZElectroFx] Show: no enemy unit found"); return; }
            OpenZElectroFx.I.DemoCycle(from, to, 3);
            var rts = Object.FindFirstObjectByType<RtsCamera>(); if (rts) rts.FlyTo((from.transform.position + to.transform.position) * 0.5f);
            Debug.Log($"[OpenZElectroFx] Show: {from.Obj.Type} #{from.Obj.Id} -> {to.Obj.Type} #{to.Obj.Id}, {Mathf.Sqrt(best):F1} m");
        }

        [MenuItem(Root + "Report (Play)")]
        static void Report()
        {
            if (!OpenZElectroFx.I) { Debug.Log("[OpenZElectroFx] not running (enter Play on a level)"); return; }
            Debug.Log(OpenZElectroFx.I.Report());
            foreach (var ev in OpenZElectroFx.I.Electrobots)
                if (ev) Debug.Log($"[OpenZElectroFx] ElectrobotView #{ev.Id}: bound {ev.Bound}, sockets found {ev.SocketsFound} (of 16 named: ElectroMuzzle, ChargeBall, RingCentre, CellsCentre, BarrelSpin, Gauge_Needle, CoilTop_L / R, Ring_0..7), glow slots {ev.GlowSlots} (want 4+)");
        }
    }
}
