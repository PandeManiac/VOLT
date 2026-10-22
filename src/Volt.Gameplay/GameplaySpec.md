# VOLT Gameplay Behavioral Specification

**Reference engine:** Unreal Engine 5.8.4, local source at
`/home/berds/applications/unreal-engine-5.8.4`.
**Status:** living document. Sections are added as milestones land.
**Last updated:** 2026-10-22.

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

- `DIFF`: Unreal's ability system owns its count container and is
  destroyed with its owner. VOLT stores tags beside entity slots, so
  it adds two safeguards Unreal's loose container does not have:
  `World.EntityDestroyed` clears the slot immediately, and every
  access validates the entity generation, so a recycled index is a
  different entity even before the destruction callback runs.
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

**Rule.** A modifier handle carries the entity, attribute, slot, and
slot generation. Removing advances the slot generation, so a stale
handle cannot remove a modifier that reuses the slot.

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

**Rule.** A handle is generation-checked. Revoking a grant or
destroying its owner retires the handle, and a later grant that reuses
the slot is not reachable through it.

- `DIFF` Unreal's `FGameplayAbilitySpecHandle` is a slot index plus a
  serial number allocated from a shared counter. VOLT embeds the owner
  and a per-slot generation, so a handle from another world can never
  resolve.

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
zero. Only `AddBase` modifiers are checked. An affordable cost executes
once against the owner's base value.

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
- `DIFF` Unreal also relies on attribute clamping for the actual
  payment. VOLT applies the same instant effect, so a cost that lands
  exactly on zero clamps and stays there.

## 4.5 Cooldowns

**Rule.** Activation applies the cooldown effect to the owner. The
owner is on cooldown while any of the cooldown effect's granted tags is
present, whatever granted it. The remaining time is the cooldown
effect's remaining duration, or infinity for an infinite cooldown.

- `SRC` `UGameplayAbility::GetCooldownTags` returns the cooldown
  effect's granted tags; `CheckCooldown` calls
  `HasAnyMatchingGameplayTags` on the owner; `ApplyCooldown` applies
  the effect to the owner.
- `SRC` `GetCooldownTimeRemaining` builds
  `FGameplayEffectQuery::MakeQuery_MatchAnyOwningTags(cooldownTags)` and
  returns the largest remaining duration among matching active
  effects. `GetActiveEffectsTimeRemaining` does not skip inhibited
  effects.
- `DIFF` VOLT looks up its own cooldown effect instance instead of
  querying by tags. The tags block reactivation, so at most one
  instance of it can exist; a cooldown that is inhibited reports its
  remaining time but does not count as "on cooldown", because its
  tags are withdrawn.

## 4.6 Lifecycle, ending, and cancellation

**Rule.** An ability ends as soon as its activation is committed unless
its definition keeps it active. `End` reports `Ended`; `Cancel`
reports `Cancelled`. Revoking an active grant cancels it first. Ending
or cancelling an inactive ability does nothing and returns `false`.

- `SRC` `UGameplayAbility::EndAbility`, `CancelAbility`, and
  `UAbilitySystemComponent::ClearAbility`, which cancels the active
  instance before removing the spec.
- `DIFF` a data definition has no tasks or input of its own, so ending
  on activation is the default; Unreal abilities stay active until
  they call `EndAbility`.

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
such a handler reports `EndedDuringActivation`; the attempt did commit,
but no active ability remains.

- `DIFF` this mirrors the effect system's `Discarded` status. Unreal
  has no return value for an activation, only the ability's own
  `IsActive` state.

## 4.7 Determinism

There is no ability clock. Cooldown expiry is an effect deadline, so
time scaling, pausing, and deterministic ordering are inherited from
the effect system. Each new grant is pushed onto the front of its
owner's list, so input activation and `CopyAbilities` walk the newest
grant first, and cancelling by tags walks the same order.

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
| Activation revoked by a handler | `EndedDuringActivation` | `DIFF` |
| Owner destroyed by a handler | `EndedDuringActivation` | `DIFF` |
| Activation by input | every grant bound to the input is attempted | `SRC` |

## 4.9 Performance

Same machine and job as the earlier parts, in process, with a grant on
every entity in a populated world and a separate set of actors for the
measured operations.

| Workload | 1,000 | 100,000 | Allocated |
| --- | ---: | ---: | ---: |
| Activate and end a lasting ability | 21.9 ns | 22.5 ns | 0 B |
| Activate with a cost | 57.5 ns | 57.0 ns | 0 B |
| Activate while on cooldown | 21.9 ns | 22.1 ns | 0 B |
| Activate with an unmet requirement | 20.8 ns | 21.0 ns | 0 B |
| Activate while blocked | 22.4 ns | 21.8 ns | 0 B |
| Activate by input | 33.8 ns | 33.4 ns | 0 B |
| Give and revoke | 13.5 ns | 13.3 ns | 0 B |
| `GetAbilityCount` | 0.69 ns | 0.70 ns | 0 B |
| Cooldown time remaining | 2.07 ns | 2.05 ns | 0 B |

Every measured path is flat in world size: an ability operation touches
only its owner's grant list and the owner's tags, never the
population. Give and revoke allocate nothing once a slot and its owner
segment are warm, and an activation allocates nothing at all.

**Limitation.** These ran with the in-process toolchain, for the same
reason as the earlier parts: untracked experiment workspaces contain
duplicate benchmark project names.

# Part 5 — Cues, events, and replication preparation

Not yet implemented.
