using Volt.Ecs;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Identifies one activation of a grant.
/// </summary>
/// <remarks>
/// <para>
/// A grant handle alone is not enough to own asynchronous work: the
/// same grant can activate, end, and activate again, and each instance
/// needs its own identity. The activation carries the grant handle and
/// the activation serial number that the grant was assigned when the
/// attempt committed.
/// </para>
/// <para>
/// Tasks started from a token stay bound to that activation. If the
/// ability retriggers, the token of the earlier activation no longer
/// resolves, and a completion from the earlier activation can never
/// continue or end the newer one.
/// </para>
/// </remarks>
public readonly struct GameplayAbilityActivation
	: IEquatable<GameplayAbilityActivation>
{
	internal GameplayAbilityActivation(
		GameplayAbilityHandle ability,
		uint id)
	{
		Ability = ability;
		Id = id;
	}

	/// <summary>
	/// Gets a token that refers to no activation.
	/// </summary>
	public static GameplayAbilityActivation None => default;

	/// <summary>
	/// Gets whether this token refers to no activation.
	/// </summary>
	public bool IsNone => Id == 0 || Ability.IsNone;

	/// <summary>
	/// Gets the grant that was activated.
	/// </summary>
	public GameplayAbilityHandle Ability { get; }

	/// <summary>
	/// Gets the activation serial number within the grant.
	/// </summary>
	public uint Id { get; }

	/// <summary>
	/// Gets the entity that owns the ability.
	/// </summary>
	public Entity Owner => Ability.Owner;

	/// <inheritdoc />
	public bool Equals(GameplayAbilityActivation other)
	{
		return Ability == other.Ability && Id == other.Id;
	}

	/// <inheritdoc />
	public override bool Equals(object? obj)
	{
		return obj is GameplayAbilityActivation other && Equals(other);
	}

	/// <inheritdoc />
	public override int GetHashCode()
	{
		return HashCode.Combine(Ability, Id);
	}

	/// <summary>
	/// Determines whether two tokens refer to the same activation.
	/// </summary>
	public static bool operator ==(
		GameplayAbilityActivation left,
		GameplayAbilityActivation right)
	{
		return left.Equals(right);
	}

	/// <summary>
	/// Determines whether two tokens refer to different activations.
	/// </summary>
	public static bool operator !=(
		GameplayAbilityActivation left,
		GameplayAbilityActivation right)
	{
		return !left.Equals(right);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return IsNone
			? "GameplayAbilityActivation(none)"
			: $"GameplayAbilityActivation({Ability}, id {Id})";
	}
}
