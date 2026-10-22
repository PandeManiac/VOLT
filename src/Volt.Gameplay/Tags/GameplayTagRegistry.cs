using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Volt.Gameplay.Tags;

/// <summary>
/// Interns gameplay tag names, resolves the dotted hierarchy, and
/// caches ancestry for allocation-free matching.
/// </summary>
/// <remarks>
/// <para>
/// Tags are schema rather than gameplay state, so one registry serves
/// the process, exactly as <c>ComponentRegistry</c> serves component
/// type identity. Registration happens during initialization and takes
/// a lock; matching reads an immutable snapshot and takes no lock.
/// </para>
/// <para>
/// Registering <c>Status.Stunned</c> implicitly registers
/// <c>Status</c> if it does not exist yet, so a tag's ancestors are
/// always present. Re-registering an existing name returns the
/// existing tag and never changes its identifier.
/// </para>
/// <para>
/// Names are compared case-insensitively, matching Unreal's
/// <c>FName</c>-based tag identity: <c>Status.Stunned</c> and
/// <c>status.stunned</c> are the same tag. The spelling used by the
/// first registration becomes the canonical name.
/// </para>
/// <para>
/// Each tag owns an immutable ancestor array ordered nearest-first and
/// an immutable single-tag set. Matching therefore walks cached
/// integers instead of splitting strings, and a warmed-up
/// <c>HasTag</c> check allocates nothing.
/// </para>
/// <para>
/// The registry is append-only for the lifetime of the process. Unreal
/// can unload tags in the editor; VOLT deliberately does not, because
/// stable identifiers are what allow containers and entity tag storage
/// to be queried without defensive copying or invalidation checks.
/// </para>
/// </remarks>
public sealed class GameplayTagRegistry
{
	private const int InitialCapacity = 64;

	private readonly Lock _registrationGate = new();
	private readonly Dictionary<string, GameplayTag> _tagsByName =
		new(StringComparer.OrdinalIgnoreCase);

	private Snapshot _snapshot = Snapshot.Empty;

	private GameplayTagRegistry()
	{
	}

	/// <summary>
	/// Gets the process-wide tag registry.
	/// </summary>
	public static GameplayTagRegistry Shared { get; } = new();

	/// <summary>
	/// Gets the number of registered tags, including tags that were
	/// only implicitly registered as ancestors.
	/// </summary>
	public int Count => Volatile.Read(ref _snapshot).Count;

	/// <summary>
	/// Registers a tag name and returns its identifier.
	/// </summary>
	/// <param name="tagName">
	/// The dot-separated tag name, such as <c>Status.Stunned</c>.
	/// </param>
	/// <returns>
	/// The existing tag when the name is already registered;
	/// otherwise a new tag.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// The name is <see langword="null"/>.
	/// </exception>
	/// <exception cref="ArgumentException">
	/// The name is not a valid gameplay tag string.
	/// </exception>
	/// <remarks>
	/// Ancestors that are not registered yet are registered implicitly.
	/// </remarks>
	public GameplayTag Register(string tagName)
	{
		ArgumentNullException.ThrowIfNull(tagName);

		lock (_registrationGate)
		{
			return RegisterCore(tagName);
		}
	}

	/// <summary>
	/// Registers a tag name if it is valid.
	/// </summary>
	/// <param name="tagName">The dot-separated tag name.</param>
	/// <param name="tag">
	/// The registered tag, or <see cref="GameplayTag.None"/> when the
	/// name is invalid.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the name is valid and registered;
	/// otherwise <see langword="false"/>.
	/// </returns>
	public bool TryRegister(string? tagName, out GameplayTag tag)
	{
		if (!IsValidGameplayTagString(tagName, out _))
		{
			tag = GameplayTag.None;
			return false;
		}

		lock (_registrationGate)
		{
			tag = RegisterCore(tagName!);
		}

		return true;
	}

