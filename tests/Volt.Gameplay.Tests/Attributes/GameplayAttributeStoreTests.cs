using Volt.Ecs;
using Volt.Gameplay.Attributes;

namespace Volt.Gameplay.Tests.Attributes;

public sealed class GameplayAttributeStoreTests
{
	[Fact]
	public void MissingAttribute_ReadsTheDefaultWithoutCreatingState()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.Equal(100f, store.GetBaseValue(entity, AttributeSchema.Health));
		Assert.Equal(100f, store.GetCurrentValue(entity, AttributeSchema.Health));
		Assert.False(store.HasAttribute(entity, AttributeSchema.Health));
		Assert.Equal(0, store.AttributedEntityCount);
	}

	[Fact]
	public void SetBaseValue_ChangesBaseAndCurrent()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.True(store.SetBaseValue(entity, AttributeSchema.Health, 70f));

		Assert.Equal(70f, store.GetBaseValue(entity, AttributeSchema.Health));
		Assert.Equal(70f, store.GetCurrentValue(entity, AttributeSchema.Health));
		Assert.True(store.HasAttribute(entity, AttributeSchema.Health));
		Assert.Equal(1, store.AttributedEntityCount);
	}

	[Fact]
	public void SetBaseValue_RepeatingTheSameValueReportsNoChange()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.True(store.SetBaseValue(entity, AttributeSchema.Health, 70f));
		Assert.False(store.SetBaseValue(entity, AttributeSchema.Health, 70f));
	}

	[Fact]
	public void SetBaseValue_WritingTheDefaultDoesNotCreateState()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.False(store.SetBaseValue(entity, AttributeSchema.Health, 100f));
		Assert.False(store.HasAttribute(entity, AttributeSchema.Health));
		Assert.Equal(0, store.AttributedEntityCount);
	}

	[Fact]
	public void SetBaseValue_ClampsToTheDefinitionRange()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 500f);

		Assert.Equal(100f, store.GetBaseValue(entity, AttributeSchema.Health));

		store.SetBaseValue(entity, AttributeSchema.Health, -50f);

		Assert.Equal(0f, store.GetBaseValue(entity, AttributeSchema.Health));
	}

	[Fact]
	public void SetBaseValue_DeadEntityThrows()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		world.DestroyEntity(entity);

		Assert.Throws<ArgumentException>(
			() => store.SetBaseValue(entity, AttributeSchema.Health, 50f));
	}

	[Fact]
	public void SetBaseValue_NonFiniteValueThrows()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => store.SetBaseValue(entity, AttributeSchema.Health, float.NaN));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => store.SetBaseValue(
				entity,
				AttributeSchema.Health,
				float.PositiveInfinity));
	}

	[Fact]
	public void ModifyBaseValue_AddsAndClamps()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 90f);

		Assert.True(store.ModifyBaseValue(entity, AttributeSchema.Health, -20f));
		Assert.Equal(70f, store.GetBaseValue(entity, AttributeSchema.Health));

		Assert.True(store.ModifyBaseValue(entity, AttributeSchema.Health, 100f));
		Assert.Equal(100f, store.GetBaseValue(entity, AttributeSchema.Health));
	}

	[Fact]
	public void AddBase_AddsBeforeMultiplication()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			20f);

		Assert.Equal(120f, store.GetCurrentValue(entity, AttributeSchema.Unbounded));
		Assert.Equal(100f, store.GetBaseValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void MultiplyAdditive_SumsTheBonuses()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyAdditive,
			1.5f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyAdditive,
			1.5f);

		// 1.5 + 1.5 sums to a 2.0 multiplier, not 1.5 * 1.5.
		Assert.Equal(
			200f,
			store.GetCurrentValue(entity, AttributeSchema.Unbounded),
			3);
	}

	[Fact]
	public void DivideAdditive_SumsTheDivisors()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.DivideAdditive,
			2f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.DivideAdditive,
			2f);

		// Divisors 2 + 2 sum to 3 with the unit bias.
		Assert.Equal(
			100f / 3f,
			store.GetCurrentValue(entity, AttributeSchema.Unbounded),
			4);
	}

	[Fact]
	public void MultiplyCompound_MultipliesEachFactor()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyCompound,
			1.5f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyCompound,
			2f);

		Assert.Equal(
			300f,
			store.GetCurrentValue(entity, AttributeSchema.Unbounded),
			3);
	}

	[Fact]
	public void CombinedModifiers_FollowTheUnrealFormula()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			20f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyAdditive,
			1.5f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.DivideAdditive,
			2f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyCompound,
			2f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddFinal,
			10f);

		// ((100 + 20) * 1.5 / 2 * 2) + 10 = 190.
		Assert.Equal(
			190f,
			store.GetCurrentValue(entity, AttributeSchema.Unbounded),
			3);
	}

	[Fact]
	public void Override_ReplacesEverythingAndFirstAddedWins()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			20f);

		GameplayModifierHandle first = store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			42f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			50f);

		Assert.Equal(42f, store.GetCurrentValue(entity, AttributeSchema.Unbounded));

		Assert.True(store.RemoveModifier(first));

		Assert.Equal(50f, store.GetCurrentValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void OverrideAfterSlotReuse_StillLosesToAnOlderOverride()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		GameplayModifierHandle discarded = store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			10f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			20f);

		store.RemoveModifier(discarded);

		// The removed modifier's slot is reused, but insertion order,
		// not slot order, decides which override wins.
		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			30f);

		Assert.Equal(20f, store.GetCurrentValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void ExactlyZeroDivisor_IsTreatedAsOne()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.DivideAdditive,
			0f);

		Assert.Equal(100f, store.GetCurrentValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void RemoveModifier_RestoresThePreviousValue()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 60f);

		GameplayModifierHandle handle = store.AddModifier(
			entity,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			100f);

		Assert.Equal(100f, store.GetCurrentValue(entity, AttributeSchema.Health));
		Assert.Equal(60f, store.GetBaseValue(entity, AttributeSchema.Health));

		Assert.True(store.RemoveModifier(handle));
		Assert.Equal(60f, store.GetCurrentValue(entity, AttributeSchema.Health));

		Assert.False(store.RemoveModifier(handle));
		Assert.Equal(0, store.GetModifierCount(entity, AttributeSchema.Health));
	}

	[Fact]
	public void StaleModifierHandle_CannotRemoveAReusedSlot()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		GameplayModifierHandle first = store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddFinal,
			10f);

		store.RemoveModifier(first);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddFinal,
			20f);

		Assert.False(store.RemoveModifier(first));
		Assert.Equal(120f, store.GetCurrentValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void AttributesOnDifferentEntities_AreIndependent()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity first = world.CreateEntity();
		Entity second = world.CreateEntity();

		store.SetBaseValue(first, AttributeSchema.Health, 80f);
		store.SetBaseValue(second, AttributeSchema.Health, 20f);

		store.SetBaseValue(first, AttributeSchema.Stamina, 10f);

		Assert.Equal(80f, store.GetCurrentValue(first, AttributeSchema.Health));
		Assert.Equal(20f, store.GetCurrentValue(second, AttributeSchema.Health));
		Assert.Equal(10f, store.GetCurrentValue(first, AttributeSchema.Stamina));
		Assert.Equal(50f, store.GetCurrentValue(second, AttributeSchema.Stamina));
	}

	[Fact]
	public void SetBaseValue_RaisesOneChangeWithOldAndNewValues()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 100f);

		List<AttributeChange> changes = [];

		store.AttributeChanged += changes.Add;

		store.SetBaseValue(entity, AttributeSchema.Health, 60f);

		AttributeChange change = Assert.Single(changes);

		Assert.Equal(entity, change.Entity);
		Assert.Equal(AttributeSchema.Health, change.Attribute);
		Assert.Equal(100f, change.OldBaseValue);
		Assert.Equal(60f, change.NewBaseValue);
		Assert.Equal(100f, change.OldCurrentValue);
		Assert.Equal(60f, change.NewCurrentValue);
		Assert.Equal(-40f, change.CurrentDelta);
	}

	[Fact]
	public void UnchangedWrite_RaisesNoChange()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 60f);

		int changes = 0;

		store.AttributeChanged += _ => changes++;

		store.SetBaseValue(entity, AttributeSchema.Health, 60f);

		Assert.Equal(0, changes);
	}

	[Fact]
	public void BaseChangeHiddenByOverride_StillRaisesWithUnchangedCurrent()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Unbounded, 100f);

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			42f);

		List<AttributeChange> changes = [];
		store.AttributeChanged += changes.Add;

		store.SetBaseValue(entity, AttributeSchema.Unbounded, 80f);

		AttributeChange change = Assert.Single(changes);

		Assert.Equal(100f, change.OldBaseValue);
		Assert.Equal(80f, change.NewBaseValue);
		Assert.Equal(42f, change.OldCurrentValue);
		Assert.Equal(42f, change.NewCurrentValue);
	}

	[Fact]
	public void ModifierThatDoesNotChangeCurrent_RaisesNoChange()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		int changes = 0;
		store.AttributeChanged += _ => changes++;

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			0f);

		Assert.Equal(0, changes);
	}

	[Fact]
	public void DestroyingEntity_ReleasesAttributesAndModifiers()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 40f);

		GameplayModifierHandle handle = store.AddModifier(
			entity,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			-10f);

		Assert.Equal(1, store.AttributedEntityCount);

		world.DestroyEntity(entity);

		Assert.Equal(0, store.AttributedEntityCount);
		Assert.False(store.HasAttribute(entity, AttributeSchema.Health));
		Assert.Equal(100f, store.GetCurrentValue(entity, AttributeSchema.Health));
		Assert.False(store.RemoveModifier(handle));
		Assert.Equal(0, store.GetModifierCount(entity, AttributeSchema.Health));
	}

	[Fact]
	public void RecycledEntityIndex_StartsWithDefaultValues()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity first = world.CreateEntity();

		store.SetBaseValue(first, AttributeSchema.Health, 10f);

		world.DestroyEntity(first);

		Entity second = world.CreateEntity();

		Assert.Equal(first.Index, second.Index);
		Assert.NotEqual(first.Generation, second.Generation);

		Assert.Equal(100f, store.GetBaseValue(second, AttributeSchema.Health));
		Assert.False(store.HasAttribute(second, AttributeSchema.Health));

		store.SetBaseValue(second, AttributeSchema.Health, 30f);

		Assert.Equal(30f, store.GetBaseValue(second, AttributeSchema.Health));
		Assert.Equal(100f, store.GetBaseValue(first, AttributeSchema.Health));
	}

	[Fact]
	public void ReplacementModifiedBeforeDestructionCallback_RetiresOldDenseState()
	{
		using World world = new();

		Entity first = world.CreateEntity();

		GameplayAttributeStore? store = null;
		Entity? replacement = null;

		// Subscribed before the store, so the replacement owns state
		// while the old generation's dense entry is still present.
		world.EntityDestroyed += destroyed =>
		{
			if (destroyed != first)
			{
				return;
			}

			replacement = world.CreateEntity();

			store!.SetBaseValue(
				replacement.Value,
				AttributeSchema.Health,
				30f);

			store.AddModifier(
				replacement.Value,
				AttributeSchema.Health,
				GameplayModifierOperation.AddFinal,
				5f);
		};

		using GameplayAttributeStore actual = new(world);
		store = actual;

		store.SetBaseValue(first, AttributeSchema.Health, 10f);

		GameplayModifierHandle oldModifier = store.AddModifier(
			first,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			-20f);

		Assert.Equal(1, store.AttributedEntityCount);

		Assert.True(world.DestroyEntity(first));

		Assert.NotNull(replacement);
		Assert.Equal(first.Index, replacement.Value.Index);
		Assert.NotEqual(first.Generation, replacement.Value.Generation);
		Assert.Equal(1, store.AttributedEntityCount);
		Assert.Equal(30f, store.GetBaseValue(
			replacement.Value,
			AttributeSchema.Health));
		Assert.Equal(35f, store.GetCurrentValue(
			replacement.Value,
			AttributeSchema.Health));
		Assert.Equal(1, store.GetModifierCount(
			replacement.Value,
			AttributeSchema.Health));
		Assert.False(store.RemoveModifier(oldModifier));

		Entity[] buffer = new Entity[store.AttributedEntityCount];

		Assert.Equal(1, store.CopyAttributedEntities(buffer));
		Assert.Equal(replacement.Value, buffer[0]);
		Assert.Equal(100f, store.GetBaseValue(first, AttributeSchema.Health));
	}

	[Fact]
	public void Clear_RemovesStateAndReportsWhetherItExisted()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.False(store.Clear(entity));

		store.SetBaseValue(entity, AttributeSchema.Health, 30f);

		Assert.True(store.Clear(entity));
		Assert.False(store.HasAttribute(entity, AttributeSchema.Health));
		Assert.Equal(100f, store.GetBaseValue(entity, AttributeSchema.Health));
		Assert.Equal(0, store.AttributedEntityCount);
	}

	[Fact]
	public void InsertingAnAttributeBeforeAnother_DoesNotShareModifierStorage()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Unbounded, 200f);

		GameplayModifierHandle unbounded = store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddFinal,
			5f);

		// Health sorts before Unbounded, so this inserts in front of the
		// entry that already owns a modifier array.
		store.SetBaseValue(entity, AttributeSchema.Health, 50f);

		GameplayModifierHandle health = store.AddModifier(
			entity,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			-10f);

		Assert.Equal(205f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(40f, store.GetCurrentValue(
			entity,
			AttributeSchema.Health));

		Assert.True(store.RemoveModifier(health));
		Assert.Equal(205f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(50f, store.GetCurrentValue(
			entity,
			AttributeSchema.Health));

		Assert.True(store.RemoveModifier(unbounded));
		Assert.Equal(200f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void Clear_InvalidatesModifierHandlesBeforeTheEntityReusesTheSlot()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		GameplayModifierHandle old = store.AddModifier(
			entity,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			-10f);

		Assert.True(store.Clear(entity));

		GameplayModifierHandle current = store.AddModifier(
			entity,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			-20f);

		Assert.False(store.RemoveModifier(old));
		Assert.True(store.RemoveModifier(current));
		Assert.Equal(0, store.GetModifierCount(
			entity,
			AttributeSchema.Health));
	}

	[Fact]
	public void CopyAttributedEntities_EnumeratesEntitiesWithState()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity[] entities = new Entity[10];

		for (int index = 0; index < entities.Length; index++)
		{
			entities[index] = world.CreateEntity();

			if (index % 2 == 0)
			{
				store.SetBaseValue(entities[index], AttributeSchema.Health, 50f);
			}
		}

		store.SetBaseValue(entities[1], AttributeSchema.Stamina, 20f);

		Entity[] buffer = new Entity[store.AttributedEntityCount];

		Assert.Equal(buffer.Length, store.CopyAttributedEntities(buffer));

		Assert.Equal(
			entities.Where((_, index) => index % 2 == 0 || index == 1),
			buffer.OrderBy(entity => entity.Index));
	}

	[Fact]
	public void EntitiesFromAnotherWorld_AreRejectedOrUnseen()
	{
		using World first = new();
		using World second = new();
		using GameplayAttributeStore store = new(first);

		Entity foreign = second.CreateEntity();

		Assert.Equal(100f, store.GetBaseValue(foreign, AttributeSchema.Health));

		Assert.Throws<ArgumentException>(
			() => store.SetBaseValue(foreign, AttributeSchema.Health, 50f));
	}

	[Fact]
	public void WarmedReadsAndWrites_AllocateNothing()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 50f);

		GameplayModifierHandle handle = store.AddModifier(
			entity,
			AttributeSchema.Health,
			GameplayModifierOperation.AddFinal,
			1f);

		store.RemoveModifier(handle);

		for (int index = 0; index < 16; index++)
		{
			store.SetBaseValue(entity, AttributeSchema.Health, 60f);
			_ = store.GetCurrentValue(entity, AttributeSchema.Health);

			GameplayModifierHandle warm = store.AddModifier(
				entity,
				AttributeSchema.Health,
				GameplayModifierOperation.AddFinal,
				1f);

			store.RemoveModifier(warm);
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int index = 0; index < 1_000; index++)
		{
			_ = store.GetCurrentValue(entity, AttributeSchema.Health);
			_ = store.GetBaseValue(entity, AttributeSchema.Health);

			store.SetBaseValue(
				entity,
				AttributeSchema.Health,
				index % 2 == 0 ? 60f : 70f);

			GameplayModifierHandle modifier = store.AddModifier(
				entity,
				AttributeSchema.Health,
				GameplayModifierOperation.AddFinal,
				1f);

			_ = store.HasAttribute(entity, AttributeSchema.Health);

			store.RemoveModifier(modifier);
		}

		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
	}

	[Fact]
	public void Dispose_UnsubscribesAndRejectsFurtherUse()
	{
		using World world = new();

		GameplayAttributeStore store = new(world);
		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Health, 50f);
		store.Dispose();

		world.DestroyEntity(entity);

		Assert.Throws<ObjectDisposedException>(
			() => store.GetCurrentValue(entity, AttributeSchema.Health));
	}

	[Fact]
	public void InvalidAttribute_IsRejected()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.Throws<ArgumentException>(
			() => store.GetBaseValue(entity, GameplayAttribute.None));

		Assert.Throws<ArgumentException>(
			() => store.AddModifier(
				entity,
				GameplayAttribute.None,
				GameplayModifierOperation.AddBase,
				1f));
	}

	[Fact]
	public void ExecuteInstantModifier_AppliesEachOperationToTheBaseValue()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.True(store.ExecuteInstantModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			30f));

		Assert.Equal(30f, store.GetBaseValue(entity, AttributeSchema.Unbounded));

		Assert.True(store.ExecuteInstantModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddFinal,
			5f));

		Assert.Equal(35f, store.GetBaseValue(entity, AttributeSchema.Unbounded));

		Assert.True(store.ExecuteInstantModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.MultiplyCompound,
			2f));

		Assert.Equal(70f, store.GetBaseValue(entity, AttributeSchema.Unbounded));

		Assert.True(store.ExecuteInstantModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.DivideAdditive,
			2f));

		Assert.Equal(35f, store.GetBaseValue(entity, AttributeSchema.Unbounded));

		Assert.True(store.ExecuteInstantModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			-5f));

		Assert.Equal(30f, store.GetBaseValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void ExecuteInstantModifier_LeavesTheValueOnADivideByZero()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.SetBaseValue(entity, AttributeSchema.Unbounded, 40f);

		Assert.False(store.ExecuteInstantModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.DivideAdditive,
			0f));

		Assert.Equal(40f, store.GetBaseValue(entity, AttributeSchema.Unbounded));
	}

	[Fact]
	public void ExecuteInstantModifier_ValidatesItsInput()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();
		Entity dead = world.CreateEntity();

		world.DestroyEntity(dead);

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			store.ExecuteInstantModifier(
				entity,
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				float.NaN));

		Assert.Throws<ArgumentException>(() =>
			store.ExecuteInstantModifier(
				dead,
				AttributeSchema.Unbounded,
				GameplayModifierOperation.AddBase,
				1f));
	}

	[Fact]
	public void SetModifierMagnitude_RevaluesOnceAndKeepsTheSlot()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();
		int changes = 0;

		GameplayModifierHandle handle = store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.AddBase,
			10f);

		store.AttributeChanged += _ => changes++;

		Assert.True(store.SetModifierMagnitude(handle, 25f));
		Assert.Equal(125f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
		Assert.Equal(1, changes);

		Assert.False(store.SetModifierMagnitude(handle, 25f));
		Assert.Equal(1, changes);

		Assert.True(store.RemoveModifier(handle));
		Assert.False(store.SetModifierMagnitude(handle, 40f));
		Assert.Equal(100f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void SetModifierMagnitude_DoesNotChangeOverridePrecedence()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			42f);

		GameplayModifierHandle later = store.AddModifier(
			entity,
			AttributeSchema.Unbounded,
			GameplayModifierOperation.Override,
			50f);

		Assert.Equal(42f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));

		// Rescaling the later override does not make it win; the first
		// insertion still has precedence.
		Assert.True(store.SetModifierMagnitude(later, 60f));
		Assert.Equal(42f, store.GetCurrentValue(
			entity,
			AttributeSchema.Unbounded));
	}

	[Fact]
	public void NonFiniteModifierMagnitude_IsRejected()
	{
		using World world = new();
		using GameplayAttributeStore store = new(world);

		Entity entity = world.CreateEntity();

		Assert.Throws<ArgumentOutOfRangeException>(
			() => store.AddModifier(
				entity,
				AttributeSchema.Health,
				GameplayModifierOperation.AddBase,
				float.NaN));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => store.AddModifier(
				entity,
				AttributeSchema.Health,
				(GameplayModifierOperation)200,
				1f));
	}
}
