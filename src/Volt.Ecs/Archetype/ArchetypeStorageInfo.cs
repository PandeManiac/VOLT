namespace Volt.Ecs.Component;

/// <summary>
/// A measured summary of one archetype's storage.
/// </summary>
/// <remarks>
/// Values come from the allocated chunks themselves rather than from a
/// running estimate, so an empty archetype reports zero entities and
/// its capacity is whatever the chunk allocator reserved. Bytes are the
/// total native allocation the chunks report, which includes padding
/// and is therefore an upper bound on live component bytes.
/// </remarks>
public readonly record struct ArchetypeStorageInfo(
	Archetype Archetype,
	int ChunkCount,
	int EntityCount,
	int Capacity,
	long AllocatedBytes)
{
	/// <summary>
	/// Gets the fraction of reserved rows that currently hold an
	/// entity, in [0, 1].
	/// </summary>
	public float Occupancy => Capacity <= 0
		? 0f
		: (float)EntityCount / Capacity;
}
