namespace Volt.Gameplay.Abilities;

/// <summary>
/// Describes what an attempt to activate an ability did.
/// </summary>
/// <remarks>
/// Failures are reported, not thrown: being on cooldown or short of a
/// cost is ordinary gameplay. The order of the checks matches Unreal's
/// <c>UGameplayAbility::CanActivateAbility</c>, so an ability that is
/// both on cooldown and blocked by tags reports the cooldown.
/// </remarks>
public enum GameplayAbilityActivationStatus : byte
{
	/// <summary>
	/// The ability activated and is still active.
	/// </summary>
	Activated = 0,

	/// <summary>
	/// The ability activated and ended during the same call, because
	/// its definition ends on activation.
	/// </summary>
	ActivatedAndEnded = 1,

	/// <summary>
	/// The handle refers to no grant: it was revoked, its owner was
	/// destroyed, or it came from another system.
	/// </summary>
	NotGranted = 2,

	/// <summary>
	/// The owner has one of the cooldown effect's granted tags.
	/// </summary>
	RejectedByCooldown = 3,

	/// <summary>
	/// The cost effect's requirements are unmet, or paying it would
	/// drive an attribute below zero.
	/// </summary>
	RejectedByCost = 4,

	/// <summary>
	/// The owner or the target does not satisfy the activation
	/// requirements.
	/// </summary>
	RejectedByRequirements = 5,

	/// <summary>
	/// Another active ability blocks this ability's asset tags.
	/// </summary>
	BlockedByActiveAbility = 6,

	/// <summary>
	/// The ability is already active and its definition does not
	/// allow retriggering.
	/// </summary>
	AlreadyActive = 7,

	/// <summary>
	/// The ability ended during the activation call: a notification
	/// revoked it, cancelled it, or destroyed its owner before the
	/// result was returned.
	/// </summary>
	/// <remarks>
	/// Reversible state applied before the cost was paid, such as the
	/// cooldown, is rolled back. State applied at or after the commit
	/// point is retained.
	/// </remarks>
	EndedDuringActivation = 8
}
