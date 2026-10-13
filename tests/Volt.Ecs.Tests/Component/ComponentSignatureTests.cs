
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class ComponentSignatureTests
{
    private static readonly ComponentType Position = new(1);
    private static readonly ComponentType Velocity = new(2);
    private static readonly ComponentType Health = new(3);
    private static readonly ComponentType Rotation = new(4);

    [Fact]
    public void Empty_HasNoComponents()
    {
        ComponentSignature signature = ComponentSignature.Empty;
        Assert.Empty(signature);
    }

    [Fact]
    public void Constructor_NoComponents_CreatesEmptySignature()
    {
        var signature = new ComponentSignature();

        Assert.Equal(ComponentSignature.Empty, signature);

        Assert.Equal(
            ComponentSignature.Empty.GetHashCode(),
            signature.GetHashCode());
    }

    [Fact]
    public void Constructor_NullArray_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ComponentSignature((ComponentType[])null!));
    }

    [Fact]
    public void Constructor_InvalidComponentType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new ComponentSignature(Position, default));
    }

    [Fact]
    public void Constructor_SortsComponentTypes()
    {
        var signature = new ComponentSignature(
            Health,
            Position,
            Velocity);

        Assert.Equal(3, signature.Count);
        Assert.Equal(Position, signature[0]);
        Assert.Equal(Velocity, signature[1]);
        Assert.Equal(Health, signature[2]);
    }

    [Fact]
    public void Constructor_RemovesDuplicateComponentTypes()
    {
        var signature = new ComponentSignature(
            Position,
            Velocity,
            Position,
            Health,
            Velocity);

        Assert.Equal(3, signature.Count);
        Assert.Equal(Position, signature[0]);
        Assert.Equal(Velocity, signature[1]);
        Assert.Equal(Health, signature[2]);
    }

    [Fact]
    public void Constructor_DoesNotModifyInputArray()
    {
        ComponentType[] input =
        [
            Health,
            Position,
            Velocity,
            Position
        ];

        ComponentType[] original = (ComponentType[])input.Clone();
        _ = new ComponentSignature(input);

        Assert.Equal(original, input);
    }

    [Fact]
    public void Constructor_DoesNotRetainMutableInputArray()
    {
        ComponentType[] input = [Position, Velocity];
        var signature = new ComponentSignature(input);

        input[0] = Health;
        input[1] = Rotation;

        Assert.Equal(Position, signature[0]);
        Assert.Equal(Velocity, signature[1]);
    }

    [Fact]
    public void Indexer_OutOfRange_Throws()
    {
        var signature = new ComponentSignature(Position);

        Assert.Throws<IndexOutOfRangeException>(
            () => _ = signature[1]);
    }

    [Fact]
    public void Contains_ExistingType_ReturnsTrue()
    {
        var signature = new ComponentSignature(
            Position,
            Velocity,
            Health);

        Assert.True(signature.Contains(Position));
        Assert.True(signature.Contains(Velocity));
        Assert.True(signature.Contains(Health));
    }

    [Fact]
    public void Contains_MissingType_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position, Velocity);

        Assert.False(signature.Contains(Health));
        Assert.False(signature.Contains(Rotation));
    }

    [Fact]
    public void Contains_EmptySignature_ReturnsFalse()
    {
        Assert.False(ComponentSignature.Empty.Contains(Position));
    }

    [Fact]
    public void Contains_InvalidType_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position);
        Assert.False(signature.Contains(default));
    }

    [Fact]
    public void ContainsAll_ExactMatch_ReturnsTrue()
    {
        var left = new ComponentSignature(Position, Velocity);
        var right = new ComponentSignature(Velocity, Position);

        Assert.True(left.ContainsAll(right));
    }

    [Fact]
    public void ContainsAll_Subset_ReturnsTrue()
    {
        var signature = new ComponentSignature(
            Position,
            Velocity,
            Health);

        var subset = new ComponentSignature(Position, Health);

        Assert.True(signature.ContainsAll(subset));
    }

    [Fact]
    public void ContainsAll_MissingType_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position, Velocity);
        var other = new ComponentSignature(Position, Health);

        Assert.False(signature.ContainsAll(other));
    }

    [Fact]
    public void ContainsAll_EmptySignature_ReturnsTrue()
    {
        var signature = new ComponentSignature(Position, Velocity);

        Assert.True(signature.ContainsAll(ComponentSignature.Empty));
        Assert.True(ComponentSignature.Empty.ContainsAll(ComponentSignature.Empty));
    }

    [Fact]
    public void ContainsAll_NonemptySignatureAgainstEmpty_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position);
        Assert.False(ComponentSignature.Empty.ContainsAll(signature));
    }

    [Fact]
    public void ContainsAll_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ComponentSignature.Empty.ContainsAll(null!));
    }

    [Fact]
    public void ContainsAny_OverlappingSignatures_ReturnsTrue()
    {
        var left = new ComponentSignature(Position, Velocity);
        var right = new ComponentSignature(Health, Velocity);

        Assert.True(left.ContainsAny(right));
        Assert.True(right.ContainsAny(left));
    }

    [Fact]
    public void ContainsAny_DisjointSignatures_ReturnsFalse()
    {
        var left = new ComponentSignature(Position, Velocity);
        var right = new ComponentSignature(Health, Rotation);

        Assert.False(left.ContainsAny(right));
        Assert.False(right.ContainsAny(left));
    }

    [Fact]
    public void ContainsAny_EmptySignature_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position, Velocity);

        Assert.False(signature.ContainsAny(ComponentSignature.Empty));
        Assert.False(ComponentSignature.Empty.ContainsAny(signature));
        Assert.False(ComponentSignature.Empty.ContainsAny(ComponentSignature.Empty));
    }

    [Fact]
    public void ContainsAny_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => ComponentSignature.Empty.ContainsAny(null!));
    }

    [Fact]
    public void With_NewType_AddsComponentInSortedOrder()
    {
        var original = new ComponentSignature(Position, Health);

        ComponentSignature result = original.With(Velocity);

        Assert.Equal(3, result.Count);
        Assert.Equal(Position, result[0]);
        Assert.Equal(Velocity, result[1]);
        Assert.Equal(Health, result[2]);
    }

    [Fact]
    public void With_ExistingType_ReturnsSameInstance()
    {
        var signature = new ComponentSignature(Position, Velocity);
        ComponentSignature result = signature.With(Position);
        Assert.Same(signature, result);
    }

    [Fact]
    public void With_InvalidType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ComponentSignature.Empty.With(default));
    }

    [Fact]
    public void With_DoesNotModifyOriginal()
    {
        var original = new ComponentSignature(Position);
        ComponentSignature result = original.With(Velocity);

        Assert.Single(original);
        Assert.False(original.Contains(Velocity));

        Assert.Equal(2, result.Count);
        Assert.True(result.Contains(Velocity));
    }

    [Fact]
    public void Without_ExistingType_RemovesComponent()
    {
        var original = new ComponentSignature(
            Position,
            Velocity,
            Health);

        ComponentSignature result = original.Without(Velocity);

        Assert.Equal(2, result.Count);
        Assert.Equal(Position, result[0]);
        Assert.Equal(Health, result[1]);
        Assert.False(result.Contains(Velocity));
    }

    [Fact]
    public void Without_MissingType_ReturnsSameInstance()
    {
        var signature = new ComponentSignature(Position);
        ComponentSignature result = signature.Without(Velocity);
        Assert.Same(signature, result);
    }

    [Fact]
    public void Without_InvalidType_ReturnsSameInstance()
    {
        var signature = new ComponentSignature(Position);
        ComponentSignature result = signature.Without(default);
        Assert.Same(signature, result);
    }

    [Fact]
    public void Without_LastComponent_ProducesEmptySignature()
    {
        var signature = new ComponentSignature(Position);
        ComponentSignature result = signature.Without(Position);

        Assert.Equal(ComponentSignature.Empty, result);
        Assert.Empty(result);
    }

    [Fact]
    public void Without_DoesNotModifyOriginal()
    {
        var original = new ComponentSignature(Position, Velocity);
        ComponentSignature result = original.Without(Position);

        Assert.Equal(2, original.Count);
        Assert.True(original.Contains(Position));

        Assert.Single(result);
        Assert.False(result.Contains(Position));
    }

    [Fact]
    public void WithAndWithout_RoundTrip_PreservesEquality()
    {
        var original = new ComponentSignature(Position, Velocity);

        ComponentSignature result = original
            .With(Health)
            .With(Rotation)
            .Without(Health)
            .Without(Rotation);

        Assert.Equal(original, result);
        Assert.Equal(original.GetHashCode(), result.GetHashCode());
    }

    [Fact]
    public void Equals_SameComponentsDifferentOrder_ReturnsTrue()
    {
        var first = new ComponentSignature(
            Position,
            Velocity,
            Health);

        var second = new ComponentSignature(
            Health,
            Position,
            Velocity);

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void Equals_DuplicateComponents_DoNotAffectEquality()
    {
        var first = new ComponentSignature(
            Position,
            Velocity,
            Position);

        var second = new ComponentSignature(Position, Velocity);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Equals_DifferentComponents_ReturnsFalse()
    {
        var first = new ComponentSignature(Position, Velocity);
        var second = new ComponentSignature(Position, Health);

        Assert.NotEqual(first, second);
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_DifferentLengths_ReturnsFalse()
    {
        var first = new ComponentSignature(Position);
        var second = new ComponentSignature(Position, Velocity);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var signature = new ComponentSignature(Position);
        ComponentSignature sameInstance = signature;

        Assert.True(signature.Equals(sameInstance));
        Assert.True(signature == sameInstance);
        Assert.False(signature != sameInstance);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position);
        ComponentSignature? missing = null;

        Assert.False(signature.Equals((ComponentSignature?)null));
        Assert.False(signature.Equals((object?)null));
        Assert.False(signature == missing);
        Assert.True(signature != missing);
    }

    [Fact]
    public void EqualityOperators_NullOperands_BehaveCorrectly()
    {
        ComponentSignature? left = null;
        ComponentSignature? right = null;
        ComponentSignature nonnull = ComponentSignature.Empty;

        Assert.True(left == right);
        Assert.False(left != right);

        Assert.False(left == nonnull);
        Assert.True(left != nonnull);

        Assert.False(nonnull == right);
        Assert.True(nonnull != right);
    }

    [Fact]
    public void Equals_UnrelatedObject_ReturnsFalse()
    {
        var signature = new ComponentSignature(Position);
        Assert.False(signature.Equals("Position"));
    }

    [Fact]
    public void GetHashCode_EquivalentSignatures_Match()
    {
        var first = new ComponentSignature(
            Position,
            Velocity,
            Health);

        var second = new ComponentSignature(
            Health,
            Position,
            Velocity,
            Position);

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void GetHashCode_RepeatedCalls_AreStable()
    {
        var signature = new ComponentSignature(Position, Velocity);
        int expected = signature.GetHashCode();

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(expected, signature.GetHashCode());
        }
    }

    [Fact]
    public void Dictionary_EquivalentSignatures_ResolveSameEntry()
    {
        var first = new ComponentSignature(Position, Velocity);
        var second = new ComponentSignature(Velocity, Position);

        var dictionary = new Dictionary<ComponentSignature, int>
        {
            [first] = 42
        };

        Assert.True(dictionary.TryGetValue(second, out int value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void HashSet_EquivalentSignatures_AreDeduplicated()
    {
        var signatures = new HashSet<ComponentSignature>
        {
            new(Position, Velocity),
            new(Velocity, Position),
            new(Position, Velocity, Position),
            new(Position, Health)
        };

        Assert.Equal(2, signatures.Count);
    }

    [Fact]
    public void Enumeration_ReturnsCanonicalOrder()
    {
        var signature = new ComponentSignature(
            Health,
            Position,
            Velocity);

        ComponentType[] result = [.. signature];

        Assert.Equal(
            [Position, Velocity, Health],
            result);
    }

    [Fact]
    public void Enumeration_EmptySignature_ReturnsNoElements()
    {
        Assert.Empty(ComponentSignature.Empty);
    }

    [Fact]
    public void LargeSignature_CanonicalizesCorrectly()
    {
        ComponentType[] input = [.. Enumerable
            .Range(1, 1000)
            .Reverse()
            .Select(id => new ComponentType((uint)id))];

        var signature = new ComponentSignature(input);
        Assert.Equal(1000, signature.Count);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal((uint)(i + 1), signature[i].Id);
        }
    }

    [Fact]
    public void LargeSignature_ContainsAllSubset()
    {
        ComponentType[] allTypes = [.. Enumerable
            .Range(1, 1000)
            .Select(id => new ComponentType((uint)id))];

        ComponentType[] subsetTypes = [.. Enumerable
            .Range(1, 1000)
            .Where(id => id % 2 == 0)
            .Select(id => new ComponentType((uint)id))];

        var all = new ComponentSignature(allTypes);
        var subset = new ComponentSignature(subsetTypes);

        Assert.True(all.ContainsAll(subset));
        Assert.False(subset.ContainsAll(all));
    }

    [Fact]
    public void LargeSignature_ContainsAnyDetectsOverlap()
    {
        ComponentType[] firstTypes = [.. Enumerable
            .Range(1, 1000)
            .Select(id => new ComponentType((uint)id))];

        ComponentType[] secondTypes = [.. Enumerable
            .Range(1000, 1000)
            .Select(id => new ComponentType((uint)id))];

        var first = new ComponentSignature(firstTypes);
        var second = new ComponentSignature(secondTypes);

        Assert.True(first.ContainsAny(second));
    }

    [Fact]
    public void LargeSignature_ContainsAnyDetectsDisjointSets()
    {
        ComponentType[] firstTypes = [.. Enumerable
            .Range(1, 1000)
            .Select(id => new ComponentType((uint)id))];

        ComponentType[] secondTypes = [.. Enumerable
            .Range(1001, 1000)
            .Select(id => new ComponentType((uint)id))];

        var first = new ComponentSignature(firstTypes);
        var second = new ComponentSignature(secondTypes);

        Assert.False(first.ContainsAny(second));
    }
}
