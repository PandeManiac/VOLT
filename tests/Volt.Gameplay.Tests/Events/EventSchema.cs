using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Events;

/// <summary>
/// The event tags shared by the event tests.
/// </summary>
internal static class EventSchema
{
	private const string Root = "VoltGameplayTests.Event";

	internal static readonly GameplayTag Event =
		Register(Root);

	internal static readonly GameplayTag Damage =
		Register($"{Root}.Damage");

	internal static readonly GameplayTag DamageReceived =
		Register($"{Root}.Damage.Received");

	internal static readonly GameplayTag Attack =
		Register($"{Root}.Attack");

	internal static readonly GameplayTag AttackHit =
		Register($"{Root}.Attack.Hit");

	internal static readonly GameplayTag AbilityInterrupted =
		Register($"{Root}.Ability.Interrupted");

	internal static readonly GameplayTag InteractionCompleted =
		Register($"{Root}.Interaction.Completed");

	internal static readonly GameplayTag Unrelated =
		Register($"{Root}.Unrelated");

	private static GameplayTag Register(string name)
	{
		return GameplayTagRegistry.Shared.Register(name);
	}
}
