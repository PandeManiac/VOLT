
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class ComponentQueryTests
{
#pragma warning disable CS0649

    private struct Position
    {
        public float X;
    }

    private struct Velocity
    {
        public float X;
    }

    private struct Health
    {
        public int Value;
    }

#pragma warning restore CS0649

    private static readonly ComponentType PositionType =
        ComponentRegistry.Register<Position>();

    private static readonly ComponentType VelocityType =
        ComponentRegistry.Register<Velocity>();

    private static readonly ComponentType HealthType =
        ComponentRegistry.Register<Health>();

    [Fact]
    public void EmptyQuery_MatchesEveryArchetype()
    {
        ComponentQuery query = new();

        Assert.True(query.Matches(
            new Archetype(ComponentSignature.Empty)));

        Assert.True(query.Matches(
            new Archetype(new ComponentSignature(PositionType))));
    }

    [Fact]
    public void Required_MustAllBePresent()
    {
        ComponentQuery query = new(
            required: new ComponentSignature(
                PositionType,
                VelocityType));

        Assert.True(query.Matches(
            new Archetype(new ComponentSignature(
                PositionType,
                VelocityType,
                HealthType))));

        Assert.False(query.Matches(
            new Archetype(new ComponentSignature(PositionType))));
    }

    [Fact]
    public void Excluded_MustBeAbsent()
    {
        ComponentQuery query = new(
            excluded: new ComponentSignature(HealthType));

        Assert.True(query.Matches(
            new Archetype(new ComponentSignature(PositionType))));

        Assert.False(query.Matches(
            new Archetype(new ComponentSignature(
                PositionType,
                HealthType))));
    }

    [Fact]
    public void Any_RequiresAtLeastOneMatch()
    {
        ComponentQuery query = new(
            any: new ComponentSignature(
                VelocityType,
                HealthType));

        Assert.True(query.Matches(
            new Archetype(new ComponentSignature(HealthType))));

        Assert.False(query.Matches(
            new Archetype(new ComponentSignature(PositionType))));
    }

    [Fact]
    public void Filters_WorkTogether()
    {
        ComponentQuery query = new(
            required: new ComponentSignature(PositionType),
            excluded: new ComponentSignature(HealthType),
            any: new ComponentSignature(VelocityType));

        Assert.True(query.Matches(
            new Archetype(new ComponentSignature(
                PositionType,
                VelocityType))));

        Assert.False(query.Matches(
            new Archetype(new ComponentSignature(
                PositionType,
                VelocityType,
                HealthType))));

        Assert.False(query.Matches(
            new Archetype(new ComponentSignature(PositionType))));
    }

    [Fact]
    public void ConflictingRequiredAndExcluded_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new ComponentQuery(
                required: new ComponentSignature(PositionType),
                excluded: new ComponentSignature(PositionType)));
    }

    [Fact]
    public void Matches_NullArchetype_Throws()
    {
        ComponentQuery query = new();

        Assert.Throws<ArgumentNullException>(
            () => query.Matches(null!));
    }
}
