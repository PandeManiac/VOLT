# VOLT Gameplay Behavioral Specification

**Reference engine:** Unreal Engine 5.8.4, local source at
`/home/berds/applications/unreal-engine-5.8.4`.
**Status:** living document. Sections are added as milestones land.
**Last updated:** 2026-10-23.

This document records the behavior VOLT implements, where that behavior
comes from, and where VOLT deliberately differs. It is the source of
truth for tests: a test should assert a rule stated here, not merely
whatever the current implementation happens to do.

## Evidence legend

| Tag | Meaning |
| --- | --- |
| `SRC` | Read directly from Unreal 5.8.4 source. File and symbol cited. |
| `UTEST` | Asserted by Unreal's own automated tests. File and test cited. |
| `DOCS` | Stated by Epic's official 5.8 documentation. Link cited. |
| `RUNTIME` | Observed in a running Unreal project. |
| `INFER` | Follows from source or documentation but was not executed. |
| `DIFF` | VOLT intentionally differs. |

## Reference material

- `Engine/Source/Runtime/GameplayTags/Classes/GameplayTagContainer.h`
  — `FGameplayTag`, `FGameplayTagContainer`, `FGameplayTagQuery`,
  `FGameplayTagQueryExpression`.
- `Engine/Source/Runtime/GameplayTags/Private/GameplayTagContainer.cpp`
  — matching, filtering, query evaluation (`FQueryEvaluator`).
- `Engine/Source/Runtime/GameplayTags/Classes/GameplayTagsManager.h`
  — `FGameplayTagNode`, registration sources.
- `Engine/Source/Runtime/GameplayTags/Private/GameplayTagsManager.cpp`
  — `RequestGameplayTag`, `ExtractParentTags`,
  `IsValidGameplayTagString`.
- `Engine/Source/Runtime/GameplayTags/Private/Tests/GameplayTagTests.cpp`
  — `GameplayTagTest_TagComparisonTest`,
  `GameplayTagTest_TagContainerTest`.
- `Engine/Source/Runtime/GameplayTags/Private/Tests/GameplayTagQueryTests.cpp`
  — `FGameplayTagQueryTest_Empty`.
- `Engine/Plugins/Runtime/GameplayAbilities/Source/GameplayAbilities/`
  — `Public/Abilities/GameplayAbility.h` and
  `Private/Abilities/GameplayAbility.cpp` (`CanActivateAbility`,
  `CheckCooldown`, `CheckCost`, `ApplyCooldown`, `ApplyCost`,
  `EndAbility`, `CancelAbility`, `PreActivate`),
  `Private/AbilitySystemComponent_Abilities.cpp` (`GiveAbility`,
  `InternalTryActivateAbility`, `CancelAbilities`,
  `ApplyAbilityBlockAndCancelTags`, `AreAbilityTagsBlocked`), and
  `Private/GameplayEffect.cpp`
  (`FActiveGameplayEffectsContainer::CanApplyAttributeModifiers`).
- Epic, *Using Gameplay Tags in Unreal Engine* (5.8):
  <https://dev.epicgames.com/documentation/en-us/unreal-engine/using-gameplay-tags-in-unreal-engine>
- Local reference project: `/home/berds/programming/unreal/VoltUnrealReference`.
  `Config/DefaultGameplayTags.ini` declares `Ability.Dash`,
  `Status.Burning`, and `Status.Stunned`.
