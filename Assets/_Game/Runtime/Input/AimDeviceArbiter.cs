namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Tier F31 (gate run 3f0ed276): which device aims is decided by the most recent REAL input, never by where a resting
    /// cursor happens to be. Real input: the mouse moved more than PointerMovePixels in a frame, a stick was deflected, or
    /// the move keys / a keyboard attack key were used. A click is not aim input: a keys player who clicks to attack with
    /// the mouse never moved keeps the keys aim. The cursor keeps the aim for PointerHoldSeconds after it last moved, so a
    /// mouse player who walks with W A S D while holding the cursor still for a moment is not switched mid-aim.
    /// Pure (times in seconds, no Unity reads); HumanInputSource feeds it each frame. PxInputDeviceTests.
    /// </summary>
    public sealed class AimDeviceArbiter
    {
        /// <summary>A mouse move counts as aim input above this many pixels in one frame (2 px, the old MouseUsed rule).</summary>
        public const float PointerMovePixels = 2f;
        /// <summary>The cursor keeps the aim this long after it last moved, even while the move keys are used.</summary>
        public const double PointerHoldSeconds = 2.0;
        /// <summary>A keyboard attack or ability key is a deliberate "the keys aim": it takes over once the cursor has rested
        /// this long (ControlBindings.MouseIdleSeconds, the controls-1 rule KeyboardAndControllerPlayTests checks).</summary>
        public const double KeyAttackHoldSeconds = ControlBindings.MouseIdleSeconds;

        double _pointerAt = double.NegativeInfinity;
        double _stickAt = double.NegativeInfinity;
        double _keysAt = double.NegativeInfinity;
        double _keyAttackAt = double.NegativeInfinity;
        bool _pointerEverMoved;
        /// <summary>fix/px-base-seat-reds (pass 5: F31 keys-only top-down 0 of 7 with switches off): the never-moved-mouse rule
        /// of NotePointerPressed is a plain bug fix, on for every build and every switch (a mouse user moves his mouse before
        /// he clicks, so a click still takes the aim for him). False = the 1.0.1566 rule. Settable for tests.</summary>
        public bool ClickNeedsMovedMouse = true;

        /// <summary>Forget every device (match start: the player has touched nothing yet).</summary>
        public void Reset()
        {
            _pointerAt = _stickAt = _keysAt = _keyAttackAt = double.NegativeInfinity;
            _pointerEverMoved = false;
        }

        public void NotePointerMoved(double now)
        {
            _pointerAt = now;
            _pointerEverMoved = true;
        }
        /// <summary>Fix round 7 (owner's call): a mouse BUTTON press (attack, ability, any mouse-bound verb) is aim input too:
        /// it hands the aim to the cursor for that press and after it, with no move needed and whatever PointerHoldSeconds
        /// says, so a mouse player who walks with W A S D and leaves the cursor resting still swings AT the cursor. The keys
        /// take the aim back only by their own rules (a keyboard attack key, or walking past the hold after this press).
        /// Round 3 (pass 3, F31 keys-only top-down 0 of 8): only for a mouse the player has MOVED since Reset (match start).
        /// A keys player who clicks to attack with a mouse he never moved keeps the keys aim (facing / the nearest enemy);
        /// handing it to a cursor resting wherever the match left it aimed every top-down swing at a stale ground point.</summary>
        public void NotePointerPressed(double now)
        {
            if (_pointerEverMoved || !ClickNeedsMovedMouse) _pointerAt = now;
        }
        public void NoteStick(double now) => _stickAt = now;
        /// <summary>The move keys were used (attack: a keyboard attack or ability key was pressed).</summary>
        public void NoteKeys(double now, bool attack = false)
        {
            _keysAt = now;
            if (attack) _keyAttackAt = now;
        }

        /// <summary>True when a mouse delta (pixels) is real aim input.</summary>
        public static bool PointerMoved(float dx, float dy) => dx * dx + dy * dy > PointerMovePixels * PointerMovePixels;

        /// <summary>The device that aims now; current when nothing has been used since Reset.</summary>
        public AimResolver.Mode Decide(double now, AimResolver.Mode current)
        {
            bool p = !double.IsNegativeInfinity(_pointerAt), s = !double.IsNegativeInfinity(_stickAt), k = !double.IsNegativeInfinity(_keysAt);
            if (!p && !s && !k) return current;
            if (s && _stickAt >= _pointerAt && _stickAt >= _keysAt) return AimResolver.Mode.Stick;
            if (p && _pointerAt >= _keysAt && _pointerAt >= _stickAt) return AimResolver.Mode.Pointer;
            // The keys were used last (after the stick or the cursor): they aim, unless the cursor moved within the hold.
            if (k && _keysAt > _stickAt && _keysAt > _pointerAt)
            {
                if (!p) return AimResolver.Mode.Keys;
                double rested = now - _pointerAt;
                bool attackSincePointer = _keyAttackAt > _pointerAt;
                return rested >= PointerHoldSeconds || (attackSincePointer && rested >= KeyAttackHoldSeconds)
                    ? AimResolver.Mode.Keys : AimResolver.Mode.Pointer;
            }
            return current;
        }
    }

    /// <summary>
    /// Tier F34 (gate run 3f0ed276): the frame's aim changes only when the player moved an input device. With no device
    /// input in a frame the previous raw aim is sent again relative to the body, so a camera that moves (follow, lean,
    /// shake), a pushed fighter or a cursor-to-ground projection that slides can never look like a player to the sim's
    /// idle rule (MechRules.HasInput: aim moved IdleAimUnits or turned IdleAimDegrees relative to the body).
    /// Pure; HumanInputSource.FinishAim applies it before the aim help. PxInputDeviceTests.
    /// </summary>
    public sealed class AimHold
    {
        bool _have;
        bool _hasAim;
        double _dx, _dz;

        public void Reset() => _have = false;

        /// <summary>
        /// touched: a device moved or a button was down this frame. px/pz: the body. On a touched frame (or before any aim
        /// was seen) the raw aim is kept as the new held offset; on an untouched frame it is replaced by the held one.
        /// </summary>
        public void Apply(bool touched, double px, double pz, ref bool hasAim, ref double ax, ref double az)
        {
            if (touched || !_have)
            {
                _have = true;
                _hasAim = hasAim;
                _dx = ax - px;
                _dz = az - pz;
                return;
            }
            hasAim = _hasAim;
            ax = px + _dx;
            az = pz + _dz;
        }
    }
}
