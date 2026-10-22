
using System.Runtime.CompilerServices;
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class ArchetypeTests
{
#pragma warning disable CS0649

	private struct Position
	{
		public float X;
		public float Y;
		public float Z;
	}

	private struct Velocity
	{
		public float X;
		public float Y;
		public float Z;
	}

	private struct Health
	{
		public int Value;
	}

	private struct Rotation
	{
		public float Value;
	}

#pragma warning restore CS0649

	private static ComponentType PositionType => ComponentRegistry.Get<Position>();
	private static ComponentType VelocityType => ComponentRegistry.Get<Velocity>();
	private static ComponentType HealthType => ComponentRegistry.Get<Health>();
	private static ComponentType RotationType => ComponentRegistry.Get<Rotation>();

	[Fact]
	public void Constructor_NullSignature_Throws()
	{
		Assert.Throws<ArgumentNullException>(
			() => new Archetype(null!));
	}

	[Fact]
	public void Constructor_EmptySignature_CreatesEmptyArchetype()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.Same(ComponentSignature.Empty, archetype.Signature);
		Assert.Equal(0, archetype.ComponentCount);
	}

	[Fact]
	public void Constructor_StoresOriginalSignature()
	{
		var signature = new ComponentSignature(PositionType, VelocityType);
		var archetype = new Archetype(signature);

		Assert.Same(signature, archetype.Signature);
	}

	[Fact]
	public void Constructor_RegisteredComponents_ResolvesMetadata()
	{
		var signature = new ComponentSignature(
			PositionType,
			VelocityType,
			HealthType);

		var archetype = new Archetype(signature);

		Assert.Equal(3, archetype.ComponentCount);

		Assert.Equal(
			typeof(Position),
			archetype.GetComponentInfo(PositionType).ClrType);

		Assert.Equal(
			typeof(Velocity),
			archetype.GetComponentInfo(VelocityType).ClrType);

		Assert.Equal(
			typeof(Health),
			archetype.GetComponentInfo(HealthType).ClrType);
	}

	[Fact]
	public void Constructor_UnregisteredComponent_Throws()
	{
		var signature = new ComponentSignature(
			new ComponentType(uint.MaxValue));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new Archetype(signature));
	}

	[Fact]
	public void Constructor_MixedRegisteredAndUnregisteredComponents_Throws()
	{
		var signature = new ComponentSignature(
			PositionType,
			new ComponentType(uint.MaxValue));

		Assert.Throws<ArgumentOutOfRangeException>(
			() => new Archetype(signature));
	}

	[Fact]
	public void Constructor_CanonicalizesComponentMetadataOrder()
	{
		ComponentType position = PositionType;
		ComponentType velocity = VelocityType;
		ComponentType health = HealthType;

		var signature = new ComponentSignature(
			health,
			position,
			velocity);

		var archetype = new Archetype(signature);

		for (int i = 0; i < signature.Count; i++)
		{
			Assert.Equal(
				signature[i],
				archetype.GetComponentInfoAt(i).Type);
		}
	}

	[Fact]
	public void Constructor_DuplicateComponentTypes_AreNotRepeated()
	{
		var signature = new ComponentSignature(
			PositionType,
			VelocityType,
			PositionType,
			VelocityType);

		var archetype = new Archetype(signature);

		Assert.Equal(2, archetype.ComponentCount);
	}

	[Fact]
	public void Contains_ExistingComponent_ReturnsTrue()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		Assert.True(archetype.Contains(PositionType));
		Assert.True(archetype.Contains(VelocityType));
	}

	[Fact]
	public void Contains_MissingComponent_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		Assert.False(archetype.Contains(HealthType));
		Assert.False(archetype.Contains(RotationType));
	}

	[Fact]
	public void Contains_InvalidComponent_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.False(archetype.Contains(default));
	}

	[Fact]
	public void Contains_EmptyArchetype_ReturnsFalse()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.False(archetype.Contains(PositionType));
	}

	[Fact]
	public void ContainsAll_ExactSignature_ReturnsTrue()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		var signature = new ComponentSignature(
			VelocityType,
			PositionType);

		Assert.True(archetype.ContainsAll(signature));
	}

	[Fact]
	public void ContainsAll_Subset_ReturnsTrue()
	{
		var archetype = new Archetype(
			new ComponentSignature(
				PositionType,
				VelocityType,
				HealthType));

		var subset = new ComponentSignature(PositionType, HealthType);

		Assert.True(archetype.ContainsAll(subset));
	}

	[Fact]
	public void ContainsAll_MissingComponent_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		var signature = new ComponentSignature(PositionType, HealthType);

		Assert.False(archetype.ContainsAll(signature));
	}

	[Fact]
	public void ContainsAll_EmptySignature_ReturnsTrue()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.True(archetype.ContainsAll(ComponentSignature.Empty));
	}

	[Fact]
	public void ContainsAll_EmptyArchetypeAndEmptySignature_ReturnsTrue()
	{
		var archetype = new Archetype(ComponentSignature.Empty);
		Assert.True(archetype.ContainsAll(ComponentSignature.Empty));
	}

	[Fact]
	public void ContainsAll_EmptyArchetypeAndNonemptySignature_ReturnsFalse()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.False(archetype.ContainsAll(
			new ComponentSignature(PositionType)));
	}

	[Fact]
	public void ContainsAll_NullSignature_Throws()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.Throws<ArgumentNullException>(
			() => archetype.ContainsAll(null!));
	}

	[Fact]
	public void ContainsAny_OverlappingSignature_ReturnsTrue()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		var signature = new ComponentSignature(HealthType, VelocityType);

		Assert.True(archetype.ContainsAny(signature));
	}

	[Fact]
	public void ContainsAny_DisjointSignature_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		var signature = new ComponentSignature(HealthType, RotationType);

		Assert.False(archetype.ContainsAny(signature));
	}

	[Fact]
	public void ContainsAny_EmptySignature_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.False(archetype.ContainsAny(ComponentSignature.Empty));
	}

	[Fact]
	public void ContainsAny_EmptyArchetype_ReturnsFalse()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.False(archetype.ContainsAny(
			new ComponentSignature(PositionType)));
	}

	[Fact]
	public void ContainsAny_NullSignature_Throws()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.Throws<ArgumentNullException>(
			() => archetype.ContainsAny(null!));
	}

	[Fact]
	public void GetComponentInfo_ExistingComponent_ReturnsCorrectMetadata()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, HealthType));

		ComponentRegistry.ComponentInfo position =
			archetype.GetComponentInfo(PositionType);

		ComponentRegistry.ComponentInfo health =
			archetype.GetComponentInfo(HealthType);

		Assert.Equal(PositionType, position.Type);
		Assert.Equal(typeof(Position), position.ClrType);
		Assert.Equal(Unsafe.SizeOf<Position>(), position.Size);

		Assert.Equal(HealthType, health.Type);
		Assert.Equal(typeof(Health), health.ClrType);
		Assert.Equal(Unsafe.SizeOf<Health>(), health.Size);
	}

	[Fact]
	public void GetComponentInfo_MissingComponent_Throws()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.Throws<ArgumentException>(
			() => archetype.GetComponentInfo(HealthType));
	}

	[Fact]
	public void GetComponentInfo_InvalidComponent_Throws()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.Throws<ArgumentException>(
			() => archetype.GetComponentInfo(default));
	}

	[Fact]
	public void GetComponentInfo_EmptyArchetype_Throws()
	{
		var archetype = new Archetype(ComponentSignature.Empty);

		Assert.Throws<ArgumentException>(
			() => archetype.GetComponentInfo(PositionType));
	}

	[Fact]
	public void TryGetComponentInfo_ExistingComponent_ReturnsTrue()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		bool found = archetype.TryGetComponentInfo(
			PositionType,
			out ComponentRegistry.ComponentInfo info);

		Assert.True(found);
		Assert.Equal(PositionType, info.Type);
		Assert.Equal(typeof(Position), info.ClrType);
	}

	[Fact]
	public void TryGetComponentInfo_MissingComponent_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		bool found = archetype.TryGetComponentInfo(
			HealthType,
			out ComponentRegistry.ComponentInfo info);

		Assert.False(found);
		Assert.Equal(default, info);
	}

	[Fact]
	public void TryGetComponentInfo_InvalidComponent_ReturnsFalse()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		bool found = archetype.TryGetComponentInfo(
			default,
			out ComponentRegistry.ComponentInfo info);

		Assert.False(found);
		Assert.Equal(default, info);
	}

	[Fact]
	public void GetComponentInfoAt_ReturnsCanonicalOrder()
	{
		var signature = new ComponentSignature(
			HealthType,
			VelocityType,
			PositionType);

		var archetype = new Archetype(signature);

		for (int i = 0; i < signature.Count; i++)
		{
			ComponentRegistry.ComponentInfo info =
				archetype.GetComponentInfoAt(i);

			Assert.Equal(signature[i], info.Type);
		}
	}

	[Fact]
	public void GetComponentInfoAt_NegativeIndex_Throws()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.Throws<IndexOutOfRangeException>(
			() => archetype.GetComponentInfoAt(-1));
	}

	[Fact]
	public void GetComponentInfoAt_IndexEqualToCount_Throws()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.Throws<IndexOutOfRangeException>(
			() => archetype.GetComponentInfoAt(archetype.ComponentCount));
	}

	[Fact]
	public void FindComponentIndex_ExistingComponent_ReturnsCanonicalIndex()
	{
		var signature = new ComponentSignature(
			HealthType,
			PositionType,
			VelocityType);

		var archetype = new Archetype(signature);

		for (int i = 0; i < signature.Count; i++)
		{
			Assert.Equal(
				i,
				archetype.FindComponentIndex(signature[i]));
		}
	}

	[Fact]
	public void FindComponentIndex_MissingComponent_ReturnsNegativeOne()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		Assert.Equal(-1, archetype.FindComponentIndex(HealthType));
	}

	[Fact]
	public void FindComponentIndex_InvalidComponent_ReturnsNegativeOne()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType));

		Assert.Equal(-1, archetype.FindComponentIndex(default));
	}

	[Fact]
	public void FindComponentIndex_EmptyArchetype_ReturnsNegativeOne()
	{
		var archetype = new Archetype(ComponentSignature.Empty);
		Assert.Equal(-1, archetype.FindComponentIndex(PositionType));
	}

	[Fact]
	public void ComponentMetadata_MatchesGlobalRegistry()
	{
		var signature = new ComponentSignature(
			PositionType,
			VelocityType,
			HealthType);

		var archetype = new Archetype(signature);

		foreach (ComponentType type in signature)
		{
			ComponentRegistry.ComponentInfo expected =
				ComponentRegistry.GetInfo(type);

			ComponentRegistry.ComponentInfo actual =
				archetype.GetComponentInfo(type);

			Assert.Equal(expected, actual);
		}
	}

	[Fact]
	public void SeparateArchetypes_WithEquivalentSignatures_HaveEquivalentMetadata()
	{
		var first = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		var second = new Archetype(
			new ComponentSignature(VelocityType, PositionType));

		Assert.Equal(first.Signature, second.Signature);
		Assert.Equal(first.ComponentCount, second.ComponentCount);

		for (int i = 0; i < first.ComponentCount; i++)
		{
			Assert.Equal(
				first.GetComponentInfoAt(i),
				second.GetComponentInfoAt(i));
		}
	}

	[Fact]
	public void ArchetypeMetadata_RemainsStableAcrossRepeatedLookups()
	{
		var archetype = new Archetype(
			new ComponentSignature(PositionType, VelocityType));

		ComponentRegistry.ComponentInfo expected =
			archetype.GetComponentInfo(PositionType);

		for (int i = 0; i < 1000; i++)
		{
			Assert.Equal(
				expected,
				archetype.GetComponentInfo(PositionType));
		}
	}
}
