# Volt.Gameplay

Optional gameplay systems: hierarchical gameplay tags, entity tag
participation, attributes, effects, and abilities. The module is a
separate assembly, so a project that never uses gameplay pays no
startup cost and the ECS core never depends on it.

The behavioral reference is Unreal Engine 5.8.4. Every implemented
rule, its source, and every intentional difference is recorded in
[GameplaySpec.md](GameplaySpec.md).

## Design in one paragraph

Tags are interned four-byte identifiers with a cached ancestor chain.
Matching is integer binary search, never string comparison, and
warmed-up matching allocates nothing. Shared definitions hold the
immutable `GameplayTagSet`; authoring and incremental mutation use
`GameplayTagContainer`. A query is an immutable expression tree over
those primitives. Nothing is polled per frame: later milestones drive
effect deadlines from the existing fixed-step schedule, and the tag
layer has no frame cost at all.

## Tags

```csharp
using Volt.Gameplay.Tags;

GameplayTagRegistry tags = GameplayTagRegistry.Shared;

GameplayTag stunned = tags.Register("Status.Stunned");
GameplayTag status = tags.Register("Status");          // already implicit
GameplayTag burning = tags.Register("Status.Burning");

// A tag knows its hierarchy.
Debug.Assert(stunned.MatchesTag(status));
Debug.Assert(!stunned.MatchesTagExact(status));
Debug.Assert(stunned.DirectParent == status);

// Immutable sets: build once, share, query for free.
GameplayTagSet requirements = GameplayTagSet.Create(status, burning);

GameplayTagSet current = GameplayTagSet.Create(stunned);

Debug.Assert(current.HasTag(status));          // ancestor matches
Debug.Assert(current.HasAll(GameplayTagSet.Empty));
Debug.Assert(!current.HasAll(requirements));   // Burning is missing

GameplayTagQuery query = GameplayTagQuery.AllOf(
    GameplayTagQuery.AnyTags(stunned, burning),
    GameplayTagQuery.NoTags(tags.Register("Status.Immune")));

Debug.Assert(query.Matches(current));

// Mutable authoring container.
GameplayTagContainer container = new(stunned);
container.Add(burning);
container.Remove(stunned);
Debug.Assert(container.HasTagExact(burning));
Debug.Assert(container.HasTag(status));

// Persist names, never ids.
string text = container.Serialize();
GameplayTagContainer restored = GameplayTagContainer.Parse(text);
```

An invalid tag matches nothing, including itself. Adding an invalid tag
to a mutable container is ignored, like Unreal's `AddTag`; an immutable
set rejects it at construction. Unregistered names resolve to
`GameplayTag.None` from `TryGetTag`, and `GetTag` throws.

Tag registration is process-wide and append-only. Register during
initialization; matching is safe from any thread afterwards.

## Entity tags

```csharp
using Volt.Ecs;
using Volt.Gameplay.Tags;

using World world = new();
using GameplayTagStore tags = new(world);

Entity player = world.CreateEntity();
GameplayTag stunned = GameplayTagRegistry.Shared.Register("Status.Stunned");

// Grants are counted: two sources can each add the same tag.
tags.AddTag(player, stunned);
tags.AddTag(player, stunned);

Debug.Assert(tags.GetTagCount(player, stunned) == 2);
Debug.Assert(tags.HasTag(player, GameplayTagRegistry.Shared.Register("Status")));

// Removing one source leaves the tag present.
tags.RemoveTag(player, stunned);
Debug.Assert(tags.HasTagExact(player, stunned));

// Destruction releases every grant; a stale handle sees nothing.
world.DestroyEntity(player);
Debug.Assert(!tags.HasTag(player, stunned));
```

The store is sparse: an entity with no tags has no entry array, and
tagging one entity in a million-entity world reserves one 1,024-slot
segment rather than a million-entry table. `HasTag` is flat in world
size (0.7 ns untagged, 1.7 ns hierarchical on a 1,000,000-entity
world in the measured benchmark). Two grants of the same tag require
two removals; destroying an entity clears all of them, and entity
generations prevent a recycled index from exposing its predecessor's
state.

## Attributes

