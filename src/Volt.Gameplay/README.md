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

## Verification

```bash
dotnet test tests/Volt.Gameplay.Tests -c Release

cd benchmarks/Volt.Benchmarks
dotnet run -c Release -- --filter '*GameplayTagBenchmarks*'
```

The test suite includes differential tests that reproduce Unreal's own
tag and query test expectations, lifecycle and invalid-input cases, a
concurrent-registration test, and allocation guards asserting that
warmed-up matching and container mutation allocate zero managed bytes.
