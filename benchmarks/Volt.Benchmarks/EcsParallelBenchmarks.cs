
using BenchmarkDotNet.Attributes;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Jobs;

namespace Volt.Benchmarks;

[MemoryDiagnoser]
public class EcsParallelBenchmarks
{
    private World _world = null!;
    private WorldQuery _query = null!;
    private ComponentQuery _componentQuery = null!;
    private WorldParallelExecutor _executor = null!;
    private Action<WorldQueryChunk> _processChunk = null!;

    [Params(1_000, 100_000, 1_000_000)]
    public int EntityCount { get; set; }

    [Params(1, 4, 8)]
    public int WorkerCount { get; set; }

    [Params(1, 8)]
    public int ChunksPerBatch { get; set; }

    [Params(1, 16)]
    public int WorkIterations { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        ComponentRegistry.Register<BenchmarkPosition>();
        ComponentRegistry.Register<BenchmarkVelocity>();

        _world = new World();

        for (int i = 0; i < EntityCount; i++)
        {
            Entity entity = _world.CreateEntity();

            _world.AddComponent(entity, new BenchmarkPosition
            {
                X = 0,
                Y = i * 0.001f,
                Z = 0
            });

            _world.AddComponent(entity, new BenchmarkVelocity
            {
                X = 1 + (i % 10) * 0.1f,
                Y = 0,
                Z = 0
            });
        }

        _componentQuery = new ComponentQuery(
            required: new ComponentSignature(
                ComponentRegistry.Get<BenchmarkPosition>(),
                ComponentRegistry.Get<BenchmarkVelocity>()));

        _query = _world.Query(_componentQuery);

        JobScheduler scheduler = new(WorkerCount);

        _executor = new WorldParallelExecutor(
            scheduler,
            ChunksPerBatch);

        _processChunk = ProcessChunk;

        foreach (WorldQueryChunk chunk in _query)
        {
            _ = chunk.Count;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _world.Dispose();
    }

    [Benchmark(Baseline = true)]
    public void Sequential()
    {
        foreach (WorldQueryChunk chunk in _query)
        {
            ProcessChunk(chunk);
        }
    }

    [Benchmark]
    public Task Parallel()
    {
        return _executor.ExecuteAsync(
            _world,
            _componentQuery,
            _processChunk);
    }

    private void ProcessChunk(WorldQueryChunk chunk)
    {
        Span<BenchmarkPosition> positions =
            chunk.GetSpan<BenchmarkPosition>();

        Span<BenchmarkVelocity> velocities =
            chunk.GetSpan<BenchmarkVelocity>();

        for (int i = 0; i < chunk.Count; i++)
        {
            float x = positions[i].Y;
            float velocity = velocities[i].X;

            for (int step = 0; step < WorkIterations; step++)
            {
                x = (x + velocity * 0.016f) * 1.00001f;
            }

            positions[i].X = x;
        }
    }
}