```csharp
using Volt.Gameplay.Attributes;

GameplayAttribute health = GameplayAttributeRegistry.Shared.Register(
    "Health",
    defaultBaseValue: 100f,
    minimum: 0f,
    maximum: 100f);

using GameplayAttributeStore attributes = new(world);

attributes.SetBaseValue(player, health, 70f);

GameplayModifierHandle burning = attributes.AddModifier(
    player,
    health,
    GameplayModifierOperation.AddFinal,
    -10f);

Debug.Assert(attributes.GetCurrentValue(player, health) == 60f);
Debug.Assert(attributes.GetBaseValue(player, health) == 70f);

attributes.AttributeChanged += change =>
    Console.WriteLine($"{change.Attribute}: {change.NewCurrentValue}");

attributes.RemoveModifier(burning);
Debug.Assert(attributes.GetCurrentValue(player, health) == 70f);
```

The base value holds permanent changes; the current value includes
modifiers. Modifiers aggregate in Unreal's order — add, multiply,
divide, compound multiply, final add, with any override replacing the
result — and the current value is cached, so reads never walk the
modifier list. Writes clamp to the registered range and raise one
`AttributeChanged` event with old and new values. A modifier handle
uses slot generations, so a stale handle cannot remove a later
modifier that reused its slot.

## Effects

```csharp
using Volt.Gameplay.Effects;

GameplayEffectDefinition burn = GameplayEffectDefinition
    .Create("Status.Burning")
    .HasDuration(10f)
    .Period(1f)
    .Modifier(health, GameplayModifierOperation.AddBase, -2f)
    .GrantTags(burning)
    .Build();

GameplayEffectDefinition slow = GameplayEffectDefinition
    .Create("Status.Slow")
    .HasDuration(5f)
    .Modifier(speed, GameplayModifierOperation.MultiplyAdditive, 0.5f)
    .GrantTags(slowed)
    .Stacking(GameplayEffectStackingType.AggregateByTarget, limit: 3)
    .Build();

using GameplayEffectSystem effects = new(world, attributes, tags);

effects.Changed += change =>
    Console.WriteLine($"{change.Kind}: {change.Definition.Name}");

GameplayEffectApplyResult applied = effects.Apply(
    player,
    burn,
    source: caster);

Debug.Assert(applied.Status == GameplayEffectApplyStatus.Applied);

// Re-applying a stackable effect merges into the existing instance.
Debug.Assert(effects.Apply(player, slow).StackCount == 1);
Debug.Assert(effects.Apply(player, slow).StackCount == 2);

// Drive the clock with the simulation step; idle effects cost one
// comparison.
effects.Advance((float)deltaSeconds);

Debug.Assert(effects.TryGetEffect(applied.Handle, out ActiveGameplayEffect active));
Debug.Assert(active.StackCount == 1);

// Removing the instance restores every value and tag it contributed.
effects.Remove(applied.Handle);
```

Instant effects execute once against the base value and are never
tracked. Duration and infinite effects hold their modifiers and granted
tags until they end, are removed, or are inhibited by their ongoing tag
requirements. Periodic effects execute once per period instead of
holding a modifier, so damage over time is permanent damage per tick.

One binary min-heap holds every expiry and periodic deadline, ordered
by due time and then by scheduling order. Advancing time processes only
what is due, so 100,000 idle effects cost the same 1.7 ns per step as an
empty world, and a large step replays every tick in order, matching the
result of many small steps. Nothing is polled per frame. If a
destruction handler recycles the target index before the effect system
sees the old destruction, the first effect linked to the replacement
retires the old chain and its deadlines; there is no world scan and no
per-frame cleanup.

Definitions are validated when built: an unregistered attribute, a
non-finite magnitude, an instant effect with a period, a stack limit
without a stacking type, or an effect that grants a tag its own ongoing
requirements forbid all throw before reaching gameplay.

## Abilities

```csharp
using Volt.Gameplay.Abilities;

GameplayEffectDefinition dashCooldown = GameplayEffectDefinition
    .Create("Ability.Dash.Cooldown")
    .HasDuration(2f)
    .GrantTags(tags.Register("Cooldown.Dash"))
    .Build();

GameplayAbilityDefinition dash = GameplayAbilityDefinition
    .Create("Ability.Dash")
    .AssetTags(tags.Register("Ability.Dash"))
    .Cost(manaCost)          // an instant effect
    .Cooldown(dashCooldown)
    .RequireToActivate(GameplayTagRequirements.Ignore(
        GameplayTagSet.Create(tags.Register("Status.Stunned"))))
    .Build();

using GameplayAbilitySystem abilities = new(world, tags, effects, attributes);

abilities.Changed += change =>
    Console.WriteLine($"{change.Kind}: {change.Definition.Name}");

GameplayAbilityHandle dashHandle =
    abilities.GiveAbility(player, dash, inputId: 1);

GameplayAbilityActivationResult result =
    abilities.TryActivate(dashHandle);

Debug.Assert(result.Status == GameplayAbilityActivationStatus.ActivatedAndEnded);

// The cost was paid and the cooldown effect now grants its tag.
Debug.Assert(abilities.IsOnCooldown(player, dash));
Debug.Assert(!abilities.TryActivate(dashHandle).Succeeded);

// A lasting ability stays active until it is ended or cancelled.
GameplayAbilityHandle channel = abilities.GiveAbility(player, channelAbility);
abilities.TryActivate(channel);
abilities.Cancel(channel);
```