- Epic, *Gameplay Ability Tasks in Unreal Engine* (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/gameplay-ability-tasks-in-unreal-engine>
  — task lifetime, `ReadyForActivation`, ending with the ability.
- Epic, `UAbilityTask` API (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/GameplayAbilities/UAbilityTask>
  — the owning `Ability` and task helpers.
- Epic, `UGameplayTask` API (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/API/Runtime/GameplayTasks/UGameplayTask>
  — `InitTask`, `ReadyForActivation`, `EndTask`, `TaskOwnerEnded`.
- Epic, `UAbilityTask_WaitDelay` API (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/GameplayAbilities/UAbilityTask_WaitDelay>
  — wait for a duration, `OnFinish`.
- Epic, `UAbilityTask_WaitGameplayEvent` API (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/GameplayAbilities/UAbilityTask_WaitGameplayEvent>
  — one-shot listening, `OnlyMatchExact`, optional external target.
- Epic, `SendGameplayEventToActor` API (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemBlueprintLibrary/SendGameplayEven->
  — event tag and payload delivery through an ability system.
- Epic, `UAbilitySystemComponent::HandleGameplayEvent` API (5.8):
  <https://dev.epicgames.com/documentation/unreal-engine/API/Plugins/GameplayAbilities/UAbilitySystemComponent/HandleGameplayEvent>
  — execute an event and count matching activations.

---

# Part 1 — Gameplay Tags

Implemented in `Volt.Gameplay.Tags`.

## 1.1 Registered tags are required

**Rule.** A tag exists only after it is registered. Resolving an
unregistered name returns the invalid tag; it does not invent one.

- `SRC` `UGameplayTagsManager::RequestGameplayTag`: if the name is not
  in the node map, the manager reports an error and returns
  `FGameplayTag()`. Only editor flows add tags on demand.
- `DOCS`: "You must add Gameplay Tags to the tag dictionary for Unreal
  Engine to be aware of them."
- `DIFF`: VOLT has no editor source model, redirects, or runtime
  unloading. `GameplayTagRegistry.TryGetTag` never mutates the
  registry; `GetTag` throws with an actionable message.

## 1.2 Names, identity, and hierarchy

**Rule.** A tag is a dot-separated name. `A.B.C` has the ancestors
`A.B` and `A`, ordered nearest-first. A tag's depth is its number of
segments. Tags are interned: two registrations of the same name yield
the same tag.

- `DOCS`: tags are hierarchical labels with any number of levels
  separated by `.`; `Event.Movement.Dash` has three levels.
- `SRC` `FGameplayTag::ParseParentTags`: splits at the last period
  repeatedly, so parents are enumerated immediate-first.
- `SRC` `FGameplayTagNode::GetSingleTagContainer`: a node carries a
  container of itself plus its ancestors.
- `DIFF`: VOLT tags are a 4-byte interned identifier rather than an
  `FName`. Ancestors are a cached array on the tag record, so matching
  performs no string work at all.
- `DIFF`: names compare case-insensitively, matching `FName`
  semantics (`SRC`-inferred from `FName` equality). The spelling used
  by the first registration is canonical.

**Rule.** Registering a tag implicitly registers all of its ancestors.

- `SRC` (structure): a node holds a parent node and child nodes, and
  `ExtractParentTags` resolves ancestors through the node map.
- `INFER`: the exact `bIsExplicitTag` flag differs between tags that
  were named directly and tags that exist only as parents. VOLT does
  not model "implicit" as a separate state: a tag is a tag. Nothing in
  the matching rules depends on how it was created.

**Rule.** The registry is append-only for the process lifetime, and an
identifier never changes meaning.

- `DIFF`: Unreal can unload tags the editor no longer references.
  VOLT does not, because stable identifiers are what let containers
  and entity tag storage avoid invalidation checks.

## 1.3 Valid names

**Rule.** A name is valid when it:

1. is non-empty;
2. does not begin or end with `.`;
3. has no empty segment (`A..B`);
4. contains no whitespace and no control characters;
5. contains none of `"`, `'`, or `,`.

- `SRC` `IsValidGameplayTagString` (private helper in
  `GameplayTagsManager.cpp`): rejects empty input, leading/trailing
  period, leading/trailing space, and characters rejected by
  `FName::IsValidXName` (the project config lists `"' ,` as invalid).
- `UTEST` `IsValidGameplayTagString` test sections: `.Tag`, `Tag.`,
  ` Tag`, `Tag `, and `Tag\t,Name` are all invalid.
- `DIFF`: VOLT rejects whitespace anywhere, not only at the ends. An
  interior space is a typo that is nearly invisible in logs, and there
  is no legitimate use for one.
- `DIFF`: VOLT accepts non-ASCII letters and `-`, which `FName` also
  accepts. No segment-length limit is imposed.

## 1.4 Exact and hierarchical matching

**Rule.** `Tag.MatchesTag(other)` is true when `Tag == other` or
`other` is an ancestor of `Tag`. `MatchesTagExact` is true only for the
same tag. The invalid tag matches nothing, including itself.

- `SRC` `FGameplayTag::MatchesTag`: delegates to the tag node's
  single-tag container, which contains the tag and its ancestors.
- `SRC` `FGameplayTag::MatchesTagExact`: returns false for an invalid
  argument, otherwise compares tag names.
- `UTEST` `GameplayTagTest_TagComparisonTest`: `Type1.MatchesTag(Damage)`
  true, `Type1.MatchesTagExact(Damage)` false, all invalid-tag cases
  false.
- `RUNTIME`: confirmed in the local Unreal project that a container
  holding `Status.Stunned` satisfies `Has Tag(Status)` with exact
  matching disabled.

**Rule.** `Container.HasTag(tag)` is true when the tag is explicit or
is an ancestor of an explicit tag. `HasTagExact` considers only
explicit tags.

- `SRC` `FGameplayTagContainer::HasTag`:
  `GameplayTags.Contains(Tag) || ParentTags.Contains(Tag)`.
- `UTEST` `GameplayTagTest_TagContainerTest`.

## 1.5 Set operations

Let `A` be the container being queried and `B` the argument. Both APIs
probe `B`'s **explicit** tags only; `A`'s cached ancestors count as
present.

| Operation | Result |
| --- | --- |
| `A.HasAny(B)` | true iff some explicit tag of `B` is present in `A` (directly or as `A`'s ancestor) |
| `A.HasAnyExact(B)` | true iff some explicit tag of `B` is explicit in `A` |
| `A.HasAll(B)` | true iff every explicit tag of `B` is present in `A` |
| `A.HasAllExact(B)` | true iff every explicit tag of `B` is explicit in `A` |

- `SRC` `FGameplayTagContainer::HasAny`, `HasAnyExact`, `HasAll`,
  `HasAllExact` inline bodies.
- `UTEST` `GameplayTagTest_TagContainerTest` for all of the above,
  including the empty cases below.

**Rule.** An empty argument is treated as follows: `HasAny` and
`HasAnyExact` return false; `HasAll` and `HasAllExact` return true.

- `SRC` early returns in each method.
- `UTEST`: `TagContainer.HasAll(EmptyContainer)` true,
  `HasAny(EmptyContainer)` false, and the same for an empty receiver.
- `DOCS`: "Calling a conditional function with an empty Gameplay Tag
  Container as the input parameter returns false, except for the All
  functions such as HasAll."

**Rule.** Container equality ignores insertion order, duplicate adds,
and cached ancestors: two containers are equal when their explicit tag
sets are equal.

- `SRC` `FGameplayTagContainer::operator==`: same explicit count and
  `HasAllExact(other)`.
- `UTEST`: `TagContainer == ReverseTagContainer` where only insertion
  order differs; a copy with an extra tag is unequal.
- `DIFF`: equality is exact set equality over sorted identifiers,
  which gives the same answer without the count-then-scan dance.

## 1.6 Tag-to-container matching and filtering

**Rule.** `Tag.MatchesAny(N)` asks whether `N` explicitly contains the
tag or one of the tag's ancestors. It is not the same question as
`N.HasTag(tag)`, which follows `N`'s ancestry instead.

- `SRC` `FGameplayTag::MatchesAny`: the tag node's single-tag
  container calls `HasAny(N)`.
- `UTEST`: `Type1.MatchesAny({Damage})` true;
  `Type1.MatchesAnyExact({Damage})` false.

**Rule.** `Filter(F)` keeps the explicit tags of the receiver that
match a tag of `F` hierarchically. `FilterExact(F)` keeps only tags
that `F` contains exactly. The result contains the kept tags as
explicit; their ancestors are cached, not added.

- `SRC` `FGameplayTagContainer::Filter` / `FilterExact`.
- `UTEST`: filtering `{Type1, Cue}` by `{Damage}` yields `{Type1}`;
  filtering by `{Cue}` exactly yields `{Cue}`.

**Rule.** `GetGameplayTagParents()` returns a container whose
**explicit** tags are the receiver's tags plus all of their ancestors.
This differs from a tag's single-tag container, where only the tag
itself is explicit.

- `SRC` `FGameplayTagContainer::GetGameplayTagParents` adds the parent
  set to the explicit set.
- `UTEST`: the parents container has `Damage` exact; the single-tag
  container has `Damage` non-exact only.

## 1.7 Queries

A query is a tree of boolean expressions. Tag predicates use the set
operations above.

| Expression | Empty tag list | Populated |
| --- | --- | --- |
| `AnyTagsMatch` | false | true iff any tag matches |
| `AllTagsMatch` | true | true iff all tags match |
| `NoTagsMatch` | true | true iff no tag matches |
| `AnyTagsExactMatch` | false | exact variant |
| `AllTagsExactMatch` | true | exact variant |
| `AnyExpressionMatch` | false | true iff any child matches |
| `AllExpressionMatch` | true | true iff all children match |
| `NoExpressionMatch` | true | true iff no child matches |

**Rule.** A query with no root expression matches nothing, including
against an empty container.

- `SRC` `FGameplayTagQuery::Matches`: `if (IsEmpty()) return false;`.
- `UTEST` `FGameplayTagQueryTest_Empty`: an empty query returns false
  for both empty and non-empty containers.

**Rule.** `NoTagsMatch` is the negation of `AnyTagsMatch`, not the
negation of `AllTagsMatch`; `NoExpressionMatch` negates "any child
matched".

- `SRC` `FQueryEvaluator::EvalNoTagsMatch` / `EvalNoExprMatch`.
- `UTEST`: `NoTagsMatch{tag}` is true for an empty container and false
  when the tag is present.

**Rule.** `Matches` short-circuits and allocates nothing once the query
is built.

- `DIFF`: Unreal serializes queries to a token stream. VOLT stores a
  small immutable object tree. Both are built off the hot path; VOLT's
  evaluation is an inlined switch over a node kind.
- `DIFF`: Unreal allows a tag set to be attached to an expression node
  even though only tag-kind nodes consult it. In Unreal's own test, an
  `AllExprMatch` node with a populated tag set and no children matches
  everything. VOLT's types cannot represent that combination, so this
  specific trap is removed rather than reproduced.
- `DIFF`: nesting is limited to 32 expressions. Evaluation recurses;
  an unbounded authored tree would overflow the stack. The limit is
  enforced when the query is built.

## 1.8 Invalid tags

| Situation | Result |
| --- | --- |
| `default(GameplayTag).IsValid` | false |
| `invalid.MatchesTag(anything)` | false |
| `anything.MatchesTag(invalid)` | false |
| `invalid.MatchesTag(invalid)` | false |
| `container.Add(invalid)` | ignored, returns false |
| `tagSet.Create(invalid)` | throws `ArgumentException` |
| `invalid.Name` | throws `InvalidOperationException` |
| `invalid.ToString()` | `"GameplayTag.None"` |

- `SRC` `FGameplayTag::MatchesTag` returns false through the
  single-tag container's `HasTag`, which rejects invalid tags.
- `SRC` `FGameplayTagContainer::AddTag`: `if (TagToAdd.IsValid())`.
- `UTEST`: invalid-tag matching cases.
- `DIFF`: the immutable set rejects invalid tags at construction
  instead of silently dropping them, because a set is usually a
  definition and a dropped tag there is a gameplay bug.

## 1.9 Serialization and reconstruction

**Rule.** Identifiers are process-local. Persist names, not ids. A set
serializes to comma-separated names sorted ordinally; parsing resolves
each name through the registry and throws if one is missing.

- `DIFF`: Unreal has binary `NetSerialize` and archive support for
  containers. VOLT does not yet have a wire format; the text form is a
  deliberate placeholder that is deterministic across processes and
  usable in tests and config. A binary format will be added with
  replication, together with a version tag, because numeric ids must
  never be written to disk.

## 1.10 Edge-case matrix

Each row is covered by an automated test.

| Case | Expected | Evidence |
| --- | --- | --- |
| Container `{A.1}` asks `HasTag(A)` | true | `RUNTIME`, `UTEST`, `SRC` |
| Container `{A.1}` asks `HasTagExact(A)` | false | `UTEST` |
| Container `{A.1}` asks `HasAll({A})` | true | `SRC` |
| Container `{A}` asks `HasAll({A.1})` | false | `SRC` |
| `HasAll(empty)` | true | `UTEST`, `DOCS` |
| `HasAny(empty)` | false | `UTEST`, `DOCS` |
| Empty container equals empty container | true | `SRC` |
| Two containers, same tags, different order | equal | `UTEST` |
| Duplicate `Add` | ignored, still one tag | `SRC` |
| `Add(invalid)` | ignored | `SRC` |
| Remove one of two tags sharing a parent | parent stops matching only when the last child goes | `SRC` (parent recomputation) |
| Remove a tag that is not explicit | false, no change | `SRC` |
| Query with no expression | false | `UTEST` |
| `AllOf()` | true | `UTEST`, `DOCS` |
| `AnyOf()` | false | `UTEST`, `DOCS` |
| `NoneOf()` | true | `UTEST`, `DOCS` |
| `AllTags()` | true | `UTEST`, `DOCS` |
| `AnyTags()` | false | `UTEST`, `DOCS` |
| `NoTags()` | true | `UTEST`, `DOCS` |
| Query nested deeper than 32 | rejected at build | `DIFF` |
| Tag name `A..B` | rejected | `SRC` (invalid), `DIFF` (explicit check) |
| Tag name `A B` | rejected | `SRC` (leading/trailing), `DIFF` (interior) |
| Tag name `A.B` | accepted | `DOCS`, `SRC` |

## 1.11 Entity tag participation

Implemented in `GameplayTagStore`.

**Rule.** Per-entity tags are reference counted grants, not a set.
Adding the same tag twice produces a count of two, and removing it
once leaves it present.

- `SRC` `FGameplayTagCountContainer`
  (`GameplayAbilities/Public/GameplayEffectTypes.h`): "tracks the
  number/count of tag applications within it. Explicitly tracks the
  tags added or removed, while simultaneously tracking the count of
  parent tags as well."
- `SRC` `UpdateTagMap_Internal` / `UpdateExplicitTags`: every update
  is a signed count delta; a grant can be added or removed without
  the tag appearing or disappearing.
- `UTEST`-style coverage in `GameplayTagStoreTests`: two grants,
  one removal, tag still present.

**Rule.** Ancestor counts are maintained alongside explicit counts,
so a hierarchical `HasTag` is a single lookup rather than a walk over
ancestors.

- `SRC` `FGameplayTagCountContainer::HasMatchingGameplayTag` reads
  `GameplayTagCountMap` (which contains parent counts), and
  `UpdateTagMap_Internal` updates the parent map after the explicit
  tag map.
- `DIFF`: VOLT keeps the explicit and total counts in one sorted
  entry array per entity. Two parallel hash maps are the natural
  Unreal shape because an ability system component owns one shared
  table; VOLT's per-entity arrays are smaller for the common case of
  a handful of tags and need no hashing.

**Rule.** Destroying an entity releases every grant, and a stale
handle cannot observe or mutate the state of a later entity that
reuses the index.

**Rule.** If an earlier destruction handler recycles the index and
tags the replacement before this store observes the old destruction,
the first write to the new generation retires the old entries and
dense membership before adding the new grant. The replacement appears
once in dense enumeration and `TaggedEntityCount` stays exact.

- `DIFF`: Unreal's ability system owns its count container and is
  destroyed with its owner. VOLT stores tags beside entity slots, so
  it adds two safeguards Unreal's loose container does not have:
  `World.EntityDestroyed` clears the slot immediately, and every
  access validates the entity generation, so a recycled index is a
  different entity even before the destruction callback runs. The
  generation transition is also a retirement boundary, because a
  callback may populate the replacement before the destruction
  callback reaches this store.
- `SRC` (generation precedent): `EntityAllocator` already guarantees
  that a stale handle never equals a newer occupant of the same
  slot. The tag store follows the same rule instead of trusting that
  callers remember to clear tags.

**Rule.** An entity with no tags has no entry array and no dense
index. Its only cost is a slot in a lazily allocated 1,024-slot
segment.

- `DIFF`: this is a VOLT-specific layout decision. Unreal allocates a
  map inside every ability system component, whether or not it has
  tags. VOLT's storage is sparse by construction: a million-entity
  world with one percent tagged reserves storage proportional to the
  tagged entities, not to the world.

**Rule.** Granting a tag to a dead entity throws rather than silently
succeeding, because a silent success would be lost gameplay state.
Removing a tag from a dead or untagged entity returns false, because
removal is idempotent by nature.

- `DIFF`: Unreal's `UpdateTagCount` can only report whether the map
  changed; it has no world against which to validate the owner.

**Rule.** Tag counts are signed 32-bit integers. An entity cannot
meaningfully accumulate `int.MaxValue` grants, and apply/remove paths
are symmetric, so overflow is treated as an application bug rather
than a supported state.

- `INFER`: this bound is a VOLT implementation limit, not Unreal
  behavior.

**Rule.** The store participates in query evaluation through the same
query type as immutable sets, using store-owned scratch spans so a
warmed query allocates nothing.

- `DIFF`: Unreal evaluates queries against a materialized
  `FGameplayTagContainer`. VOLT reads the entity's entry array
  directly into reusable spans, avoiding a copy per evaluation.

## 1.12 Performance

Measured 2026-10-22 on an AMD Ryzen 9 9950X3D, .NET 10.0.13,
BenchmarkDotNet 0.15.8, in-process job, 3 warmups, 5 iterations.
Tags are interned before measurement.

| Workload | Mean | Allocated |
| --- | ---: | ---: |
| Sparse exact lookup (1 tag) | 0.34 ns | 0 B |
| Dense exact lookup (40 tags) | 3.98 ns | 0 B |
| Sparse hierarchical lookup (deep leaf → root) | 1.23 ns | 0 B |
| Dense hierarchical lookup | 4.76 ns | 0 B |
| `HasAny` against 2 of 40 tags | 3.75 ns | 0 B |
| `HasAll` against 2 of 40 tags | 9.68 ns | 0 B |
| Nested query, 4 nodes | 22.2 ns | 0 B |
| Mutable container `HasTag` | 4.84 ns | 0 B |
| Mutable container `HasAll` | 7.33 ns | 0 B |
| Container `Add` + `Remove` | 181.8 ns | 0 B |
| Build immutable set of 40 tags | 455 ns | 600 B |
| `Filter` over 40 tags | 151 ns | 184 B |
| Registry lookup by name (lock held) | 19.6 ns | 0 B |
| Re-register existing tag | 18.6 ns | 0 B |

`Filter` and set construction allocate because they return a new
immutable set; both are authoring or query-preparation operations.
Matching, query evaluation, and warmed mutation allocate nothing.
`Add` + `Remove` is dominated by recomputing parents, which is the
price of exact parent counts after removal; adding alone is cheaper.

Entity tag storage, same machine and job, one percent of entities
tagged:

| Workload | 1,000 entities | 100,000 entities | 1,000,000 entities | Allocated |
| --- | ---: | ---: | ---: | ---: |
| Hierarchical `HasTag` on a tagged entity | 1.71 ns | 1.73 ns | 1.74 ns | 0 B |
| `HasTag` on an untagged entity | 0.66 ns | 0.68 ns | 0.67 ns | 0 B |
| Nested query on a tagged entity | 19.6 ns | 19.7 ns | 19.8 ns | 0 B |
| Grant + remove on a warmed entity | 63.7 ns | 63.7 ns | 64.0 ns | 0 B |
| Copy all tagged handles (1% of world) | 10 ns | 1.16 μs | 18.0 μs | 0 B |

Dense participation (every entity tagged):

| Workload | 1,000 entities | 100,000 entities | Allocated |
| --- | ---: | ---: | ---: |
| Hierarchical `HasTag` | 2.43 ns | 2.49 ns | 0 B |
| Create + destroy untagged entity | 86.9 ns | 106.4 ns | 0 B |
| Create + grant + destroy tagged entity | 131.7 ns | 152.9 ns | 0 B |

Lookup cost is flat in world size, which is the point of indexing by
entity slot instead of hashing. The dense case adds about 0.75 ns to
a lookup. Tagging an existing entity for the first time allocates its
entry array; after that, grants and removals reuse it, so creating
and destroying tagged entities allocates nothing once the slot array
is warm.

The sparse copy is proportional to the tagged population, not the
world, and performs no allocation because the caller supplies the
destination span.

**Limitation.** These ran with the in-process toolchain because the
checkout contains untracked experiment workspaces with duplicate
`Volt.Benchmarks.csproj` names, which breaks BenchmarkDotNet's
out-of-process project discovery. Re-run with the default toolchain
after those copies are removed; in-process numbers are suitable for
relative comparison, not for absolute claims about a shipping build.

---

# Part 2 — Attributes

Implemented in `Volt.Gameplay.Attributes`.

## 2.1 Identity and declaration

**Rule.** An attribute is a registered, interned four-byte identity
with one immutable definition for the process.

- `SRC` `FGameplayAttribute` is a reflected `FProperty` pointer plus
  its owning attribute set; identity comes from reflection and the
  property path.
- `DIFF`: VOLT registers a name and a definition explicitly, so an
  attribute does not require reflection, an attribute-set class, or
  a `UObject`. This makes attributes usable from plain .NET code and
  keeps the identity compact.
- `DIFF`: names are case-insensitive and dots are allowed only as a
  visual grouping separator. Attributes are flat; `Vitals.Health`
  does not create a `Vitals` attribute or any hierarchy.

**Rule.** Re-registering a name with a different default or range is
an error, not a silent override.

- `INFER`: Unreal cannot hit this case because attribute identity is
  the property itself; two systems cannot disagree about the default
  of one property.
- `DIFF`: an explicit registry can be called twice, so VOLT rejects
  the conflicting definition instead of letting load order decide.

## 2.2 Base and current values

**Rule.** The base value contains only permanent changes. The current
value includes temporary modifiers. Writes to the base value
recalculate the current value.

- `SRC` `FGameplayAttributeData`
  (`AttributeSet.h`): `GetCurrentValue` "includes temporary buffs",
  `GetBaseValue` "only includes permanent changes".
- `SRC` (aggregation flow): the ability system passes the base value
  through `FAggregator::EvaluateWithBase` to obtain the current
  value; base modifiers never mutate the attribute's `BaseValue`.
- `UTEST`-style coverage: adding an `AddBase` modifier changes the
  current value but leaves the base value untouched.

## 2.3 Modifier operations and evaluation order

**Rule.** Modifiers aggregate in this exact order:

```
((Base + Sum(AddBase)) * MultiplyAdditive / DivideAdditive
  * Product(MultiplyCompound)) + Sum(AddFinal)
```

with a unit bias for the multiplicative sums:

- `AddBase` contributes its magnitude.
- `MultiplyAdditive` contributes `magnitude - 1` to a sum that starts
  at one. Two 1.5 modifiers produce a 2.0 multiplier.
- `DivideAdditive` contributes `magnitude - 1` to a divisor sum that
  starts at one. Two 2.0 divisors divide by three.
- `MultiplyCompound` multiplies the running result by each magnitude.
- `AddFinal` contributes its magnitude after all multiplication.

- `SRC` `EGameplayModOp` (`GameplayEffectTypes.h`): the equation is
  documented verbatim in the enum comment.
- `SRC` `FAggregatorModChannel::EvaluateWithBase`
  (`GameplayEffectAggregator.cpp`): implements that equation.
- `SRC` `FAggregatorModChannel::SumMods`: `Sum = Bias; Sum += Magnitude
  - Bias`, and `GameplayEffectUtilities::GetModifierBiasByModifierOp`
  returns `{0, 1, 1, 0, 0, 0}` for the six operations.

**Rule.** Any active `Override` replaces the entire result; the base
and all other modifiers are ignored.

- `SRC` `FAggregatorModChannel::EvaluateWithBase`: iterates the
  override list first and returns the first qualifying magnitude
  before computing anything else.
- `DIFF`: "first" is defined by insertion sequence, not by storage
  slot. VOLT recycles modifier slots, so an explicit sequence number
  keeps the winner stable when a slot is reused. Unreal never reuses
  an aggregator slot in place, so its array order already means
  insertion order.

**Rule.** An exactly zero aggregated divisor is replaced by one to
avoid division by zero.

- `SRC`: `if (FMath::IsNearlyZero(Division)) Division = 1.f;`.
- `DIFF`: Unreal also replaces near-zero divisors within its
  `IsNearlyZero` tolerance (about `1e-4`), which silently discards a
  deliberately small divisor. VOLT substitutes one only for an exact
  zero, where the alternative is infinity.

**Rule.** Modifier magnitudes and base values must be finite; NaN and
infinity are rejected at the call site.

- `DIFF`: Unreal accepts any float from game code and relies on the
  game's clamping hook. VOLT rejects non-finite input because it
  cannot be clamped meaningfully and turns every later comparison
  into a false result.

## 2.4 Clamping

**Rule.** Base and current values are clamped to the definition's
`[Minimum, Maximum]` range after every mutation. Infinite bounds mean
unclamped. A NaN result is mapped to the minimum.

- `SRC` `AttributeSet::PreAttributeChange` and
  `PreAttributeBaseChange`: Unreal provides virtual hooks and states
  that clamping is the game's responsibility; there is no built-in
  range.
- `DIFF`: VOLT stores the range in the definition. Every mutation is
  clamped consistently, and the policy is visible at the declaration
  instead of in an override that can be forgotten.
- `DIFF`: a NaN current value is mapped to the minimum. Unreal would
  keep the NaN, and every comparison against it would be false.

**Rule.** Dynamic bounds (for example, health clamped by a buffed
maximum health) are not part of the attribute itself and must be
applied by game code.

- `INFER`: Unreal has the same limitation; `MaxHealth` is an ordinary
  attribute, and the health clamp in `PreAttributeChange` reads it
  manually.

## 2.5 Evaluation and change notification

**Rule.** The current value is cached. It is recalculated only when a
base value or a modifier changes, never on read.

- `DIFF`: Unreal's aggregator evaluates on demand through
  `Evaluate`/`EvaluateWithBase`, and the ability system caches the
  result at set points. VOLT makes the cache explicit per attribute
  entry and updates it at every mutation, so reads are one binary
  search with no aggregation work.

**Rule.** One notification is raised per mutation when either the
base or the current value changed, after clamping. It carries the
old and new values for both. A base change that is hidden by an
override still notifies, with an unchanged current value. No
notification is raised when nothing changed, when an entity is
destroyed, or when a store is cleared.

- `SRC` (event model): Unreal exposes `OnGameplayAttributeValueChange`
  on the ability system component and pre/post change hooks in the
  attribute set.
- `DIFF`: VOLT raises one store-level event instead of a delegate per
  attribute. A per-attribute delegate would allocate a subscriber
  list for every attribute on every entity; listeners filter by the
  attribute in the payload instead.

## 2.6 Attribute dependencies

**Rule.** Attributes are independent. A modifier targets exactly one
attribute and does not read another.

- `SRC`/`INFER`: Unreal attributes are also independent floats. Any
  relationship, such as health depending on maximum health, is
  expressed by a gameplay effect or by an execution calculation, not
  by the attribute system.
- `DIFF`: none. This is a shared limitation, documented so it is not
  mistaken for unfinished work.

## 2.7 Lifecycle and handles

**Rule.** Destroying an entity releases its attribute entries and
their modifiers without raising change notifications. A stale entity
handle cannot read or write the next occupant's values.

**Rule.** If a destruction handler recycles the index and writes
attributes to the replacement before this store observes the old
destruction, the first write to the new generation retires the old
entry array and dense membership first. The replacement's first write
therefore creates one clean entry, not a duplicate dense index.

**Rule.** A modifier handle carries the entity, attribute, slot, and
slot generation. Removing advances the slot generation. Clearing an
entity also advances every used modifier generation before reusing the
storage, so a stale handle cannot remove a modifier installed later
even though the entity generation did not change.

- `SRC`/`UTEST` analog: `ScheduledHandle` and `Entity` already use
  generation counters for this purpose. The modifier handle follows
  the same rule.
- `INFER`: Unreal's `FActiveGameplayEffectHandle` provides the same
  protection for effects; VOLT's per-modifier handle is finer
  grained because modifiers can exist without an effect yet.

## 2.8 Edge-case matrix

| Case | Expected | Evidence |
| --- | --- | --- |
| Read an attribute the entity never touched | registered default, no state created | `DIFF` |
| Write the default to untouched state | no change, no allocation | `DIFF` |
| Base 100, `AddBase` 20 | current 120, base 100 | `SRC` |
| Base 100, two `MultiplyAdditive` 1.5 | current 200 | `SRC` |
| Base 100, two `DivideAdditive` 2 | current 33.33 | `SRC` |
| Base 100, `MultiplyCompound` 1.5 and 2 | current 300 | `SRC` |
| Full chain `((100+20)×1.5÷2×2)+10` | 190 | `SRC` |
| Override 42 with other modifiers | 42 | `SRC` |
| Two overrides 42 then 50 | 42; 50 after removing 42 | `SRC`, `DIFF` (sequence) |
| Exactly zero divisor sum | behaves as divisor 1 | `SRC`, `DIFF` |
| Current above maximum | clamped to maximum | `DIFF` |
| NaN computed value | minimum | `DIFF` |
| Base change hidden by override | event with unchanged current | `DIFF` |
| Add a zero-magnitude `AddBase` | no event | `DIFF` |
| Remove an already removed modifier | false, no change | `UTEST` analog |
| Destroy an entity with modifiers | state released, no event | `DIFF` |
| Replacement modified before the destruction callback | one dense entry, old modifiers retired | `DIFF` |
| Clear then add a modifier on the same entity | old handle rejected; arrays reused | `DIFF` |
| Insert an attribute before another | modifier storage is not shared | `DIFF` |
| Write to a dead entity | throws | `DIFF` |
| Read from a dead entity | default value | `DIFF` |
| Unregistered attribute anywhere | throws | `DIFF` |

## 2.9 Performance

Same machine and job as the tag benchmarks. One percent of entities
own attribute state; the dense case initializes every entity.

| Workload | 1,000 | 100,000 | 1,000,000 | Allocated |
| --- | ---: | ---: | ---: | ---: |
| `GetCurrentValue`, state present | 2.33 ns | 2.31 ns | 2.28 ns | 0 B |
| `GetCurrentValue`, no state | 1.44 ns | 1.45 ns | 1.74 ns | 0 B |
| `GetBaseValue`, state present | 2.35 ns | 2.32 ns | 2.29 ns | 0 B |
| `SetBaseValue` twice (alternating) | 24.2 ns | 24.0 ns | 23.8 ns | 0 B |
| Add + remove a modifier with 8 active | 40.2 ns | 40.1 ns | 40.4 ns | 0 B |
| Dense `GetCurrentValue` (every entity initialized) | 2.25 ns | 2.25 ns | — | 0 B |

Cached reads are flat in world size and do not depend on the number
of active modifiers. Mutation cost is the aggregation itself, not
storage growth: both cases reuse their arrays after warmup, and the
store raises no event and allocates nothing when nobody subscribed.
Adding a new attribute to an entity for the first time allocates its
entry array; a modifier's first use allocates its slot array.

# Part 3 — Effects

Implemented in `Volt.Gameplay.Effects`.

## 3.1 Definitions and instances

**Rule.** A `GameplayEffectDefinition` is immutable, shared, and
validated at construction. Applying it creates a compact instance that
stores only what differs between applications: the definition, the
target and source, the stack count, the clocks, and the modifier
handles needed to undo the effect.

- `SRC` three Unreal types: `UGameplayEffect` is the data asset,
  `FGameplayEffectSpec` is the per-application copy that captures
  attributes and evaluates magnitudes, and `FActiveGameplayEffect` is
  the tracked instance with start time, duration, and period handles.
- `DIFF`: VOLT folds the spec into the instance. There is no separate
  spec object, because a spec in Unreal exists mainly to carry
  captured attribute values and set-by-caller magnitudes, and this
  milestone supports constant magnitudes only.
- `DIFF`: the source is an `Entity` identity rather than an ability
  system component. It is an identity, not a live reference, so a
  damage-over-time effect keeps burning after its caster dies, exactly
  as in Unreal.
- `DIFF`: definitions are validated eagerly. An unregistered
  attribute, a non-finite magnitude, a negative limit, an instant
  effect with a period, or a limit without a stacking type throws when
  the definition is built instead of failing on the first application.

## 3.2 Duration policies

**Rule.** Instant effects execute once and are never tracked, hold no
modifiers, and grant no tags. Infinite effects stay active until
removed. Effects with a duration expire after their duration has
elapsed in simulation time.

- `SRC` `UAbilitySystemComponent::ApplyGameplayEffectSpecToSelf`
  (`AbilitySystemComponent.cpp`): only a spec whose
  `DurationPolicy != Instant` is passed to
  `ApplyGameplayEffectSpec`; an instant spec goes through
  `ExecuteGameplayEffect` and never enters the active container. Its
  return value is `FActiveGameplayEffectHandle::GetInstantExecutedHandle()`.
- `SRC` `FActiveGameplayEffectsContainer::InternalOnActiveGameplayEffectAdded`
  is the only path that grants tags, and it is unreachable for an
  instant spec.
- `DIFF`: Unreal silently ignores a period configured on an instant
  effect (`GetPeriod()` forcibly returns `NO_PERIOD`). VOLT rejects
  that definition, because it cannot do what it declares.
- `DIFF`: `Apply` returns a status instead of a handle that encodes
  "instant" through a special value. `GameplayEffectHandle.None` means
  "no tracked instance", and the status says whether the application
  executed, created an instance, stacked, was rejected, or ended
  during application.

## 3.3 Modifiers

**Rule.** An instant effect folds each modifier into the base value in
declaration order. The operations are: `Override` replaces the value,
`AddBase` and `AddFinal` add, the two multiply operations multiply,
and `DivideAdditive` divides. A division by exactly zero leaves the
value unchanged. Each step is clamped before the next one runs.

- `SRC` `FActiveGameplayEffectsContainer::ApplyModToAttribute` calls
  `FAggregator::StaticExecModOnBaseValue`, which is the operation set
  above and skips division when the divisor is nearly zero
  (`GameplayEffectAggregator.cpp`).
- `DIFF`: the near-zero tolerance becomes an exact zero test, so a
  deliberately small divisor keeps its meaning.

**Rule.** A non-periodic effect with a duration or infinite duration
holds its modifiers for as long as it is active: the current value
changes and the base value does not. A periodic effect holds no
modifiers and instead executes its modifiers as instant changes once
per period.

- `SRC` `FActiveGameplayEffectsContainer::AddActiveGameplayEffectGrantedTagsAndModifiers`:
  aggregator mods are registered only when
  `Effect.Spec.GetPeriod() <= NO_PERIOD`; the periodic branch schedules
  a repeating timer instead.
- `SRC` `FActiveGameplayEffectsContainer::InternalExecutePeriodicGameplayEffect`
  calls `ExecuteActiveEffectsFrom`, which is the same instant path.

**Rule.** When `FactorInStackCount` is enabled, a modifier's magnitude
is scaled by the stack count: a non-compound operation contributes
`(magnitude - bias) * stacks + bias`, and a compound multiplier
contributes `magnitude ^ stacks`, where the bias is one for the
multiply and divide operations and zero otherwise. `Override` ignores
the stack count entirely.

- `SRC` `GameplayEffectUtilities::ComputeStackedModifierMagnitude` and
  `GetModifierBiasByModifierOp` (`GameplayEffectTypes.cpp`).
- `SRC`: the scaling is applied by `FGameplayEffectSpec::GetModifierMagnitude`,
  which is used both for aggregator mods and for periodic execution,
  so a stacked damage-over-time tick is scaled too.

## 3.4 Granted tags

**Rule.** Each active instance contributes one grant of each granted
tag, whatever its stack count. The grant is withdrawn when the
instance is inhibited or removed. Instant effects never grant tags.

- `SRC` `UpdateTagMap(Effect.Spec.Def->GetGrantedTags(), 1)` in
  `AddActiveGameplayEffectGrantedTagsAndModifiers` and `-1` in
  `RemoveActiveGameplayEffectGrantedTagsAndModifiers`.
- Consequence: two effects granting the same tag leave it present when
  one of them ends, which the store's reference counting provides.

## 3.5 Requirements

**Rule.** An application is refused when the target does not have every
required tag or does have an ignored tag. Matching is non-exact in both
directions, so requiring `Status` accepts `Status.Stunned`.

- `SRC` `FGameplayTagRequirements::RequirementsMet` and
  `FGameplayEffectSpec::Def->CanApply`.
- A refused application changes nothing and reports
  `RejectedByRequirements`; failing a requirement is gameplay, not an
  error.

**Rule.** An active effect whose ongoing requirements are unmet is
inhibited: its modifiers and granted tags are removed, its clock keeps
running, and it is restored when the requirements hold again. The
initial state is decided before the effect grants its own tags.

- `SRC` `UAbilitySystemComponent::SetActiveGameplayEffectInhibit`
  removes or re-adds granted tags and aggregator mods, and
  `FActiveGameplayEffectsContainer::InternalOnActiveGameplayEffectAdded`
  evaluates the requirements before the effect's tags exist.
- `SRC` `CheckDuration` ignores inhibition, so an inhibited effect
  still expires on schedule.
- `DIFF`: re-evaluation is driven by the tag store's `TagsChanged`
  notification instead of per-ability-system tag delegates. The effect
  system coalesces the work per entity and iterates to a fixpoint with
  a bounded number of passes.
- `DIFF`: a definition that grants a tag its own ongoing requirements
  forbid is rejected at build time. Unreal would oscillate between
  granting and withdrawing the tag forever.

**Rule.** An active effect is removed as soon as its removal
requirements are satisfied; this is a hard removal, not an inhibition.

- `SRC` the deprecated `RemovalTagRequirements` / `URemoveOtherGameplayEffectComponent`
  family: "Tag requirements that if met will remove this effect."

## 3.6 Stacking

**Rule.** Only a non-instant effect can stack. `AggregateBySource`
merges applications with the same definition and the same source;
`AggregateByTarget` merges every application of the definition on the
target. `None` creates an independent instance per application.

- `SRC` `FActiveGameplayEffectsContainer::FindStackableActiveGameplayEffect`
  compares `Spec.Def` and, for `AggregateBySource`, the instigator.
  It also returns early for instant effects and for `None`.
- `DIFF`: the source is compared as an `Entity` value.

**Rule.** One application adds one stack. A stack limit of zero means
no limit. An application at the limit still succeeds: the count stays
at the limit and the duration and period are refreshed according to
their policies.

- `SRC` `bDenyOverflowApplication` defaults to false, so
  `HandleActiveGameplayEffectStackOverflow` allows the application and
  the count is clamped by `FMath::Min(NewStackCount, StackLimitCount)`.
- `DIFF`: VOLT applies one stack per call, so a burst is a loop of
  applications. Unreal lets a spec carry several stacks at once; the
  clamped outcome is the same, and this keeps the common path free of
  a loop over a count that is almost always one.

**Rule.** On a successful stack application the duration policy
behaves as follows: `RefreshOnSuccessfulApplication` restarts the
duration from now, `NeverRefresh` leaves it untouched, and
`ExtendDuration` adds a full duration to the remaining time. The period
policy either restarts the period from now or leaves the schedule
unchanged.

- `SRC` `ApplyGameplayEffectSpec` (`GameplayEffect.cpp`):
  `CarryOverDuration` plus `RestartActiveGameplayEffectDuration` for
  the duration policies, and the `StackPeriodResetPolicy` guard that
  skips re-registering the period timer.

**Rule.** When a stacked effect reaches the end of its duration,
`ClearEntireStack` removes the instance, `RemoveSingleStackAndRefreshDuration`
removes one stack and restarts the duration (removing the instance
when the last stack goes), and `RefreshDuration` restarts the duration
without touching the count.

- `SRC` `FActiveGameplayEffectsContainer::CheckDuration`, including
  `StacksToRemove = 1` and the `RefreshStartTime` flags.

## 3.7 Scheduling and determinism

**Rule.** Expiries and periodic ticks share one binary min-heap ordered
by due time, then by the order in which they were scheduled. Only
entries that are actually due are processed, in due-time order, so the
same history produces the same event sequence.

- `DIFF`: Unreal registers a world timer per active effect. VOLT keeps
  one heap, which makes an idle population cost one comparison per
  step instead of one timer callback per effect, and makes ordering
  explicit rather than dependent on timer manager internals.
- `DIFF`: a tick that is due at application time executes during
  `Apply` when `ExecuteOnApplication` is set; Unreal schedules it for
  the next frame tick. The observable difference is one frame of
  latency, not a different number of ticks.

**Rule.** Every due boundary is processed, even when one step covers
several of them. A period boundary always advances from the previous
scheduled boundary, so a large step replays each tick in order and
produces the same result as many small steps.

- `INFER` from the timer semantics: a repeating timer that is already
  past due fires once per tick of the timer manager, so the count over
  a long interval is the same, while VOLT reproduces the exact
  sequence inside one step.

**Rule.** When a periodic tick and the duration expiry fall on the same
instant, the tick executes first and the effect then expires.

- `SRC` `CheckDuration`: `CheckForFinalPeriodicExec` runs
  `InternalExecutePeriodicGameplayEffect` when the period timer is also
  due, before removing the effect.

**Rule.** Ticks that come due while an effect is inhibited are skipped,
not replayed. On the transition back to active, `NeverReset` keeps the
original schedule grid, `ResetPeriod` schedules one full period from
now, and `ExecuteAndResetPeriod` executes once and then schedules a
full period.

- `SRC` `EGameplayEffectPeriodInhibitionRemovedPolicy` and the
  periodic branch of `AddActiveGameplayEffectGrantedTagsAndModifiers`.
- `DIFF`: the skipped boundaries are fast-forwarded arithmetically
  instead of being fired and discarded one by one; the resulting
  schedule is identical.

**Rule.** `Advance` accepts only a finite, non-negative step. Zero is a
no-op, which is what a paused game supplies. Calling it from inside an
effect notification throws instead of corrupting the traversal.

- `DIFF`: Unreal has no equivalent because time comes from the world
  tick; VOLT makes the contract explicit and testable.

## 3.8 Lifecycle and re-entrancy

**Rule.** Destroying an entity releases its effect instances silently,
the way the attribute and tag stores release their own state. No
`Removed` notification is raised, because the values no longer exist
for anyone to observe.

**Rule.** Destruction notification order is not a safety guarantee. If
a handler recycles the target index and affects the replacement before
this system observes the old destruction, the first link for the new
generation retires every instance still owned by the old generation.
The old slots return to the freelist, the active count is decremented,
and old heap entries become stale without a world scan. Only the
replacement's instances remain linked.

**Rule.** A handle carries the target, the slot, and the slot
generation. Removing an instance advances the generation, so a stale
handle cannot remove a later instance that reused the slot.

**Rule.** Notifications are delivered after the change is committed,
and a handler may query the system, apply or remove effects, or
destroy the entity. An instance is linked into the entity's list and
marked active before its modifiers are added, so a handler that
reacts to the first modifier can clean it up completely. If the
instance ends during its own application, `Apply` reports `Discarded`.

## 3.9 Edge-case matrix

| Case | Expected | Evidence |
| --- | --- | --- |
| Instant effect applied | base changes, no instance, no tag grant | `SRC` |
| Instant effect on a full health bar | clamped, no state created | `SRC`, `DIFF` |
| Instant effect with a period | definition rejected | `DIFF` |
| Duration effect applied | current value changes, base unchanged | `SRC` |
| Periodic effect between ticks | current equals base | `SRC` |
| Periodic tick at the expiry instant | tick runs, then removal | `SRC` |
| Periodic tick while inhibited | skipped, schedule advances | `SRC`, `DIFF` |
| Two instances granting one tag | count 2; one removal leaves the tag | `SRC` |
| Application requirement unmet | `RejectedByRequirements`, no side effect | `SRC` |
| Required parent tag, child granted | requirement satisfied | `SRC` |
| Ongoing requirement unmet | inhibited, modifiers and tags withdrawn | `SRC` |
| Effect grants a tag it forbids | definition rejected | `DIFF` |
| Removal requirement satisfied | instance removed | `SRC` |
| Re-apply below the stack limit | one instance, stack +1, magnitude rescaled | `SRC` |
| Re-apply at the stack limit | count clamped, duration refreshed | `SRC` |
| Stack limit without a stacking type | definition rejected | `DIFF` |
| `AggregateBySource`, same source | one instance | `SRC` |
| `AggregateBySource`, other source | two instances | `SRC` |
| `AggregateByTarget`, other source | one instance, newest source | `SRC` |
| `RemoveSingleStackAndRefreshDuration` at one stack | instance removed | `SRC` |
| `RefreshDuration` policy | instance never expires | `SRC` |
| Inhibited effect reaches its duration | expires | `SRC` |
| Entity destroyed with active effects | silent release, no notification | `DIFF` |
| Entity index reused | new occupant sees no effects | `DIFF` |
| Index recycled before the destruction notification | old instances retired; old deadlines cannot execute | `DIFF` |
| Stale effect handle | removal rejected | `DIFF` |
| Handler applies an effect during application | both instances active | `DIFF` |
| Handler destroys the entity during application | `Discarded`, no leak | `DIFF` |
| Negative or non-finite step | throws | `DIFF` |
| `Advance` from a notification | throws | `DIFF` |

## 3.10 Performance

Same machine and job as the tag and attribute benchmarks, in process.
Two worlds are prepared: one with a duration effect on every entity,
and one with no gameplay state at all.

| Workload | 1,000 | 100,000 | Allocated |
| --- | ---: | ---: | ---: |
| Apply + remove a duration effect | 151 ns | 152 ns | 0 B |
| Apply an instant effect | 24.3 ns | 24.4 ns | 0 B |
| Apply a stack at the limit | 15.0 ns | 15.0 ns | 0 B |
| Apply an effect that fails its requirements | 10.7 ns | 10.7 ns | 0 B |
| One periodic tick | 29.1 ns | 29.4 ns | 0 B |
| Advance, every effect idle | 1.78 ns | 1.73 ns | 0 B |
| Advance, no gameplay state | 1.63 ns | 1.63 ns | 0 B |
| Attribute read with an active effect | 2.28 ns | 3.02 ns | 0 B |
| `HasTag` granted by an active effect | 3.32 ns | 3.32 ns | 0 B |
| `ActiveEffectCount` | — | 0.00 ns | 0 B |

Idle stepping is flat in the number of active effects and in world
size: 100,000 idle effects cost the same 1.7 ns as an empty world,
which is the entire point of the heap. A tick costs one instant
modifier execution plus one heap pop and push. Applying and removing
an effect allocates nothing once the slot and modifier arrays are
warm, because slots are recycled and their modifier arrays are kept.

**Limitation.** These ran with the in-process toolchain, for the same
reason as the earlier parts: untracked experiment workspaces contain
duplicate benchmark project names.

# Part 4 — Abilities

Implemented in `Volt.Gameplay.Abilities`.

## 4.1 Definitions and grants

**Rule.** A `GameplayAbilityDefinition` is immutable, shared, and
validated at construction. A grant stores only the definition, the
owner, the level, the input binding, the activation target, and whether
the instance is active. Granting one definition twice creates two
independent grants.

- `SRC` `UAbilitySystemComponent::GiveAbility` adds a new
  `FGameplayAbilitySpec` on every call; the `UGameplayAbility` class it
  points at is shared.
- `DIFF`: a VOLT definition carries data, not an `ActivateAbility`
  override. The runtime raises `Activated`, and game code reacts to
  it. Costs and cooldowns are gameplay effects, as in Unreal.
- `DIFF`: the definition is validated eagerly. A non-instant cost, an
  instant cooldown, a cooldown that grants no tags, and a cooldown
  that grants one of the ability's own cancel tags all throw when the
  definition is built. Unreal reports the tagless cooldown at runtime
  with a log warning, and would let the other combinations misbehave.
- `DIFF`: the level is metadata. Unreal passes it to the cost and
  cooldown effects so their magnitudes can scale; this milestone's
  effect definitions have constant magnitudes, so the level is
  reported on grants and notifications only.

**Rule.** A handle is scoped to the system that created it and is
generation-checked. The handle embeds the system identity, the owner,
the slot, and the slot generation. Revoking a grant or destroying its
owner retires the handle, and a later grant that reuses the slot is not
reachable through it. A handle from another system is rejected even
when that system uses the same world, owner, slot, and generation.

- `SRC` Unreal's `FGameplayAbilitySpecHandle` is a slot index plus a
  serial number allocated from a shared counter. The owning ability
  system component is implicit, so the same numeric handle in a
  different component is not meaningful.
- `DIFF` VOLT can create more than one ability system over one world,
  so a bare slot and generation would alias. The handle carries a
  process-unique system id, and `TryResolve` compares it first. The id
  is never reused until the 32-bit counter wraps.

## 4.2 Activation checks

**Rule.** An activation attempt runs, in this order: cooldown, cost,
owner tag requirements, target tag requirements, blocking by an active
ability, and finally the already-active rule. A failure changes
nothing and reports a status; none of these outcomes throws.

- `SRC` `UGameplayAbility::CanActivateAbility` checks
  `CheckCooldown`, then `CheckCost`, then
  `DoesAbilitySatisfyTagRequirements`, in that order.
- `SRC` `UAbilitySystemComponent::InternalTryActivateAbility` calls
  `CanActivateAbility` first and only then applies the
  instanced-per-actor already-active rule, so a cooling-down,
  already-active ability reports the cooldown.
- `SRC` `DoesAbilitySatisfyTagRequirements` checks blocked tags before
  required tags and fails when either set fails. Tag matching is
  non-exact: requiring `Status` accepts `Status.Stunned`.
- `DIFF` Unreal receives source and target tag containers from the
  caller and checks four separate requirement pairs
  (`Source*Tags`/`Target*Tags`). VOLT checks the owner and an optional
  activation target from the stored definitions.
- `DIFF` a non-empty target requirement is unmet when the target is
  absent or dead. Unreal skips the target checks when the caller passes
  no target tags.

## 4.3 Blocking

**Rule.** While an ability is active, an activation whose asset tags
match its `BlockAbilitiesWithTags` is rejected with
`BlockedByActiveAbility`. The requesting grant is excluded, so a
self-blocking ability reports `AlreadyActive` instead. Blocking ends as
soon as the blocking ability ends.

- `SRC` `UGameplayAbility::PreActivate` calls
  `ApplyAbilityBlockAndCancelTags(GetAssetTags(), ...,
  BlockAbilitiesWithTag, ...)`, and `AreAbilityTagsBlocked` tests the
  candidate's asset tags against the blocked set with non-exact
  matching. `EndAbility` removes them again.
- `DIFF` VOLT derives blocking from the active ability list instead of
  keeping a per-owner blocked-tag container, so no state has to be
  reference-counted. As a consequence, effects cannot block abilities
  through a blocked-ability-tag component in this milestone.
- `DIFF` excluding the requesting grant is the only behavioral
  difference; Unreal would report the ability blocked by itself.

## 4.4 Costs

**Rule.** An activation is rejected with `RejectedByCost` when the cost
effect's own application requirements are unmet, or when an additive
cost modifier would take the owner's current attribute value below
zero. Only `AddBase` modifiers are checked. The check runs before the
activation commits and again immediately before payment, after
cancellation and cooldown application have run. An affordable cost
executes once against the owner's base value.

- `SRC` `UGameplayAbility::CheckCost` delegates to
  `FActiveGameplayEffectsContainer::CanApplyAttributeModifiers`, which
  iterates the cost spec's modifiers, skips every operation except
  `Additive`, and fails when `CurrentValue + CostValue < 0`.
- `SRC` `UGameplayAbility::ApplyCost` applies the cost effect to the
  owner.
- `DIFF` Unreal's affordability check does not inspect the cost
  effect's tag requirements; a rejecting cost simply applies nothing
  and activation continues for free. VOLT treats that as a failed cost
  so an ability can never activate without paying.
- `DIFF` Unreal checks affordability once, before activation callbacks.
  VOLT re-checks immediately before payment because a `Cancelled`
  handler or the cooldown's modifiers may have spent the resource in
  the meantime. This turns a would-be clamped payment into a rejected
  activation with the cooldown rolled back.
- `DIFF` Unreal relies on attribute clamping for the actual payment.
  VOLT applies the same instant effect, so a cost that lands exactly
  on zero clamps and stays there.

## 4.5 Cooldowns

**Rule.** The cooldown effect's application requirements are part of
the activation check. Activation then applies the cooldown before the
cost. A cooldown that refuses to apply, is discarded, or leaves neither
its tags nor a live instance rejects the activation with
`RejectedByCooldown` before any cost is paid.

- `SRC` `UGameplayAbility::GetCooldownTags` returns the cooldown
  effect's granted tags; `CheckCooldown` calls
  `HasAnyMatchingGameplayTags` on the owner; `ApplyCooldown` applies
  the effect to the owner. `CommitAbility` applies the cooldown before
  the cost.
- `DIFF` Unreal never checks the cooldown effect's own application
  requirements in `CanActivateAbility`; a rejected cooldown leaves the
  ability usable. VOLT rejects the activation instead, for the same
  reason it rejects a cost that will not apply.
- `DIFF` Unreal's `CommitAbility` ignores a failed cooldown application
  and still charges the cost. VOLT binds the two: a cooldown failure
  aborts before payment, and a cost failure reverses the cooldown
  change the activation made. The cost is the commit point because it
  is the only irreversible side effect. Rollback removes a new
  instance, or restores a pre-existing stack's count, source, deadline,
  period, and heap order when the activation stacked one.
- `DIFF` an applied cooldown that is inhibited still counts as
  applied. Its tags are withdrawn, so the ability can activate again,
  but the remaining-time query reports the inhibited instance exactly
  as Unreal's `GetActiveEffectsTimeRemaining` does.

**Rule.** The owner is on cooldown while any of the cooldown effect's
granted tags is present, whatever granted it. The remaining time is the
longest remaining duration among all active effects that grant one of
those tags, or infinity for an infinite instance.

- `SRC` `GetCooldownTimeRemaining` builds
  `FGameplayEffectQuery::MakeQuery_MatchAnyOwningTags(cooldownTags)` and
  returns the largest remaining duration among matching active
  effects. `GetActiveEffectsTimeRemaining` does not skip inhibited
  effects.
- `DIFF` none. VOLT now performs the same tag query through
  `GameplayEffectSystem.GetMaxRemainingDuration`; an unrelated effect
  that grants a cooldown tag contributes its duration. A matching tag
  that no effect owns still blocks activation but reports zero
  remaining time, as in Unreal.

## 4.6 Lifecycle, ending, and cancellation

**Rule.** An ability ends as soon as its activation is committed unless
its definition keeps it active or attaches a plan. `End` reports
`Ended`; `Cancel` reports `Cancelled`. Revoking an active grant cancels
it first. Ending or cancelling an inactive ability does nothing and
returns `false`.

- `SRC` `UGameplayAbility::EndAbility`, `CancelAbility`, and
  `UAbilitySystemComponent::ClearAbility`, which cancels the active
  instance before removing the spec.
- `DIFF` a data definition has no activation code or input of its
  own. Without a plan it ends on activation by default; with a plan,
  its wait steps keep it active until the plan ends. Unreal abilities
  stay active until their code calls `EndAbility`.

**Rule.** Activation cancels every active ability whose asset tags
match `CancelAbilitiesWithTags`, except the activating grant, and does
so before raising its own `Activated` notification.

- `SRC` `PreActivate` calls `ApplyAbilityBlockAndCancelTags` with
  `bExecuteCancelTags` set, which calls `CancelAbilities` and passes
  the requesting ability as the ignored instance.

**Rule.** An active ability is cancelled when the owner's tags change
and any `CancelOnTags` tag is then present. A tag that was already
present at activation does not cancel by itself; the next tag change
re-evaluates it.

- `DIFF` Unreal has no automatic cancel-on-tag rule on the ability;
  game code or a gameplay effect component calls `CancelAbilities`.
  VOLT drives it from the tag store's `TagsChanged` notification, the
  same signal the effect system uses for its ongoing requirements.
- `DIFF` a cooldown effect cannot grant a cancel tag, because the
  cooldown applies during activation and would cancel the instance
  that just started.

**Rule.** Notifications are delivered after the change is committed,
and handlers may grant, revoke, activate, and cancel abilities. An
activation that is revoked, cancelled, or whose owner is destroyed by
such a handler reports `EndedDuringActivation`. If it ends before the
cost is paid, reversible state such as the cooldown is rolled back;
if it ends after the commit point, the paid cost and cooldown remain.
A stacked cooldown is restored to its prior stack rather than removed.

- `DIFF` this mirrors the effect system's `Discarded` status. Unreal
  has no return value for an activation, only the ability's own
  `IsActive` state.

**Rule.** A commit that fails its cost or cooldown is rolled back
without an ability notification. It raises neither `Activated` nor
`Cancelled`; the failure is reported by the activation result, and the
grant remains as it was before the attempt. Notifications are only
raised for changes that actually happened.

- `DIFF` Unreal's `CommitAbility` ends the instance, so listeners can
  observe an `EndAbility` for an activation that never succeeded. VOLT
  keeps a failed commit indistinguishable from a failed pre-check, as
  far as the `Changed` stream is concerned.

**Rule.** Each grant has an activation identity, and an activation
marks its grant while it owns it. A notification cannot start a
nested activation of the same grant; the nested attempt reports
`AlreadyActive`. This includes the `Ended` notification raised by a
retrigger, the cancellation of a target, and every effect callback
during the commit.

- `DIFF` Unreal has no return value for a nested `TryActivateAbility`
  and no explicit activation guard; a handler that reactivates during
  `EndAbility` starts a second activation. VOLT rejects it so one
  attempt cannot apply its cost twice or increment the active count
  twice.

**Rule.** A grant being revoked cannot be reactivated. Revocation marks
the grant before its `Cancelled` notification, a nested
`RevokeAbility` of the same handle returns `false`, and the outer call
completes the removal. The active count therefore never releases an
active grant without its `Cancelled` cleanup.

- `DIFF` Unreal's `ClearAbility` has no re-entrancy guard. VOLT's guard
  makes the outer revocation authoritative and keeps a handler from
  resurrecting the grant between cleanup and release.

**Rule.** Bulk operations scope themselves to the grants that exist
when they start. `CancelAll`, `CancelByTags`, tag-driven cancellation,
`RevokeAll`, and input activation capture that set once; a grant added
by a notification is not part of the operation, and a grant that was
cancelled and then reactivated is identified by its new activation id
and is not cancelled a second time.

- `DIFF` Unreal's `CancelAbilities` builds `AbilitiesToCancel` first,
  which gives cancellations the same snapshot scope. VOLT implements
  the snapshot with reusable capture buffers, so it is allocation-free
  after warmup and survives nested operations by taking another buffer
  from the same pool.

**Rule.** Tag-change notifications are coalesced. A handler that adds
or removes tags during a `Cancelled` notification sets a pending flag
instead of recursing, and the runtime runs another cancellation pass
for the newest tag state. The number of passes is bounded.

- `DIFF` the effect system uses the same coalescing pattern for ongoing
  requirements; Unreal has no automatic tag-driven cancellation.

**Rule.** Destroying an owner releases its grants silently. If an
earlier `EntityDestroyed` handler recycles the entity index and grants
to the replacement before this system observes the old destruction,
the first operation that establishes state for the new generation
releases the old grants. Slots, grants, and active counters therefore
cannot outlive an owner even when notification order is adversarial.

- `DIFF` Unreal has no world-wide destruction notification; the
  component's owner is destroyed as a whole. VOLT keys grants by
  entity and must handle index reuse explicitly.

## 4.7 Determinism

There is no ability clock. Cooldown expiry is an effect deadline, so
time scaling, pausing, and deterministic ordering are inherited from
the effect system. Each new grant is pushed onto the front of its
owner's list, so input activation and `CopyAbilities` walk the newest
grant first, and every captured bulk operation walks the same order.
Capture happens before the first notification, so the scope of a call
is fixed by the state at its start rather than by callback timing.
Tag-driven cancellation runs at most eight coalesced passes per
external tag change; a handler that keeps changing tags cannot extend
it without bound.

## 4.8 Edge-case matrix

| Case | Expected | Evidence |
| --- | --- | --- |
| Grant to a dead entity | throws | `DIFF` |
| Level below one | throws | `DIFF` |
| Two grants of one definition | two independent grants | `SRC` |
| Revoked handle activation | `NotGranted` | `DIFF` |
| Cost affordable exactly to zero | activates, value clamps at zero | `SRC` |
| Cost would go below zero | `RejectedByCost`, nothing spent | `SRC` |
| Multiplier-only cost at zero | activates; check skips non-additive modifiers | `SRC` |
| Cost effect requirement unmet | `RejectedByCost` | `DIFF` |
| Cooldown and missing requirement | `RejectedByCooldown` | `SRC` |
| Cost and missing requirement | `RejectedByCost` | `SRC` |
| External effect grants a cooldown tag | activation rejected | `SRC` |
| Infinite cooldown | never activates again; remaining is infinity | `SRC` |
| Cooldown effect grants no tags | definition rejected | `DIFF` |
| Instant cooldown effect | definition rejected | `DIFF` |
| Cooldown grants an ability cancel tag | definition rejected | `DIFF` |
| Blocker active | matching asset tags report `BlockedByActiveAbility` | `SRC` |
| Blocker ends | activation allowed | `SRC` |
| Active without retrigger | `AlreadyActive` | `SRC` |
| Active with retrigger | `Ended` then a new `Activated` | `SRC` |
| Cancel tag arrives | `Cancelled` | `DIFF` |
| Cancel tag present before activation | activates; the next tag change cancels | `DIFF` |
| Cancelling ability activates | targets cancelled before its own `Activated` | `SRC` |
| Revoke an active grant | `Cancelled` then `Revoked` | `SRC` |
| Owner destroyed | grants released silently | `DIFF` |
| Entity index reused | new occupant has no grants | `DIFF` |
| Index recycled before the destruction notification | old grants released; counters match the new owner | `DIFF` |
| Activation revoked by a handler | `EndedDuringActivation` | `DIFF` |
| Owner destroyed by a handler | `EndedDuringActivation` | `DIFF` |
| Activation by input | every grant bound to the input is attempted | `SRC` |
| Cost requirement invalidated during commit | `RejectedByCost`, no cost or cooldown | `DIFF` |
| Cost spent by a cancellation callback | `RejectedByCost`, cooldown rolled back | `DIFF` |
| Failed activation stacked an existing cooldown | stack count, source, and deadline restored | `DIFF` |
| Cooldown requirement invalidated during commit | `RejectedByCooldown`, no cost | `DIFF` |
| Cooldown removed during application | `RejectedByCooldown`, no cost | `DIFF` |
| Activation ended during cooldown application | `EndedDuringActivation`, cooldown rolled back, no cost | `DIFF` |
| Commit aborted after a failed effect | no `Activated` or `Cancelled` | `DIFF` |
| Reactivation from a revocation's `Cancelled` | `NotGranted`, no active leak | `DIFF` |
| Nested revoke from `Cancelled` | `false`; the outer revoke completes | `DIFF` |
| Grant added by a revocation callback | survives `RevokeAll` | `DIFF` |
| Ability activated by a cancellation callback | survives `CancelAll`/`CancelByTags` | `DIFF` |
| Ability activated during tag cancellation | survives that pass | `DIFF` |
| Grant added during input activation | not attempted by that press | `DIFF` |
| Nested activation from a retrigger's `Ended` | `AlreadyActive`, one active instance | `DIFF` |
| Handle from another system on the same world | `NotGranted`; handles differ | `DIFF` |
| External timed cooldown effect | remaining time is its duration | `SRC` |
| Inhibited cooldown instance | remaining time reported, no tag block | `SRC` |
| Matching tag with no effect behind it | on cooldown, remaining zero | `SRC` |

## 4.9 Performance

Same machine and job as the earlier parts, in process, with a grant on
every entity in a populated world and a separate set of actors for the
measured operations. Allocations are zero on every warmed-up path, as
asserted by the allocation guards as well as the benchmark columns.

| Workload | 1,000 | 100,000 | Allocated |
| --- | ---: | ---: | ---: |
| Activate and end a lasting ability | 46.3 ns | 45.2 ns | 0 B |
| Activate with a cost (32 cost, 32 gain) | 101.0 ns | 100.4 ns | 0 B |
| Activate with an unaffordable cost | 35.0 ns | 34.9 ns | 0 B |
| Activate while on cooldown | 35.5 ns | 35.3 ns | 0 B |
| Activate with an unmet requirement | 30.9 ns | 30.9 ns | 0 B |
| Activate while blocked | 36.3 ns | 36.8 ns | 0 B |
| Activate by input | 56.0 ns | 58.0 ns | 0 B |
| Give and revoke | 15.9 ns | 15.5 ns | 0 B |
| `GetAbilityCount` | 0.72 ns | 0.71 ns | 0 B |
| Cooldown time remaining | 5.24 ns | 5.21 ns | 0 B |

The cost workload alternates `-64` and `+64` on a value of `1e9`, whose
float unit in the last place is 64. Every activation therefore changes
the stored value, and the pair returns to the exact starting value; the
rejection workload starts at zero and never pays. The earlier `-0.001`
cost could not change the value at that magnitude.

| Grants on one owner | Input | Blocked (last blocker) | Activate all + cancel all | Activate all + cancel by tag |
| ---: | ---: | ---: | ---: | ---: |
| 1 | 54.1 ns | 40.1 ns | 52.5 ns | 115.1 ns |
| 8 | 369.9 ns | 79.1 ns | 414.0 ns | 520.5 ns |
| 32 | 1,522.0 ns | 225.4 ns | 1,604.6 ns | 1,960.1 ns |
| 64 | 2,938.3 ns | 400.5 ns | 3,255.1 ns | 3,720.6 ns |
| 256 | 12,144.0 ns | 1,535.7 ns | 13,183.2 ns | 14,404.6 ns |

Input activation and both cancellation workloads are linear in the
grant count: about 47 ns per input grant and 51 to 56 ns per grant for
the combined activation-and-cancellation rows at 256 grants, flat from
1 to 256. The block scan is linear in the active blockers, about 6.0 ns
per blocker at 256. The cancellation rows include the activation pass
that puts the grants into the active state; the input row is that
activation baseline.**Fixed in this audit.** Input activation and bulk cancellation
rescanned the owner's grant list from its head after every callback,
which was quadratic in the grant count. Both now capture the scoped
handles once into a reusable per-system buffer, so the 256-grant cases
take about 12 to 13 microseconds including activation. The regression
benchmarks above stay flat from 1 to 256 grants.

**Limitation.** These ran with the in-process toolchain, for the same
reason as the earlier parts: untracked experiment workspaces contain
duplicate benchmark project names. The cancellation benchmarks
deliberately include the activation pass that seeds the active set in
the measured method, so they report activation plus cancellation
rather than cancellation alone.

## 4.10 Cross-store lifecycle contract

**Rule.** `World.DestroyEntity` commits the destruction and releases
the index before it invokes `EntityDestroyed`. The first handler may
create a replacement at the same index and assign tags, attributes,
effects, or abilities to it. Every gameplay store therefore treats a
generation change as a retirement boundary: it releases the previous
generation's entries, dense membership, modifiers, effect chain, or
grants before storing state for the replacement. A store callback that
later receives the old destruction sees a generation mismatch and must
not touch the new generation.

**Rule.** Cleanup traversals capture the generation of the instance or
the next slot before invoking user code. A callback that removes an
instance, recycles a slot, or populates a replacement cannot cause the
outer traversal to remove the replacement's state or clear its newly
linked list.

**Rule.** Notifications are delivered after their mutation commits. An
exception from a handler propagates to the caller; the committed change
remains observable, except where an operation defines a rollback (the
ability cooldown commit). Internal traversal guards are released in
`finally` blocks, so a throwing notification cannot leave `Advance`
re-entrancy blocked or an entity stuck in the effect refresh state. The
stores do not promise atomic rollback of arbitrary user callbacks.

**Rule.** A modifier installation is not complete until its handle is
published. `GameplayAttributeStore.AddModifier` raises its change from
inside the call, so a listener can stack the effect, inhibit it, remove
it, or clear the attribute before the effect system receives the
handle. After the call returns, the effect system revalidates the
instance and its inhibition state; a handle belonging to an ended or
inhibited instance is withdrawn immediately, and a surviving handle is
refreshed against the instance's current stack count. A stack applied
from that notification therefore changes the magnitude rather than
leaving the old one behind.

**Rule.** Inhibition cleanup revalidates the instance generation after
withdrawing modifiers. Removing a modifier raises an attribute
notification; a listener may remove the inhibited instance and let a
replacement acquire the same effect slot. The tag-withdrawal step must
not run against that replacement. The same rule applies to every
cleanup path that invokes user code between two pieces of slot state.

### 4.10.1 Randomized cross-store testing

`LifecycleStressHarness` runs a deterministic state machine over tags,
attributes, effects, and abilities. A fixed `SplitMix64` stream drives
both top-level operations and operations requested from committed
notifications, so a seed plus an operation limit reproduces the exact
trace. The matrix runs 128 seeds at 500 operations under both
destruction-callback orders, for 128,000 top-level operations per full
run; callback operations are additional.

The harness exercises entity creation and destruction, index reuse,
multiple generations created from one destruction handler, tag and
attribute dense membership, direct modifiers, effect application,
stacking, expiry, removal, inhibition, ability grants and activation,
cooldown rollback, and stale handles retained across every kind of
retirement. Callbacks are generated from tag, attribute, effect, and
ability notifications. A separate probe throws at each of those four
boundaries and verifies that the exception propagates without leaving a
stale counter, refresh guard, or unresolvable slot.

An independent model stores only public-contract state: counted tags,
base and additive values, effect instances and stacks, inhibition,
grants, and active ability state. After every top-level operation the
harness compares the model with public queries, dense enumeration,
stale-handle resolution, global counters, and per-entity effect and
ability lists. Failures report the seed, operation, store dump, and the
recent trace with entity, effect, ability, and modifier generations.
`ReplayFailingPrefix` reruns the shortest faithful prefix through the
first failing operation.

The model does not inspect store internals. Two ordering allowances are
deliberate: tag callbacks do not generate operations while an effect or
ability call is mid-transition, and an effect transition for a handle
whose `Applied` notification has not yet arrived is ignored until the
final `Applied` snapshot. The per-entity and global counts still reject
an orphan or a leaked instance at the end of the operation. These are
test-oracle boundaries, not relaxed production invariants.

- `DIFF` Unreal's destruction and effect notification model has no
  equivalent of a recycled index becoming observable between the
  destruction commit and a store's callback. VOLT makes the lifetime
  rule explicit because its stores are separate from the world.
- `UTEST` `CrossStoreLifecycleTests`, `GameplayEffectRecyclingTests`,
  `GameplayNotificationExceptionTests`, and the seeded
  `LifecycleStressTests` matrix cover the combined order, successive
  generations, post-exception invariants, and randomized interleavings.

| Cross-store case | Expected | Evidence |
| --- | --- | --- |
| Replacement tagged first | one dense entry; old tags retired | `UTEST` |
| Replacement attributed first | one dense entry; old modifiers retired | `UTEST` |
| Replacement gets a duration effect first | old effects retired; new effect active | `UTEST` |
| Replacement gets an infinite effect first | old infinite instance cannot leak | `UTEST` |
| Replacement granted an ability first | old grants and active count released | `UTEST` |
| Every store populated together | only the replacement's state remains | `UTEST` |
| Multiple generations before any store callback | only the newest generation remains | `UTEST` |
| Removal callback reuses the next effect slot | the new instance survives | `UTEST` |
| Throwing tag or attribute notification | mutation committed; clear/remove still works | `UTEST` |
| Throwing expiry notification | effect removed; next `Advance` works | `UTEST` |
| Throwing refresh notification | refresh flag reset; later tag changes evaluate | `UTEST` |
| Stack from a modifier notification | magnitude matches the new stack | `UTEST` |
| Remove from a modifier notification | pending modifier is withdrawn | `UTEST` |
| Slot recycled during inhibition | replacement tags survive | `UTEST` |
| 128 seeds, both destruction orders | all model and stale-handle checks pass | `UTEST` |
| Recycle an untracked entity | 72 ns, 0 B | `BENCH` |
| Recycle a tag + attribute + four-effect entity | 436 ns, 0 B | `BENCH` |

The churn rows use `GameplayLifecycleBenchmarks` in process. The tracked
case destroys the entity, creates the replacement, tags it, sets an
attribute, and applies four infinite effects before the stores observe
the destruction. The attribute store retains its modifier arrays across
the generation boundary, and both cases allocate zero managed bytes.

# Part 5 — Gameplay events and ability tasks

Implemented in `Volt.Gameplay.Events` and `Volt.Gameplay.Tasks`.

## 5.1 Gameplay events

**Rule.** A gameplay event is a value with a registered tag, an
optional source entity, an optional target entity, and an optional
typed payload. Publishing copies the value; a high-frequency event does
not allocate a managed event object. The tag carries the meaning and
the hierarchy, exactly as it does for abilities and effects.

- `DOCS` Epic describes sending a gameplay event to an actor as an
event tag plus an `FGameplayEventData` payload, delivered through the
actor's ability system (`SendGameplayEventToActor`). The ability system
can also execute the event directly and reports how many matching
abilities it activated (`HandleGameplayEvent`).
- `DIFF` VOLT's event router is independent of the ability system.
Abilities, tasks, effects, and application code share one
`GameplayEventSystem` over a world; an event is not tied to an
activation attempt. This keeps event routing usable outside gameplay
abilities and avoids a second event path.
- `DIFF` the payload is a compact discriminated value
(`GameplayEventPayload`) rather than an open `FGameplayEventData`-style
object graph. The built-in kinds cover the common numeric and entity
cases without boxing. An application that needs a new shape extends
the payload union rather than allocating a context object per event.

**Rule.** Subscriptions are indexed by tag. Dispatch walks the exact
tag bucket, then its ancestors from nearest to farthest, then the
wildcard bucket. It never scans unrelated subscriptions. A
hierarchical subscription in an ancestor bucket matches descendants;
an exact subscription in an ancestor bucket does not. A `GameplayTag.None`
subscription receives every event after the tagged subscriptions.

- `DOCS` `UAbilityTask_WaitGameplayEvent` documents `OnlyMatchExact`:
when it is false, nested tags can trigger the wait.
- `DIFF` Unreal routes through the actor's ability system. VOLT indexes
by tag directly, so dispatch scales with matching subscriptions rather
than with the number of listeners on unrelated tags or with the number
of abilities. With no subscriptions a publish is one counter check
(about 3 ns in the measured table).
- `DIFF` delivery order is specified: exact bucket first, then each
ancestor nearest to farthest, then wildcard; within a bucket, the
newest subscription runs first. Unreal does not document an ordering
contract for its event callback map.

**Rule.** Dispatch captures the matching subscriptions and their
generations before invoking any handler. Each delivery revalidates the
subscription and the scoped owner. A handler may subscribe,
unsubscribe itself or another subscription, publish another event,
cancel an ability, complete or cancel a task, or destroy an entity.
A subscription removed during dispatch does not run; a subscription
added during dispatch does not receive the event already in flight.

- `DIFF` this is the same snapshot-and-revalidate rule the ability
  system uses for bulk operations (section 4.6). Unreal code generally
  iterates its callback arrays directly, so mutation during a callback
  is the caller's responsibility.

**Rule.** Recursive publishing is bounded at 16 nested dispatches. A
handler that publishes without a terminating condition receives an
`InvalidOperationException`; the depth counter is released in `finally`
and subscriptions committed before the exception remain committed.

- `DIFF` Unreal has no equivalent recursion guard in its event
  delivery; an unbounded handler would recurse until the stack fails.

**Rule.** A subscription may be scoped to an entity. Destroying that
entity removes all of its scoped subscriptions before the world
recycles the index. A scoped subscription stores the owner's
generation, so a recycled index cannot revalidate the old subscription.
Unscoped subscriptions live until removed or the system is disposed.

- `DIFF` Unreal's ability system owns its event delegates and tears
  them down with the component. VOLT keys subscriptions by entity and
  must handle world-wide destruction explicitly, as the other stores
  do (section 4.10).

**Rule.** Notifications are post-commit. A throwing handler propagates
to the publisher. Subscriptions added or removed before the throw stay
committed; later deliveries in that dispatch do not run; the dispatch
depth is released.

## 5.2 Task identity and ownership

**Rule.** A task is owned by an activation token, not by a grant. The
token carries the grant handle and the activation serial number. A
task started for an earlier activation cannot complete, cancel,
continue, or end a later activation of the same grant.

- `DOCS` an `UAbilityTask` has an `Ability` field described as the
  gameplay ability that created it, and Epic states that a task is
  guaranteed to end no later than its owning ability.
- `DIFF` Unreal tasks are `UObject`s bound to the ability instance.
  VOLT uses a compact record with an activation token, so a retrigger
  creates a different owner identity without allocating a new managed
  task object. The token is also part of the task handle.

**Rule.** A task handle embeds the task system identity, the owner
entity, the task slot, the slot generation, and the owning activation
id. A handle from another task system, a completed task, a cancelled
task, a previous activation, or a recycled slot does not resolve.
`Cancel`, `Complete`, and `TryGetTask` return `false` for such a
handle; they do not throw.

- `DIFF` Unreal's task pointers are object references, so a stale
pointer is only as safe as its UObject lifetime. VOLT's handles are
value types and are checked against the slot generation and activation
identity on every use.

**Rule.** Task state is pooled. Each pending task is one record in a
system-wide slot array, linked into its activation's list and its
owner's list. There is no managed object per task, no virtual task
class hierarchy, and no delegate per task. Kinds are discriminated by
`GameplayAbilityTaskKind`, with kind-specific fields in the same
record.

- `DIFF` Unreal uses a class per task (`UAbilityTask_WaitDelay`,
  `UAbilityTask_WaitGameplayEvent`, and so on) with virtual
  `Activate`/`OnDestroy` behavior. VOLT keeps the kinds as data so a
  wait does not allocate and does not pay for a virtual dispatch. New
  kinds add a value to the enum and a branch where their semantics
  differ; they do not add a class or a per-task object.

**Rule.** The task runtime is created on first use. A definition with
no plan and no application task never allocates the task system, never
subscribes to tag changes, and never enters a deadline heap.

- `DIFF` Unreal's ability system has one ability task component whose
  task array exists as part of the component. VOLT's lazy ownership is
  stricter: the feature costs nothing until a task exists.

## 5.3 Duration tasks and scheduling

**Rule.** `WaitDuration` completes after a number of simulation
seconds. Time is whatever the caller passes to
`GameplayAbilityTaskSystem.Advance`; the runtime has no wall clock and
no thread. A zero step is a no-op, which is how a paused game behaves.
Time scaling is the caller's scaled step. A negative, NaN, or infinite
step throws.

- `DOCS` `UAbilityTask_WaitDelay` waits for a time and exposes an
  `OnFinish` delegate. Epic's page does not state whether that timer is
  world time, game time, or affected by pause or dilation.
- `INFER` Unreal's timer manager is normally driven by world time, so
  `WaitDelay` is likely affected by pause and time dilation. VOLT does
  not rely on that inference: it takes the scaled step explicitly, so
  pause, scaling, and single-stepping are deterministic and testable.
- `DIFF` VOLT deadlines are not Unreal timer handles. They live in a
  binary min-heap ordered by `(deadline, creation sequence)`, with a
  per-task heap index for O(log n) cancellation. No captured delegate
  is created per task. A large step completes the deadlines pending at
  the start; tasks created by those completions are deferred to the
  next advance (section 5.13).
- `DIFF` `Volt.Core.Timing.SimulationScheduler` schedules registered
  delegates against the fixed-step clock. Reusing it would require a
  delegate or capture per task and would not expose the per-entry
  cancellation and generation checks a task handle needs. The task
  heap stores values only and supports O(log n) removal; the caller
  drives it with the same scaled step.

**Rule.** Task creation never completes a task synchronously. A zero
duration task is scheduled at the current simulation time and completes
during the next positive `Advance`. A task created during an advance is
eligible on the next advance, even if its deadline has already passed.
This bounds work per step and matches the one-frame latency of a
zero-duration timer.

- `DIFF` Unreal's `ReadyForActivation` runs during the ability's
  execution and a zero-time timer may fire on a later frame. VOLT makes
  the deferral explicit and uniform for all task kinds.

**Rule.** A large advance processes every task that was pending and due
when the call started, in `(deadline, sequence)` order. Equal deadlines
complete in creation order. A task created by a completion handler is
deferred to the next advance, so one handler cannot extend the current
step without bound. Because the deferred task starts from the
end-of-step clock, chained waits are not independent of how a large
manual advance is subdivided; the fixed-step loop is the supported
deterministic path (section 5.13).

- `DIFF` Unreal processes timers per frame through its timer manager.
  VOLT's due-time ordering is deterministic for a given fixed-step
  sequence.

## 5.4 Gameplay-event tasks

**Rule.** `WaitEvent` completes when the first matching event is
delivered. A wait can match exactly or hierarchically and can filter by
source, target, or both. It is one-shot: the subscription is removed
before the completion notification. Several tasks may wait for the same
event; each receives it once, in subscription order.

- `DOCS` `UAbilityTask_WaitGameplayEvent` listens for a tag event and
  keeps listening while `OnlyTriggerOnce` is false; `OnlyMatchExact`
  controls nested-tag matching, and `OptionalExternalTarget` redirects
  the wait. Epic's page documents the one-shot listening behavior but
  not whether the task ends itself after the first event.
- `DIFF` VOLT's event task is always one-shot. Repeating waits are
  expressed by starting another task; there is no boolean that changes
  the lifetime of the subscription. This keeps cleanup a single rule.

**Rule.** An event task delivers on the simulation step that publishes
the event, not on the next advance. A completion handler therefore runs
inside `Publish`. Because the task is removed before the handler, a
handler that publishes the same event again cannot complete the same
task twice.

- `DIFF` Unreal event delivery is synchronous inside the ability
  system call as well, but the task's cleanup detail is not documented.
  VOLT specifies the order: unlink, unsubscribe, remove any deadline,
  then notify.

**Rule.** When an event arrives, the task revalidates its activation.
If the activation has ended but the task was not released (an
inconsistent state that should not occur through the public API), the
task is released silently rather than completing work for a dead
activation.

- `DIFF` defensive release instead of delivering to a dead owner. This
  mirrors the gameplay stores' generation checks at their boundaries.

## 5.5 Tag-condition tasks

**Rule.** `WaitTag` completes when a tag becomes present or absent. The
condition is evaluated when the task starts and on each tag change for
the owner; it is not polled. If the condition already holds at start,
the task is armed at the current time and completes on the next
positive advance. If it holds because of a later tag change, the task
is armed at that change and completes on the next advance. If the
condition flips back before that advance, the task stays pending and
waits for the next matching change.

- `DOCS` Epic's ability task documentation lists tasks that wait on
  gameplay events; it does not document a tag-presence wait helper with
  these exact semantics. This is a VOLT capability built on the tag
  store's change notification.
- `DIFF` Unreal's common pattern is a repeating timer or a custom task
  that polls or binds to tag delegates. VOLT indexes tag-condition
  tasks in a per-owner list, so a tag change visits only the tasks that
  watch tags, not every duration or event task the entity owns.
- `DIFF` exact versus hierarchical matching is explicit via the `exact`
  parameter. The default accepts a granted descendant, matching the tag
  store's hierarchical `HasTag`.

**Rule.** A tag-condition task must have a registered tag; a wait mode
other than `Present` or `Absent` throws when the task is created. A
task with an invalid tag is rejected by the ability plan builder as
well.

## 5.6 Ability integration and plans

**Rule.** An activated ability can start tasks through
`GameplayAbilitySystem.Tasks`. Tasks are created only while the
activation is the current activation of its grant; otherwise the start
returns `GameplayAbilityTaskHandle.None`. Ending, cancelling, revoking,
or destroying an ability releases every task of its activation before
the ability notification is raised.

- `DOCS` Epic states a task may end itself with `EndTask` or be
  terminated when its owning ability ends, and is guaranteed to end no
  later than the ability. `UGameplayTask::TaskOwnerEnded` kills the task
  and calls `OnDestroy`.
- `DIFF` VOLT has no `UObject` task to destroy. Releasing the record
  unlinks it from both lists, removes its event subscription and
  deadline, decrements the pending counter, and retires its generation.
  It then raises one `Cancelled` notification per task.
- `DIFF` cancellation notifications are raised after all of the
  activation's tasks are unlinked, not one by one during unlink. A
  handler therefore never observes a partially torn-down task set, and
  a handler that queries the activation's pending count sees zero.

**Rule.** A plan is immutable authored data: wait for a duration, wait
for an event, wait for a tag, apply an effect, publish an event, or end.
The plan builder validates its steps and the definition builder keeps
the ability active while a plan exists. A plan step owns exactly one
task at a time; when that task completes, the runtime advances to the
next step. The end of the plan ends the ability normally.

- `DIFF` Unreal expresses this sequence in the ability's
  `ActivateAbility` body with `UAbilityTask` nodes and latent Blueprint
  execution. VOLT's plan is data, so an ability definition can be
  authored, shared, and tested without a derived class. A plan has no
  branching, loops, or per-step conditions; those belong in application
  code using the low-level task API.

**Rule.** If game code cancels a task that is the current plan step,
the ability ends normally instead of staying active with no pending
task. Cancellation caused by the ability's own end is ignored by the
plan driver because the activation has already been marked inactive.

- `DIFF` Unreal's latent nodes typically resume or abort through their
  own delegates; there is no single "cancelled step ends the ability"
  rule. VOLT chooses the rule that cannot leave a plan stalled.

**Rule.** Retriggering ends the old activation and starts a new one.
The old activation's tasks are cancelled with the old token before the
new activation is committed. A stale task handle cannot cancel a task
of the new activation, and a completion from the old activation cannot
end or mutate the new one.

- `DIFF` Unreal's instanced-per-actor abilities can reuse the same
  ability object; VOLT's activation token makes the identity boundary
  explicit and testable.

**Rule.** Ending an ability with pending tasks is not an error. The
ability's `Ended` or `Cancelled` notification is raised after the task
cancellation notifications. Revocation raises `Cancelled` for the
ability after cancelling its tasks, then `Revoked`. Destroying the
owner releases grants and tasks silently, with no notifications.

- `DIFF` Unreal's `EndAbility` ends tasks as part of the ability's
  teardown; VOLT fixes the notification order so observers cannot see
  a live task after the ability is gone.

## 5.7 Developer API

**Rule.** The runtime exposes two layers over one execution system:

- Low-level: `GameplayAbilitySystem.Tasks` with `WaitDuration`,
  `WaitEvent`, `WaitTag`, `Cancel`, `Complete`, `TryGetTask`,
  `CopyTasks`, `CancelAll`, and the `Changed` notification.
- Composition: `GameplayAbilityPlan.Create()` with `Wait`, `WaitEvent`,
  `WaitTag`, `ApplyEffect`, `PublishEvent`, and `End`, attached with
  `GameplayAbilityDefinitionBuilder.Plan`.

Both create the same `TaskSlot` records and use the same event router
and deadline heap. The plan layer does not have a second scheduler.

- `DIFF` Unreal has one task class hierarchy and latent Blueprint
  nodes; VOLT separates the low-level runtime from the authoring
  composition so a data definition can stay declarative while game code
  can still start and cancel arbitrary tasks.

**Rule.** A task handle is not needed to keep a task alive. The task
runtime owns it until completion, cancellation, or ability end. A
handle is only needed to cancel, complete, or inspect a specific task.

**Rule.** `GameplayAbilityTaskChange` reports `Started`, `Completed`,
or `Cancelled`, with the task kind, completion reason, plan step, and
the event that completed an event task. The payload is a value type and
the notification allocates nothing.

## 5.8 Integrated demonstration

`samples/Volt.GameplayDemo` is a deterministic, graphics-free console
application. It runs these scenarios and asserts each result:

1. Charged Dash pays 10 stamina, starts a 0.25 s wind-up, waits for
   `Event.Dash.Released`, applies a 0.5 s speed buff that grants
   `Status.Dashing`, then waits for the tag to disappear and ends.
2. A stun during the wind-up cancels the ability. The paid cost and
   cooldown remain, the pending task is released, a stale task handle
   is refused, and a late release event is not delivered.
3. Charged Dash and Focus run concurrently with one task each.
   Cancelling the dash leaves the focus activation and its task
   untouched.
4. Application tasks use the low-level API with no plan: a duration
   task is inspected, completed, and shown stale; ending an ability
   cancels its task; a stale handle cannot cancel a task of the next
   activation.
5. Destroying an owner with a pending task releases it, and a later
   event is not delivered to the destroyed owner.

Run it with:

```bash
dotnet run --project samples/Volt.GameplayDemo -c Release
```

The sample prints every transition and exits non-zero if an assertion
fails, so it can be used as a smoke test in addition to the unit suite.

## 5.9 Determinism and exception policy

**Rule.** On one thread with one sequence of `Advance` calls, task
completion order is deterministic. Duration and tag tasks are ordered
by `(deadline, sequence)`; event tasks are ordered by subscription
order within the event router. Task slots are allocated from a free
list and retired with a new generation, so slot reuse does not change
observable order.

**Rule.** Task notifications are post-commit. A `Completed` or
`Cancelled` handler observes the task already unlinked, unsubscribed,
and removed from the heap. A `Started` handler observes a pending task.
A handler may start or cancel tasks, publish events, end the ability,
revoke the grant, or destroy the owner. Every path revalidates the
activation and generation instead of trusting a captured slot.

**Rule.** An exception from a task or event handler propagates to the
caller. State committed before the exception remains committed. If a
completion handler throws, the remaining tasks due in that advance are
not processed; the task that threw is already retired, and the advance
re-entrancy flag is released in `finally`. If one task cancellation
handler throws during ability teardown, the remaining cancellation
notifications for that activation are skipped, but every task has
already been released and the ability end still completes. Exceptions
are never swallowed. A plan step or activation-commit exception is
terminal: the activation is cancelled and the exception is rethrown,
as specified in section 5.12.

- `DIFF` Unreal generally logs or guards delegate failures; VOLT's
  contract is that the exception reaches the caller and internal state
  stays consistent. This mirrors the effect and ability systems.

## 5.10 Edge-case matrix

| Case | Expected | Evidence |
| --- | --- | --- |
| Publish with no subscriptions | returns 0; no allocation | `DIFF` |
| Publish an unregistered tag | throws | `DIFF` |
| Exact subscription on an ancestor | does not receive a descendant event | `DIFF` |
| Hierarchical subscription on an ancestor | receives a descendant event | `DOCS` |
| Wildcard subscription | receives every event after tagged subscriptions | `DIFF` |
| Two tasks wait for one event | each receives it once, in subscription order | `DIFF` |
| Handler unsubscribes itself | its remaining code runs; later candidates unaffected | `DIFF` |
| Handler unsubscribes another pending subscription | that subscription does not run | `DIFF` |
| Handler subscribes during dispatch | new subscription misses the in-flight event | `DIFF` |
| Handler publishes recursively | nested dispatch uses its own capture buffer | `DIFF` |
| Recursion beyond 16 levels | throws; depth is released | `DIFF` |
| Handler throws | exception propagates; committed subscriptions remain | `DIFF` |
| Subscription scoped to a destroyed entity | removed silently; stale index cannot revalidate | `DIFF` |
| Handle from another task system | rejected | `DIFF` |
| Stale task handle | `Cancel`/`Complete`/`TryGetTask` return false | `DIFF` |
| Task from a previous activation | cannot cancel or end the new activation | `DIFF` |
| Zero-duration task | completes on the next positive advance | `DIFF` |
| Negative or non-finite duration | throws | `DIFF` |
| Negative or non-finite advance | throws | `DIFF` |
| Zero advance | no work; zero-duration tasks wait | `DIFF` |
| Large advance | every due task in deadline order | `DIFF` |
| Equal deadlines | creation order | `DIFF` |
| Task created during an advance | eligible on the next advance | `DIFF` |
| Task created during a completion handler | eligible on the next advance | `DIFF` |
| WaitTag condition already true at start | armed at current time; completes next advance | `DIFF` |
| WaitTag condition flips back before advance | stays pending; next change re-arms | `DIFF` |
| Tag change with no tag tasks | does not walk the owner's task list | `DIFF` |
| End with pending tasks | all released; cancellation notifications precede ability notification | `DIFF` |
| Cancel during a task completion | activation ends; remaining tasks released | `DIFF` |
| Revoke with pending tasks | tasks cancelled, then `Cancelled`, then `Revoked` | `DIFF` |
| Owner destroyed with pending tasks | released silently; counters reach zero | `DIFF` |
| Entity index recycled during destruction | old tasks released before the new generation adopts the slot | `DIFF` |
| Retrigger with pending tasks | old tasks retire with the old token | `DIFF` |
| System disposed with pending tasks | subscriptions removed and records cleared | `DIFF` |
| Cancelled plan step | ability ends normally | `DIFF` |
| Completion handler ends the ability | remaining tasks released; no stale completion | `DIFF` |
| Completion handler reactivates the ability | old activation cannot affect the new one | `DIFF` |
| Completion handler throws | task retired; advance flag released | `DIFF` |
| Event task after ability end | no delivery; subscription removed | `DIFF` |
| Event task after owner destruction | no delivery; subscription removed | `DIFF` |
| Task cancellation handler throws | all tasks already released; ability end completes | `DIFF` |
| Plan step handler throws | activation cancelled; tasks released; cost kept; exception rethrown | `DIFF` |
| Plan failure cleanup also throws | `AggregateException` with both failures; activation inactive | `DIFF` |
| Cost or cooldown notification throws during commit | activation cancelled; committed effect kept | `DIFF` |
| Task created by a completion | deferred to next advance; zero duration included | `DIFF` |
| Zero task created during a completion | does not starve equal deadlines already due | `DIFF` |
| Tag change with 10,000 unrelated tag waits | visits only the changed tag's buckets | `DIFF` |
| High tag id with one event listener | one segment allocated, not a dense tag array | `DIFF` |
| Manual `Advance` while integrated | `InvalidOperationException`; no double advance | `DIFF` |
| Integration disposed | callback unregistered; manual advancement restored | `DIFF` |
| Paused loop | no gameplay advancement; `StepOnce` advances one delta | `DIFF` |

## 5.11 Performance

**Rule.** Event dispatch scales with matching subscriptions, not with
the number of listeners on unrelated tags. With no subscriptions the
cost is a counter check. Task maintenance is driven by an indexed
subscription or a due deadline; an idle task is not visited per frame.

The event table measures the number of listeners configured on the
same tag (relevant), on a different tag (irrelevant), or on an ancestor
(hierarchical). The no-listener case uses a separate system.

| Workload | 1,000 listeners | 10,000 listeners | Allocated |
| --- | ---: | ---: | ---: |
| Publish, no listeners | 3.2 ns | 3.4 ns | 0 B |
| Publish, one listener | 40.1 ns | 43.3 ns | 0 B |
| Publish, many relevant | 18.4 µs | 201.6 µs | 0 B |
| Publish, many irrelevant | 21.6 ns | 18.2 ns | 0 B |
| Publish, hierarchical | 42.3 ns | 39.5 ns | 0 B |
| Subscribe and unsubscribe | 22.9 ns | 16.5 ns | 0 B |

Many relevant deliveries cost about 18 to 20 ns per listener.
Many irrelevant listeners cost the same as an empty bucket: the 10,000
subscriptions on another tag are not visited. Hierarchical delivery
costs about the same as one exact listener plus an ancestor bucket
walk.

| Warmed task operation | Mean | Allocated |
| --- | ---: | ---: |
| Start and cancel a duration task | 92.5 ns | 0 B |
| Start and complete a duration task | 77.8 ns | 0 B |
| Start and cancel an event task | 93.1 ns | 0 B |
| Start and complete an event task | 143.3 ns | 0 B |
| Start and cancel a tag task | 113.2 ns | 0 B |
| Start, satisfy, and release a tag task | 254.3 ns | 0 B |
| Advance with no tasks | 4.9 ns | 0 B |

| Idle population | Advance cost | Allocated |
| ---: | ---: | ---: |
| 1,000 tasks | 4.9 ns | 0 B |
| 10,000 tasks | 5.8 ns | 0 B |
| 100,000 tasks | 5.3 ns | 0 B |

The idle advance is constant within measurement noise: 100,000 idle
tasks do not cost 100,000 checks. The runtime reads the heap root,
sees it is not due, and returns. This is the critical performance rule
for this milestone.

| Batch operation | 1,000 tasks | 10,000 tasks |
| --- | ---: | ---: |
| Expire all due tasks | 1,165 µs | 4,005 µs |
| Cancel all tasks | 321 µs | 893 µs |
| End an ability with tasks | 236 µs | 648 µs |
| Destroy an owner with tasks | 98 µs | 437 µs |

Batch expiry is about 0.4 µs per task at 10,000 and includes one
completion notification and one release per task. Batch cancellation
is about 0.09 µs per task; ending an ability is about 0.065 µs per
task; destroying the owner is about 0.044 µs per task. The batch
numbers have wide error bars at this sample size and are indicative of
linear scaling, not precise constants.

The task benchmarks were run on the cloud verification VM (2 logical
cores, .NET 10.0.13) with `--warmupCount 1 --iterationCount 3`; the
earlier parts' tables were measured on the workstation. Absolute
values are therefore not directly comparable. The scaling shape and
the zero-allocation results are.

**Allocation evidence.** The single-task and idle rows report zero
managed allocation from `MemoryDiagnoser`. The batch benchmark classes
use one measured invocation per iteration to pair cleanup with the
operation, so the in-process harness's per-invocation overhead would
swamp a memory column; their allocation claim is instead guarded by
`GameplayAbilityTaskAllocationTests`, including a 10,000-task bulk
expiry, 256-task ability ending, event completion, tag completion, and
the idle population. Those guards measure
`GC.GetAllocatedBytesForCurrentThread` around the warmed operations and
assert exactly zero.

**Workload sources.** `GameplayEventBenchmarks`,
`GameplayAbilityTaskBenchmarks`,
`GameplayAbilityTaskIdleBenchmarks`,
`GameplayAbilityTaskScalingBenchmarks`, and
`GameplayAbilityTaskDestructionBenchmarks` in
`benchmarks/Volt.Benchmarks`.

## 5.12 Plan failure and activation exception policy

**Rule.** A plan step that throws cancels its activation, releases
every task and subscription, clears the plan cursor, and rethrows the
original exception. The ability is inactive when the caller receives
the exception; it is never left active with no pending task and no
step that could resume it. Costs and cooldowns already committed are
deliberately not rolled back, exactly as in any other cancellation.

- The cursor is cleared before cancellation notifications run, so a
  task cancellation handler cannot try to resume the broken plan.
- The activation raises its normal `Cancelled` notification. A
  failure during the initial commit may occur before `Activated` was
  observed; the committed mutation still remains, and the activation
  is cancelled rather than silently stranded.
- If a cleanup notification also throws, the original failure and the
  cleanup failure are reported in an `AggregateException`; neither is
  swallowed or allowed to replace the other silently.
- A failed plan does not revoke the grant. Once the throwing listener
  is removed and any cooldown has expired, the ability can be
  activated again from its first step.
- An effect step whose target is no longer alive is skipped, matching
  an application that cannot reach its target; it is not an
  exception and does not fault the plan.

Exceptions from the effect, event, task, and ability notification
loops remain post-commit and propagate to the caller. The plan policy
adds a terminal state for a failure that would otherwise strand an
active activation; it does not change the existing rule that state
committed before a notification failure remains committed.

- `DIFF` Unreal's ability tasks generally log or guard delegate
  failures. VOLT's contract is explicit: plan failure is a
  cancellation plus propagation, not a silent log.
- Tests: `GameplayAbilityPlanFailureTests` covers throwing publish,
  effect, cooldown, duration-resume, event-resume, tag-resume,
  cancellation-cleanup, and a successful retry after the cause is
  removed.

## 5.13 Temporal semantics of chained tasks

**Rule.** A completion runs at the end-of-step clock. A task created
by that completion is eligible on the next advance, even when its
duration is zero. Chained waits are therefore intentionally not
independent of how a large step is subdivided:

```text
A waits 1 s; completing A starts B, which waits 1 s.
Advance(10)             -> A completes at 10, B is pending until 11
Advance(1); Advance(9)  -> A completes at 1, B completes at 10
```

This matches the fixed-step loop and `SimulationScheduler`: work
registered during a step cannot run in that same step. The alternative
logical-time replay would require completing every nested link inside
one call and needs a termination policy for endlessly chained
callbacks; VOLT deliberately chooses the fixed-step rule instead.

The zero-duration case is moved just past the step boundary
(`Math.BitIncrement(stepTime)`) when it is created during an advance.
Without that, an ineligible zero-time task could sit at the heap root
with the current deadline and starve an equal-deadline task that was
already pending. A task created before the advance keeps its exact
deadline and all due tasks from that population complete in order.

- `DIFF` This is a VOLT-specific fixed-step rule, not a claim about
  Unreal timer behavior.
- Tests: `GameplayAbilityTaskChainTests` covers one large advance,
  subdivided advances, zero-duration deferral, equal-deadline
  starvation, long zero-task chains, and deadline ordering.

## 5.14 Tag-wait index

**Rule.** A tag-condition task is indexed by its watched tag and by
whether matching is exact or hierarchical. A committed tag mutation
visits the exact bucket for that tag and, for hierarchical waits, the
buckets for the tag and each of its ancestors. It does not walk every
task on the entity.

The tag store exposes an internal `TagChanged(Entity, GameplayTag)`
notification immediately after the mutation commits and before the
general `TagsChanged` notification. The task runtime subscribes to the
detailed event; effects and abilities continue to use the general
event. The task handler only schedules a deadline and never invokes
user code.

| Watched tag waits | Relevant add+remove | Irrelevant add+remove |
| ---: | ---: | ---: |
| 1 | 161.0 ns | 168.2 ns |
| 64 | 160.5 ns | 154.4 ns |
| 1,024 | 166.9 ns | 171.9 ns |
| 10,000 | 165.4 ns | 173.0 ns |

The pair includes one add and one remove, so each mutation is roughly
80 ns including the tag store, ability notification, and effect
notification work. The cost is flat in the number of unrelated waits.

**Tradeoff.** Each entity that has used a tag-condition wait keeps two
small dictionaries (exact and hierarchical watched tags). They are
retained when empty and released when the entity is destroyed or its
slot is recycled. Removing them eagerly would make every later wait on
that entity allocate a new dictionary, breaking the warmed
zero-allocation guard. The first tag wait on an entity therefore has a
one-time dictionary allocation; subsequent start/cancel/completion is
zero-allocation.

## 5.15 Sparse event-bucket storage

**Rule.** Event bucket heads are stored in 1,024-entry segments
allocated on demand. A tag id of 100,000 allocates one 4 KiB segment
and the segment pointer table, not a dense `int[100,001]` array.
Bucket lookup is one segment load plus one index, and the hot publish
path remains two loads instead of one.

Measured on the cloud verification VM:

| Registered tags | Publish one listener | Subscribe + unsubscribe | Router + one listener allocated |
| ---: | ---: | ---: | ---: |
| 1,000 | 23.1 ns | 17.4 ns | 5,960 B |
| 100,000 | 24.7 ns | 17.9 ns | 6,712 B |

The extra 752 bytes at 100,000 tags is the wider pointer table plus one
additional 4 KiB segment for the used bucket. A dense array would need
about 400 KiB for the same id range. Existing exact, hierarchical,
irrelevant, and wildcard routing benchmarks are unchanged in shape.

- `DIFF` Unreal's generic event callback map is a tag-keyed map; the
  segmented table is a VOLT storage choice for predictable lookup and
  sparse memory.
- Tests: `GameplayEventSystemTests` and
  `GameplayEventReentrancyTests` continue to cover exact, hierarchical,
  wildcard, source/target, recycling, and recursion behavior.
  `GameplayEventSparseBenchmarks` covers the long-id case.

## 5.16 Automatic fixed-step integration

**Rule.** `GameplaySimulation` is an optional composition object in
`Volt.Gameplay` that registers one post-fixed-update callback on an
existing `GameLoop`. It does not create a clock, timer, thread, or
second scheduler. `Volt.Core` still knows nothing about gameplay.

```csharp
using Volt.Core.Timing;
using Volt.Gameplay.Simulation;

GameLoop loop = new();
GameplaySimulation simulation = new(loop, abilities, effects);

// The application advances the loop once per rendered frame.
loop.Advance();
```

The order inside one executed fixed step is:

1. Game fixed-update callbacks, in registration order.
2. Scheduled simulation callbacks due on that step.
3. The gameplay integration:
   a. `GameplayEffectSystem` advances by the loop's fixed delta. Expiry
      and period work runs, tag grants/removals commit, and tag-driven
      ability cancellation or tag-task arming happens here.
   b. `GameplayAbilityTaskSystem` advances by the same fixed delta.
      Duration and tag deadlines that were already pending complete in
      deadline order. Event tasks were completed synchronously when
      their event was published.
   c. Plan continuations run from task completion notifications, and
      may apply effects or publish events. Those effects and events are
      immediate, just as they are without the integration.
4. Remaining post-fixed-update callbacks, in registration order after
   the integration.

Consequences follow these rules:

- A task started by game fixed-update logic is advanced in the same
  step's gameplay phase, so a wait whose deadline equals the fixed
  delta completes in that step.
- An effect applied by game fixed-update logic advances in the same
  step, so a one-step effect can expire and remove its tag in that
  step.
- A task created during a completion callback is deferred to the next
  step (section 5.13). There is no unbounded same-step chain.
- A tag-condition task armed by an effect expiry is completed in the
  same step's task phase, not the next one.
- Events published during fixed updates are delivered before the
  effects phase. Events published from effects or task completions are
  delivered during the gameplay phase, before later post-fixed
  callbacks.
- With no gameplay state, the integration costs one callback and two
  clock comparisons. It does not create the task runtime unless an
  ability uses a task.
- Pausing the loop skips the integration entirely; `StepOnce` runs it
  exactly once. Time scaling changes how many fixed steps the loop
  executes, never the fixed delta.
- Catch-up executes the integration once per executed step, so effects
  and tasks cannot drift from the loop's integer step count.

**Double-advance rule.** While attached, the public
`GameplayEffectSystem.Advance` and
`GameplayAbilityTaskSystem.Advance` throw
`InvalidOperationException`. The integration calls internal
`AdvanceFromSimulation` methods. This is a check per advance call, not
per entity. Disposing the integration releases both claims and restores
manual advancement.

**Ownership.** The integration does not own the world or stores.
Dispose it before the ability and effect systems. It holds no static
state, so independent worlds and loops are independent. A second
integration over the same services is rejected before it changes any
clock claim. Disposing the integration does not cancel pending tasks or
remove subscriptions; it only stops automatic advancement.

- `DIFF` Unreal has no directly equivalent fixed-step gameplay driver
  in the referenced documentation; this ordering is a VOLT design for a
  deterministic fixed simulation, not a claim about Unreal runtime
  behavior. No Unreal project was executed in this environment.
- Tests: `GameplaySimulationTests` covers one advance per step, effect
  expiry plus tag completion in one step, tasks started from fixed
  updates, pause, single step, render-only frames, time scale, zero
  time scale, catch-up, double-advance rejection, teardown, a second
  integration, and destruction during a fixed step.

## 5.17 Performance additions

All numbers below are from the cloud verification VM (2 logical cores,
.NET 10.0.13) with `--warmupCount 1 --iterationCount 3`. They are
indicative and not directly comparable to the earlier workstation
tables.

| Fixed step | 1,000 population | 10,000 population | 100,000 population | Allocated |
| --- | ---: | ---: | ---: | ---: |
| No gameplay | 73.3 ns | 79.3 ns | 83.5 ns | 0 B |
| Empty integration | 80.0 ns | 89.8 ns | 86.7 ns | 0 B |
| Idle effects and tasks | 97.9 ns | 98.6 ns | 94.9 ns | 0 B |

The idle gameplay population is constant: 100,000 idle effects and
100,000 idle tasks add about 11 ns to an empty fixed step. Nothing is
scanned per frame.

| Event-heavy fixed step | Listeners | Mean | Allocated |
| --- | ---: | ---: | ---: |
| Publish one event from a fixed callback | 0 | 90.2 ns | 0 B |
| | 1 | 125.5 ns | 0 B |
| | 1,000 | 14.7 µs | 0 B |
| | 10,000 | 148.4 µs | 0 B |

| Cold batch workload (registration included) | 1,000 | 10,000 | 100,000 |
| --- | ---: | ---: | ---: |
| Create and complete duration tasks | 1.21 ms | 2.89 ms | 47.2 ms |
| Apply and expire effects | 0.86 ms | 3.64 ms | 16.9 ms |

The batch rows include the setup work in the measured method, because
the in-process harness has no measured-per-invocation reset hook in
this version. They are cold-path numbers and should not be compared
with the warmed single-task table. The idle and event rows use warmed
state and report zero managed allocation.

**Allocation evidence.** `GameplaySimulationTests` and the task/event
allocation guards measure `GC.GetAllocatedBytesForCurrentThread` around
warmed integration steps and event/task operations; the benchmark
`MemoryDiagnoser` columns are consistent with those guards.

**Workload sources.**
`GameplaySimulationBenchmarks`,
`GameplaySimulationEventBenchmarks`,
`GameplaySimulationTaskBurstBenchmarks`,
`GameplaySimulationEffectBurstBenchmarks`,
`GameplayTagWaitScalingBenchmarks`, and
`GameplayEventSparseBenchmarks`.

## 5.18 Known limitations

- The gameplay runtime is single-threaded. Events and tasks are not a
  cross-thread queue, and `Advance` must be called from the simulation
  thread.
- Event payloads are a closed discriminated union. Application-specific
  payloads require extending `GameplayEventPayload`; there is no
  arbitrary managed context object or boxing path.
- Event matching is tag-based. There is no separate event channel or
  string-keyed event name, and source/target filters are entity equality
  only.
- An event is delivered synchronously. There is no deferred queue,
  priority, or cross-frame coalescing. Recursive dispatch is bounded at
  16 levels.
- Tag-condition completion waits for the next `Advance` after the tag
  change, so it has up to one simulation step of latency. This is
  intentional to keep tag changes free of user callbacks.
- Plans are linear. They have no branching, loops, conditions, or
  parallel steps; those require application code with the low-level
  task API. Plans also only apply effects to the owner or the activation
  target.
- The task model currently implements duration, gameplay-event, and
  tag-condition waits. A tick-based or custom task kind would add a
  kind-specific branch and record fields; there is no public extension
  point for third-party task kinds yet.
- Tasks are not individually paused. Pausing the simulation with a zero
  step pauses all of them; time scaling applies to all deadlines.
- Cues, animation notifies, and replication prediction remain outside
  this milestone.
- The fixed-step integration is a post-fixed phase. An application that
  needs gameplay to run between specific fixed-update callbacks can use
  the manual APIs instead; there is no per-callback ordering editor.
- Manual and automatic advancement cannot be mixed for one set of
  services in the same frame. Detach the integration to regain manual
  control.
- A large manual `Advance` call is not equivalent to several fixed
  steps for chained task callbacks; the fixed step loop is the
  supported deterministic path.

# Part 6 — Cues and replication preparation

Not yet implemented.
