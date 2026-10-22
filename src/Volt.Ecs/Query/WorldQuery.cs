
using System.Collections;

namespace Volt.Ecs.Component;

/// <summary>
/// Enumerates nonempty chunks belonging to archetypes that
/// satisfy a component query.
/// </summary>
/// <remarks>
/// Structural changes to the world invalidate active enumerators.
/// Component value updates do not.
/// </remarks>
public sealed class WorldQuery : IEnumerable<WorldQueryChunk>
{
	private readonly World _world;
	private readonly ComponentQuery _query;

	internal WorldQuery(World world, ComponentQuery query)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(query);

		_world = world;
		_query = query;
	}

	public Enumerator GetEnumerator()
	{
		return new Enumerator(_world, _query);
	}

	IEnumerator<WorldQueryChunk> IEnumerable<WorldQueryChunk>.GetEnumerator()
	{
		return GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	public struct Enumerator : IEnumerator<WorldQueryChunk>
	{
		private readonly World _world;
		private readonly ComponentQuery _query;
		private readonly int _version;

		private readonly List<Archetype> _archetypes;

		private int _archetypeIndex;
		private int _chunkIndex;

		private WorldQueryChunk _current;

		readonly object IEnumerator.Current => Current;

		public readonly WorldQueryChunk Current
		{
			get
			{
				ValidateVersion();
				return _current;
			}
		}

		internal Enumerator(World world, ComponentQuery query)
		{
			_world = world;
			_query = query;
			_version = world.StructuralVersion;

			_archetypes = world.GetMatchingArchetypes(query);

			_archetypeIndex = 0;
			_chunkIndex = -1;

			_current = default;
		}

		public bool MoveNext()
		{
			ValidateVersion();

			while (_archetypeIndex < _archetypes.Count)
			{
				Archetype archetype = _archetypes[_archetypeIndex];

				List<ArchetypeChunk> chunks =
					_world.GetChunks(archetype);

				_chunkIndex++;

				while (_chunkIndex < chunks.Count)
				{
					ArchetypeChunk chunk = chunks[_chunkIndex];

					if (!chunk.IsEmpty)
					{
						_current = new WorldQueryChunk(chunk);
						return true;
					}

					_chunkIndex++;
				}

				_archetypeIndex++;
				_chunkIndex = -1;
			}

			_current = default;
			return false;
		}

		public void Reset()
		{
			ValidateVersion();

			_archetypeIndex = 0;
			_chunkIndex = -1;
			_current = default;
		}

		public readonly void Dispose()
		{

		}

		private readonly void ValidateVersion()
		{
			if (_world.StructuralVersion != _version)
			{
				throw new InvalidOperationException(
					"The world was structurally modified during query iteration.");
			}
		}
	}
}
