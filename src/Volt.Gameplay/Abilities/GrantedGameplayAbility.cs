using Volt.Ecs;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// A read-only snapshot of one granted ability.
/// </summary>
/// <remarks>
/// The snapshot is a copy. It stays valid after the grant is revoked,
/// but its <see cref="Handle"/> then refers to no ability.
/// </remarks>
public readonly struct GrantedGameplayAbility
{
	internal GrantedGameplayAbility(
		GameplayAbilityHandle handle,
		GameplayAbilityDefinition definition,
		Entity owner,
		int level,
		int inputId,
		bool isActive)
	{
		Handle = handle;
		Definition = definition;
		Owner = owner;
		Level = level;
		InputId = inputId;
		IsActive = isActive;
	}

	/// <summary>
	/// Gets the handle of the grant.
	/// </summary>
	public GameplayAbilityHandle Handle { get; }

	/// <summary>
	/// Gets the shared definition.
	/// </summary>
	public GameplayAbilityDefinition Definition { get; }

	/// <summary>
	/// Gets the entity the ability was granted to.
	/// </summary>
	public Entity Owner { get; }

	/// <summary>
	/// Gets the level the ability was granted at.
	/// </summary>
	public int Level { get; }

	/// <summary>
	/// Gets the input binding, or zero when the ability is not bound
	/// to an input.
	/// </summary>
	public int InputId { get; }

	/// <summary>
	/// Gets whether the ability is currently active.
	/// </summary>
	public bool IsActive { get; }

	/// <inheritdoc />
	public override string ToString()
	{
		return $"GrantedGameplayAbility({Definition.Name}) on {Owner}, " +
			$"level {Level}, {(IsActive ? "active" : "inactive")}";
	}
}