	/// <summary>
	/// Resolves a registered tag name.
	/// </summary>
	/// <param name="tagName">The dot-separated tag name.</param>
	/// <param name="tag">
	/// The registered tag, or <see cref="GameplayTag.None"/> when the
	/// name is not registered.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the name is registered.
	/// </returns>
	/// <remarks>
	/// This method never registers a tag. Unreal's runtime
	/// <c>RequestGameplayTag</c> likewise returns an invalid tag for an
	/// unregistered name outside the editor, instead of inventing one.
	/// </remarks>
	public bool TryGetTag(string? tagName, out GameplayTag tag)
	{
		tag = GameplayTag.None;

		if (string.IsNullOrEmpty(tagName))
		{
			return false;
		}

		lock (_registrationGate)
		{
			return _tagsByName.TryGetValue(tagName, out tag);
		}
	}

	/// <summary>
	/// Resolves a registered tag name, or throws.
	/// </summary>
	/// <param name="tagName">The dot-separated tag name.</param>
	/// <returns>The registered tag.</returns>
	/// <exception cref="ArgumentException">
	/// The name is not registered or not a valid tag string.
	/// </exception>
	public GameplayTag GetTag(string tagName)
	{
		if (!TryGetTag(tagName, out GameplayTag tag))
		{
			throw new ArgumentException(
				$"The gameplay tag '{tagName}' is not registered. " +
				"Register it before resolving it by name.",
				nameof(tagName));
		}

		return tag;
	}

	/// <summary>
	/// Gets the canonical name of a tag.
	/// </summary>
	/// <param name="tag">The tag.</param>
	/// <exception cref="InvalidOperationException">
	/// The tag is invalid.
	/// </exception>
	public string GetName(GameplayTag tag)
	{
		if (!TryGetEntry(tag, out Entry entry))
		{
			throw new InvalidOperationException(
				"The invalid gameplay tag has no name. A tag must be " +
				"obtained from a registry before it can be inspected.");
		}

		return entry.Name;
	}

	/// <summary>
	/// Gets the number of dot-separated segments in a tag name.
	/// </summary>
	/// <param name="tag">The tag.</param>
	/// <returns>The depth, or zero for the invalid tag.</returns>
	public int GetDepth(GameplayTag tag)
	{
		return TryGetEntry(tag, out Entry entry) ? entry.Depth : 0;
	}

	/// <summary>
	/// Gets the immediate parent of a tag.
	/// </summary>
	/// <param name="tag">The tag.</param>
	/// <returns>
	/// The parent, or <see cref="GameplayTag.None"/> for a root tag or
	/// the invalid tag.
	/// </returns>
	public GameplayTag GetDirectParent(GameplayTag tag)
	{
		return TryGetEntry(tag, out Entry entry)
			? entry.Parent
			: GameplayTag.None;
	}

	/// <summary>
	/// Gets a tag together with all of its ancestors.
	/// </summary>
	/// <param name="tag">The tag.</param>
	/// <returns>
	/// An immutable set whose explicit tags are the tag and its
	/// ancestors.
	/// </returns>
	/// <exception cref="InvalidOperationException">
	/// The tag is invalid.
	/// </exception>
	/// <remarks>
	/// The set is cached at registration, so this call allocates
	/// nothing and can be used in a hot path.
	/// </remarks>
	public GameplayTagSet GetParents(GameplayTag tag)
	{
		if (!TryGetEntry(tag, out Entry entry))
		{
			throw new InvalidOperationException(
				"The invalid gameplay tag has no parents. A tag must be " +
				"obtained from a registry before it can be inspected.");
		}

		return entry.ParentTagSet;
	}

