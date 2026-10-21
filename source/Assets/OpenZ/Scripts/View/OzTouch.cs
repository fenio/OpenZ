using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace OpenZ
{
    /// <summary>
    /// Touch screen and gamepad play, as one small layer in front of the game's own input code.
    ///
    /// Every screen of the game (menus, briefing, HUD, build menu, minimap, orders, camera) polls the mouse the Input System calls
    /// current, and its keyboard. This layer owns a second, virtual mouse ("OzTouchPointer"). While a finger or a gamepad drives,
    /// it writes that mouse's place and buttons once a frame and keeps it the current one, so the game reads fingers and sticks as
    /// the pointer events it already understands. While the real mouse is in use the layer only watches: it writes nothing, the
    /// virtual mouse is not current, mouse and keyboard play is exactly what it was.
    ///
    /// Setting (OPTIONS, ADVANCED tab, TOUCH · GAMEPAD; PlayerPrefs oz_touch):
    ///  AUTO      (default) a touch on a touch screen starts the touch controls, a gamepad stick or button starts the gamepad
    ///            pointer, the real mouse takes over again as soon as it moves or clicks.
    ///  TOUCH ON  the touch controls always. Without a touch screen device (a screen that only sends mouse events) the left
    ///            button of the system mouse is the finger: a tap on the ground orders, the other buttons and the wheel pass through.
    ///  OFF       the layer does nothing. It lets go of the pointer when the real mouse is next used (so the setting can be
    ///            stepped past OFF by touch or gamepad alone); from then on touches and gamepads are ignored.
    ///
    /// One finger:
    ///  tap           left click. On the map, with own units selected and neither an own unit nor an own fort / factory under the
    ///                finger: the right click instead, so the order the right button gives (move, attack, capture, enter,
    ///                repair). A robot squad selected and an own carrier with room under the finger: the order (board it).
    ///  double tap    the game's own double click (every unit of that type on screen).
    ///  drag          left button held from where the finger went down: the selection box on the map, the minimap's drag, a
    ///                slider, a panel in ARRANGE INTERFACE.
    ///  hold          the pointer rests under the finger with no button: tooltips, the unit card of a build picture, the hover
    ///                bar and the order cursor. Lifted after HoldSeconds it clicks nothing.
    /// Two fingers (touch screen only):
    ///  drag          the map follows the fingers (RtsCamera.DragPixels); in a menu the lists scroll (wheel steps).
    ///  pinch         zoom (RtsCamera.ZoomBy).
    ///  tap           right click where the first finger went down: cancel a placement, a rally point, NO in a question.
    /// Between touches the pointer is parked outside the screen: nothing stays hovered, no edge scroll, no cursor.
    ///
    /// Keys: TapKey presses a key for one frame on the keyboard the game reads (the keys held on the real keyboard stay held);
    /// the on-screen keys of the HUD (OpenZHud.TouchKeys.cs) and the gamepad (OzTouch.Gamepad.cs) use it.
    /// View and input side only: the sim never reads a device.
    /// </summary>
    [DefaultExecutionOrder(-32000)]                                                  // before every script that reads the mouse this frame
    public sealed partial class OzTouch : MonoBehaviour
    {
        // ------------------------------------------------------------------ setting
        public const string Pref = "oz_touch", HelpPref = "oz_touch_help", DeviceName = "OzTouchPointer", KeysName = "OzTouchKeys";
        /// <summary>The OPTIONS row (both menus, ADVANCED tab) and its tooltip.</summary>
        public const string Label = "TOUCH · GAMEPAD";
        public const string Tip = "TOUCH · GAMEPAD|Experimental: touch screen and gamepad input. AUTO: the touch controls start with a touch on a touch screen, the gamepad pointer with a gamepad; the mouse takes over again when it moves. TOUCH ON: touch controls always, also for a screen that only sends mouse clicks (a tap on the ground then gives the order). OFF: mouse and keyboard only, from the next move of the mouse. On a phone set the interface scale to 160 %.|";
        public enum Mode { Auto, On, Off }
        public static readonly string[] ModeNames = { "AUTO", "TOUCH ON", "OFF" };
        static int mode = -1;

        public static Mode Setting
        {
            get { if (mode < 0) mode = Mathf.Clamp(PlayerPrefs.GetInt(Pref, 0), 0, 2); return (Mode)mode; }
            set
            {
                mode = Mathf.Clamp((int)value, 0, 2);
                if (mode == 0) PlayerPrefs.DeleteKey(Pref); else PlayerPrefs.SetInt(Pref, mode);
                PlayerPrefs.Save();
                Debug.Log($"[OpenZ touch] setting: {ModeNames[mode]} ({Pref} {mode}, saved)");
            }
        }
        public static string ModeName => ModeNames[(int)Setting];
        /// <summary>The OPTIONS row steps through AUTO, TOUCH ON, OFF (dir under 0: backwards).</summary>
        public static void StepMode(int dir) { Setting = (Mode)(((int)Setting + (dir < 0 ? 2 : 1)) % 3); }

        // ------------------------------------------------------------------ state the rest of the game may ask
        public enum Driver { Mouse, Touch, Pad }
        /// <summary>Who moves the pointer the game reads: the real mouse (the layer is passive), a finger, or a gamepad.</summary>
        public static Driver Now => inst ? inst.driver : Driver.Mouse;
        /// <summary>A finger or a gamepad drives: the on-screen keys show (OpenZHud.TouchKeys.cs).</summary>
        public static bool Driving => Now != Driver.Mouse;
        /// <summary>The pointer does not follow the system's own pointer (a finger on a touch screen, a gamepad): the cursor is
        /// drawn at the game's pointer by the software cursor (OzSoftCursor), not by the system at its own.</summary>
        public static bool SoftPointer => inst && (inst.driver == Driver.Pad || (inst.driver == Driver.Touch && !inst.mouseFinger));
        /// <summary>TOUCH ON without a touch screen in use: the system mouse's left button is the finger.</summary>
        public static bool MouseFinger => inst && inst.driver == Driver.Touch && inst.mouseFinger;
        /// <summary>The gamepad part is in the build (OzTouch.Gamepad.cs).</summary>
        public static bool PadCompiled { get { bool yes = false; PadProbe(ref yes); return yes; } }
        /// <summary>Checks: gestures since the start.</summary>
        public static int Taps, OrderTaps, Drags, Holds, RightTaps, PanFrames, PinchFrames, KeyTaps, Switches;

        // ------------------------------------------------------------------ tuning
        /// <summary>A finger that travels further than this (fraction of the screen height, 8 px at least) drags; closer, it taps.</summary>
        public static float SlopFraction = 0.012f;
        /// <summary>A still finger down longer than this is a hold (information, no click). The game's tooltips come after 0.35 s.</summary>
        public static float HoldSeconds = 0.45f;
        /// <summary>Two fingers down and one of them up again within this, without travel: the right click.</summary>
        public static float TwoTapSeconds = 0.35f;
        /// <summary>After a touch the system mouse is not believed for this long (a touch screen may move the system pointer too).</summary>
        public static float EchoSeconds = 0.75f;
        static float Slop => Mathf.Max(8f, Screen.height * SlopFraction);
        static readonly Vector2 Parked = new Vector2(-200f, -200f);

        // ------------------------------------------------------------------ parts
        static OzTouch inst;
        Driver driver = Driver.Mouse;
        Mouse virt, real;
        int takeFrame = -1, backFrame = -1;

        struct Out { public Vector2 pos; public int buttons; }                                     // buttons: 1 left, 2 right, 4 middle
        readonly Queue<Out> seq = new Queue<Out>(8);                                                // scripted frames (a click = press, release), one a frame
        Out want, sent; bool sentAny;
        float wheelOut, wheelAcc;

        struct Contact { public int id; public Vector2 pos, start; public bool ended, fresh; }
        readonly List<Contact> contacts = new List<Contact>(10);
        readonly List<int> prevIds = new List<int>(10);
        const int MouseId = -7;
        float lastTouchAt = -100f;
        bool mouseFinger, mouseDown, mouseRest; Vector2 mouseStart, mouseRestAt, realLast; bool realSeen;

        enum Phase { Idle, Pending, Hold, Drag, Two }
        Phase phase;
        int idA = -1, idB = -1;
        Vector2 startA, posA, posB, mid0, lastMid; float downAt, twoAt, dist0, lastDist; bool twoMoved;

        readonly List<Key> tapKeys = new List<Key>(4), heldKeys = new List<Key>(4), sentKeys = new List<Key>(8);

        // the gamepad part (OzTouch.Gamepad.cs); without that file these calls are removed by the compiler
        static partial void PadProbe(ref bool yes);
        partial void PadActivity(ref bool active);
        partial void PadEnter();
        partial void PadSet(Vector2 p);
        partial void TickPad(float now);

        // ------------------------------------------------------------------ life
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { inst = null; mode = -1; Taps = OrderTaps = Drags = Holds = RightTaps = PanFrames = PinchFrames = KeyTaps = Switches = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (inst) return;
            var go = new GameObject("OzTouch"); DontDestroyOnLoad(go); inst = go.AddComponent<OzTouch>();
        }

        void OnEnable() { EnhancedTouchSupport.Enable(); }

        void OnDisable()
        {
            if (driver != Driver.Mouse) Hand(Driver.Mouse);
            EnhancedTouchSupport.Disable();
        }

        void OnDestroy()
        {
            if (virt != null && virt.added) InputSystem.RemoveDevice(virt);
            if (inst == this) inst = null;
        }

        // ------------------------------------------------------------------ frame
        bool Quiet => seq.Count == 0 && sent.buttons == 0 && want.buttons == 0;

        void Update()
        {
            // the game reads the virtual pointer for this whole frame (the system mouse may have received an event since)
            if (driver != Driver.Mouse && virt != null && virt.added && Time.frameCount > takeFrame && Mouse.current != virt) virt.MakeCurrent();
            // handed back: a last event of the virtual pointer may still arrive; the system mouse stays the one the game reads
            if (driver == Driver.Mouse && Time.frameCount <= backFrame && real != null && real.added && Mouse.current == virt) real.MakeCurrent();

            float now = Time.unscaledTime;
            var set = Setting;
            if (set == Mode.Off && driver == Driver.Mouse) { prevIds.Clear(); return; }

            ReadContacts(now, set);
            bool pad = false;
            if (set != Mode.Off) PadActivity(ref pad);
            bool mouse = RealMouseActivity(now);

            Driver to = driver;
            if (set == Mode.Off) { if (mouse) to = Driver.Mouse; }                                  // OFF takes hold when the real mouse is used: a player with nothing but a touch screen or a gamepad can still step the setting on
            else if (contacts.Count > 0 && !mouseFinger) to = Driver.Touch;                         // a finger on a touch screen
            else if (pad) to = Driver.Pad;
            else if (set == Mode.On) { if (driver != Driver.Pad || mouse || contacts.Count > 0) to = Driver.Touch; }
            else if (mouse) to = Driver.Mouse;                                                      // AUTO: the real mouse is back
            if (to != driver && Quiet) Hand(to);                                                    // never while a button is held on the virtual pointer

            if (driver == Driver.Mouse) return;
            if (driver == Driver.Touch) TickTouch(now); else TickPad(now);
            Emit();
        }

        /// <summary>The pointer changes hands.</summary>
        void Hand(Driver to)
        {
            var from = driver;
            if (to == Driver.Mouse)
            {
                driver = to; phase = Phase.Idle; idA = idB = -1; seq.Clear(); heldKeys.Clear(); mouseFinger = false; mouseDown = false; mouseRest = false;
                want = new Out { pos = Parked };
                if (virt != null && virt.added && sent.buttons != 0) Send(want, Vector2.zero, 0f);  // no button stays down on it
                if (real != null && real.added) real.MakeCurrent();
                backFrame = Time.frameCount + 3;
            }
            else
            {
                if (from == Driver.Mouse)
                {
                    var cur = Mouse.current;
                    if (virt == null || !virt.added) virt = InputSystem.AddDevice<Mouse>(DeviceName);
                    if (cur != null && cur != virt) { real = cur; cur.MakeCurrent(); }              // the game reads the system mouse for this one more frame: the pointer's first place is on its way
                    else if (real == null || !real.added) FindReal();
                    if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>(KeysName);        // the game's input code does not run without a keyboard device
                    takeFrame = Time.frameCount;
                    Vector2 p = real != null && real.added ? real.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    realLast = p; realSeen = real != null;
                    sent = new Out { pos = p }; sentAny = false; want = sent;
                }
                driver = to; phase = Phase.Idle; idA = idB = -1; heldKeys.Clear();
                if (to == Driver.Pad) PadEnter();
            }
            Switches++;
            Debug.Log($"[OpenZ touch] pointer: {from} -> {to} (setting {ModeName}, touch screen {(Touchscreen.current != null ? Touchscreen.current.name : "none")}, gamepads {Gamepad.all.Count}, system mouse {(real != null && real.added ? real.name : "none")})");
        }

        void FindReal()
        {
            real = null;
            var all = InputSystem.devices;
            for (int i = 0; i < all.Count; i++)
                if (all[i] is Mouse m && m != virt && m.enabled) { real = m; break; }
        }

        /// <summary>The system mouse moved, clicked or turned its wheel by itself (not as the echo of a touch).</summary>
        bool RealMouseActivity(float now)
        {
            if (driver == Driver.Mouse) return false;                                               // nobody to take the pointer back from
            if (real == null || !real.added) { FindReal(); realSeen = false; }
            if (real == null) return false;
            Vector2 p = real.position.ReadValue();
            bool moved = realSeen && (p - realLast).sqrMagnitude > 36f;                             // more than 6 px in a frame: not sensor noise
            realLast = p; realSeen = true;
            if (now - lastTouchAt < EchoSeconds) return false;
            var sc = real.scroll.ReadValue();                                                       // TRACKPAD SCROLL: a sideways swipe is the mouse too
            return moved || real.leftButton.isPressed || real.rightButton.isPressed || real.middleButton.isPressed || Mathf.Abs(sc.y) > 0.01f || Mathf.Abs(sc.x) > 0.01f;
        }

        // ------------------------------------------------------------------ contacts: fingers, or the system mouse as one finger
        void ReadContacts(float now, Mode set)
        {
            contacts.Clear();
            var act = Touch.activeTouches;
            for (int i = 0; i < act.Count; i++)
            {
                var t = act[i]; var ph = t.phase;
                if (ph == TouchPhase.None) continue;
                contacts.Add(new Contact { id = t.touchId, pos = t.screenPosition, start = t.startScreenPosition, ended = ph == TouchPhase.Ended || ph == TouchPhase.Canceled, fresh = !prevIds.Contains(t.touchId) });
            }
            if (contacts.Count > 0) { lastTouchAt = now; mouseFinger = false; mouseDown = false; }
            else if (set == Mode.On && now - lastTouchAt >= EchoSeconds)
            {
                if (real == null || !real.added) { var cur = Mouse.current; if (cur != null && cur != virt) real = cur; else FindReal(); }
                mouseFinger = real != null;
                if (mouseFinger)
                {
                    Vector2 p = real.position.ReadValue(); bool down = real.leftButton.isPressed;
                    if (!mouseDown && (down || real.leftButton.wasPressedThisFrame)) { mouseDown = down; mouseStart = p; contacts.Add(new Contact { id = MouseId, pos = p, start = p, ended = !down, fresh = true }); }
                    else if (mouseDown) { mouseDown = down; contacts.Add(new Contact { id = MouseId, pos = p, start = mouseStart, ended = !down }); }
                }
            }
            else { mouseFinger = false; mouseDown = false; }
            prevIds.Clear();
            for (int i = 0; i < contacts.Count; i++) if (!contacts[i].ended) prevIds.Add(contacts[i].id);
        }

        // ------------------------------------------------------------------ gestures
        /// <summary>No finger down: on a touch screen the pointer is parked. The system mouse as the finger: the pointer follows it
        /// (hover as with a mouse), except right after a click, where it stays parked until the system pointer moves again: a screen
        /// that sends its taps as mouse clicks leaves the system pointer on the button just tapped, and nothing should stay hovered there.</summary>
        Out Rest()
        {
            if (mouseFinger && real != null && real.added)
            {
                Vector2 p = real.position.ReadValue();
                if (mouseRest && (p - mouseRestAt).sqrMagnitude <= 9f) return new Out { pos = Parked };
                mouseRest = false;
                return new Out { pos = p };
            }
            return new Out { pos = Parked };
        }
        void End()
        {
            phase = Phase.Idle; idA = idB = -1;
            if (mouseFinger && real != null && real.added) { mouseRest = true; mouseRestAt = real.position.ReadValue(); }
            want = Rest();
        }
        void Lift() { seq.Enqueue(new Out { pos = posA }); End(); }
        void Click(Vector2 p, int button) { seq.Enqueue(new Out { pos = p, buttons = button }); seq.Enqueue(new Out { pos = p }); }

        void Tap(Vector2 p)
        {
            bool order = TapIsOrder(p);
            Taps++; if (order) OrderTaps++;
            Click(p, order ? 2 : 1);
        }

        void TickTouch(float now)
        {
            Contact a = default, b = default; bool hasA = false, hasB = false;
            if (phase != Phase.Idle)
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    if (c.id == idA) { a = c; hasA = true; }
                    else if (phase == Phase.Two && c.id == idB) { b = c; hasB = true; }
                }
            if (phase == Phase.Idle)
            {
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i]; if (!c.fresh) continue;                                    // a finger left over from the gesture before is not a new touch
                    idA = c.id; startA = c.start; posA = c.pos; downAt = now; a = c; hasA = true; phase = Phase.Pending;
                    break;
                }
                if (phase == Phase.Idle) { want = Rest(); return; }
            }
            switch (phase)
            {
                case Phase.Pending:
                case Phase.Hold:
                {
                    if (!hasA) { End(); break; }                                                    // gone without an end (the device went away)
                    posA = a.pos;
                    if (!a.ended && !mouseFinger)
                        for (int i = 0; i < contacts.Count; i++)
                        {
                            var c = contacts[i]; if (c.id == idA || c.ended || !c.fresh) continue;
                            idB = c.id; posB = c.pos; phase = Phase.Two; twoAt = now; twoMoved = false;
                            mid0 = lastMid = (posA + posB) * 0.5f; dist0 = lastDist = (posA - posB).magnitude;
                            want = new Out { pos = Parked };                                        // two fingers: nothing is hovered
                            return;
                        }
                    if ((posA - startA).sqrMagnitude > Slop * Slop)
                    {
                        phase = Phase.Drag; Drags++;
                        seq.Enqueue(new Out { pos = startA, buttons = 1 });                         // the press where the finger went down: the box starts there
                        want = new Out { pos = posA, buttons = 1 };
                        if (a.ended) Lift();
                        break;
                    }
                    if (a.ended) { if (phase == Phase.Pending) Tap(startA); End(); break; }
                    if (phase == Phase.Pending && now - downAt > HoldSeconds) { phase = Phase.Hold; Holds++; }
                    want = new Out { pos = posA };                                                  // under the finger with no button: hover
                    break;
                }
                case Phase.Drag:
                {
                    if (hasA) posA = a.pos;
                    want = new Out { pos = posA, buttons = 1 };
                    if (!hasA || a.ended) Lift();
                    break;
                }
                case Phase.Two:
                {
                    if (hasA && !a.ended && hasB && !b.ended)
                    {
                        posA = a.pos; posB = b.pos;
                        Vector2 mid = (posA + posB) * 0.5f; float dist = (posA - posB).magnitude;
                        if (!twoMoved && ((mid - mid0).sqrMagnitude > Slop * Slop || Mathf.Abs(dist - dist0) > Slop * 1.5f)) { twoMoved = true; lastMid = mid; lastDist = dist; }
                        if (twoMoved) TwoMove(mid - lastMid, lastDist, dist);
                        lastMid = mid; lastDist = dist;
                        want = new Out { pos = Parked };
                    }
                    else
                    {
                        if (!twoMoved && now - twoAt <= TwoTapSeconds) { RightTaps++; Click(startA, 2); }
                        End();
                    }
                    break;
                }
            }
        }

        /// <summary>Two fingers travel: in a match the map follows them and a pinch zooms; anywhere else the travel turns the wheel.</summary>
        void TwoMove(Vector2 d, float before, float after)
        {
            var hud = OpenZHud.I; var cam = hud ? hud.rts : null;
            if (cam && cam.isActiveAndEnabled && hud.W != null && !hud.Modal && !OzConfirm.Blocks)
            {
                if (d.sqrMagnitude > 0f) { cam.DragPixels(d); PanFrames++; }
                if (before > 16f && after > 16f && Mathf.Abs(after - before) > 0.01f) { cam.ZoomBy(before / after); PinchFrames++; }
                return;
            }
            wheelAcc -= d.y;                                                                        // fingers down the screen = the list comes down = wheel up
            float notch = Mathf.Max(24f, Screen.height * 0.05f);
            while (Mathf.Abs(wheelAcc) >= notch) { float s = Mathf.Sign(wheelAcc); wheelOut += s * 120f; wheelAcc -= s * notch; }
        }

        /// <summary>A tap on the map with own units selected is their order, unless it lands on an own unit (select it) or on an own
        /// fort / factory (its window opens). The same hit tests as the game's own click (PlayerInput.ScreenRect, the HUD's blockers).</summary>
        public static bool TapIsOrder(Vector2 p)
        {
            var hud = OpenZHud.I;
            if (!hud || !hud.isActiveAndEnabled || hud.W == null || !hud.input || hud.Modal || OzConfirm.Blocks) return false;
            var input = hud.input; var W = hud.W; var me = hud.Player;
            if (W.GameOver || input.Placing != null || (hud.Rally && hud.Rally.Armed) || hud.IsOverUi(p)) return false;
            bool any = false, robots = false;
            foreach (var s in input.Selection)
                if (s != null && !s.Destroyed && s.Owner == me) { any = true; if (s.Kind == ZKind.Robot) robots = true; }
            if (!any) return false;
            foreach (var o in W.Objects)
            {
                if (o.Owner != me || o.Destroyed || o.KillAt >= 0 || o.IsFortDefender) continue;
                if (o.Kind != ZKind.Robot && o.Kind != ZKind.Vehicle && o.Kind != ZKind.Cannon) continue;
                if (!input.ScreenRect(o).Contains(p)) continue;
                if (robots && o.CanTakePassengers(me) && !input.Selection.Contains(o)) continue;   // an own carrier with room: the squad boards it
                return false;                                                                       // an own unit: the tap selects it
            }
            if (hud.hudVisible && hud.Production && hud.Production.ProducerUnder(p) != null) return false;   // an own fort or factory: its window
            return true;
        }

        // ------------------------------------------------------------------ out: the virtual pointer
        void Emit()
        {
            Out o = seq.Count > 0 ? seq.Dequeue() : want;
            Vector2 delta = Vector2.zero; float wheel = wheelOut, wheelX = 0f; wheelOut = 0f;
            if (mouseFinger && real != null && real.added)
            {   // the system mouse under one finger: its other buttons, its wheel and its travel pass through
                int through = (real.rightButton.isPressed ? 2 : 0) | (real.middleButton.isPressed ? 4 : 0);
                var sc = real.scroll.ReadValue(); wheel += sc.y; wheelX = sc.x; delta = real.delta.ReadValue();   // TRACKPAD SCROLL: the sideways scroll passes through too (OzTrackpadScroll.cs)
                if (through != 0 || (sent.buttons & 6) != 0)
                {   // they act where the system pointer is, also on the frame they are let go
                    if (o.pos == Parked) o.pos = real.position.ReadValue();
                    mouseRest = false;
                }
                o.buttons |= through;
            }
            else if (sentAny) delta = o.pos - sent.pos;
            if (sentAny && o.pos == sent.pos && o.buttons == sent.buttons && wheel == 0f && wheelX == 0f && delta == Vector2.zero) return;
            Send(o, delta, wheel, wheelX);
        }

        void Send(Out o, Vector2 delta, float wheel, float wheelX = 0f)
        {
            var st = new MouseState { position = o.pos, delta = delta, scroll = new Vector2(wheelX, wheel) };
            if ((o.buttons & 1) != 0) st = st.WithButton(MouseButton.Left);
            if ((o.buttons & 2) != 0) st = st.WithButton(MouseButton.Right);
            if ((o.buttons & 4) != 0) st = st.WithButton(MouseButton.Middle);
            InputSystem.QueueStateEvent(virt, st);
            sent = o; sentAny = true;
        }

        // ------------------------------------------------------------------ out: keys
        /// <summary>Press a key for one frame on the keyboard the game reads (the on-screen BACK key, the gamepad's buttons).</summary>
        public static void TapKey(Key k) { if (inst && k != Key.None) { inst.tapKeys.Add(k); KeyTaps++; } }

        void LateUpdate()
        {
            if (tapKeys.Count == 0 && heldKeys.Count == 0 && sentKeys.Count == 0) return;
            var kb = Keyboard.current;
            if (kb == null) kb = InputSystem.AddDevice<Keyboard>(KeysName);
            var st = new KeyboardState();
            var all = kb.allKeys;
            for (int i = 0; i < all.Count; i++)
            {
                var kc = all[i]; if (kc == null) continue;
                var code = kc.keyCode;
                if (code == Key.None || (int)code > Keyboard.KeyCount) continue;
                if (kc.isPressed && !sentKeys.Contains(code)) st.Set(code, true);                   // what is held on the real keyboard stays held
            }
            sentKeys.Clear();
            for (int i = 0; i < heldKeys.Count; i++) { st.Set(heldKeys[i], true); if (!sentKeys.Contains(heldKeys[i])) sentKeys.Add(heldKeys[i]); }
            for (int i = 0; i < tapKeys.Count; i++) { st.Set(tapKeys[i], true); if (!sentKeys.Contains(tapKeys[i])) sentKeys.Add(tapKeys[i]); }
            tapKeys.Clear();
            InputSystem.QueueStateEvent(kb, st);                                                     // the frame after: the same state without the tapped keys
        }

        // ------------------------------------------------------------------ checks
        /// <summary>Checks: put the gamepad's pointer at a screen point (nothing without the gamepad part).</summary>
        public static void ProofPointer(Vector2 p) { if (inst) inst.PadSet(p); }

        /// <summary>One line for the logs and the checks.</summary>
        public static string Describe()
            => $"setting {ModeName}, pointer {Now}{(MouseFinger ? " (system mouse as the finger)" : "")}, software cursor {SoftPointer}, taps {Taps} (orders {OrderTaps}), drags {Drags}, holds {Holds}, two-finger taps {RightTaps}, pan frames {PanFrames}, pinch frames {PinchFrames}, keys {KeyTaps}, hand-overs {Switches}";
    }
}
