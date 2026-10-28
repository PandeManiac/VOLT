namespace Volt.Editor.Core.Scenes;

/// <summary>
/// An authored object with a stable identity independent of a runtime
/// ECS handle.
/// </summary>
public sealed class SceneEntity
{
	private readonly List<AuthoredComponent> _components = [];

	public SceneEntity(Guid id, string name)
	{
		if (id == Guid.Empty)
		{
			throw new ArgumentException(
				"Scene entities require a non-empty identity.",
				nameof(id));
		}

		Id = id;
		Name = name ?? string.Empty;
	}

	public Guid Id { get; }

	public string Name { get; set; }

	public Guid? ParentId { get; internal set; }

	public TransformData Transform { get; } = new();

	public IReadOnlyList<AuthoredComponent> Components => _components;

	public T? GetComponent<T>()
		where T : AuthoredComponent
	{
		foreach (AuthoredComponent component in _components)
		{
			if (component is T typed)
			{
				return typed;
			}
		}

		return null;
	}

	public AuthoredComponent? GetComponent(string typeId)
	{
		ArgumentNullException.ThrowIfNull(typeId);

		foreach (AuthoredComponent component in _components)
		{
			if (string.Equals(
				component.TypeId,
				typeId,
				StringComparison.Ordinal))
			{
				return component;
			}
		}

		return null;
	}

	public bool HasComponent(string typeId)
	{
		return GetComponent(typeId) is not null;
	}

	public void AddComponent(AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(component);

		if (HasComponent(component.TypeId))
		{
			throw new InvalidOperationException(
				$"Entity '{Name}' already has a '{component.TypeId}' " +
				"component.");
		}

		_components.Add(component);
	}

	internal void InsertComponent(int index, AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(component);
		_components.Insert(Math.Clamp(index, 0, _components.Count), component);
	}

	public bool RemoveComponent(AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(component);
		return _components.Remove(component);
	}

	public bool RemoveComponent(string typeId)
	{
		AuthoredComponent? component = GetComponent(typeId);
		return component is not null && _components.Remove(component);
	}

	internal void ClearComponents()
	{
		_components.Clear();
	}

	internal void AddLoadedComponent(AuthoredComponent component)
	{
		_components.Add(component);
	}

	public SceneEntity Clone(Guid? newId = null)
	{
		SceneEntity clone = new(newId ?? Guid.NewGuid(), Name)
		{
			ParentId = ParentId
		};

		clone.Transform.CopyFrom(Transform);

		foreach (AuthoredComponent component in _components)
		{
			clone._components.Add(component.Clone());
		}

		return clone;
	}

	public override string ToString()
	{
		return string.IsNullOrWhiteSpace(Name)
			? Id.ToString()
			: Name;
	}
}
