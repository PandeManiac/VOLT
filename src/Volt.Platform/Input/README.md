# Volt.Platform.Input

`Volt.Platform.Input` turns the windowing backend's native events into
one immutable, per-window input snapshot per frame. Games and tools
read the snapshot; only this folder touches SDL's input events.

```csharp
window.PollEvents();

InputSnapshot input = window.Input.Snapshot;

if (input.WasKeyPressed(KeyCode.R))
{
    Restart();
}

if (input.IsKeyDown(KeyCode.W))
{
    MoveForward(deltaSeconds);
}

if (input.WasMouseButtonPressed(MouseButton.Left))
{
    Use();
}
```

## Ownership and lifetime

Every `Volt.Platform.Windows.Window` owns exactly one `WindowInput`,
exposed as `window.Input`. It is created with the window and lives
until the window is disposed; it is not a singleton, and there is no
process-wide "active" keyboard, mouse, or camera. Two windows have
independent held state, edges, motion, and focus.

`WindowInput.Dispose` stops accepting input and empties the snapshot.
Disposing it does not change relative mouse mode: release policy
belongs to the application, which is expected to release the pointer
before its window is destroyed. `Window.Dispose` disposes its input
after unregistering the window and before destroying the native
window.

The public API exposes no SDL type, native handle, or native event
layout. The SDL-specific pieces are internal: `Internal.SdlLifetime`
owns the pump and window routing, `Internal.SdlInputTranslator` reads
the tagged event union, and `Internal.SdlKeyMap` translates physical
scancodes.

## Pumping and frame lifecycle

Input is driven by the existing global event pump. One
`window.PollEvents()` call is one input frame for every registered
VOLT window:

1. Every window begins a frame: one-shot edges and accumulated motion
   are cleared; held state and the last pointer position survive.
2. SDL's process-wide queue is drained. Only window, keyboard, and
   mouse events are routed, and each by its native window ID, so
   pumping from window A can update window B without leaking A's
   input into B. Other members of SDL's union are dropped instead of
   being interpreted as window IDs.
3. Keyboard focus and pointer focus are queried once, and every window
   freezes an `InputSnapshot` from its own state and that focus.

Call `PollEvents` once per application frame, then read
`window.Input.Snapshot`. Polling again before reading starts a new
frame, so an edge is reported by exactly one completed frame and is
never replayed. Resize, pixel-size, close, and quit handling is
unchanged, and the window event queue is not touched by the input
path. The pump is the only SDL event consumer in the engine; the input
subsystem never polls SDL itself.

Before the first poll, `Snapshot` is `InputSnapshot.Empty`: no focus,
no keys or buttons down, no edges, and no motion.

## Keys and buttons

`KeyCode` is a backend-independent physical-key enumeration. The
backend maps SDL scancodes to it through a lookup table, so the key
that sits where `W` is on a US layout keeps the `KeyCode.W` identity
on every keyboard layout. The current set covers letters, number
keys, function keys, arrows, navigation keys, modifiers, and common
punctuation. Unsupported or unknown scancodes are ignored.

`MouseButton` covers `Left`, `Right`, `Middle`, `X1`, and `X2`; the
adapter maps SDL's native button numbers explicitly because they are
not in the same order.

Every key and button has three queries with the same semantics:

- `IsKeyDown` / `IsMouseButtonDown`: held at the end of the frame.
- `WasKeyPressed` / `WasMouseButtonPressed`: up-to-down transition
  during the frame.
- `WasKeyReleased` / `WasMouseButtonReleased`: down-to-up transition
  during the frame.

A press and release within one frame reports both edges; `IsKeyDown`
reflects the last transition. A held key or button never repeats its
press edge, native key repeats add no edge, and duplicate down or up
events without the opposite transition are ignored. Unknown enum
values are safe: they are never reported as down, pressed, or
released.

Actions should consume the edge, not the held state. `IsKeyDown`
answers "what is held now", while `WasKeyPressed` answers "what
happened this frame"; only the edge can represent a click or a tap
that both started and ended between two polls, and only the edge is
naturally one-shot. The mesh demo's capture controller makes its
capture-versus-fire decision from `WasMouseButtonPressed` alone, so a
held button cannot fire twice and no second edge tracker exists to
disagree with it.

`KeyCode.Unknown` is zero and is never a valid key, so a default value
is inert. `KeyCode` and `MouseButton` values are dense so a snapshot
can pack them into a fixed bitset; they are not native codes.

## Mouse coordinates and motion

