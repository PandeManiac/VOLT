namespace Volt.Editor.Core.Scenes;

public enum SceneChangeKind
{
	Added = 0,
	Removed = 1,
	Renamed = 2,
	Reparented = 3,
	TransformChanged = 4,
	ComponentAdded = 5,
	ComponentRemoved = 6,
	ComponentChanged = 7,
	Reloaded = 8
}

public readonly record struct SceneChange(
	SceneChangeKind Kind,
	Guid? EntityId);

/// <summary>
/// One authored scene: a validated hierarchy of entities and their
/// component data.
/// </summary>
/// <remarks>
/// The document is the save/load unit and the source of truth for
/// Play Mode. It contains no ECS world, so it can be edited while a
/// runtime world exists and survives stopping that world unchanged.
/// </remarks>
public sealed class SceneDocument
{
	private readonly List<SceneEntity> _entities = [];
	private readonly Dictionary<Guid, SceneEntity> _byId = [];

	public SceneDocument(string name)
		: this(Guid.NewGuid(), name)
	{
	}

	internal SceneDocument(Guid id, string name)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException(
				"A scene requires a non-empty identity.",
				nameof(id));
		}

		Id = id;
		Name = name ?? "Untitled";
	}

	public event Action<SceneDocument, SceneChange>? Changed;

	public Guid Id { get; }

	public string Name { get; set; }

	public IReadOnlyList<SceneEntity> Entities => _entities;

	public int EntityCount => _entities.Count;

	public SceneEntity? Find(Guid id)
	{
		return _byId.GetValueOrDefault(id);
	}

	public bool Contains(Guid id)
	{
		return _byId.ContainsKey(id);
	}

	public SceneEntity CreateEntity(
		string name,
		Guid? parentId = null,
		Guid? id = null)
	{
		ValidateParentForNewEntity(parentId);

		Guid identity = id ?? Guid.NewGuid();

		if (_byId.ContainsKey(identity))
		{
			throw new InvalidOperationException(
				$"The scene already contains entity '{identity}'.");
		}

		SceneEntity entity = new(identity, name);
		entity.ParentId = parentId;
		Insert(entity, _entities.Count);
		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Added,
			entity.Id));

		return entity;
	}

	public void AddEntity(SceneEntity entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		if (_byId.ContainsKey(entity.Id))
		{
			throw new InvalidOperationException(
				$"The scene already contains entity '{entity.Id}'.");
		}

		if (entity.ParentId is Guid parentId &&
			!Contains(parentId))
		{
			throw new InvalidOperationException(
				"The entity parent does not exist in this scene.");
		}

		Insert(entity, _entities.Count);
		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Added,
			entity.Id));
	}

	/// <summary>
	/// Removes an entity and its descendants in one operation.
	/// </summary>
	public RemovedEntityBatch RemoveEntity(Guid id)
	{
		if (!_byId.ContainsKey(id))
		{
			return RemovedEntityBatch.Empty;
		}

		HashSet<Guid> removed = [id];
		bool added;

		do
		{
			added = false;

			foreach (SceneEntity entity in _entities)
			{
				if (entity.ParentId is Guid parentId &&
					removed.Contains(parentId) &&
					removed.Add(entity.Id))
				{
					added = true;
				}
			}
		}
		while (added);

		List<RemovedSceneEntity> entries = [];

		for (int index = _entities.Count - 1; index >= 0; index--)
		{
			SceneEntity entity = _entities[index];

			if (!removed.Contains(entity.Id))
			{
				continue;
			}

			entries.Add(new RemovedSceneEntity(entity, index));
			_entities.RemoveAt(index);
			_byId.Remove(entity.Id);
		}

		entries.Reverse();

		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Removed,
			id));

		return new RemovedEntityBatch(entries);
	}

	public bool RenameEntity(Guid id, string name)
	{
		SceneEntity entity = _byId.TryGetValue(id, out SceneEntity? found)
			? found
			: throw new KeyNotFoundException(
				$"No entity '{id}' exists in the scene.");

		name = string.IsNullOrWhiteSpace(name) ? "GameObject" : name.Trim();

		if (string.Equals(entity.Name, name, StringComparison.Ordinal))
		{
			return false;
		}

		entity.Name = name;
		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Renamed,
			id));

		return true;
	}

	public bool CanReparent(Guid id, Guid? newParentId)
	{
		SceneEntity entity = _byId.TryGetValue(id, out SceneEntity? found)
			? found
			: throw new KeyNotFoundException(
				$"No entity '{id}' exists in the scene.");

		if (newParentId is not Guid parentId)
		{
			return true;
		}

		if (parentId == id || !_byId.ContainsKey(parentId))
		{
			return false;
		}

		foreach (SceneEntity descendant in GetDescendants(entity.Id))
		{
			if (descendant.Id == parentId)
			{
				return false;
			}
		}

		return true;
	}

	public bool ReparentEntity(Guid id, Guid? newParentId)
	{
		SceneEntity entity = _byId.TryGetValue(id, out SceneEntity? found)
			? found
			: throw new KeyNotFoundException(
				$"No entity '{id}' exists in the scene.");

		if (newParentId is Guid parentId && !_byId.ContainsKey(parentId))
		{
			throw new ArgumentException(
				"The requested parent is not part of this scene.",
				nameof(newParentId));
		}

		if (!CanReparent(id, newParentId))
		{
			throw new InvalidOperationException(
				"Reparenting would create a hierarchy cycle.");
		}

		if (entity.ParentId == newParentId)
		{
			return false;
		}

		entity.ParentId = newParentId;
		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Reparented,
			id));

		return true;
	}

	public IReadOnlyList<SceneEntity> GetChildren(Guid? parentId)
	{
		List<SceneEntity> result = [];

		foreach (SceneEntity entity in _entities)
		{
			if (entity.ParentId == parentId)
			{
				result.Add(entity);
			}
		}

		return result;
	}

	public IReadOnlyList<SceneEntity> GetDescendants(Guid id)
	{
		if (!_byId.ContainsKey(id))
		{
			return [];
		}

		List<SceneEntity> result = [];
		Queue<Guid> pending = new();
		pending.Enqueue(id);

		while (pending.Count > 0)
		{
			Guid parent = pending.Dequeue();

			foreach (SceneEntity entity in _entities)
			{
				if (entity.ParentId != parent)
				{
					continue;
				}

				result.Add(entity);
				pending.Enqueue(entity.Id);
			}
		}

		return result;
	}

	public SceneEntity DuplicateEntity(Guid id)
	{
		SceneEntity source = _byId.TryGetValue(id, out SceneEntity? found)
			? found
			: throw new KeyNotFoundException(
				$"No entity '{id}' exists in the scene.");

		Dictionary<Guid, Guid> replacements = [];
		List<SceneEntity> originals = [source, .. GetDescendants(id)];

		foreach (SceneEntity original in originals)
		{
			replacements.Add(original.Id, Guid.NewGuid());
		}

		SceneEntity? cloneRoot = null;

		foreach (SceneEntity original in originals)
		{
			SceneEntity clone = original.Clone(replacements[original.Id]);
			clone.Name = MakeUniqueName(original.Name);

			if (original == source)
			{
				clone.ParentId = source.ParentId;
			}

			else if (original.ParentId is Guid parent &&
				replacements.TryGetValue(parent, out Guid replacement))
			{
				clone.ParentId = replacement;
			}

			Insert(clone, _entities.Count);
			cloneRoot ??= clone;
		}

		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Added,
			cloneRoot?.Id));

		return cloneRoot!;
	}

	public void NotifyTransformChanged(Guid id)
	{
		if (!_byId.ContainsKey(id))
		{
			throw new KeyNotFoundException(
				$"No entity '{id}' exists in the scene.");
		}

		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.TransformChanged,
			id));
	}

	public void NotifyComponentChanged(Guid id, AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(component);

		if (!_byId.TryGetValue(id, out SceneEntity? entity) ||
			!entity.Components.Contains(component))
		{
			throw new InvalidOperationException(
				"The component does not belong to the specified entity.");
		}

		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.ComponentChanged,
			id));
	}

	/// <summary>
	/// Notifies observers after a component was added or removed. The
	/// component no longer has to belong to the entity.
	/// </summary>
	public void NotifyComponentStructureChanged(Guid id)
	{
		if (!_byId.ContainsKey(id))
		{
			throw new KeyNotFoundException(
				$"No entity '{id}' exists in the scene.");
		}

		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.ComponentChanged,
			id));
	}

	public void NotifyReloaded()
	{
		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Reloaded,
			null));
	}

	/// <summary>
	/// Validates hierarchy links and returns every entity in
	/// depth-first order for a UI tree.
	/// </summary>
	public IReadOnlyList<SceneEntity> BuildHierarchyOrder()
	{
		List<SceneEntity> ordered = [];
		HashSet<Guid> visited = [];

		void Visit(Guid? parent)
		{
			foreach (SceneEntity entity in _entities)
			{
				if (entity.ParentId != parent ||
					!visited.Add(entity.Id))
				{
					continue;
				}

				ordered.Add(entity);
				Visit(entity.Id);
			}
		}

		Visit(null);

		return ordered;
	}

	internal void InsertLoaded(SceneEntity entity, int index)
	{
		Insert(entity, Math.Clamp(index, 0, _entities.Count));
	}

	internal void RestoreRemoved(RemovedEntityBatch batch)
	{
		foreach (RemovedSceneEntity removed in batch.Entities)
		{
			Insert(removed.Entity, removed.Index);
		}

		Changed?.Invoke(this, new SceneChange(
			SceneChangeKind.Reloaded,
			null));
	}

	internal void ValidateLoad()
	{
		foreach (SceneEntity entity in _entities)
		{
			if (entity.ParentId is Guid parentId &&
				!Contains(parentId))
			{
				throw new InvalidDataException(
					$"Entity '{entity.Name}' references a missing " +
					$"parent '{parentId}'.");
			}
		}

		foreach (SceneEntity entity in _entities)
		{
			HashSet<Guid> seen = [];
			Guid? current = entity.Id;

			while (current is Guid id)
			{
				if (!seen.Add(id))
				{
					throw new InvalidDataException(
						"The scene hierarchy contains a cycle.");
				}

				current = _byId.GetValueOrDefault(id)?.ParentId;
			}
		}
	}

	private void Insert(SceneEntity entity, int index)
	{
		if (!_byId.TryAdd(entity.Id, entity))
		{
			throw new InvalidOperationException(
				$"The scene already contains entity '{entity.Id}'.");
		}

		_entities.Insert(Math.Clamp(index, 0, _entities.Count), entity);
	}

	private void ValidateParentForNewEntity(Guid? parentId)
	{
		if (parentId is Guid parent && !Contains(parent))
		{
			throw new ArgumentException(
				"The requested parent is not part of this scene.",
				nameof(parentId));
		}
	}

	private string MakeUniqueName(string source)
	{
		string baseName = source.EndsWith(" (Copy)", StringComparison.Ordinal)
			? source
			: $"{source} (Copy)";

		if (!_entities.Any(entity => string.Equals(
			entity.Name,
			baseName,
			StringComparison.Ordinal)))
		{
			return baseName;
		}

		int suffix = 2;

		while (_entities.Any(entity => string.Equals(
			entity.Name,
			$"{baseName} {suffix}",
			StringComparison.Ordinal)))
		{
			suffix++;
		}

		return $"{baseName} {suffix}";
	}
}

public readonly record struct RemovedSceneEntity(
	SceneEntity Entity,
	int Index);

public sealed class RemovedEntityBatch
{
	public RemovedEntityBatch(IReadOnlyList<RemovedSceneEntity> entities)
	{
		Entities = entities;
	}

	public static RemovedEntityBatch Empty { get; } = new([]);

	public IReadOnlyList<RemovedSceneEntity> Entities { get; }

	public bool IsEmpty => Entities.Count == 0;
}
