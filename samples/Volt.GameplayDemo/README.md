# Volt.GameplayDemo

A deterministic, graphics-free demonstration of event-driven gameplay
abilities, ability tasks, and gameplay-to-presentation cues. No graphics
device, window, or platform package is required.

```bash
dotnet run --project samples/Volt.GameplayDemo -c Release
```

The sample builds a small content set from `DemoSchema`: stamina and
speed attributes, a dash cost, a cooldown, a temporary dash buff, a
stun, a guard window, and four abilities. It then advances simulation
time manually and prints every observable transition.

## Scenarios

1. **Charged Dash**: pays 10 stamina, runs a 0.25 s wind-up, waits for
   `VoltDemo.Event.Dash.Released`, applies a 0.5 s speed buff, waits for
   the buff's `Status.Dashing` tag to disappear, and ends.
2. **Interrupted wind-up**: a stun cancels the dash. The paid cost and
   cooldown remain, the pending task is released, its handle is stale,
   and advancing time plus a late release event cannot revive it.
3. **Overlapping abilities**: charged dash and focus own one task each.
   Cancelling the dash leaves the focus activation and its task intact.
4. **Application tasks**: an ability with no plan starts a duration task
   directly, inspects it, completes it, and shows that the completed
   handle is stale. Ending an ability cancels its task, and a handle
   from that activation cannot cancel the next activation's task.
5. **Owner destruction**: destroying an entity with a pending task
   releases it and leaves no listener behind.
6. **Charged counterattack**: pays a 5 stamina cost, applies a 0.5 s
   guard, and races an incoming-hit event against a 0.5 s timeout in one
   composite wait. The event wins, and a tag and attribute condition
   selects the standard or the enhanced counter. The standard branch
   starts a registered custom task and waits for the demo's interaction
   ticker to complete it; the enhanced branch lands immediately when
   the target is stunned. A tag wait on the guard then ends the ability.
   A stun during the interaction interrupts the ability, releases the
   custom task, and leaves its pending handle stale.
7. **Pause, single step, and time scaling**: a pending task does not
   advance while the loop is paused, advances once with `StepOnce`, and
   scales with the loop's time scale.
8. **Gameplay cue presentation**: an effect drives persistent add,
   stack-update, periodic execute, and expiration transitions; charged
   and interrupted abilities own cues that clean up on completion or
   cancellation; target destruction retires a cue. A bounded FIFO is
   drained into a graphics-free mock consumer, which asserts that every
   added cue was eventually removed.

Every expectation is an assertion. The process exits with status 1 and
prints the failing assertion if the behavior changes.
