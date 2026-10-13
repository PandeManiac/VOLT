using Volt.Ecs.Component;

namespace Volt.Ecs;

/// <summary>
/// Identifies the chunk and row containing an entity.
/// </summary>
/// <remarks>
/// Entity locations are maintained by World and may change
/// when entities migrate between archetypes or when chunk
/// rows are compacted through swap-back removal.
/// </remarks>
internal readonly struct EntityLocation
{
    public ArchetypeChunk Chunk { get; }
    public int Row { get; }

    public EntityLocation(ArchetypeChunk chunk, int row)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (row < 0 || row >= chunk.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(row));
        }

        Chunk = chunk;
        Row = row;
    }
}