- `MousePosition` is the position from the most recent motion event in
  **logical window coordinates**. It is zero until the first motion
  event and holds the last position across frames without motion. On
  a high-density display the logical units are smaller than
  framebuffer pixels, so position must not be compared with, or used
  as, a pixel coordinate.
- `MouseMotion` is the accumulated relative movement of the frame in
  logical units. Multiple native motion events are summed, the value
  resets at the next poll, and it is never clamped to the window
  bounds or wrapped.
- `RelativeMotion` is the part of that movement that arrived while
  relative mouse mode was active. It is the value an FPS camera should
  consume; it is zero outside capture, including the frame that
  starts capture, so pre-capture movement cannot jump the view.
  Consume either `MouseMotion` or `RelativeMotion`, never both.
- `WheelDelta` is the accumulated wheel movement in notches, positive
  up, with flipped-device values normalized by the adapter. It resets
  at the next poll.

The subsystem never warps, re-centers, or globally positions the
pointer, and it never applies desktop-global movement.

## Focus

`InputSnapshot.KeyboardFocused` reports whether this window had the
native keyboard focus at the end of the frame. `MouseFocus` is one of
`None` (no native window owns the pointer), `OwnWindow`, or
`OtherWindow` (another native window of this process owns it).

Losing keyboard focus clears held keys and buttons and suppresses the
frame's edges without reporting releases. An unfocused window
therefore produces no gameplay press edges, a previously held key
cannot get stuck down, and a click that lands on an unfocused window
cannot become a gameplay click. Regaining focus does not invent a
press: a key that is still physically held stays up until the backend
reports a new down transition. Repeated focus transitions are safe,
and focus is queried after dispatch so a change and a simultaneous
press in one poll resolve to the state the frame ended in.

A frame is also treated as a whole, not just as its final state. When
a focus-lost notification arrives, the frame is cleared immediately
and `InputSnapshot.FocusLostDuringFrame` stays true even if focus is
back before the frame completes. Such a frame reports no held input,
no edges, no motion, and no wheel, so a key or click delivered during
the unfocused part of the poll cannot appear as a gameplay edge once
the final focus query says the window is focused again. An
application that owns a captured pointer should release it when that
flag is set, because `KeyboardFocused` alone cannot distinguish a
quiet focused frame from one that lost focus and recovered. The flag
is conservative: it also drops transitions that arrived after focus
returned within the same poll, and the next frame is normal.

## Relative mouse mode

`WindowInput` owns the native operations:

- `TrySetRelativeMouseMode(bool)` returns whether the backend reported
  success.
- `IsRelativeMouseModeEnabled` queries the live native state instead
  of trusting the last request, because a call can fail yet still
  change the mode.
- `DiscardRelativeMotion` drains native relative motion accumulated
  before a transition.

The platform never enables or disables the mode on its own. Making a
click capture the pointer, releasing on Escape or focus loss, and
retrying a failed release are application policy; the mesh demo keeps
that state machine in `CameraCaptureController` over these delegates.
Motion arrives through the same accumulated motion events as
`MouseMotion`, so an application that reads `RelativeMotion` does not
also read SDL's separate relative-motion accumulator, and movement is
not counted twice.

## Thread affinity and allocation

Every member must be used on the thread that created the window,
which is the application's main thread. The pump holds an internal
lock around SDL's global queue, but input state itself is not
thread-safe.

After initialization, polling a frame and reading a snapshot perform
no managed allocation: the accumulator is a struct of bitsets and
vectors, snapshots are structs, and queries are bit tests. There are
no per-frame arrays, dictionaries, LINQ queries, or boxed enumerators.
The per-window state is fixed size; native calls are the only work
proportional to activity. The test suite measures
`GC.GetAllocatedBytesForCurrentThread` across 10,000 warmed-up quiet
and input-active frames and requires zero bytes for both. That
measurement covers the managed input path only; SDL's internal native
allocations are outside it.

## Limitations

- The supported key set is the subset listed in `KeyCode`; keypad,
  media, and IME/text-input events are not exposed.
- No gamepad, touch, pen, or text-input support.
- `MousePosition` is event-driven, so it is zero until the pointer
  first moves over the window.
- Only the primary window's client area is represented; no multi-seat
  or per-device routing.
- The pump is process-wide and assumes one application thread.
- A click that both focuses an unfocused window and presses a button
  arrives in one poll whose final focus is true and that contains no
  focus-lost notification. The press is therefore indistinguishable
  from an ordinary click; blocking it would require pre-dispatch focus
  history, which the pump does not keep. A sample may see such a click
  capture the pointer, depending on the backend's event order.
