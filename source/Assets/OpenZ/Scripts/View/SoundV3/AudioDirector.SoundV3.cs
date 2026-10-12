using System;
using System.Linq;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Called from AudioDirector.cs through small hunks (Update, OnSimEvent, PollInput, EnteredLines, BuildingDeath, UpdateWrecks ...).
    /// Everything plays through the director's own table events (openz_sound_events.json) and its voice channels, so the mix, limiters,
    /// in-view culling, chatter setting and radio discipline stay the ones every other sound uses.
    /// </summary>
    public partial class AudioDirector
    {
        // ------------------------------------------------------------------ frame + sim events
        void V3Tick()
        {
            if (!OzSoundV3.Enabled || game == null || game.World == null) return;
            float dt = Time.deltaTime;
            OzSoundV3.VehicleTick(game, dt);
            OzSoundV3.BuildingTick(this, game, dt);
        }

        float v3GrenadeWarnAt = -100f;
        void V3SimEvent(ZEvent e, ZObject a, ZObject b)
        {
            if (!OzSoundV3.Enabled) return;
            switch (e.type)
            {
                case ZEventType.UnitBuilt: if (a != null && b != null && b.Owner == Player) OzSoundV3.UnitReady(); break;
                case ZEventType.CannonStored: if (a != null && a.Owner == Player) OzSoundV3.UnitReady(); break;
                case ZEventType.MissileFired: if (e.flag && a != null && a.Owner != Player && a.Owner != Team.Neutral) V3GrenadeWarning(e); break;
                case ZEventType.Death: if (a != null && (a.Kind == ZKind.Vehicle || a.Kind == ZKind.Cannon)) OzSoundV3.SlewEnd(a); break;
            }
        }

        /// <summary>An enemy grenade flies at our robots (one of ours within 48 px of the landing point, on screen): Alan warns
        /// ("Grenade!", "Incoming pineapple!", "Scatter!" ...), 45 %, at most every 6 s, only when Alan is free.</summary>
        void V3GrenadeWarning(ZEvent e)
        {
            if (Time.time - v3GrenadeWarnAt < 6f || AlanBusy || UnityEngine.Random.value > 0.45f) return;
            var to = Px(e.x, e.y); if (!OnScreen(to)) return;
            bool near = false;
            foreach (var o in W.Objects)
                if (o.Kind == ZKind.Robot && o.Owner == Player && !o.Destroyed && Mathf.Abs(o.CenterX - e.x) <= 48 && Mathf.Abs(o.CenterY - e.y) <= 48) { near = true; break; }
            if (!near) return;
            v3GrenadeWarnAt = Time.time; Say("vo_grenade_warning");
        }

        // ------------------------------------------------------------------ deaths / captures / buildings
        /// <summary>BoardingView.CrewDies: the gunner / commander thrown out of a destroyed vehicle or gun gets a death sound (the robot death
        /// pools by blast: a cry or a voiceless body death, 40 %, through the 2-cries-per-second limiter) as the body flies.</summary>
        public static void CrewDeath(Vector3 p, int id)
        {
            if (!I || !OzSoundV3.Enabled || !DeathVoicesLive || UnityEngine.Random.value > 0.4f) return;
            I.Schedule(0.15f, () => { if (I) I.DeathSound(p, DeathVoices.Cause.Blast, id); });
        }
        /// <summary>EnteredLines: our robot took a gun: Rachel confirms half of the time after Alan's line (ann_gun_captured, new).</summary>
        void V3GunCaptured() { if (OzSoundV3.Enabled && UnityEngine.Random.value < 0.5f) Schedule(1.4f, () => Announce("ann_gun_captured")); }
        /// <summary>BuildingDeath: one of OUR factories / radars / repair stations is destroyed: Rachel says which (ann_building_destroyed, new).</summary>
        void V3BuildingDestroyed(ZObject o)
        {
            if (!OzSoundV3.Enabled || o == null || o.Owner != Player) return;
            string f = o.Type.Contains("factory") ? "factory" : o.Type == "radar" ? "radar" : o.Type == "repair" ? "repair" : "building";
            Schedule(1.0f, () => Announce("ann_building_destroyed", f));
        }
        /// <summary>UpdateWrecks: a fresh fire loop (a wreck or a burning building just caught fire) starts with a flare-up.</summary>
        void V3FireStart(int id, Vector3 p, float t)
        {
            if (!OzSoundV3.Enabled || Time.time - t > 3f) return;
            bool building = burning.ContainsKey(id);
            if (building || UnityEngine.Random.value < 0.5f) Schedule(building ? 0.2f : 0.6f, () => OzSoundV3.Ignite("fire:" + id, p, building ? 1f : 0.7f));
        }

        // ------------------------------------------------------------------ orders / selection (PollInput)
        /// <summary>HUD group buttons (OpenZHud.GroupClick): a mouse recall counts like the keyboard's (ui_group_recall).</summary>
        public static float MouseRecallAt = -10f;
        /// <summary>The order cue by what the order does (capture a flag, enter a vehicle / gun / the fort, pick up grenades, repair, exit crew);
        /// attack and plain moves keep ui_order_attack / ui_order_move.</summary>
        string V3OrderCue(bool attack)
        {
            if (!OzSoundV3.Enabled) return attack ? "ui_order_attack" : "ui_order_move";
            if (input && Mathf.Abs(PlayerInput.EjectedAt - input.MarkerTime) < 0.0001f) return "ui_order_exit";
            if (attack) return "ui_order_attack";
            if (input)
                foreach (var u in input.Selection)
                {
                    if (u == null || u.Waypoints.Count == 0) continue;
                    var wp = u.Waypoints[u.Waypoints.Count - 1];
                    switch (wp.mode)
                    {
                        case WpMode.Enter: case WpMode.EnterFort: return "ui_order_enter";
                        case WpMode.PickupGrenades: return "ui_order_pickup";
                        case WpMode.UnitRepair: case WpMode.CraneRepair: return "ui_order_repair";
                    }
                    if (wp.refId >= 0) { var t = W.GetAny(wp.refId); if (t != null && t.Type == "flag") return "ui_order_capture"; }
                }
            return "ui_order_move";
        }
        /// <summary>The EXIT CREW order: no move cue and no order acknowledgement ("On my way!"); OnEjected says the exit line.</summary>
        bool V3EjectOrder() => OzSoundV3.Enabled && input && Mathf.Abs(PlayerInput.EjectedAt - input.MarkerTime) < 0.0001f;
        /// <summary>The selection cue: a Shift+click that removed a unit = the deselect tick and no voice; double-click / Ctrl+A / R / V /
        /// C select-all = its own two-note chirp; a mouse recall = the recall cue.</summary>
        string V3SelectCue(bool recall, out bool voice)
        {
            voice = true;
            if (!OzSoundV3.Enabled) return recall ? "ui_group_recall" : "ui_select_unit";
            if (Mathf.Abs(PlayerInput.RemovedAt - input.SelectTime) < 0.0001f) { voice = false; return "ui_deselect"; }
            if (recall || Time.unscaledTime - MouseRecallAt < 0.3f) return "ui_group_recall";
            if (Mathf.Abs(PlayerInput.SelectAllAt - input.SelectTime) < 0.0001f && input.Selection.Count > 1) return "ui_select_all";
            return "ui_select_unit";
        }

        // ------------------------------------------------------------------ menus
        /// <summary>OzSoundV3.SliderPreview: the EFFECTS / VOICE slider's sample on its own bus, heard while the pause menu holds the
        /// listener (AudioListener.pause).</summary>
        public static void PreviewBus(string id)
        {
            if (!I && !EnsureLive()) return;
            I.lastStarted = null;
            if (I.Play(id, I.ListenerPos, -1) && I.lastStarted != null && I.lastStarted.src)
            { I.lastStarted.ignorePause = true; I.lastStarted.src.ignoreListenerPause = true; }
        }
    }
}
