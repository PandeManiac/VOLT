namespace Volt.Gameplay.Attributes;

/// <summary>
/// Identifies one registered gameplay attribute, such as health or
/// movement speed.
/// </summary>
/// <remarks>
/// <para>
/// Like <see cref="Tags.GameplayTag"/>, an attribute is a four-byte
/// interned identifier. Its numeric value is not stable between
/// processes; persist <see cref="Name"/> and resolve it again on load.
/// </para>
/// <para>
/// Attributes are flat, not hierarchical. Unreal derives attribute
/// identity from an <c>FProperty</c> inside an attribute set; VOLT
/// uses an explicit registry so an attribute can be declared without
/// reflection, and so stores can index attributes densely.
/// </para>
/// </remarks>
public readonly struct GameplayAttribute :
	IEquatable<GameplayAttribute>,
	IComparable<GameplayAttribute>
{
	internal GameplayAttribute(uint id)
	{
		Id = id;
	}

	/// <summary>
	/// Gets an attribute that refers to no registered attribute.
	/// </summary>
	public static GameplayAttribute None => default;

	/// <summary>
	/// Gets the interned identifier of this attribute.
	/// </summary>
	public uint Id { get; }

	/// <summary>
	/// Gets whether this attribute was issued by the registry.
	/// </summary>
	public bool IsValid => Id != 0;

	/// <summary>
	/// Gets the canonical name of this attribute.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// This is the invalid attribute.
	/// </exception>
	public string Name => GameplayAttributeRegistry.Shared.GetName(this);

	/// <summary>
	/// Gets the registered definition of this attribute.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// This is the invalid attribute.
	/// </exception>
	public GameplayAttributeDefinition Definition =>
		GameplayAttributeRegistry.Shared.GetDefinition(this);

	/// <summary>
	/// Gets the value an entity uses before it is given an explicit
	/// base value.
	/// </summary>
	public float DefaultBaseValue =>
		GameplayAttributeRegistry.Shared.GetDefinition(this).DefaultBaseValue;

	/// <inheritdoc />
	public bool Equals(GameplayAttribute other)
	{
		return Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayAttribute other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return (int)Id;
	}

	/// <inheritdoc />
	public int CompareTo(GameplayAttribute other)
	{
		return Id.CompareTo(other.Id);
	}

	/// <summary>
	/// Reports whether two attributes are the same registered
	/// attribute.
	/// </summary>
	public static bool operator ==(
		GameplayAttribute left,
		GameplayAttribute right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Reports whether two attributes differ.
	/// </summary>
	public static bool operator !=(
		GameplayAttribute left,
		GameplayAttribute right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsValid
			? GameplayAttributeRegistry.Shared.GetName(this)
			: "GameplayAttribute.None";
	}
}
