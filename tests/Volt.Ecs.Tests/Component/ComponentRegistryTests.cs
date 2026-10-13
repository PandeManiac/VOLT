
using Volt.Ecs.Component;
using Xunit;

namespace Volt.Ecs.Tests.Component;

public sealed class ComponentRegistryTests
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

    private struct Empty
    {

    }

    private struct Mixed
    {
        public byte Flag;
        public long Value;
    }

#pragma warning restore CS0649

    [Fact]
    public void Register_ReturnsValidComponentType()
    {
        ComponentType type = ComponentRegistry.Register<Position>();

        Assert.True(type.IsValid);
        Assert.NotEqual(0u, type.Id);
    }

    [Fact]
    public void Register_SameType_ReturnsSameId()
    {
        ComponentType first = ComponentRegistry.Register<Position>();
        ComponentType second = ComponentRegistry.Register<Position>();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Register_DifferentTypes_ReturnDifferentIds()
    {
        ComponentType position = ComponentRegistry.Register<Position>();
        ComponentType velocity = ComponentRegistry.Register<Velocity>();

        Assert.NotEqual(position, velocity);
    }

    [Fact]
    public void Get_ReturnsSameIdAsRegister()
    {
        ComponentType registered = ComponentRegistry.Register<Health>();
        ComponentType retrieved = ComponentRegistry.Get<Health>();

        Assert.Equal(registered, retrieved);
    }

    [Fact]
    public void Get_UnregisteredType_RegistersAutomatically()
    {
        ComponentType type = ComponentRegistry.Get<Empty>();

        Assert.True(type.IsValid);
        Assert.Equal(type, ComponentRegistry.Register<Empty>());
    }

    [Fact]
    public void Register_RepeatedCalls_DoNotCreateDuplicates()
    {
        ComponentType original = ComponentRegistry.Register<Position>();
        int countBefore = ComponentRegistry.Count;

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(original, ComponentRegistry.Register<Position>());
        }

        Assert.Equal(countBefore, ComponentRegistry.Count);
    }

    [Fact]
    public void GetInfo_ReturnsCorrectComponentType()
    {
        ComponentType type = ComponentRegistry.Register<Position>();
        ComponentRegistry.ComponentInfo info = ComponentRegistry.GetInfo(type);

        Assert.Equal(type, info.Type);
        Assert.Equal(typeof(Position), info.ClrType);
    }

    [Fact]
    public void GetInfo_ReturnsCorrectComponentSize()
    {
        ComponentType position = ComponentRegistry.Register<Position>();
        ComponentType health = ComponentRegistry.Register<Health>();

        Assert.Equal(12, ComponentRegistry.GetInfo(position).Size);
        Assert.Equal(sizeof(int), ComponentRegistry.GetInfo(health).Size);
    }

    [Fact]
    public void GetInfo_ReturnsPositiveAlignment()
    {
        ComponentType position = ComponentRegistry.Register<Position>();
        ComponentType health = ComponentRegistry.Register<Health>();

        Assert.True(ComponentRegistry.GetInfo(position).Alignment > 0);
        Assert.True(ComponentRegistry.GetInfo(health).Alignment > 0);
    }

    [Fact]
    public void GetInfo_AlignmentDoesNotExceedSize_ForNonemptyComponents()
    {
        ComponentType position = ComponentRegistry.Register<Position>();
        ComponentType health = ComponentRegistry.Register<Health>();

        ComponentRegistry.ComponentInfo positionInfo = ComponentRegistry.GetInfo(position);
        ComponentRegistry.ComponentInfo healthInfo = ComponentRegistry.GetInfo(health);

        Assert.InRange(positionInfo.Alignment, 1, positionInfo.Size);
        Assert.InRange(healthInfo.Alignment, 1, healthInfo.Size);
    }

    [Fact]
    public void GetInfo_MixedLayout_HasExpectedSize()
    {
        ComponentType type = ComponentRegistry.Register<Mixed>();
        ComponentRegistry.ComponentInfo info = ComponentRegistry.GetInfo(type);

        Assert.Equal(typeof(Mixed), info.ClrType);
        Assert.Equal(16, info.Size);
        Assert.True(info.Alignment > 0);
    }

    [Fact]
    public void TryGetInfo_RegisteredType_ReturnsTrue()
    {
        ComponentType type = ComponentRegistry.Register<Velocity>();
        bool found = ComponentRegistry.TryGetInfo(type, out ComponentRegistry.ComponentInfo info);

        Assert.True(found);
        Assert.Equal(type, info.Type);
        Assert.Equal(typeof(Velocity), info.ClrType);
    }

    [Fact]
    public void TryGetInfo_DefaultType_ReturnsFalse()
    {
        bool found = ComponentRegistry.TryGetInfo(default, out ComponentRegistry.ComponentInfo info);

        Assert.False(found);
        Assert.Equal(default, info);
    }

    [Fact]
    public void TryGetInfo_UnregisteredId_ReturnsFalse()
    {
        var invalid = new ComponentType(uint.MaxValue);
        bool found = ComponentRegistry.TryGetInfo(invalid, out ComponentRegistry.ComponentInfo info);

        Assert.False(found);
        Assert.Equal(default, info);
    }

    [Fact]
    public void GetInfo_DefaultType_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ComponentRegistry.GetInfo(default));
    }

    [Fact]
    public void GetInfo_UnregisteredId_Throws()
    {
        var invalid = new ComponentType(uint.MaxValue);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ComponentRegistry.GetInfo(invalid));
    }

    [Fact]
    public void ConcurrentRegistration_SameType_ReturnsSameId()
    {
        const int iterationCount = 1000;
        var types = new ComponentType[iterationCount];

        Parallel.For(0, iterationCount, i =>
        {
            types[i] = ComponentRegistry.Register<Position>();
        });

        ComponentType expected = ComponentRegistry.Get<Position>();
        Assert.All(types, type => Assert.Equal(expected, type));
    }

    [Fact]
    public void ConcurrentRegistration_DifferentTypes_ProducesDistinctIds()
    {
        var types = new ComponentType[4];

        Parallel.Invoke(
            () => types[0] = ComponentRegistry.Register<Position>(),
            () => types[1] = ComponentRegistry.Register<Velocity>(),
            () => types[2] = ComponentRegistry.Register<Health>(),
            () => types[3] = ComponentRegistry.Register<Mixed>());

        Assert.Equal(types.Length, types.Distinct().Count());
    }

    [Fact]
    public void ConcurrentMetadataLookup_ReturnsConsistentResults()
    {
        ComponentType type = ComponentRegistry.Register<Position>();
        const int iterationCount = 1000;
        var results = new ComponentRegistry.ComponentInfo[iterationCount];

        Parallel.For(0, iterationCount, i =>
        {
            results[i] = ComponentRegistry.GetInfo(type);
        });

        Assert.All(results, info =>
        {
            Assert.Equal(type, info.Type);
            Assert.Equal(typeof(Position), info.ClrType);
            Assert.Equal(12, info.Size);
        });
    }

    [Fact]
    public void ComponentIds_AreStableAcrossRepeatedLookups()
    {
        ComponentType position = ComponentRegistry.Get<Position>();
        ComponentType velocity = ComponentRegistry.Get<Velocity>();

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(position, ComponentRegistry.Get<Position>());
            Assert.Equal(velocity, ComponentRegistry.Get<Velocity>());
        }
    }

    [Fact]
    public void MetadataLookup_DoesNotChangeRegistrationCount()
    {
        ComponentType type = ComponentRegistry.Register<Position>();
        int countBefore = ComponentRegistry.Count;

        for (int i = 0; i < 100; i++)
        {
            ComponentRegistry.GetInfo(type);
            ComponentRegistry.TryGetInfo(type, out _);
        }

        Assert.Equal(countBefore, ComponentRegistry.Count);
    }
}
