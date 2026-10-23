using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tests.Attributes;
using Volt.Gameplay.Tests.Tags;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// Covers an effect slot recycled while inhibition is withdrawing its
/// modifiers.
/// </summary>
/// <remarks>
/// The attribute notification raised by a modifier removal is
/// user-observable. Removing the inhibited instance and applying another
/// one from that notification can hand the same effect slot to a new
/// instance. The rest of the inhibition cleanup must not then look the
/// slot up by index and withdraw the new instance's tags.
/// </remarks>
public sealed class GameplayEffectInhibitionReentrancyTests
{
	[Fact]
	public void RecyclingTheSlotDuringInhibition_DoesNotWithdrawTheReplacementTags()
	{
		using EffectRuntime runtime = new();
		Entity entity = runtime.CreateAffectedEntity();

		runtime.Tags.AddTag(entity, TagSchema.StatusBurning);
		runtime.Effects.Apply(entity, EffectSchema.OnlyWhileBurning);

		bool replaced = false;

		runtime.Attributes.AttributeChanged += change =>
		{
			if (replaced
				|| change.Entity != entity
				|| change.Attribute != AttributeSchema.Unbounded)
			{
				return;
			}

			replaced = true;

			Assert.True(runtime.Effects.TryFindEffect(
				entity,
				EffectSchema.OnlyWhileBurning,
				out ActiveGameplayEffect inhibiting));
			Assert.True(runtime.Effects.Remove(inhibiting.Handle));

			runtime.Effects.Apply(entity, EffectSchema.Stun);
		};

		Assert.True(runtime.Tags.RemoveTag(entity, TagSchema.StatusBurning));

		Assert.True(replaced);
		Assert.True(runtime.Effects.TryFindEffect(
			entity,
			EffectSchema.Stun,
			out _));
		Assert.Equal(
			1,
			runtime.Tags.GetTagCount(entity, TagSchema.StatusStunned));
		Assert.Equal(1, runtime.Effects.ActiveEffectCount);
	}
}