Activation runs Unreal's checks in order: cooldown, cost, owner tag
requirements, target tag requirements, blocking by an active ability,
and finally the already-active rule. Each failure is reported as a
`GameplayAbilityActivationStatus` and changes nothing. Costs and
cooldowns are effects, so a cooldown is simply the tags its effect
grants: an external effect that grants the same tag blocks the ability
too, and cooldown time is the longest remaining duration among all
effects that grant those tags.

The cooldown is applied before the cost, and both are checked again at
commit. A cooldown that refuses to apply rejects the activation before
payment; a cost that becomes unaffordable or refuses to apply rolls the
cooldown back, so a failed commit pays nothing. An activation that a
notification ends before payment also rolls the cooldown back. Rollback
is operation-specific: a new cooldown is removed, while a stacked
existing cooldown keeps its previous stack count, source, and deadline.
A failed commit raises no `Activated` or `Cancelled` notification; the
result carries the failure.

Handles are scoped to the ability system that created them as well as
generation-checked, so two systems over one world cannot resolve each
other's grants. Notifications may re-enter the system: an activation or
revocation in progress refuses a nested activation or a second
revocation of the same grant, and bulk cancellation, revocation, and
input activation operate on the grants present when they start. A
grant added by a callback survives the operation that triggered it.
Destroying an owner releases its grants even if another destruction
handler recycles the entity index before this system observes the
destruction.

An ability ends on activation by default, because a data definition has
no tasks to keep it alive; call `EndOnActivate(false)` for one that
lasts. `CancelOnTags` cancels a lasting ability when the owner gains a
matching tag, `CancelAbilitiesWithTags` cancels other abilities on
activation, and `BlockAbilitiesWithTags` blocks matching asset tags
while active. Grants are validated when built: a non-instant cost, an
instant cooldown, a cooldown without granted tags, and a cooldown that
grants the ability's own cancel tag all throw before reaching gameplay.

## Gameplay events

```csharp
using Volt.Gameplay.Events;

GameplayTag damageReceived = tags.Register("Event.Damage.Received");

using GameplayEventSystem events = new(world);

GameplayEventSubscription subscription = events.Subscribe(
    damageReceived,
    gameplayEvent =>
    {
        if (gameplayEvent.Payload.TryGetNumber(out double amount))
        {
            Console.WriteLine($"{amount} damage to {gameplayEvent.Target}");
        }
    },
    owner: player);

events.Publish(new GameplayEvent(
    damageReceived,
    source: attacker,
    target: player,
    payload: GameplayEventPayload.FromNumber(12.5)));

events.Unsubscribe(subscription);
```

Subscriptions are indexed by tag, so publishing touches only the
buckets that can match: the exact tag, its ancestors for hierarchical
listeners, and the wildcard bucket. Listeners on unrelated tags are
never scanned. Exact matching is the default; pass
`GameplayEventMatch.Hierarchical` to receive descendants. Dispatch
captures the matching subscriptions before invoking any handler and
revalidates each delivery, so a handler may unsubscribe itself,
subscribe another listener, publish another event, cancel an ability,
or destroy an entity without invalidating the iteration. A subscription
scoped to an entity is removed when that entity is destroyed, and the
stored generation stops a recycled index from reviving it. Recursive
publishing is bounded at 16 levels.

## Ability tasks

```csharp
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tasks;

GameplayAbilityDefinition chargedDash = GameplayAbilityDefinition
    .Create("Ability.ChargedDash")
    .AssetTags(tags.Register("Ability.Dash"))
    .Cost(staminaCost)
    .Cooldown(dashCooldown)
    .CancelOnTags(tags.Register("Status.Stunned"))
    .Plan(
        GameplayAbilityPlan
            .Create()
            .Wait(0.25f)
            .WaitEvent(tags.Register("Event.Dash.Released"))
            .ApplyEffect(dashBuff)
            .WaitTag(
                tags.Register("Status.Dashing"),
                GameplayTagWaitMode.Absent)
            .End()
            .Build())
    .Build();

GameplayAbilityHandle handle = abilities.GiveAbility(player, chargedDash);

GameplayAbilityActivationResult result = abilities.TryActivate(handle);

// Keep the token to start application tasks on this activation.
GameplayAbilityActivation activation = result.Activation;

GameplayAbilityTaskHandle task = abilities.Tasks.WaitDuration(
    activation,
    0.5f);

abilities.Tasks.Changed += change =>
    Console.WriteLine($"{change.Kind}: {change.TaskKind}");
```

