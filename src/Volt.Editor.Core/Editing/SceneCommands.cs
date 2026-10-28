using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Editing;

public sealed class AddEntityCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly SceneEntity _entity;

	public AddEntityCommand(
		SceneDocument scene,
		string name,
		Guid? parentId = null)
	{
		ArgumentNullException.ThrowIfNull(scene);

		_scene = scene;
		_entity = new SceneEntity(Guid.NewGuid(), name)
		{
			ParentId = parentId
		};

		Description = $"Create {_entity.Name}";
	}

	public string Description { get; }

	public SceneEntity Entity => _entity;

	public void Execute()
	{
		_scene.AddEntity(_entity);
	}

	public void Undo()
	{
		_scene.RemoveEntity(_entity.Id);
	}
}

public sealed class DeleteEntityCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private RemovedEntityBatch _removed = RemovedEntityBatch.Empty;

	public DeleteEntityCommand(SceneDocument scene, Guid entityId)
	{
		ArgumentNullException.ThrowIfNull(scene);
		_scene = scene;
		_entityId = entityId;

		SceneEntity? entity = scene.Find(entityId);
		Description = entity is null
			? "Delete entity"
			: $"Delete {entity.Name}";
	}

	public string Description { get; }

	public void Execute()
	{
		_removed = _scene.RemoveEntity(_entityId);
	}

	public void Undo()
	{
		if (!_removed.IsEmpty)
		{
			_scene.RestoreRemoved(_removed);
		}
	}
}

/// <summary>
/// Copies an object, and everything under it, as one undoable step.
/// </summary>
/// <remarks>
/// The copy is created on the first execute, so its new ids and names
/// are what the scene produced. Undo removes that copy's subtree, and
/// redo restores exactly the entities that were removed, keeping the
/// ids stable across a redo.
/// </remarks>
public sealed class DuplicateEntityCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _sourceId;
	private RemovedEntityBatch _removed = RemovedEntityBatch.Empty;
	private SceneEntity? _copy;

	public DuplicateEntityCommand(SceneDocument scene, Guid sourceId)
	{
		ArgumentNullException.ThrowIfNull(scene);

		SceneEntity source = scene.Find(sourceId) ??
			throw new KeyNotFoundException(
				$"No entity '{sourceId}' exists in the scene.");

		_scene = scene;
		_sourceId = sourceId;
		Description = $"Duplicate {source.Name}";
	}

	public string Description { get; }

	/// <summary>
	/// Gets the object this command copies.
	/// </summary>
	public Guid SourceId => _sourceId;

	/// <summary>
	/// Gets the copy created by <see cref="Execute"/>.
	/// </summary>
	public SceneEntity? Copy => _copy;

	public void Execute()
	{
		if (_copy is null)
		{
			_copy = _scene.DuplicateEntity(_sourceId);
			return;
		}

		if (!_removed.IsEmpty)
		{
			_scene.RestoreRemoved(_removed);
			_removed = RemovedEntityBatch.Empty;
		}
	}

	public void Undo()
	{
		if (_copy is null || _scene.Find(_copy.Id) is null)
		{
			return;
		}

		_removed = _scene.RemoveEntity(_copy.Id);
	}
}

public sealed class RenameEntityCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private readonly string _oldName;
	private readonly string _newName;

	public RenameEntityCommand(
		SceneDocument scene,
		Guid entityId,
		string newName)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		_scene = scene;
		_entityId = entityId;
		_oldName = scene.Find(entityId)?.Name ??
			throw new KeyNotFoundException(
				$"No entity '{entityId}' exists in the scene.");

		_newName = newName.Trim();
		Description = $"Rename {_oldName}";
	}

	public string Description { get; }

	public void Execute()
	{
		_scene.RenameEntity(_entityId, _newName);
	}

	public void Undo()
	{
		_scene.RenameEntity(_entityId, _oldName);
	}
}

public sealed class ReparentEntityCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private readonly Guid? _oldParent;
	private readonly Guid? _newParent;

	public ReparentEntityCommand(
		SceneDocument scene,
		Guid entityId,
		Guid? newParentId)
	{
		ArgumentNullException.ThrowIfNull(scene);

		SceneEntity entity = scene.Find(entityId) ??
			throw new KeyNotFoundException(
				$"No entity '{entityId}' exists in the scene.");

		if (!scene.CanReparent(entityId, newParentId))
		{
			throw new InvalidOperationException(
				"Reparenting would create a hierarchy cycle.");
		}

		_scene = scene;
		_entityId = entityId;
		_oldParent = entity.ParentId;
		_newParent = newParentId;
		Description = $"Reparent {entity.Name}";
	}

	public string Description { get; }

	public void Execute()
	{
		_scene.ReparentEntity(_entityId, _newParent);
	}

	public void Undo()
	{
		_scene.ReparentEntity(_entityId, _oldParent);
	}
}

