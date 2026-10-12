using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// mouse wheel zooms smoothly (eased, clamped between a close tactical view and the full-map overview).
    /// Pitch 55, FOV 32, north stays up, focus clamped to the map rectangle. Pan speed scales with the zoom height.
    /// </summary>
    [ExecuteAlways]
    public class RtsCamera : MonoBehaviour
    {
        public Vector2 mapSizeMeters = new Vector2(144, 180);
        public float panSpeed = 25f, edgePx = 6f, pitch = 55f;
        [Tooltip("Close tactical view height (m)")] public float minHeight = 22f;
        [Tooltip("Full overview height (m)")] public float maxHeight = 230f;
        [Tooltip("Current / starting height (m)")] public float height = 70f;
        [Tooltip("Height change per wheel notch (fraction)")] public float wheelStep = 0.15f;
        [Tooltip("Zoom easing time (s)")] public float zoomSmooth = 0.18f;
        public Vector3 focus = new Vector3(116, 0, -166);
        [Tooltip("Scripted drift for recordings / z-fighting checks (m/s, 0 = off)")] public Vector2 autoDrift;

        // project's (its newest build): after holdRampDelay s the target speed grows by holdRampRate per s up to 1 + holdRampMax
        // (x3 at 0.35 + 2 / 1.25 = 1.95 s); the speed follows the target by holdRampFollow per 60 Hz frame; released = x1 again.
        [Header("Scroll boost: hold to ramp + SHIFT")]
        [Tooltip("Seconds a scroll (WASD / arrows or the screen edge) is held before it starts to speed up (reference: 0.35)")] public float holdRampDelay = 0.35f;
        [Tooltip("Extra speed gained per second of holding after the delay (reference: 1.25 = +125 % per s)")] public float holdRampRate = 1.25f;
        [Tooltip("Most extra speed from holding (reference: 2 = x3 top speed, reached 1.95 s into the hold). 0 = no ramp")] public float holdRampMax = 2f;
        [Tooltip("How fast the speed follows the ramp: the fraction of the gap closed per 60 Hz frame (reference: 0.28). Frame-rate independent here")] [Range(0.01f, 1f)] public float holdRampFollow = 0.28f;
        [Tooltip("A turn bigger than this (deg, against the direction the hold started with) restarts the ramp, the speed eases back down. 180 = only a release restarts it (the reference: rolling W to D or sliding along the edges keeps the speed). 60 = a 90 deg roll restarts, a diagonal (45) keeps it. 0 = any change restarts")] [Range(0f, 180f)] public float holdRampResetAngle = 180f;
        [Tooltip("SHIFT held while scrolling multiplies the scroll speed by this (1 = off)")] public float shiftBoost = 2f;
        [Tooltip("SHIFT boost ease in / out time (s, smoothstep: no jump)")] public float shiftEase = 0.25f;
        [Tooltip("Most combined speed from the hold ramp x SHIFT (x6 uncapped = 159 m/s at the 70 m default height, the L02 map crossed in about 1.6 s: too fast to steer; x4.5 = 119 m/s)")] public float comboMax = 4.5f;
        float holdTime, holdBoost = 1f, holdPeak = 1f, shiftT, shiftMul = 1f, shiftPeak = 1f, comboPeak = 1f; int holdTurns; Vector2 holdDir;
        /// <summary>
        /// multiplier and how long the current scroll has been held (unscaled s, restarts on release or a turn).</summary>
        public float HoldBoost => holdBoost;
        public float ShiftMul => shiftMul;
        public float HoldTime => holdTime;

        float targetHeight = -1, zoomVel;
        /// <summary>
        /// (deg, 0 = north up, the RTS default). Only a script turns it (OpenZEnding's orbit, yawHeld); released, it eases back to 0.
        /// `unclamped`: the script owns the focus (the orbit keeps the fort centred; it was framed inside the map clamp at heading 0).</summary>
        [System.NonSerialized] public float yaw;
        [System.NonSerialized] public bool yawHeld, unclamped;
        float yawVel;
        bool flying; Vector3 flyTarget;
        float shake;
        /// <summary>The shake's offset inside the camera position of the last Place() (zero while it does not shake). The motion blur
        /// gate takes it off before it asks whether the player moves the camera.</summary>
        public Vector3 ShakeOffset { get; private set; }
        /// <summary>
        /// now ~3 px) and passes OzShakeGate (on screen + global cooldown).</summary>
        public const float ShakeGain = 0.5f;
        /// <summary>Explosion camera shake (impact handler), decays in ~0.4 s. No position: UI only (end-screen stamp).</summary>
        public void Shake(float amount) { Shake(amount, focus, "ui", false); }
        /// <summary></summary>
        public float TakeShake() { float s = shake; shake = 0f; return s; }
        /// <summary>World shake at <paramref name="at"/>: only when on screen and the gate allows it. Returns true when it kicked.
        /// always = the second part of an event whose kick the gate has just let through (not held by the pause between kicks; the
        /// two switches and the on-screen test still apply).</summary>
        public bool Shake(float amount, Vector3 at, string src, bool checkScreen = true, bool always = false)
        {
            float a = amount * ShakeGain;
            float h = Mathf.Max(1f, height), span = 2f * h / Mathf.Sin(pitch * Mathf.Deg2Rad) * Mathf.Tan(16f * Mathf.Deg2Rad);
            float px = 0.5f * a * (0.6f + h * 0.01f) / span * 1080f;
            if (!OzShakeGate.Allow(src, at, px, checkScreen, always)) return false;
            shake = Mathf.Max(shake, a); return true;
        }
        /// <summary>Eased zoom to a height (same smoothing as the wheel).</summary>
        public void ZoomTo(float h) { targetHeight = Mathf.Clamp(h, minHeight, maxHeight); }
        [Tooltip("Optional: keep this transform centred (recordings, tests)")] public Transform follow;
        /// <summary>
        /// The middle-button drag moves the view this many times the ground distance under the mouse delta (1 = the old 1:1 grab).
        /// Linear, no acceleration: the drag stays smooth and predictable.</summary>
        [Tooltip("Middle-button drag speed (x the 1:1 ground grab)")] [Range(1f, 2f)] public float middleDragFactor = 1.3f;

        /// <summary>Zod fly-to (zplayer.cpp:1761-1846): each 100 Hz frame the view moves 10 % of the remaining distance;
        /// scrolling is disabled while flying. Time-based here: t = 1 - 0.9^(dt*100).</summary>
        public void FlyTo(Vector3 worldPoint)
        {
            if (Application.isPlaying && InputLocked) { LockedFlyTos++; return; }
            if (Application.isPlaying) AudioDirector.CameraJump(focus, worldPoint); flyTarget = new Vector3(worldPoint.x, 0, worldPoint.z); flying = true; flyStart = Time.unscaledTime;
        }
        /// <summary>
        /// (Replay / next level / menu load a new scene, which releases it), the player cannot move the view: WASD / arrows, screen-edge
        /// scroll, middle-button drag, wheel zoom and every FlyTo (minimap clicks, message / factory toasts, Space, group double taps,
        /// next-unit keys) are ignored. Only the ending's own camera (follow, ZoomTo, yaw) moves it. Counters for the Play check.</summary>
        public static bool InputLocked => OpenZEnding.LocksCamera;
        public static int LockedFlyTos, LockedInputs;
        // clamped area never arrived (the focus is clamped every frame, the target was not), and keys were ignored while flying. Now the
        // target is clamped with the focus, any pan key cancels a fly-to or a follow, and a fly-to ends after FlyTimeout s at the latest.
        float flyStart; public const float FlyTimeout = 3f;
        Vector2 startMouse = new Vector2(-1, -1); bool mouseMoved;   // edge scroll only after the player moved the mouse

        void Start() { targetHeight = height; Place(); }
        void OnValidate() { targetHeight = height; Place(); }

        void Update()
        {
            if (!Application.isPlaying) { Place(); return; }
            if (targetHeight < 0) targetHeight = height;
            var kb = Keyboard.current; var mouse = Mouse.current;
            Vector2 dir = Vector2.zero;
            if (InputLocked)
            {
                if ((kb != null && kb.anyKey.isPressed) || (mouse != null && Mathf.Abs(mouse.scroll.ReadValue().y) > 0.01f)) LockedInputs++;
                kb = null; mouse = null; flying = false;
            }
            bool panKey = kb != null && (kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed || kb.upArrowKey.isPressed || kb.downArrowKey.isPressed
                || (!(kb.ctrlKey.isPressed || kb.altKey.isPressed) && (OzBindings.Held(OzAction.ScrollLeft) || OzBindings.Held(OzAction.ScrollRight) || OzBindings.Held(OzAction.ScrollUp) || OzBindings.Held(OzAction.ScrollDown))));   // menu_exe_v1: bound keys (W A S D)
            if (panKey) { flying = false; if (follow && Application.isPlaying) follow = null; }
            if (flying && Time.unscaledTime - flyStart > FlyTimeout) flying = false;
            if (flying)
            {
                float t = 1f - Mathf.Pow(0.9f, Time.unscaledDeltaTime * 100f);
                focus = Vector3.Lerp(focus, flyTarget, t);
                if ((focus - flyTarget).sqrMagnitude < 0.01f) { focus = flyTarget; flying = false; }
            }
            if (follow) { focus = new Vector3(follow.position.x, 0, follow.position.z); flying = false; }
            bool modifier = kb != null && (kb.ctrlKey.isPressed || kb.altKey.isPressed);   // Ctrl+A etc. are Zod orders, not pans
            if (kb != null && !flying)
            {
                if (kb.leftArrowKey.isPressed || (!modifier && OzBindings.Held(OzAction.ScrollLeft))) dir.x -= 1;       // menu_exe_v1: bound keys, arrows fixed
                if (kb.rightArrowKey.isPressed || (!modifier && OzBindings.Held(OzAction.ScrollRight))) dir.x += 1;
                if (kb.upArrowKey.isPressed || (!modifier && OzBindings.Held(OzAction.ScrollUp))) dir.y += 1;
                if (kb.downArrowKey.isPressed || (!modifier && OzBindings.Held(OzAction.ScrollDown))) dir.y -= 1;
            }
            if (mouse != null && Application.isFocused)
            {
                Vector2 p = mouse.position.ReadValue();
                if (mouse.middleButton.isPressed)
                {   // Zod middle-button "fly": the view follows the mouse delta (zplayer_events.cpp:88-109)
                    Vector2 md = mouse.delta.ReadValue();
                    float mPerPx = 2f * height * Mathf.Tan(16f * Mathf.Deg2Rad) / Mathf.Max(1, Screen.height) / Mathf.Sin(pitch * Mathf.Deg2Rad);
                    focus += new Vector3(md.x, 0, md.y) * (mPerPx * Mathf.Max(0.1f, middleDragFactor)); flying = false;
                }
                if (startMouse.x < 0) startMouse = p; else if (!mouseMoved && (p - startMouse).sqrMagnitude > 16f) mouseMoved = true;
                if (mouseMoved && !flying && !mouse.middleButton.isPressed && p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height)
                {
                    if (p.x < edgePx) dir.x -= 1; else if (p.x > Screen.width - edgePx) dir.x += 1;
                    if (p.y < edgePx) dir.y -= 1; else if (p.y > Screen.height - edgePx) dir.y += 1;
                }
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f)
                {
                    float notches = Mathf.Clamp(wheel / 120f, -3f, 3f);          // Windows reports 120 per notch
                    if (Mathf.Abs(notches) < 0.05f) notches = Mathf.Sign(wheel); // devices that report 1 per notch
                    targetHeight = Mathf.Clamp(targetHeight * Mathf.Pow(1f - wheelStep, notches), minHeight, maxHeight);
                }
            }
            UpdateConfine(kb);
            height = Mathf.SmoothDamp(height, targetHeight, ref zoomVel, zoomSmooth, Mathf.Infinity, Time.unscaledDeltaTime);
            float speed = panSpeed * Mathf.Lerp(0.6f, 2.6f, Mathf.InverseLerp(minHeight, maxHeight, height));
            Vector2 move = dir.normalized * speed * ScrollBoost(dir, kb) + autoDrift;
            var mv = new Vector3(move.x, 0, move.y);
            if (Mathf.Abs(yaw) > 0.01f) mv = Quaternion.Euler(0f, yaw, 0f) * mv;
            focus += mv * Time.unscaledDeltaTime;
            if (!yawHeld && Mathf.Abs(yaw) > 0.001f)
            {   // released by its script: back to north-up, eased (the end screen covers it)
                yaw = Mathf.SmoothDampAngle(yaw, 0f, ref yawVel, 0.9f, Mathf.Infinity, Time.unscaledDeltaTime);
                if (Mathf.Abs(yaw) < 0.02f) { yaw = 0f; yawVel = 0f; unclamped = false; }
            }
            Place();
        }

        /// <summary>
        /// edge (corners too) share one hold. Unscaled time, so pause and slow motion do not change it, and frame-rate independent.
        /// No scroll direction (keys up, the mouse off the edge, a fly-to, the ending lock: kb and mouse are null there)
        /// SHIFT eases in and out over shiftEase s (smoothstep). Map clamping stays in Place().</summary>
        float ScrollBoost(Vector2 dir, Keyboard kb)
        {
            float dt = Time.unscaledDeltaTime;
            bool shift = kb != null && kb.shiftKey.isPressed;
            shiftT = Mathf.MoveTowards(shiftT, shift ? 1f : 0f, dt / Mathf.Max(0.001f, shiftEase));
            shiftMul = Mathf.Lerp(1f, Mathf.Max(0.1f, shiftBoost), shiftT * shiftT * (3f - 2f * shiftT));
            if (dir == Vector2.zero)
            {
                if (holdTime >= 1f)
                    Debug.Log($"[OpenZ Camera] scroll hold {holdTime:0.00} s: ramp peak x{holdPeak:0.00}, SHIFT peak x{shiftPeak:0.00}, combined peak x{comboPeak:0.00} (cap x{comboMax:0.##}), {holdTurns} turn restart(s); released, ramp reset to x1");
                holdTime = 0f; holdBoost = 1f; holdPeak = 1f; shiftPeak = 1f; comboPeak = 1f; holdTurns = 0; holdDir = Vector2.zero;
                return shiftMul;
            }
            Vector2 d = dir.normalized;
            if (holdDir == Vector2.zero) holdDir = d;
            else if (Vector2.Angle(d, holdDir) > holdRampResetAngle) { holdTime = 0f; holdDir = d; holdTurns++; }
            holdTime += dt;
            float target = holdTime > holdRampDelay ? 1f + Mathf.Min(Mathf.Max(0f, holdRampMax), (holdTime - holdRampDelay) * Mathf.Max(0f, holdRampRate)) : 1f;
            holdBoost += (target - holdBoost) * (1f - Mathf.Pow(1f - Mathf.Clamp(holdRampFollow, 0.01f, 1f), dt * 60f));
            holdPeak = Mathf.Max(holdPeak, holdBoost); shiftPeak = Mathf.Max(shiftPeak, shiftMul);
            float combo = Mathf.Min(holdBoost * shiftMul, Mathf.Max(1f, comboMax));
            comboPeak = Mathf.Max(comboPeak, combo);
            return combo;
        }

        // window edge is not a screen edge); holding Alt releases it (Alt is a modifier only: Alt+right = plain move keeps working),
        // the pause menu and a window without focus release it too, edge scroll OFF never confines. Fullscreen is never touched (the
        // OS already keeps the cursor on the game). Not applied inside the editor (the Game view would trap the mouse; the decision
        // is still logged) unless ConfineInEditor.
        public static bool ConfineInEditor = false;
        public static string ConfineState = "";
        bool confineWant, confineSet; string confineWhy = "";
        void UpdateConfine(Keyboard kb)
        {
            bool windowed = Screen.fullScreenMode == FullScreenMode.Windowed;
            bool alt = kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
            var g = ZGame.I; bool paused = g != null && g.paused;
            bool want = windowed && edgePx > 0f && Application.isFocused && !alt && !paused;
            string why = !windowed ? "fullscreen: untouched" : edgePx <= 0f ? "edge scroll off" : !Application.isFocused ? "window not focused" : alt ? "Alt held: released" : paused ? "paused: released" : "windowed: confined for edge scroll (hold Alt to release)";
            bool apply = !Application.isEditor || ConfineInEditor;
            if (windowed || confineSet)
            {
                var mode = want && apply ? CursorLockMode.Confined : CursorLockMode.None;
                if (Cursor.lockState != mode) Cursor.lockState = mode;
                confineSet = mode == CursorLockMode.Confined;
            }
            if (want != confineWant || why != confineWhy)
            {
                confineWant = want; confineWhy = why;
                ConfineState = (want ? "CONFINED" : "FREE") + " · " + why + (apply ? "" : " (editor: not applied)");
                Debug.Log($"[OpenZ Camera] mouse {ConfineState}");
            }
        }

        void OnDisable() { if (Application.isPlaying && confineSet) { Cursor.lockState = CursorLockMode.None; confineSet = false; } }

        public void SetHeightImmediate(float h) { height = targetHeight = Mathf.Clamp(h, minHeight, maxHeight); zoomVel = 0; Place(); }

        public void Place()
        {
            float h = Mathf.Clamp(height, minHeight, maxHeight);
            // keep the frame inside the map: the lower screen edge hits the ground ~0.42 h behind the focus at pitch 55 / FOV 32
            float south = Mathf.Min(h * 0.42f, mapSizeMeters.y * 0.45f), side = Mathf.Min(h * 0.3f, mapSizeMeters.x * 0.45f);
            south = OzCamEdge.South(this, h, south);                                                       // the lower limit gives way just far enough to lift a building, flag or gun out from under the HUD panels (OzCamEdge.cs)
            if (!unclamped || !Application.isPlaying)
            {
                focus.x = Mathf.Clamp(focus.x, side, mapSizeMeters.x - side);
                focus.z = Mathf.Clamp(focus.z, -mapSizeMeters.y + south, -Mathf.Min(10f, mapSizeMeters.y * 0.1f));
            }
            flyTarget.x = Mathf.Clamp(flyTarget.x, side, mapSizeMeters.x - side);                            // NB36: a reachable target
            flyTarget.z = Mathf.Clamp(flyTarget.z, -mapSizeMeters.y + south, -Mathf.Min(10f, mapSizeMeters.y * 0.1f));
            float back = h / Mathf.Tan(pitch * Mathf.Deg2Rad);
            Vector3 jitter = Vector3.zero;
            if (Application.isPlaying && shake > 0.001f && !OzShakeGate.SettingOn) shake = 0f;
            if (Application.isPlaying && shake > 0.001f)
            {
                jitter = new Vector3(Mathf.PerlinNoise(Time.time * 31f, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, Time.time * 29f) - 0.5f, Mathf.PerlinNoise(Time.time * 23f, 5.1f) - 0.5f) * shake * (0.6f + h * 0.01f);
                shake *= Mathf.Exp(-Time.unscaledDeltaTime * 7f);
            }
            ShakeOffset = jitter;
            float yw = Application.isPlaying ? yaw : 0f;
            transform.position = focus + Quaternion.Euler(0f, yw, 0f) * new Vector3(0, h, -back) + jitter;
            transform.rotation = Quaternion.Euler(pitch, yw, 0);
        }
    }
}
