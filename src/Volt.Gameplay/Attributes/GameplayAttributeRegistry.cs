using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Volt.Gameplay.Attributes;

/// <summary>
/// Registers gameplay attributes and their numeric ranges.
/// </summary>
/// <remarks>
/// <para>
/// Attributes are schema, so one registry serves the process, exactly
/// like the tag and component registries. Registration takes a lock
/// and is expected during initialization; reads take no lock.
/// </para>
/// <para>
/// Registering the same name again returns the existing attribute if
/// the definition matches and throws if it does not. Silently
/// accepting a different default or range would make two systems
/// disagree about the same attribute depending on which loaded first.
/// </para>
/// <para>
/// Names are case-insensitive, following the same rule as tags and
/// Unreal's reflected property names. Dots are allowed for grouping
/// (<c>Vitals.Health</c>) even though attributes are flat; the dot has
/// no matching meaning and does not create a parent attribute.
/// </para>
/// </remarks>
public sealed class GameplayAttributeRegistry
{
	private const int InitialCapacity = 32;

	private readonly Lock _registrationGate = new();
	private readonly Dictionary<string, GameplayAttribute> _attributesByName =
		new(StringComparer.OrdinalIgnoreCase);

	private Snapshot _snapshot = Snapshot.Empty;

	private GameplayAttributeRegistry()
	{
	}

	/// <summary>
	/// Gets the process-wide attribute registry.
	/// </summary>
	public static GameplayAttributeRegistry Shared { get; } = new();

	/// <summary>
	/// Gets the number of registered attributes.
	/// </summary>
	public int Count => Volatile.Read(ref _snapshot).Count;

	/// <summary>
	/// Registers an attribute.
	/// </summary>
	/// <param name="name">The canonical attribute name.</param>
	/// <param name="defaultBaseValue">
	/// The base and current value used before an entity has explicit
	/// state.
	/// </param>
	/// <param name="minimum">
	/// The lower bound applied to base and current values, or
	/// <see cref="float.NegativeInfinity"/> for none.
	/// </param>
	/// <param name="maximum">
	/// The upper bound applied to base and current values, or
	/// <see cref="float.PositiveInfinity"/> for none.
	/// </param>
	/// <returns>
	/// The existing attribute when the same name and definition are
	/// already registered; otherwise a new attribute.
	/// </returns>
	/// <exception cref="ArgumentException">
	/// The name is invalid, a value is not finite where required, the
	/// range is inverted, or the default lies outside the range.
	/// </exception>
	/// <exception cref="InvalidOperationException">
	/// The name is registered with a different definition.
	/// </exception>
	public GameplayAttribute Register(
		string name,
		float defaultBaseValue = 0f,
		float minimum = float.NegativeInfinity,
		float maximum = float.PositiveInfinity)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		if (!IsValidAttributeName(name, out string? error))
		{
			throw new ArgumentException(
				$"'{name}' is not a valid attribute name. {error}",
				nameof(name));
		}

		if (!float.IsFinite(defaultBaseValue))
		{
			throw new ArgumentException(
				"An attribute default value must be finite.",
				nameof(defaultBaseValue));
		}

		if (float.IsNaN(minimum) || float.IsNaN(maximum))
		{
			throw new ArgumentException(
				"An attribute range cannot use NaN as a bound.",
				nameof(minimum));
		}

		if (minimum > maximum)
		{
			throw new ArgumentException(
				$"The attribute range is inverted: minimum " +
				$"{minimum} is greater than maximum {maximum}.",
				nameof(minimum));
		}

		if (defaultBaseValue < minimum || defaultBaseValue > maximum)
		{
			throw new ArgumentException(
				$"The default value {defaultBaseValue} lies outside the " +
				$"range {minimum}..{maximum}. A default outside its own " +
				"range would be clamped the first time it is read.",
				nameof(defaultBaseValue));
		}

