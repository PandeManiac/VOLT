using Volt.Ecs;

namespace Volt.Gameplay.Abilities;

/// <summary>
/// Describes one activation attempt.
/// </summary>
/// <remarks>
/// The result is a value type, so a rejected attempt allocates
/// nothing. <see cref="Handle"/> identifies the grant that was
/// attempted, which is useful when the attempt came from an input
/// binding rather than from a handle the caller already holds.
/// </remarks>
public readonly struct GameplayAbilityActivationResult
{
	internal GameplayAbilityActivationResult(
		GameplayAbilityActivationStatus status,
		GameplayAbilityHandle handle,
		Entity owner,
		Entity target,
		int level,
		uint activationId)
	{
		Status = status;
		Handle = handle;
		Owner = owner;
		Target = target;
		Level = level;
		ActivationId = activationId;
	}

	/// <summary>
	/// Gets what the attempt did.
	/// </summary>
	public GameplayAbilityActivationStatus Status { get; }

	/// <summary>
	/// Gets the attempted grant.
	/// </summary>
	public GameplayAbilityHandle Handle { get; }

	/// <summary>
	/// Gets the entity that owns the ability.
	/// </summary>
	public Entity Owner { get; }

	/// <summary>
	/// Gets the entity the ability was activated against, or
	/// <c>default(Entity)</c> when it was not targeted.
	/// </summary>
	public Entity Target { get; }

	/// <summary>
	/// Gets the level of the grant.
	/// </summary>
	public int Level { get; }

	/// <summary>
	/// Gets the serial number of the activation this attempt committed,
	/// or zero when the attempt did not commit.
	/// </summary>
	public uint ActivationId { get; }

	/// <summary>
	/// Gets the activation token that tasks and plans bind to, or
	/// <see cref="GameplayAbilityActivation.None"/> when the attempt did
	/// not commit.
	/// </summary>
	public GameplayAbilityActivation Activation => ActivationId == 0
		? GameplayAbilityActivation.None
		: new GameplayAbilityActivation(Handle, ActivationId);

	/// <summary>
	/// Gets whether the ability activated, whether or not it is still
	/// active afterwards.
	/// </summary>
	public bool Succeeded =>
		Status is GameplayAbilityActivationStatus.Activated
			or GameplayAbilityActivationStatus.ActivatedAndEnded;

	/// <inheritdoc />
	public override string ToString()
	{
		return $"{Status} {Handle} at level {Level}";
	}
}