public sealed class SetTransformCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private readonly TransformData _before;
	private readonly TransformData _after;

	public SetTransformCommand(
		SceneDocument scene,
		Guid entityId,
		TransformData newTransform,
		TransformData? previousTransform = null)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(newTransform);

		SceneEntity entity = scene.Find(entityId) ??
			throw new KeyNotFoundException(
				$"No entity '{entityId}' exists in the scene.");

		_scene = scene;
		_entityId = entityId;
		_before = (previousTransform ?? entity.Transform).Clone();
		_after = newTransform.Clone();
		Description = $"Transform {entity.Name}";
	}

	public string Description { get; }

	public void Execute()
	{
		Apply(_after);
	}

	public void Undo()
	{
		Apply(_before);
	}

	private void Apply(TransformData value)
	{
		SceneEntity entity = _scene.Find(_entityId) ??
			throw new InvalidOperationException(
				"The entity no longer exists in the scene.");

		entity.Transform.CopyFrom(value);
		_scene.NotifyTransformChanged(_entityId);
	}
}

public sealed class AddComponentCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private readonly AuthoredComponent _component;

	public AddComponentCommand(
		SceneDocument scene,
		Guid entityId,
		AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(component);

		_scene = scene;
		_entityId = entityId;
		_component = component;
		Description = $"Add {component.DisplayName}";
	}

	public string Description { get; }

	public AuthoredComponent Component => _component;

	public void Execute()
	{
		SceneEntity entity = GetEntity();
		entity.AddComponent(_component);
		_scene.NotifyComponentStructureChanged(_entityId);
	}

	public void Undo()
	{
		SceneEntity entity = GetEntity();
		entity.RemoveComponent(_component);
		_scene.NotifyComponentStructureChanged(_entityId);
	}

	private SceneEntity GetEntity()
	{
		return _scene.Find(_entityId) ??
			throw new InvalidOperationException(
				"The entity no longer exists in the scene.");
	}
}

public sealed class RemoveComponentCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private readonly AuthoredComponent _component;
	private readonly int _index;

	public RemoveComponentCommand(
		SceneDocument scene,
		Guid entityId,
		AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(component);

		SceneEntity entity = scene.Find(entityId) ??
			throw new KeyNotFoundException(
				$"No entity '{entityId}' exists in the scene.");

		int index = -1;

		for (int componentIndex = 0;
			componentIndex < entity.Components.Count;
			componentIndex++)
		{
			if (ReferenceEquals(
				entity.Components[componentIndex],
				component))
			{
				index = componentIndex;
				break;
			}
		}

		if (index < 0)
		{
			throw new InvalidOperationException(
				"The component does not belong to the entity.");
		}

		_scene = scene;
		_entityId = entityId;
		_component = component;
		_index = index;
		Description = $"Remove {component.DisplayName}";
	}

	public string Description { get; }

	public void Execute()
	{
		SceneEntity entity = GetEntity();
		entity.RemoveComponent(_component);
		_scene.NotifyComponentStructureChanged(_entityId);
	}

	public void Undo()
	{
		SceneEntity entity = GetEntity();
		entity.InsertComponent(_index, _component);
		_scene.NotifyComponentStructureChanged(_entityId);
	}

	private SceneEntity GetEntity()
	{
		return _scene.Find(_entityId) ??
			throw new InvalidOperationException(
				"The entity no longer exists in the scene.");
	}
}

public sealed class ComponentPropertyCommand : IEditorCommand
{
	private readonly SceneDocument _scene;
	private readonly Guid _entityId;
	private readonly AuthoredComponent _component;
	private readonly EditorPropertyDescriptor _property;
	private readonly object? _before;
	private readonly object? _after;

	public ComponentPropertyCommand(
		SceneDocument scene,
		Guid entityId,
		AuthoredComponent component,
		EditorPropertyDescriptor property,
		object? newValue)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(component);
		ArgumentNullException.ThrowIfNull(property);

		SceneEntity entity = scene.Find(entityId) ??
			throw new KeyNotFoundException(
				$"No entity '{entityId}' exists in the scene.");

		if (!entity.Components.Contains(component))
		{
			throw new InvalidOperationException(
				"The component does not belong to the entity.");
		}

		_scene = scene;
		_entityId = entityId;
		_component = component;
		_property = property;
		_before = property.GetValue(component);
		_after = newValue;
		Description = $"Edit {component.DisplayName}.{property.DisplayName}";
	}

	public string Description { get; }

	public void Execute()
	{
		Apply(_after);
	}

	public void Undo()
	{
		Apply(_before);
	}

	private void Apply(object? value)
	{
		_property.SetValue(_component, value);
		_scene.NotifyComponentChanged(_entityId, _component);
	}
}
