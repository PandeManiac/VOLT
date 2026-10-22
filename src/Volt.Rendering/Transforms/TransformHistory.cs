using System.Runtime.InteropServices;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;

namespace Volt.Rendering.Transforms;

/// <summary>
/// Stores the previous and current fixed-step pose of every
/// renderable entity so a renderer can interpolate between them.
/// </summary>
/// <remarks>
/// <para>
/// History is keyed by the complete <see cref="Entity"/> value, so
/// the allocator id, slot, and generation all participate. A reused
/// slot therefore cannot inherit the previous occupant's pose, and a
/// handle from another world is never mistaken for a local entity.
/// </para>
/// <para>
/// Call <see cref="Capture"/> once per completed fixed step, after
/// every fixed-update and scheduled callback for that step.
/// <c>GameLoop.RegisterPostFixedUpdate</c> provides exactly that
/// point. Each call shifts the stored current pose to previous and
/// reads the new current pose from the components, so a frame that
/// executes several steps retains only the last two completed
/// simulation states and a frame that executes none changes nothing.
/// </para>
/// <para>
/// A newly captured entity starts with identical previous and current
/// poses, so it does not sweep in from another entity's stale
/// transform. The same rule applies on reactivation:
/// <see cref="Capture"/> prunes the entry of every entity that is no
/// longer renderable, whether it was destroyed, lost a required
/// component, had its renderer removed, or carries an invalid mesh or
/// material handle. An entity that becomes renderable again therefore
/// starts a fresh pair on its first valid capture and holds its pose
/// until a second state is captured, instead of sweeping from a pose
/// recorded before it disappeared.
/// </para>
/// <para>
/// Pruning is automatic at each capture, so entries for destroyed
/// entities do not accumulate unless the caller stops capturing. The
/// scan is skipped when every active entry was observed by the
/// capture. <see cref="Remove"/> remains an eager release for
/// applications that want an entry gone immediately. A change that
/// happens and is reverted between two captures is not observable:
/// both captures were valid states, so the pair describes that one
/// step.
/// </para>
/// <para>
/// The history never writes to the world. It reads the same
/// components and handle validity rules as
/// <see cref="Extraction.EcsRenderExtractor"/>, allocates only when
/// the entity count or dictionary capacity grows, and is
/// single-threaded like the world and the loop.
/// </para>
/// </remarks>
public sealed class TransformHistory
{
	private readonly World _world;
	private readonly ComponentQuery _query;
	private readonly WorldQuery _chunks;
	private Dictionary<Entity, PosePair> _poses;

	// Secondary index used only to detect a recycled allocator slot
	// so the previous occupant's entry can be dropped. History
	// lookups always use the complete Entity value.
	private Dictionary<ulong, Entity> _slotOwners;

	// Keys of entries pruned by the capture in progress. Reused so
	// steady-state capture allocates nothing.
	private List<Entity> _staleEntities = new();

	// Monotonic marker written into every entry captured by the
	// current call. Entries left with an older marker are no longer
	// renderable and are pruned before the capture returns.
	private long _captureSerial;

	/// <summary>
	/// Creates history for the entities currently in a world.
	/// </summary>
	/// <param name="world">The world to read. It must outlive this instance.</param>
	/// <param name="initialCapacity">
	/// Expected number of renderable entities. The dictionary grows
	/// when more appear; this only avoids early rehashing.
	/// </param>
	/// <exception cref="ArgumentNullException">
	/// <paramref name="world"/> is null.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// <paramref name="initialCapacity"/> is negative.
	/// </exception>
	/// <remarks>
	/// Every renderable entity starts with identical previous and
	/// current poses, so interpolating before the first simulation
	/// step is stable.
	/// </remarks>
	public TransformHistory(World world, int initialCapacity = 64)
	{
		ArgumentNullException.ThrowIfNull(world);

		if (initialCapacity < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(initialCapacity),
				initialCapacity,
				"The initial capacity cannot be negative.");
		}

		_world = world;

