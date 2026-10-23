using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tests.Attributes;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// Covers re-entrancy through the attribute notification raised while an
/// effect is installing one of its modifiers.
/// </summary>
/// <remarks>
/// The effect is already active and linked when the notification fires,
/// but its modifier count is not yet published. A listener may stack or
/// remove the instance from that callback, so the installer must
/// reconcile the handle it just received instead of assuming the
/// instance stayed unchanged.
/// </remarks>
public sealed class GameplayEffectReentrantModifierTests
{
	private static readonly GameplayEffectDefinition StackingTwo =
		GameplayEffectDefinition
			.Create("Test.Reentrant.StackingTwo")
			.HasDuration(5f)
			.Modifier(
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				2f)
			.Stacking(GameplayEffectStackingType.AggregateByTarget, 3)
			.Build();

	[Fact]
	public void StackingFromTheModifierNotification_RefreshesTheModifierMagnitude()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		bool appliedAgain = false;

		runtime.Attributes.AttributeChanged += change =>
		{
			if (appliedAgain
				|| change.Entity != entity
				|| change.Attribute != AttributeSchema.Unbounded)
			{
				return;
			}

			appliedAgain = true;

			runtime.Effects.Apply(entity, StackingTwo);
		};

		GameplayEffectApplyResult result = runtime.Effects.Apply(
			entity,
			StackingTwo);

		Assert.True(appliedAgain);
		Assert.Equal(GameplayEffectApplyStatus.Applied, result.Status);
		Assert.True(runtime.Effects.TryFindEffect(
			entity,
			StackingTwo,
			out ActiveGameplayEffect effect));
		Assert.Equal(2, effect.StackCount);
		Assert.Equal(
			104f,
			runtime.Attributes.GetCurrentValue(
				entity,
				AttributeSchema.Unbounded));
	}

	[Fact]
	public void RemovingFromTheModifierNotification_WithdrawsThePendingModifier()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		bool removed = false;

		runtime.Attributes.AttributeChanged += change =>
		{
			if (removed
				|| change.Entity != entity
				|| change.Attribute != AttributeSchema.Unbounded)
			{
				return;
			}

			removed = true;

			Assert.True(runtime.Effects.TryFindEffect(
				entity,
				StackingTwo,
				out ActiveGameplayEffect effect));
			Assert.True(runtime.Effects.Remove(effect.Handle));
		};

		GameplayEffectApplyResult result = runtime.Effects.Apply(
			entity,
			StackingTwo);

		Assert.True(removed);
		Assert.Equal(GameplayEffectApplyStatus.Discarded, result.Status);
		Assert.Equal(0, runtime.Effects.ActiveEffectCount);
		Assert.Equal(
			100f,
			runtime.Attributes.GetCurrentValue(
				entity,
				AttributeSchema.Unbounded));
	}
}