Tasks are owned by an activation token, not just a grant, so a retrigger
cannot reuse an earlier activation's context. A task handle carries the
task system, owner, slot generation, and activation id; completing,
cancelling, retriggering, revoking, or destroying the owner retires it.
`Cancel`, `Complete`, and `TryGetTask` return `false` for a stale handle
instead of touching a recycled slot.

A plan is data, and every step runs on the same low-level task runtime
as application code. `Wait` and `WaitTag` use a deadline heap;
`WaitEvent` uses the event router. Tag-condition tasks are indexed by
the watched tag and by exact or hierarchical matching, so a tag change
visits only the tasks watching that tag or an ancestor, not every task
on the owner. Ending an ability releases all of its tasks before the
ability notification, and the `Cancelled` task notifications follow the
release. A zero-duration task completes on the next positive
`Advance`, a zero step is a pause, and tasks created during an advance
wait for the next advance.

Chained waits follow the fixed-step rule: a task created by a
completion callback starts from the end-of-step clock and is eligible
on the next advance. That makes the fixed-step loop the deterministic
path for long chains; a single large manual advance is not equivalent
to several fixed steps. If a plan step or activation notification
throws, the activation is cancelled, its tasks and subscriptions are
released, committed costs and cooldowns remain, and the exception is
rethrown. A cleanup failure is reported with the original in an
`AggregateException`; an ability is never left active with no plan
cursor and no pending task.

## Fixed-step simulation

```csharp
using Volt.Core.Timing;
using Volt.Gameplay.Simulation;

GameLoop loop = new();
GameplaySimulation simulation = new(loop, abilities, effects);

// Once per rendered frame; zero or more fixed steps run inside.
loop.Advance();
```

`GameplaySimulation` is the optional bridge from the engine loop to
gameplay. It registers one post-fixed-update callback, so the order in
every executed fixed step is:

1. Game fixed-update callbacks and scheduled simulation callbacks.
2. Effects advance: expiries, periods, and tag changes.
3. Tasks advance: duration and tag deadlines complete; plans continue.
4. Remaining post-fixed callbacks.

Event waits complete synchronously when their event is published, as
they do without the integration. A task started by game fixed-update
logic can complete in that same step; a task created by a completion
callback waits for the next step. Pause, single-step, time scaling, and
catch-up are the loop's semantics: the integration advances once for
every executed fixed step and never invents a second clock.

While attached, calling `effects.Advance` or `tasks.Advance` directly
throws `InvalidOperationException`, so the same clock cannot be
advanced twice in a step. Dispose `GameplaySimulation` to unregister
the callback and return to manual control. It does not own the world or
the stores, and it does not create a task runtime until an ability
uses one.

Construction requires the exact effect system the abilities apply
costs, cooldowns, and plan effects through; a different instance in the
same world is rejected, because advancing it would leave the ability
clocks unmoved. A clock claim also carries the identity of its
integration, so construction that fails part way through releases only
the claims it took and never detaches an integration that already owns
the clock.

Run `samples/Volt.GameplayDemo` for the complete charged-dash example,
including a stun interruption, overlapping abilities, stale handles,
owner destruction, pause, single-step, time scaling, and clean
shutdown.

## Projects

