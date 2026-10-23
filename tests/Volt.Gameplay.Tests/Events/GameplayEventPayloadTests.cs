using Volt.Ecs;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Events;

/// <summary>
/// Covers the compact payload union and the event value itself.
/// </summary>
public sealed class GameplayEventPayloadTests
{
	[Fact]
	public void NumberPayload_RoundTrips()
	{
		GameplayEventPayload payload =
			GameplayEventPayload.FromNumber(42.5);

		Assert.Equal(GameplayEventPayloadKind.Number, payload.Kind);
		Assert.False(payload.IsEmpty);
		Assert.Equal(42.5, payload.Number);
		Assert.True(payload.TryGetNumber(out double number));
		Assert.Equal(42.5, number);
		Assert.False(payload.TryGetEntity(out _));
		Assert.False(payload.TryGetTag(out _));
	}

	[Fact]
	public void EntityPayload_RoundTrips()
	{
		Entity entity = new(allocatorId: 3, index: 7, generation: 2);

		GameplayEventPayload payload =
			GameplayEventPayload.FromEntity(entity);

		Assert.Equal(GameplayEventPayloadKind.Entity, payload.Kind);
		Assert.Equal(entity, payload.Entity);
		Assert.True(payload.TryGetEntity(out Entity value));
		Assert.Equal(entity, value);
	}

	[Fact]
	public void TagPayload_RoundTrips()
	{
		GameplayEventPayload payload =
			GameplayEventPayload.FromTag(EventSchema.Damage);

		Assert.Equal(GameplayEventPayloadKind.Tag, payload.Kind);
		Assert.Equal(EventSchema.Damage, payload.Tag);
		Assert.True(payload.TryGetTag(out GameplayTag value));
		Assert.Equal(EventSchema.Damage, value);
	}

	[Fact]
	public void NonePayload_IsEmpty()
	{
		GameplayEventPayload payload = GameplayEventPayload.None;

		Assert.True(payload.IsEmpty);
		Assert.Equal(GameplayEventPayloadKind.None, payload.Kind);
		Assert.Equal("None", payload.ToString());
	}

	[Fact]
	public void ReadingTheWrongShape_Throws()
	{
		GameplayEventPayload number =
			GameplayEventPayload.FromNumber(1d);

		Assert.Throws<InvalidOperationException>(() => number.Entity);
		Assert.Throws<InvalidOperationException>(() => number.Tag);

		GameplayEventPayload tag =
			GameplayEventPayload.FromTag(EventSchema.Attack);

		Assert.Throws<InvalidOperationException>(() => tag.Number);
	}

	[Fact]
	public void NonFiniteNumber_IsRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(
			() => GameplayEventPayload.FromNumber(double.NaN));
		Assert.Throws<ArgumentOutOfRangeException>(
			() => GameplayEventPayload.FromNumber(
				double.PositiveInfinity));
	}

	[Fact]
	public void InvalidTagPayload_IsRejected()
	{
		Assert.Throws<ArgumentException>(
			() => GameplayEventPayload.FromTag(GameplayTag.None));
	}

	[Fact]
	public void Event_KeepsTagSourceTargetAndPayload()
	{
		Entity source = new(1, 2, 3);
		Entity target = new(4, 5, 6);

		GameplayEvent gameplayEvent = new(
			EventSchema.DamageReceived,
			source,
			target,
			GameplayEventPayload.FromNumber(25d));

		Assert.True(gameplayEvent.IsValid);
		Assert.Equal(EventSchema.DamageReceived, gameplayEvent.Tag);
		Assert.Equal(source, gameplayEvent.Source);
		Assert.Equal(target, gameplayEvent.Target);
		Assert.Equal(25d, gameplayEvent.Payload.Number);
	}

	[Fact]
	public void Event_WithoutATag_IsRejected()
	{
		Assert.Throws<ArgumentException>(
			() => new GameplayEvent(GameplayTag.None));
		Assert.False(default(GameplayEvent).IsValid);
	}
}