		lock (_registrationGate)
		{
			if (_attributesByName.TryGetValue(name, out GameplayAttribute existing))
			{
				GameplayAttributeDefinition definition = GetDefinitionCore(existing);

				if (definition.DefaultBaseValue != defaultBaseValue
					|| definition.Minimum != minimum
					|| definition.Maximum != maximum)
				{
					throw new InvalidOperationException(
						$"The attribute '{definition.Name}' is already " +
						"registered with a different definition. An " +
						"attribute has one default and one range for the " +
						"process; declare it once and share the handle.");
				}

				return existing;
			}

			Snapshot snapshot = _snapshot;
			GameplayAttribute attribute = new((uint)snapshot.Count + 1);

			GameplayAttributeDefinition created = new(
				attribute,
				name,
				defaultBaseValue,
				minimum,
				maximum);

			AppendEntry(created);

			_attributesByName.Add(name, attribute);

			return attribute;
		}
	}

	/// <summary>
	/// Resolves a registered attribute name.
	/// </summary>
	/// <param name="name">The attribute name.</param>
	/// <param name="attribute">
	/// The attribute, or <see cref="GameplayAttribute.None"/> when the
	/// name is not registered.
	/// </param>
	/// <returns>
	/// <see langword="true"/> when the name is registered.
	/// </returns>
	public bool TryGet(string? name, out GameplayAttribute attribute)
	{
		attribute = GameplayAttribute.None;

		if (string.IsNullOrEmpty(name))
		{
			return false;
		}

		lock (_registrationGate)
		{
			return _attributesByName.TryGetValue(name, out attribute);
		}
	}

	/// <summary>
	/// Resolves a registered attribute name, or throws.
	/// </summary>
	/// <param name="name">The attribute name.</param>
	/// <exception cref="ArgumentException">
	/// The name is not registered.
	/// </exception>
	public GameplayAttribute Get(string name)
	{
		if (!TryGet(name, out GameplayAttribute attribute))
		{
			throw new ArgumentException(
				$"The gameplay attribute '{name}' is not registered. " +
				"Register it before resolving it by name.",
				nameof(name));
		}

		return attribute;
	}

	/// <summary>
	/// Gets the canonical name of an attribute.
	/// </summary>
	/// <param name="attribute">The attribute.</param>
	/// <exception cref="InvalidOperationException">
	/// The attribute is invalid.
	/// </exception>
	public string GetName(GameplayAttribute attribute)
	{
		return GetDefinition(attribute).Name;
	}

	/// <summary>
	/// Gets the definition of an attribute.
	/// </summary>
	/// <param name="attribute">The attribute.</param>
	/// <exception cref="InvalidOperationException">
	/// The attribute is invalid.
	/// </exception>
	public GameplayAttributeDefinition GetDefinition(GameplayAttribute attribute)
	{
		if (!TryGetDefinition(attribute, out GameplayAttributeDefinition definition))
		{
			throw new InvalidOperationException(
				"The invalid gameplay attribute has no definition. An " +
				"attribute must be obtained from a registry before it " +
				"can be inspected.");
		}

		return definition;
	}

	/// <summary>
	/// Attempts to get the definition of an attribute.
	/// </summary>
	/// <param name="attribute">The attribute.</param>
	/// <param name="definition">The definition.</param>
	/// <returns>
	/// <see langword="false"/> for the invalid or an unregistered
	/// attribute.
	/// </returns>
	public bool TryGetDefinition(
		GameplayAttribute attribute,
		out GameplayAttributeDefinition definition)
	{
		Snapshot snapshot = Volatile.Read(ref _snapshot);

		uint id = attribute.Id;

		if (id == 0 || id > (uint)snapshot.Count)
		{
			definition = default;
			return false;
		}

		definition = snapshot.Definitions[(int)(id - 1)];
		return true;
	}

	/// <summary>
	/// Enumerates every registered attribute in registration order.
	/// </summary>
	public IEnumerable<GameplayAttribute> EnumerateAttributes()
	{
		Snapshot snapshot = Volatile.Read(ref _snapshot);

		for (int index = 0; index < snapshot.Count; index++)
		{
			yield return new GameplayAttribute((uint)index + 1);
		}
	}

	/// <summary>
	/// Reports whether a string is a valid attribute name.
	/// </summary>
	/// <param name="name">The name to validate.</param>
	/// <param name="error">
	/// A description of the problem, or <see langword="null"/> when
	/// the name is valid.
	/// </param>
	/// <remarks>
	/// A name may not be empty, contain whitespace or control
	/// characters, or begin or end with a period. Attributes are flat,
	/// so there is no requirement that a dotted prefix exists.
	/// </remarks>
	public static bool IsValidAttributeName(
		string? name,
		[NotNullWhen(false)] out string? error)
	{
		if (string.IsNullOrEmpty(name))
		{
			error = "An attribute name may not be empty.";
			return false;
		}

		if (name[0] == '.' || name[^1] == '.')
		{
			error = "An attribute name may not begin or end with a period.";
			return false;
		}

		for (int index = 0; index < name.Length; index++)
		{
			char character = name[index];

			if (char.IsWhiteSpace(character))
			{
				error = "An attribute name may not contain whitespace.";
				return false;
			}

			if (char.IsControl(character) || character == '"' || character == '\'')
			{
				error =
					$"An attribute name may not contain the character " +
					$"'{character}'.";
				return false;
			}

			if (character == '.'
				&& index + 1 < name.Length
				&& name[index + 1] == '.')
			{
				// A dot is a visual grouping separator; consecutive
				// dots would produce an empty group.
				error =
					"An attribute name may not contain consecutive " +
					"periods.";
				return false;
			}
		}

		error = null;
		return true;
	}

	private GameplayAttributeDefinition GetDefinitionCore(
		GameplayAttribute attribute)
	{
		return _snapshot.Definitions[(int)(attribute.Id - 1)];
	}

	private void AppendEntry(GameplayAttributeDefinition definition)
	{
		Snapshot snapshot = _snapshot;
		GameplayAttributeDefinition[] definitions = snapshot.Definitions;

		if (snapshot.Count == definitions.Length)
		{
			GameplayAttributeDefinition[] grown =
				new GameplayAttributeDefinition[definitions.Length * 2];

			definitions.AsSpan().CopyTo(grown);
			definitions = grown;
		}

		definitions[snapshot.Count] = definition;

		Volatile.Write(
			ref _snapshot,
			new Snapshot(definitions, snapshot.Count + 1));
	}

	private sealed class Snapshot(
		GameplayAttributeDefinition[] definitions,
		int count)
	{
		public static Snapshot Empty { get; } =
			new(new GameplayAttributeDefinition[InitialCapacity], 0);

		public GameplayAttributeDefinition[] Definitions { get; } = definitions;
		public int Count { get; } = count;
	}
}
