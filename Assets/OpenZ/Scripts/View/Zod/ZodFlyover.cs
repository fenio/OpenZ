// OpenZ (GPL-3.0) - Commander Zod briefing: the slow map fly-over. Owns the game camera while RtsCamera is disabled and flies
// between named shots that zod_beats.json anchors on the words of the briefing ("Welcome to the desert" -> establish, "flag"
// -> the nearest neutral flag, "bridges" -> a bridge, "blue fort" -> the enemy fort, "gears" -> home). Every shot is an orbit
// (target on the ground, distance, pitch, yaw + a slow yaw drift) and the camera eases to it with SmoothDamp on each
// parameter, so any timing gives smooth, eased moves. "home" is exactly the RtsCamera pose the level starts with: the hand-back
// to the player has no jump. The listener rig and the height-based audio / LOD rules follow through rts.focus / rts.height.
// v2: the briefing is about 8 s now (shots every 1-1.5 s), so the flight may move faster (180 m/s, zoom 240 m/s).
// level's SHOT LIST now (ZodFlyover.Cine.cs: whole map, sunrise, enemy fort, our base easing into the RTS camera; one slow move per
// shot, hard cuts or a short dip, the cuts on Zod's line breaks). The orbit flight below stays for UseShotList = false.
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class ZodFlyover                                                            // cinematic briefing: + ZodFlyover.Cine.cs
    {
        // target, and so rts.focus / the listener, on the map); slide = a dolly velocity (m/s) of the whole shot; low = a horizon shot;
        // sun = the shot faces the sunrise and keeps the rising sun disc in the frame (tilt follows the sun elevation)
        struct Shot { public Vector3 target; public float dist, pitch, yaw, drift, tilt; public Vector3 slide; public bool low, sun; }

        readonly RtsCamera rts; readonly ZGame game; readonly Transform camT;
        readonly Dictionary<string, Shot> shots = new Dictionary<string, Shot>();
        Vector3 homeFocus, homePos; Quaternion homeRot; float homeHeight;
        Vector3 target, targetVel; float dist, distVel, pitch, pitchVel, yaw, yawVel;
        Shot goal; float smooth = 1.2f, goalYawDrift; string goalName;
        public bool Active { get; private set; }
        public string Current => cine != null ? CineCurrent : goalName;                                   // cinematic briefing: the shot on screen
        /// <summary>The flyover that owns the camera now (FlybySky reads it), null when none.</summary>
        public static ZodFlyover Live { get; private set; }
        /// <summary>FlybySky: the painted sky is wanted (a briefing shot, not the hand-back to "home"); it fades out over ~1 s at "home".</summary>
        public bool SkyWanted => Active && camT && (cine != null ? CineSkyWanted : goalName != "home");   // camT gone = its level was unloaded mid-briefing; shot list: until the last shot
        /// <summary>ZodBriefing: Zod's voice is playing (the dip whoosh is ducked under it).</summary>
        public System.Func<bool> VoiceOn;
        /// <summary>Low horizon shots: camera height over the ground (m, the 28 m cliff floor still applies), view pitch under the horizon
        /// (deg), distance to the on-map aim point (m), map-edge margin (m), the sun's offset from the frame centre (deg, + = sun on the
        /// right), dolly speed (m/s), and the dip whoosh gain while Zod speaks. Statics: tune in Play, no rebuild.</summary>
        public static float LowHeight = 30f, LowViewPitch = 2.5f, LowAim = 100f, LowMargin = 6f, LowSunOffsetDeg = 7f, LowSlide = 2.5f, DipDuck = 0.75f;
        /// <summary>Sun-facing low shots: the sun disc is held at SunFrameK of the way from the frame centre to the top edge (the sun climbs
        /// ~0.5 deg/s after the 0.915 sunrise start, 9 -> 16 deg during a briefing with the GENERAL joke); the view may look up to LowMaxUp
        /// deg over the horizon for that.</summary>
        public static float SunFrameK = 0.72f, LowMaxUp = 4f;
        float tilt, tiltVel, lowK, lowVel; Vector3 goalSlide; Vector2 mapSize; static bool dipWarned; Camera camC;

        public ZodFlyover(RtsCamera rts, ZGame game)
        {
            this.rts = rts; this.game = game; camT = rts ? rts.transform : Camera.main ? Camera.main.transform : null;
        }

        /// <summary>Takes the camera: records the RtsCamera home pose, resolves the level's shots, cuts to `first`.</summary>
        public bool Begin(string first = "overview")
        {
            if (!rts || !camT || game == null || game.World == null) return false;
            rts.Place();                                                                              // the pose the level starts with
            homeFocus = rts.focus; homeHeight = rts.height; homePos = camT.position; homeRot = camT.rotation;
            rts.enabled = false;
            ResolveShots();
            var s = shots.TryGetValue(first, out var f) ? f : shots["home"];
            target = s.target; dist = s.dist; pitch = s.pitch; yaw = s.yaw;
            targetVel = Vector3.zero; distVel = pitchVel = yawVel = 0f;
            goal = s; goalName = first; goalYawDrift = 0f;
            tilt = s.tilt; tiltVel = 0f; lowK = s.low ? 1f : 0f; lowVel = 0f; goalSlide = Vector3.zero;
            Active = true;
            Live = this; FlybySky.Ensure();
            if (CineBegin()) return true;                                                             // cinematic briefing: the level's shot list
            Apply();
            return true;
        }

        public bool Has(string shot) => shots.ContainsKey(shot);

        public void SetShot(string name, float smoothTime = 1.2f)
        {
            if (cine != null) return;                                                                 // cinematic briefing: the json beat shots are superseded (HandBack lands it)
            if (!Active || !shots.TryGetValue(name, out var s)) return;
            if (s.low && !goal.low) PlayDip();
            goal = s; goalName = name; smooth = Mathf.Max(0.2f, smoothTime); goalYawDrift = 0f; goalSlide = Vector3.zero;
        }

        /// <summary>Distance (m) and angle (deg) between the camera and the home pose.</summary>
        public float HomeError => CineHomeBusy ? 99f : camT ? Vector3.Distance(camT.position, homePos) + Quaternion.Angle(camT.rotation, homeRot) * 0.1f : 0f;   // cinematic briefing: 99 until the hand-back is done

        public void Tick(float dt)
        {
            if (!Active || dt <= 0f) return;
            if (cine != null) { CineTick(dt); return; }                                               // cinematic briefing
            if (goalName != "home") goalYawDrift += goal.drift * dt;
            float maxSpeed = 180f;                                                                    // v2: 70 -> 180 m/s (short briefing)
            goalSlide += goal.slide * dt;
            target = Vector3.SmoothDamp(target, goal.target + goalSlide, ref targetVel, smooth, maxSpeed, dt);
            tilt = Mathf.SmoothDamp(tilt, goal.sun ? SunTilt(goal) : goal.tilt, ref tiltVel, smooth, 60f, dt);
            lowK = Mathf.SmoothDamp(lowK, goal.low ? 1f : 0f, ref lowVel, smooth, 10f, dt);
            dist = Mathf.SmoothDamp(dist, goal.dist, ref distVel, smooth, 240f, dt);
            pitch = Mathf.SmoothDampAngle(pitch, goal.pitch, ref pitchVel, smooth, 60f, dt);
            yaw = Mathf.SmoothDampAngle(yaw, goal.yaw + goalYawDrift, ref yawVel, smooth, 60f, dt);
            Apply();
        }

        void Apply()
        {
            if (!camT) return;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var pos = target - rot * Vector3.forward * dist;
            if (goalName != "home")
            {
                float floor = GroundAt(pos) + 28f;                                                    // never skim the cliffs
                if (pos.y < floor) pos.y = floor;
            }
            if (lowK > 1e-3f) pos = Vector3.Lerp(pos, InsideMap(pos), Mathf.Clamp01(lowK));
            camT.position = pos; camT.rotation = Quaternion.LookRotation(target - pos, Vector3.up);
            if (Mathf.Abs(tilt) > 1e-3f) camT.rotation = camT.rotation * Quaternion.Euler(-tilt, 0f, 0f);
            if (rts)
            {   // the audio listener rig, footstep / LOD height rules follow the flight (restored exactly on hand-back)
                rts.focus = new Vector3(target.x, 0f, target.z);
                rts.height = Mathf.Clamp(pos.y, rts.minHeight, rts.maxHeight);
            }
        }

        float GroundAt(Vector3 p) => game ? game.TerrainY(p.x, p.z) : 0f;

        /// <summary>Hands the camera back to the player at the exact home pose (RtsCamera re-enabled).</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            CineEnd();                                                                                // cinematic briefing: the game fov back, no dip overlay left
            if (Live == this) Live = null;
            if (rts)
            {
                rts.focus = homeFocus; rts.SetHeightImmediate(homeHeight);                              // Place(): the same pose as homePos / homeRot
                rts.enabled = true;                                                                   // the player's camera (it was on before)
            }
            else if (camT) { camT.position = homePos; camT.rotation = homeRot; }
        }

        // ------------------------------------------------------------------------------------------------ shots from the map
        void ResolveShots()
        {
            shots.Clear();
            var W = game.World; var me = game.player; var enemy = me == Team.Red ? Team.Blue : Team.Red;
            Vector2 size = rts.mapSizeMeters;
            Vector3 centre = new Vector3(size.x * 0.5f, 0f, -size.y * 0.5f);
            Vector3 myFort = FortCentre(me, centre), theirFort = FortCentre(enemy, centre);
            float span = Mathf.Max(size.x, size.y);
            float homePitch = rts.pitch, homeDist = homeHeight / Mathf.Sin(homePitch * Mathf.Deg2Rad);
            Vector3 homeTarget = homePos + homeRot * Vector3.forward * homeDist; homeTarget.y = 0f;

            shots["home"] = new Shot { target = homeTarget, dist = homeDist, pitch = homePitch, yaw = 0f };
            shots["overview"] = new Shot { target = centre, dist = Mathf.Clamp(span * 1.25f, 150f, 290f), pitch = 62f, yaw = -12f, drift = 1.1f };
            shots["establish"] = new Shot { target = Vector3.Lerp(centre, myFort, 0.25f), dist = Mathf.Clamp(span * 0.8f, 110f, 190f), pitch = 50f, yaw = 14f, drift = -1.0f };

            // flags: neutral first, the one closest to the player's fort, then another one towards the centre
            var flags = new List<ZObject>(W.Flags);
            flags.Sort((a, b) => Score(a, myFort).CompareTo(Score(b, myFort)));
            Vector3 f1 = flags.Count > 0 ? Ground(flags[0]) : Vector3.Lerp(centre, myFort, 0.5f);
            Vector3 f2 = f1;
            float best = float.MaxValue;
            foreach (var f in flags)
            {
                var p = Ground(f); if ((p - f1).sqrMagnitude < 20f * 20f) continue;
                float d = (p - centre).sqrMagnitude + (f.Owner == Team.Neutral ? 0f : 4000f);
                if (d < best) { best = d; f2 = p; }
            }
            shots["flag_near"] = new Shot { target = f1, dist = 72f, pitch = 50f, yaw = -16f, drift = 1.4f };
            shots["flag_far"] = new Shot { target = f2, dist = 80f, pitch = 52f, yaw = 12f, drift = -1.2f };

            // a neutral factory (producer) near the player
            Vector3 fac = Vector3.Lerp(f1, f2, 0.5f); best = float.MaxValue;
            foreach (var o in W.Objects)
            {
                if (o.Kind != ZKind.Building || !o.IsProducer || o.IsFort || o.Destroyed) continue;
                var p = Ground(o); float d = (p - myFort).sqrMagnitude + (o.Owner == Team.Neutral ? 0f : 90000f);
                if (d < best) { best = d; fac = p; }
            }
            shots["factory"] = new Shot { target = fac, dist = 78f, pitch = 52f, yaw = 20f, drift = -1.0f };

            // the bridge nearest the line between the two forts (river / canal crossings)
            Vector3 mid = Vector3.Lerp(myFort, theirFort, 0.5f), br = mid; best = float.MaxValue;
            foreach (var o in W.Objects)
            {
                if (!o.IsBridge || o.Destroyed) continue;
                var p = Ground(o); float d = (p - mid).sqrMagnitude;
                if (d < best) { best = d; br = p; }
            }
            shots["bridge"] = new Shot { target = br, dist = 66f, pitch = 47f, yaw = -20f, drift = 1.2f };
            shots["enemy_fort"] = new Shot { target = theirFort, dist = 92f, pitch = 52f, yaw = 10f, drift = 1.0f };
            shots["enemy_zone"] = new Shot { target = Vector3.Lerp(centre, theirFort, 0.55f), dist = Mathf.Clamp(span * 0.7f, 100f, 160f), pitch = 56f, yaw = -8f, drift = 0.8f };
            shots["centre"] = new Shot { target = centre, dist = Mathf.Clamp(span * 0.75f, 110f, 180f), pitch = 55f, yaw = 6f, drift = 0.8f };

            // vehicles (L17: the 20 in the bottom rows, 2 m per tile); the whole group in frame
            var parked = new List<Vector3>();
            foreach (var o in W.Objects) if (o.Kind == ZKind.Vehicle && o.Owner == Team.Neutral && !o.Destroyed) parked.Add(Ground(o));
            Vector3 cp = centre; int cpN = 0; float cpR = 0f;
            foreach (var p in parked)
            {
                int k = 0; Vector3 sum = Vector3.zero;
                foreach (var q in parked) if ((q - p).sqrMagnitude < 60f * 60f) { k++; sum += q; }
                if (k > cpN) { cpN = k; cp = sum / k; }
            }
            foreach (var q in parked) if ((q - cp).sqrMagnitude < 75f * 75f) cpR = Mathf.Max(cpR, (q - cp).magnitude);
            shots["carpark"] = new Shot { target = cp, dist = Mathf.Clamp(55f + 1.4f * cpR, 75f, 150f), pitch = 54f, yaw = 12f, drift = -1.0f };

            // The camera sits 6 m inside the map edge, ~30 m over the ground, looks a few degrees under the horizon and dollies inwards:
            // the frame shows the map, the finished CliffsV3 outside world past the edge (hazed into the sky), the painted sunrise sky
            // (FlybySky) and, in "horizon", the sun disc on the side the shadows fall away from (right third of the frame).
            mapSize = size;
            Vector3 sunH = SunFlat();
            float sunYaw = Mathf.Atan2(sunH.x, sunH.z) * Mathf.Rad2Deg;
            var hs = LowShot(centre, sunYaw - LowSunOffsetDeg, size); hs.sun = true; shots["horizon"] = hs;
            Vector3 ff = theirFort - myFort; ff.y = 0f;
            shots["horizon_forts"] = LowShot(myFort, ff.sqrMagnitude > 1f ? Mathf.Atan2(ff.x, ff.z) * Mathf.Rad2Deg : sunYaw, size);
        }

        Shot LowShot(Vector3 through, float yawDeg, Vector2 size)
        {
            var fwd = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            float back = EdgeDistance(through, -fwd, size, LowMargin);                               // back from `through` to the map edge
            Vector3 cam = through - fwd * back; cam.y = 0f;
            float ahead = back + EdgeDistance(through, fwd, size, LowMargin);                         // the map in front of the camera
            float aimD = Mathf.Clamp(Mathf.Min(LowAim, ahead - 4f), 30f, LowAim);
            Vector3 aim = cam + fwd * aimD; aim.y = 0f;
            float h = Mathf.Max(LowHeight, GroundAt(cam) + 28.5f);                                    // over the 28 m cliff floor of Apply
            float orbitPitch = Mathf.Atan2(h, aimD) * Mathf.Rad2Deg;
            return new Shot { target = aim, dist = Mathf.Sqrt(h * h + aimD * aimD), pitch = orbitPitch, yaw = yawDeg, drift = 0f,
                              tilt = Mathf.Max(0f, orbitPitch - LowViewPitch), slide = fwd * LowSlide, low = true };
        }

        /// <summary>Tilt of a sun-facing low shot now: LowViewPitch under the horizon, or less (up to LowMaxUp over it) so the rising sun
        /// stays at SunFrameK of the half frame height.</summary>
        float SunTilt(Shot s)
        {
            if (!camC && camT) camC = camT.GetComponent<Camera>();
            float half = (camC ? camC.fieldOfView : 32f) * 0.5f;
            var sun = RenderSettings.sun;
            var vs = FlybySky.VisualSun(sun ? -sun.transform.forward : new Vector3(0.94f, 0.16f, -0.3f).normalized);
            float el = Mathf.Asin(Mathf.Clamp(vs.y, -1f, 1f)) * Mathf.Rad2Deg;
            float down = Mathf.Clamp(Mathf.Min(LowViewPitch, SunFrameK * half - el), -LowMaxUp, LowViewPitch);
            return Mathf.Max(0f, s.pitch - down);
        }

        /// <summary>Horizontal distance along d from p to the map rect inset by m (0 if p is outside).</summary>
        static float EdgeDistance(Vector3 p, Vector3 d, Vector2 size, float m)
        {
            float t = 1e4f;
            if (d.x > 1e-4f) t = Mathf.Min(t, (size.x - m - p.x) / d.x); else if (d.x < -1e-4f) t = Mathf.Min(t, (m - p.x) / d.x);
            if (d.z > 1e-4f) t = Mathf.Min(t, (-m - p.z) / d.z); else if (d.z < -1e-4f) t = Mathf.Min(t, (-size.y + m - p.z) / d.z);
            return Mathf.Max(0f, t);
        }

        Vector3 InsideMap(Vector3 p)
        {
            if (mapSize.x <= 0f) return p;
            p.x = Mathf.Clamp(p.x, LowMargin, mapSize.x - LowMargin); p.z = Mathf.Clamp(p.z, -mapSize.y + LowMargin, -LowMargin);
            return p;
        }

        /// <summary>The horizontal direction to the DayNight sun at the start of the briefing (the sunrise: east).</summary>
        static Vector3 SunFlat()
        {
            var sun = RenderSettings.sun;
            Vector3 s = sun ? -sun.transform.forward : Vector3.right; s.y = 0f;
            return s.sqrMagnitude > 1e-4f ? s.normalized : Vector3.right;
        }

        void PlayDip()
        {   // sfx_batch_v1 ter_flyby_dip_whoosh (3 soft wind swells, UI bus at the start-whoosh level), a little lower while Zod speaks
            float duck = VoiceOn != null && VoiceOn() ? DipDuck : 1f;
            if (AudioDirector.Has("ter_flyby_dip_whoosh")) { AudioDirector.UI("ter_flyby_dip_whoosh", duck); return; }
            AudioDirector.UI("ui_level_preview", duck);
            if (!dipWarned) { dipWarned = true; Debug.LogWarning("[ZodFlyover] ter_flyby_dip_whoosh is not in the sound table (apply sfx_batch_v1): the dip plays the ui_level_preview cue"); }
        }

        float Score(ZObject f, Vector3 fort) => (Ground(f) - fort).sqrMagnitude + (f.Owner == Team.Neutral ? 0f : 1e6f);

        Vector3 Ground(ZObject o) { var p = game.CenterWorld(o, false); p.y = 0f; return p; }

        Vector3 FortCentre(Team t, Vector3 fallback)
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (var o in game.World.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) { sum += Ground(o); n++; }
            return n > 0 ? sum / n : fallback;
        }
    }
}
