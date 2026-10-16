using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Jobs;

namespace Volt.Benchmarks;

/// <summary>
/// Focused large/small ECS parallel workloads for MediumRun confirmation.
/// </summary>
[MemoryDiagnoser]
public class EcsParallelFocusedBenchmarks
{
    private World _smallWorld = null!;
    private World _largeWorld = null!;
    private ComponentQuery _query = null!;
    private WorldParallelExecutor _smallSerial = null!;
    private WorldParallelExecutor _smallParallel = null!;
    private WorldParallelExecutor _largeSerial = null!;
    private WorldParallelExecutor _largeParallel = null!;
    private Action<WorldQueryChunk> _lightWork = null!;
    private Action<WorldQueryChunk> _heavyWork = null!;

    [GlobalSetup]
    public void Setup()
    {
        ComponentRegistry.Register<BenchmarkPosition>();
        ComponentRegistry.Register<BenchmarkVelocity>();

        _query = new ComponentQuery(
            required: new ComponentSignature(
                ComponentRegistry.Get<BenchmarkPosition>(),
                ComponentRegistry.Get<BenchmarkVelocity>()));

        _smallWorld = CreateWorld(1_000);
        _largeWorld = CreateWorld(1_000_000);

        _smallSerial = new WorldParallelExecutor(new JobScheduler(1), 1);
        _smallParallel = new WorldParallelExecutor(new JobScheduler(8), 8);
        _largeSerial = new WorldParallelExecutor(new JobScheduler(1), 1);
        _largeParallel = new WorldParallelExecutor(new JobScheduler(8), 8);

        _lightWork = ProcessLight;
        _heavyWork = ProcessHeavy;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _smallWorld.Dispose();
        _largeWorld.Dispose();
    }

    [Benchmark]
    public Task SmallSerial() => _smallSerial.ExecuteAsync(
        _smallWorld, _query, _lightWork);

    [Benchmark]
    public Task SmallParallel() => _smallParallel.ExecuteAsync(
        _smallWorld, _query, _heavyWork);

    [Benchmark]
    public Task LargeSerial() => _largeSerial.ExecuteAsync(
        _largeWorld, _query, _lightWork);

    [Benchmark]
    public Task LargeParallel() => _largeParallel.ExecuteAsync(
        _largeWorld, _query, _heavyWork);

    private static World CreateWorld(int entityCount)
    {
        World world = new();

        for (int i = 0; i < entityCount; i++)
        {
            Entity entity = world.CreateEntity();
            world.AddComponent(entity, new BenchmarkPosition
            {
                X = 0,
                Y = i * 0.001f,
                Z = 0
            });
            world.AddComponent(entity, new BenchmarkVelocity
            {
                X = 1 + (i % 10) * 0.1f,
                Y = 0,
                Z = 0
            });
        }

        return world;
    }

    private static void ProcessLight(WorldQueryChunk chunk) =>
        Process(chunk, 1);

    private static void ProcessHeavy(WorldQueryChunk chunk) =>
        Process(chunk, 16);

    private static void Process(WorldQueryChunk chunk, int iterations)
    {
        Span<BenchmarkPosition> positions =
            chunk.GetSpan<BenchmarkPosition>();
        Span<BenchmarkVelocity> velocities =
            chunk.GetSpan<BenchmarkVelocity>();

        for (int i = 0; i < chunk.Count; i++)
        {
            float x = positions[i].Y;
            float velocity = velocities[i].X;

            for (int step = 0; step < iterations; step++)
            {
                x = (x + velocity * 0.016f) * 1.00001f;
            }

            positions[i].X = x;
        }
    }
}
