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

Not yet implemented. Milestone C will add attribute identity,
base/current values, modifier ordering, clamping, and change events.

# Part 3 — Effects

Not yet implemented. Milestone D will add instant, duration, infinite,
and periodic effects, granted tags with contribution counts, stacking,
requirements, expiration, and deterministic scheduling.

# Part 4 — Abilities

Not yet implemented. Milestone E will add granting, activation
requirements, costs, cooldowns, cancellation, and lifecycle.

# Part 5 — Cues, events, and replication preparation

Not yet implemented.