	/// <summary>
	/// Gets the cached ancestors of a tag, ordered nearest-first.
	/// </summary>
	/// <param name="tag">The tag.</param>
	/// <returns>
	/// The ancestors, or an empty span for the invalid tag or a root
	/// tag.
	/// </returns>
	/// <remarks>
	/// The span refers to registry-owned memory that lives for the
	/// process lifetime. It is intended for tag machinery rather than
	/// game code; use <see cref="GameplayTag.MatchesTag"/> instead.
	/// </remarks>
	public ReadOnlySpan<GameplayTag> GetAncestors(GameplayTag tag)
	{
		return TryGetEntry(tag, out Entry entry)
			? entry.Ancestors
			: ReadOnlySpan<GameplayTag>.Empty;
	}

	/// <summary>
	/// Determines whether a tag is the other tag or one of its
	/// descendants.
	/// </summary>
	/// <param name="tag">The tag to test.</param>
	/// <param name="other">The tag or ancestor to match.</param>
	/// <returns>
	/// <see langword="false"/> when either tag is invalid.
	/// </returns>
	public bool Matches(GameplayTag tag, GameplayTag other)
	{
		if (tag.Id == 0 || other.Id == 0)
		{
			return false;
		}

		if (tag.Id == other.Id)
		{
			return true;
		}

		return TryGetEntry(tag, out Entry entry)
			&& entry.Ancestors.AsSpan().Contains(other);
	}

	/// <summary>
	/// Determines whether a tag is a strict descendant of another.
	/// </summary>
	/// <param name="tag">The tag to test.</param>
	/// <param name="ancestor">The ancestor to match.</param>
	/// <returns>
	/// <see langword="false"/> when either tag is invalid, or when
	/// both tags are the same.
	/// </returns>
	public bool IsDescendantOf(GameplayTag tag, GameplayTag ancestor)
	{
		if (tag.Id == 0 || ancestor.Id == 0 || tag.Id == ancestor.Id)
		{
			return false;
		}

		return TryGetEntry(tag, out Entry entry)
			&& entry.Ancestors.AsSpan().Contains(ancestor);
	}

	/// <summary>
	/// Reports whether a string is a valid gameplay tag name.
	/// </summary>
	/// <param name="tagName">The name to validate.</param>
	/// <param name="error">
	/// A description of the first problem, or <see langword="null"/>
	/// when the name is valid.
	/// </param>
	/// <remarks>
	/// <para>
	/// A valid name is non-empty, dot-separated, and free of empty
	/// segments. It may not begin or end with a period, may not
	/// contain whitespace, control characters, quotes, or commas, and
	/// may not contain consecutive periods.
	/// </para>
	/// <para>
	/// These rules reproduce the intent of Unreal's
	/// <c>IsValidGameplayTagString</c>: a leading or trailing period,
	/// leading or trailing space, and an invalid character inside are
	/// all rejected. VOLT rejects any whitespace anywhere instead of
	/// only at the ends, because an interior space is almost always a
	/// typo and is nearly impossible to spot in a log.
	/// </para>
	/// </remarks>
	public static bool IsValidGameplayTagString(
		string? tagName,
		[NotNullWhen(false)] out string? error)
	{
		if (string.IsNullOrEmpty(tagName))
		{
			error = "A gameplay tag name may not be empty.";
			return false;
		}

		if (tagName[0] == '.' || tagName[^1] == '.')
		{
			error = "A gameplay tag name may not begin or end with a period.";
			return false;
		}

		bool previousWasSeparator = false;

		for (int index = 0; index < tagName.Length; index++)
		{
			char character = tagName[index];

			if (character == '.')
			{
				if (previousWasSeparator)
				{
					error =
						"A gameplay tag name may not contain consecutive " +
						"periods; every segment must have a name.";
					return false;
				}

				previousWasSeparator = true;
				continue;
			}

			previousWasSeparator = false;

			if (char.IsWhiteSpace(character))
			{
				error =
					"A gameplay tag name may not contain whitespace. " +
					"Use a period to separate segments.";
				return false;
			}

			if (char.IsControl(character)
				|| character == '"'
				|| character == '\''
				|| character == ',')
			{
				error =
					$"A gameplay tag name may not contain the character " +
					$"'{character}'.";
				return false;
			}
		}

		error = null;
		return true;
	}