		ComponentSignature required = new(
			ComponentRegistry.Get<PositionComponent>(),
			ComponentRegistry.Get<RotationComponent>(),
			ComponentRegistry.Get<ScaleComponent>(),
			ComponentRegistry.Get<MeshRendererComponent>());

		_query = new ComponentQuery(required: required);
		_chunks = world.Query(_query);
		_poses = new Dictionary<Entity, PosePair>(initialCapacity);
		_slotOwners = new Dictionary<ulong, Entity>(initialCapacity);

		foreach (WorldQueryChunk chunk in _chunks)
		{
			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			Span<PositionComponent> positions =
				chunk.GetSpan<PositionComponent>();

			Span<RotationComponent> rotations =
				chunk.GetSpan<RotationComponent>();

			Span<ScaleComponent> scales =
				chunk.GetSpan<ScaleComponent>();

			Span<MeshRendererComponent> renderers =
				chunk.GetSpan<MeshRendererComponent>();

			for (int row = 0; row < entities.Length; row++)
			{
				if (!IsRenderable(renderers[row]))
				{
					continue;
				}

				TransformPose pose = new(
					positions[row].Value,
					rotations[row].Value,
					scales[row].Value);

				_poses[entities[row]] = new PosePair(pose, pose);
				_slotOwners[SlotKey(entities[row])] = entities[row];
			}
		}
	}

	/// <summary>
	/// Gets how many renderable entities currently have history.
	/// </summary>
	/// <remarks>
	/// This counts active entries only. <see cref="Capacity"/> reports
	/// the dictionary's high-water size, which can remain larger after
	/// entity churn.
	/// </remarks>
	public int Count => _poses.Count;

	/// <summary>
	/// Gets the dictionary capacity retained for history entries.
	/// </summary>
	/// <remarks>
	/// This is a high-water mark, not an active count: the dictionary
	/// does not shrink when entities are pruned or removed. A long
	/// run that peaked at many simultaneous renderable entities keeps
	/// that capacity until <see cref="Clear"/> is called. The active
	/// entry count is <see cref="Count"/>.
	/// </remarks>
	public int Capacity => _poses.Capacity;

	/// <summary>
	/// Reads the previous and current pose of an entity.
	/// </summary>
	/// <param name="entity">The entity to look up.</param>
	/// <param name="previous">The pose at the previous step.</param>
	/// <param name="current">The pose at the latest step.</param>
	/// <returns>
	/// <c>true</c> when the entity has history; otherwise
	/// <c>false</c> and both poses are default.
	/// </returns>
	public bool TryGetPose(
		Entity entity,
		out TransformPose previous,
		out TransformPose current)
	{
		if (_poses.TryGetValue(entity, out PosePair pair))
		{
			previous = pair.Previous;
			current = pair.Current;

			return true;
		}

		previous = default;
		current = default;

		return false;
	}

	/// <summary>
	/// Captures the state committed by one completed fixed step and
	/// prunes entities that are no longer renderable.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The world was structurally modified during the capture.
	/// </exception>
	/// <remarks>
	/// <para>
	/// Call this after every mutation for the step, including
	/// scheduled callbacks. A structural change during the capture
	/// invalidates the query, which fails instead of storing a mixed
	/// snapshot.
	/// </para>
	/// <para>
	/// Every entity that is renderable at capture time has its pair
	/// advanced by one state and stamped with the capture's serial.
	/// Entries not stamped by this call belong to entities that were
	/// destroyed, lost a required component, or became non-renderable;
	/// they are removed before the method returns, so a later
	/// reactivation starts from an identical previous and current
	/// pose. The prune walks the active history, not the whole world,
	/// and runs only when the capture observed fewer renderable
	/// entities than active entries, so a steady scene does not pay for
	/// it. It allocates nothing once its scratch list has grown.
	/// </para>
	/// </remarks>
	public void Capture()
	{
		_captureSerial++;

		int capturedCount = 0;

		foreach (WorldQueryChunk chunk in _chunks)
		{
			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			Span<PositionComponent> positions =
				chunk.GetSpan<PositionComponent>();

			Span<RotationComponent> rotations =
				chunk.GetSpan<RotationComponent>();

			Span<ScaleComponent> scales =
				chunk.GetSpan<ScaleComponent>();

			Span<MeshRendererComponent> renderers =
				chunk.GetSpan<MeshRendererComponent>();

			for (int row = 0; row < entities.Length; row++)
			{
				if (!IsRenderable(renderers[row]))
				{
					continue;
				}

				TransformPose pose = new(
					positions[row].Value,
					rotations[row].Value,
					scales[row].Value);

				ref PosePair pair =
					ref CollectionsMarshal.GetValueRefOrAddDefault(
						_poses,
						entities[row],
						out bool exists);

				if (exists)
				{
					pair.Previous = pair.Current;
					pair.Current = pose;
				}
				else
				{
					// Removing the previous occupant cannot resize the
					// dictionary, so the reference just obtained stays
					// valid.
					ForgetRecycledSlot(entities[row]);
					pair = new PosePair(pose, pose);
				}

				pair.CapturedSerial = _captureSerial;
				capturedCount++;
			}
		}

		// The dictionary can only contain an unstamped entry when an
		// entity stopped being renderable since the previous capture.
		// In steady state every entry was just stamped, so the history
		// walk is skipped entirely.
		if (_poses.Count > capturedCount)
		{
			PruneUncaptured();
		}
	}

	/// <summary>
	/// Discards an entity's history immediately.
	/// </summary>
	/// <param name="entity">The entity being destroyed.</param>
	/// <returns>
	/// <c>true</c> when an entry was removed; otherwise
	/// <c>false</c>.
	/// </returns>
	/// <remarks>
	/// This is an eager release, not a requirement for correctness.
	/// <see cref="Capture"/> prunes entries of entities that are no
	/// longer renderable, including destroyed ones, so <see cref="Remove"/> is
	/// only needed when the caller wants the entry gone before the next
	/// capture.
	/// </remarks>
	public bool Remove(Entity entity)
	{
		ulong key = SlotKey(entity);

		if (_slotOwners.TryGetValue(key, out Entity owner) && owner == entity)
		{
			_slotOwners.Remove(key);
		}

		return _poses.Remove(entity);
	}

	/// <summary>
	/// Collapses an entity's history onto its current pose.
	/// </summary>
	/// <param name="entity">The entity that was teleported.</param>
	/// <returns>
	/// <c>true</c> when the entity is alive and has the required
	/// components; otherwise <c>false</c>.
	/// </returns>
	/// <remarks>
	/// Call this after an explicit teleport. Without it, the next
	/// capture would interpolate from the pre-teleport pose and sweep
	/// the entity across the world for one step.
	/// </remarks>
	public bool Snap(Entity entity)
	{
		if (!TryReadPose(entity, out TransformPose pose))
		{
			return false;
		}

		ForgetRecycledSlot(entity);

		ref PosePair pair = ref CollectionsMarshal.GetValueRefOrAddDefault(
			_poses,
			entity,
			out _);

		pair.Previous = pose;
		pair.Current = pose;
		pair.CapturedSerial = _captureSerial;

		return true;
	}

	/// <summary>
	/// Collapses every renderable entity's history onto its current
	/// pose and removes entries for entities that are no longer
	/// renderable.
	/// </summary>
	/// <remarks>
	/// Use after a bulk reposition, such as loading a level, when no
	/// entity should sweep from its previous location. The operation
	/// reconciles the same lifecycle cases as <see cref="Capture"/>:
	/// a recycled slot drops the previous occupant, and destroyed
	/// entities, entities that lost a required component, and entities
	/// with invalid handles are pruned. It is a linear bulk pass and
	/// allocates nothing once the prune scratch list has grown.
	/// </remarks>
	public void SnapAll()
	{
		_captureSerial++;

		int capturedCount = 0;

		foreach (WorldQueryChunk chunk in _chunks)
		{
			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			Span<PositionComponent> positions =
				chunk.GetSpan<PositionComponent>();

			Span<RotationComponent> rotations =
				chunk.GetSpan<RotationComponent>();

			Span<ScaleComponent> scales =
				chunk.GetSpan<ScaleComponent>();

			Span<MeshRendererComponent> renderers =
				chunk.GetSpan<MeshRendererComponent>();

			for (int row = 0; row < entities.Length; row++)
			{
				if (!IsRenderable(renderers[row]))
				{
					continue;
				}

				TransformPose pose = new(
					positions[row].Value,
					rotations[row].Value,
					scales[row].Value);

				// A rebuild can move a new entity into a slot whose
				// previous occupant still has history.
				ForgetRecycledSlot(entities[row]);

				_poses[entities[row]] = new PosePair(
					pose,
					pose,
					_captureSerial);

				capturedCount++;
			}
		}

		if (_poses.Count > capturedCount)
		{
			PruneUncaptured();
		}
	}

	/// <summary>
	/// Removes every entity's history and releases the capacity
	/// retained after a large peak.
	/// </summary>
	/// <remarks>
	/// The pose index, the slot-owner index, and the prune scratch list
	/// are replaced with empty instances, releasing their storage
	/// instead of only clearing the entries. <see cref="Capacity"/> is
	/// zero afterwards and the next <see cref="Capture"/> repopulates
	/// from the world. The allocation for the replacements happens only
	/// here, never during an ordinary frame.
	/// </remarks>
	public void Clear()
	{
		_poses = new Dictionary<Entity, PosePair>();
		_slotOwners = new Dictionary<ulong, Entity>();
		_staleEntities = new List<Entity>();
	}

	private void PruneUncaptured()
	{
		_staleEntities.Clear();

		foreach (KeyValuePair<Entity, PosePair> entry in _poses)
		{
			if (entry.Value.CapturedSerial != _captureSerial)
			{
				_staleEntities.Add(entry.Key);
			}
		}

		for (int index = 0; index < _staleEntities.Count; index++)
		{
			Remove(_staleEntities[index]);
		}
	}

	private void ForgetRecycledSlot(Entity entity)
	{
		ulong key = SlotKey(entity);

		if (_slotOwners.TryGetValue(key, out Entity previous) &&
			previous != entity)
		{
			_poses.Remove(previous);
		}

		_slotOwners[key] = entity;
	}

	private static ulong SlotKey(Entity entity)
	{
		return ((ulong)entity.AllocatorId << 32) | entity.Index;
	}

	private bool TryReadPose(Entity entity, out TransformPose pose)
	{
		pose = default;

		if (!_world.IsAlive(entity) ||
			!_world.HasComponent<PositionComponent>(entity) ||
			!_world.HasComponent<RotationComponent>(entity) ||
			!_world.HasComponent<ScaleComponent>(entity) ||
			!_world.HasComponent<MeshRendererComponent>(entity))
		{
			return false;
		}

		MeshRendererComponent renderer =
			_world.GetComponent<MeshRendererComponent>(entity);

		if (!IsRenderable(renderer))
		{
			return false;
		}

		pose = new TransformPose(
			_world.GetComponent<PositionComponent>(entity).Value,
			_world.GetComponent<RotationComponent>(entity).Value,
			_world.GetComponent<ScaleComponent>(entity).Value);

		return true;
	}

	private static bool IsRenderable(in MeshRendererComponent renderer)
	{
		return renderer.Mesh.IsValid && renderer.Material.IsValid;
	}

	private struct PosePair
	{
		public PosePair(
			TransformPose previous,
			TransformPose current,
			long capturedSerial = 0)
		{
			Previous = previous;
			Current = current;
			CapturedSerial = capturedSerial;
		}

		public TransformPose Previous;
		public TransformPose Current;

		// Capture serial that most recently observed this entry.
		// Entries older than the capture in progress are pruned.
		public long CapturedSerial;
	}
}