| Type | Purpose |
| --- | --- |
| `GameplayTag` | Interned tag handle with hierarchy queries |
| `GameplayTagRegistry` | Name interning, validation, cached ancestry |
| `GameplayTagSet` | Immutable set used by definitions and queries |
| `GameplayTagContainer` | Mutable authoring container |
| `GameplayTagQuery` | Nested AND/OR/NOT expression over tags |
| `GameplayTagStore` | Reference-counted tags for one world's entities |
| `GameplayAttribute` | Interned attribute handle with a shared definition |
| `GameplayAttributeStore` | Base/current values, modifiers, and change events |
| `GameplayModifierHandle` | Generation-checked removal handle for a modifier |
| `GameplayEffectDefinition` | Immutable effect: duration, period, modifiers, tags, stacking |
| `GameplayEffectDefinitionBuilder` | Fluent, validating authoring path |
| `GameplayEffectSystem` | Application, scheduling, inhibition, and removal |
| `GameplayEffectHandle` | Generation-checked handle to an active instance |
| `GameplayTagRequirements` | Required and ignored tag pair used for gating |
| `ActiveGameplayEffect` | Read-only snapshot of one active instance |
| `GameplayEffectChange` | Notification payload for every effect transition |
| `GameplayAbilityDefinition` | Immutable ability: requirements, cost, cooldown, block and cancel tags |
| `GameplayAbilityDefinitionBuilder` | Fluent, validating authoring path |
| `GameplayAbilitySystem` | Grants, activation checks, blocking, and cancellation |
| `GameplayAbilityHandle` | System- and generation-checked handle to a grant |
| `GameplayAbilityChange` | Notification payload for every ability transition |
| `GrantedGameplayAbility` | Read-only snapshot of one grant |
| `GameplayEventSystem` | Tag-indexed gameplay event routing and scoped subscriptions |
| `GameplayEventSubscription` | System- and generation-checked subscription handle |
| `GameplayEvent` | Value-type event with tag, source, target, and payload |
| `GameplayEventPayload` | Discriminated numeric, entity, or tag payload |
| `GameplayAbilityTaskSystem` | Pooled task storage, deadlines, and event or tag waits |
| `GameplayAbilityTaskHandle` | System-, owner-, generation-, and activation-checked task handle |
| `GameplayAbilityTaskChange` | Notification payload for started, completed, or cancelled tasks |
| `GameplayAbilityTaskInfo` | Read-only snapshot of one pending task |
| `GameplayAbilityPlan` | Immutable authored wait and action sequence |
| `GameplayAbilityPlanBuilder` | Fluent, validating plan authoring path |
| `GameplayAbilityActivation` | Grant plus activation id, used as task ownership |
| `GameplaySimulation` | Optional fixed-step driver for effects and tasks |

## Verification

```bash
dotnet test tests/Volt.Gameplay.Tests -c Release

cd benchmarks/Volt.Benchmarks
dotnet run -c Release -- --filter '*GameplayTagBenchmarks*'
dotnet run -c Release -- --filter '*GameplayAbilityBenchmarks*'
dotnet run -c Release -- --filter '*GameplayAbilityScalingBenchmarks*'
dotnet run -c Release -- --filter '*GameplayLifecycleBenchmarks*'
dotnet run -c Release -- --filter '*GameplayEventBenchmarks*'
dotnet run -c Release -- --filter '*GameplayAbilityTaskBenchmarks*'
dotnet run -c Release -- --filter '*GameplayAbilityTaskIdleBenchmarks*'
dotnet run -c Release -- --filter '*GameplayAbilityTaskScalingBenchmarks*'
dotnet run -c Release -- --filter '*GameplayAbilityTaskDestructionBenchmarks*'
dotnet run -c Release -- --filter '*GameplayTagWaitScalingBenchmarks*'
dotnet run -c Release -- --filter '*GameplayEventSparseBenchmarks*'
dotnet run -c Release -- --filter '*GameplaySimulationBenchmarks*'
dotnet run -c Release -- --filter '*GameplaySimulationEventBenchmarks*'
dotnet run -c Release -- --filter '*GameplaySimulationTaskBurstBenchmarks*'
dotnet run -c Release -- --filter '*GameplaySimulationEffectBurstBenchmarks*'

dotnet run --project samples/Volt.GameplayDemo -c Release
```

The test suite includes differential tests that reproduce Unreal's own
tag and query test expectations, effect and ability behavior read from
the 5.8.4 source, lifecycle and invalid-input cases, adversarial
destruction ordering, cross-store recycling, re-entrancy and notification
exception cases, a concurrent-registration test, and allocation guards
asserting that warmed-up matching, container mutation, effect
application, effect ticking, ability granting, ability activation,
event dispatch and subscription, task start, event and tag waits, bulk
task expiry, ability ending with tasks, and entity recycling with live
gameplay state allocate zero managed bytes. The gameplay demo is a
separate executable smoke test whose assertions cover the combined
charged-dash lifecycle.

`LifecycleStressHarness` adds a seed-replayable state machine over all
four stores. It generates operations at the top level and from
notifications, recycles entity and effect slots, retains stale handles,
and checks an independent tag/attribute/effect/ability model after every
operation. The permanent matrix is 128 seeds at 500 operations under
both destruction callback orders; failures report the seed, operation,
trace, and store dump, and a failing prefix can be replayed exactly.
