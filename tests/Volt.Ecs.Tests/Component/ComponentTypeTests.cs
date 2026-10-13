
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class ComponentTypeTests
{
    [Fact]
    public void DefaultComponentType_IsInvalid()
    {
        ComponentType componentType = default;

        Assert.False(componentType.IsValid);
        Assert.Equal(0u, componentType.Id);
    }

    [Fact]
    public void ZeroId_IsInvalid()
    {
        var componentType = new ComponentType(0);
        Assert.False(componentType.IsValid);
    }

    [Fact]
    public void NonzeroId_IsValid()
    {
        var componentType = new ComponentType(1);

        Assert.True(componentType.IsValid);
        Assert.Equal(1u, componentType.Id);
    }

    [Fact]
    public void MaximumId_IsValid()
    {
        var componentType = new ComponentType(uint.MaxValue);

        Assert.True(componentType.IsValid);
        Assert.Equal(uint.MaxValue, componentType.Id);
    }

    [Fact]
    public void SameId_ProducesEqualComponentTypes()
    {
        var first = new ComponentType(42);
        var second = new ComponentType(42);

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.True(first == second);
        Assert.False(first != second);
    }

    [Fact]
    public void DifferentIds_ProduceUnequalComponentTypes()
    {
        var first = new ComponentType(1);
        var second = new ComponentType(2);

        Assert.NotEqual(first, second);
        Assert.False(first.Equals(second));
        Assert.False(first == second);
        Assert.True(first != second);
    }

    [Fact]
    public void Equals_ObjectWithSameId_ReturnsTrue()
    {
        var componentType = new ComponentType(42);
        object boxed = new ComponentType(42);

        Assert.True(componentType.Equals(boxed));
    }

    [Fact]
    public void Equals_UnrelatedObject_ReturnsFalse()
    {
        var componentType = new ComponentType(42);

        Assert.False(componentType.Equals("42"));
        Assert.False(componentType.Equals(null));
    }

    [Fact]
    public void EqualComponentTypes_HaveEqualHashCodes()
    {
        var first = new ComponentType(42);
        var second = new ComponentType(42);

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void HashSet_DeduplicatesEqualComponentTypes()
    {
        var types = new HashSet<ComponentType>
        {
            new(1),
            new(2),
            new(1),
            new(3),
            new(2)
        };

        Assert.Equal(3, types.Count);
    }

    [Fact]
    public void Dictionary_CanUseComponentTypeAsKey()
    {
        var dictionary = new Dictionary<ComponentType, string>
        {
            [new ComponentType(1)] = "Position",
            [new ComponentType(2)] = "Velocity"
        };

        Assert.Equal("Position", dictionary[new ComponentType(1)]);
        Assert.Equal("Velocity", dictionary[new ComponentType(2)]);
    }

    [Fact]
    public void CompareTo_OrdersByNumericId()
    {
        var lower = new ComponentType(10);
        var higher = new ComponentType(20);

        Assert.True(lower.CompareTo(higher) < 0);
        Assert.True(higher.CompareTo(lower) > 0);
        Assert.Equal(0, lower.CompareTo(new ComponentType(10)));
    }

    [Fact]
    public void ComparisonOperators_UseNumericOrdering()
    {
        var lower = new ComponentType(10);
        var higher = new ComponentType(20);
        var equal = new ComponentType(10);

        Assert.True(lower < higher);
        Assert.True(lower <= higher);
        Assert.True(higher > lower);
        Assert.True(higher >= lower);

        Assert.True(lower <= equal);
        Assert.True(lower >= equal);

        Assert.False(lower > equal);
        Assert.False(lower < equal);
    }

    [Fact]
    public void Sorting_ProducesAscendingIdOrder()
    {
        var types = new List<ComponentType>
        {
            new(30),
            new(10),
            new(40),
            new(20)
        };

        types.Sort();

        Assert.Equal(
            new uint[] { 10, 20, 30, 40 },
            types.Select(type => type.Id));
    }

    [Fact]
    public void ToString_ContainsComponentTypeId()
    {
        var componentType = new ComponentType(42);
        Assert.Equal("ComponentType(42)", componentType.ToString());
    }

    [Fact]
    public void DefaultComponentTypes_AreEqual()
    {
        ComponentType first = default;
        ComponentType second = default;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