	/// <summary>
	/// Enumerates every registered tag in registration order.
	/// </summary>
	/// <returns>The registered tags.</returns>
	/// <remarks>
	/// Enumeration allocates an iterator; use it for tooling and
	/// diagnostics, not in a simulation loop.
	/// </remarks>
	public IEnumerable<GameplayTag> EnumerateTags()
	{
		Snapshot snapshot = Volatile.Read(ref _snapshot);

		for (int index = 0; index < snapshot.Count; index++)
		{
			yield return new GameplayTag((uint)index + 1);
		}
	}

	private GameplayTag RegisterCore(string tagName)
	{
		if (_tagsByName.TryGetValue(tagName, out GameplayTag existing))
		{
			return existing;
		}

		if (!IsValidGameplayTagString(tagName, out string? error))
		{
			throw new ArgumentException(
				$"'{tagName}' is not a valid gameplay tag name. {error}",
				nameof(tagName));
		}

		int separatorIndex = tagName.LastIndexOf('.');

		GameplayTag parent = separatorIndex < 0
			? GameplayTag.None
			: RegisterCore(tagName[..separatorIndex]);

		GameplayTag[] ancestors;

		if (parent.IsValid)
		{
			ref Entry parentEntry = ref GetEntry(parent);

			ancestors = new GameplayTag[parentEntry.Ancestors.Length + 1];
			ancestors[0] = parent;
			parentEntry.Ancestors.CopyTo(ancestors, 1);
		}
		else
		{
			ancestors = [];
		}

		Snapshot snapshot = _snapshot;
		GameplayTag tag = new((uint)snapshot.Count + 1);

		Entry entry = new(
			tagName,
			parent,
			ancestors.Length + 1,
			ancestors,
			GameplayTagSet.CreateWithParents(tag, ancestors));

		AppendEntry(entry);

		_tagsByName.Add(tagName, tag);

		return tag;
	}

	private void AppendEntry(Entry entry)
	{
		Snapshot snapshot = _snapshot;
		Entry[] entries = snapshot.Entries;

		if (snapshot.Count == entries.Length)
		{
			Entry[] grown = new Entry[entries.Length * 2];

			entries.AsSpan().CopyTo(grown);
			entries = grown;
		}

		entries[snapshot.Count] = entry;

		// The entry is fully written before the snapshot that counts
		// it is published. A reader holding the previous snapshot
		// never indexes the slot that is being filled.
		Volatile.Write(ref _snapshot, new Snapshot(entries, snapshot.Count + 1));
	}

	private ref Entry GetEntry(GameplayTag tag)
	{
		Snapshot snapshot = _snapshot;

		return ref snapshot.Entries[(int)(tag.Id - 1)];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private bool TryGetEntry(GameplayTag tag, out Entry entry)
	{
		Snapshot snapshot = Volatile.Read(ref _snapshot);

		uint id = tag.Id;

		if (id == 0 || id > (uint)snapshot.Count)
		{
			entry = default;
			return false;
		}

		entry = snapshot.Entries[(int)(id - 1)];
		return true;
	}

	private struct Entry(
		string name,
		GameplayTag parent,
		int depth,
		GameplayTag[] ancestors,
		GameplayTagSet parentTagSet)
	{
		public string Name { get; } = name;
		public GameplayTag Parent { get; } = parent;
		public int Depth { get; } = depth;
		public GameplayTag[] Ancestors { get; } = ancestors;
		public GameplayTagSet ParentTagSet { get; } = parentTagSet;
	}

	private sealed class Snapshot(Entry[] entries, int count)
	{
		public static Snapshot Empty { get; } =
			new(new Entry[InitialCapacity], 0);

		public Entry[] Entries { get; } = entries;
		public int Count { get; } = count;
	}
}
