using Volt.Gameplay.Attributes;

namespace Volt.Gameplay.Tests.Attributes;

/// <summary>
/// The attribute definitions shared by the attribute tests.
/// </summary>
/// <remarks>
/// The registry is process-wide, so tests share one schema instead of
/// registering colliding names. Bounded attributes exercise clamping;
/// the unbounded one isolates aggregation arithmetic.
/// </remarks>
internal static class AttributeSchema
{
	private const string Root = "VoltGameplayTests.Attributes";

	internal static readonly GameplayAttribute Health =
		GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Health",
			defaultBaseValue: 100f,
			minimum: 0f,
			maximum: 100f);

	internal static readonly GameplayAttribute Stamina =
		GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Stamina",
			defaultBaseValue: 50f,
			minimum: 0f,
			maximum: 50f);

	internal static readonly GameplayAttribute Speed =
		GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Speed",
			defaultBaseValue: 10f,
			minimum: 0f,
			maximum: float.PositiveInfinity);

	internal static readonly GameplayAttribute Unbounded =
		GameplayAttributeRegistry.Shared.Register(
			$"{Root}.Unbounded",
			defaultBaseValue: 100f);
}
