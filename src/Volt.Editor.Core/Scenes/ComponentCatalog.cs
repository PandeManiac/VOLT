using System.Numerics;
using System.Reflection;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// One editable property of an authored component.
/// </summary>
public sealed class EditorPropertyDescriptor
{
	internal EditorPropertyDescriptor(
		PropertyInfo property,
		string displayName,
		EditorPropertyKind kind,
		float minimum,
		float maximum,
		bool readOnly)
	{
		ArgumentNullException.ThrowIfNull(property);

		Property = property;
		Name = property.Name;
		DisplayName = displayName;
		Kind = kind;
		ValueType = property.PropertyType;
		Minimum = minimum;
		Maximum = maximum;
		IsReadOnly = readOnly || property.SetMethod is null;
		EnumNames = ValueType.IsEnum
			? Enum.GetNames(ValueType)
			: [];

		_reader = property.GetMethod is null
			? _ => null
			: component => property.GetValue(component);

		_writer = property.SetMethod is null
			? (_, _) => { }
		: (component, value) => property.SetValue(component, value);
	}

	private readonly Func<AuthoredComponent, object?> _reader;
	private readonly Action<AuthoredComponent, object?> _writer;

	public string Name { get; }

	public string DisplayName { get; }

	public EditorPropertyKind Kind { get; }

	public Type ValueType { get; }

	public float Minimum { get; }

	public float Maximum { get; }

	public bool IsReadOnly { get; }

	public IReadOnlyList<string> EnumNames { get; }

	internal PropertyInfo Property { get; }

	public object? GetValue(AuthoredComponent component)
	{
		ArgumentNullException.ThrowIfNull(component);
		return _reader(component);
	}

	public void SetValue(AuthoredComponent component, object? value)
	{
		ArgumentNullException.ThrowIfNull(component);

		if (IsReadOnly)
		{
			throw new InvalidOperationException(
				$"Property '{Name}' is read-only.");
		}

		_writer(component, value);
	}
}

/// <summary>
/// Metadata for one authored component type.
/// </summary>
public sealed class EditorComponentDescriptor
{
	internal EditorComponentDescriptor(
		Type componentType,
		EditorComponentAttribute attribute,
		Func<AuthoredComponent> factory,
		IReadOnlyList<EditorPropertyDescriptor> properties)
	{
		ComponentType = componentType;
		TypeId = attribute.ComponentTypeId;
		DisplayName = attribute.DisplayName;
		Factory = factory;
		Properties = properties;
	}

	public Type ComponentType { get; }

	public string TypeId { get; }

	public string DisplayName { get; }

	public Func<AuthoredComponent> Factory { get; }

	public IReadOnlyList<EditorPropertyDescriptor> Properties { get; }

	public AuthoredComponent Create()
	{
		return Factory();
	}
}

/// <summary>
/// A registry of authored component types and their cached property
/// metadata.
/// </summary>
/// <remarks>
/// Reflection runs once when a component type is registered, never
/// while the inspector is painting. Applications can register their
/// own component data with <see cref="Register{TComponent}"/>.
/// </remarks>
public sealed class ComponentCatalog
{
	private readonly Dictionary<string, EditorComponentDescriptor>
		s_byTypeId = new(StringComparer.Ordinal);

	private readonly Dictionary<Type, EditorComponentDescriptor>
		s_byType = [];

	private readonly List<EditorComponentDescriptor> s_ordered = [];

	public static ComponentCatalog Default { get; } = CreateDefault();

	public IReadOnlyList<EditorComponentDescriptor> All => s_ordered;

	public static ComponentCatalog CreateDefault()
	{
		ComponentCatalog catalog = new();
		catalog.Register<MeshRendererData>();
		catalog.Register<LightData>();
		catalog.Register<SpinBehaviourData>();
		catalog.Register<MoveBehaviourData>();
		catalog.Register<CameraData>();
		return catalog;
	}

	public EditorComponentDescriptor Register<TComponent>(
		Func<TComponent>? factory = null)
		where TComponent : AuthoredComponent
	{
		Type type = typeof(TComponent);

		if (s_byType.ContainsKey(type))
		{
			throw new InvalidOperationException(
				$"Component type '{type.Name}' is already registered.");
		}

		EditorComponentAttribute attribute =
			type.GetCustomAttribute<EditorComponentAttribute>()
			?? throw new InvalidOperationException(
				$"Component type '{type.Name}' has no " +
				$"{nameof(EditorComponentAttribute)}.");

		if (s_byTypeId.ContainsKey(attribute.ComponentTypeId))
		{
			throw new InvalidOperationException(
				$"Component type identifier '{attribute.ComponentTypeId}' " +
				"is already registered.");
		}

		Func<AuthoredComponent> componentFactory = factory is null
			? () => (AuthoredComponent)(
				Activator.CreateInstance(type) ??
				throw new InvalidOperationException(
					$"Component type '{type.Name}' could not be created."))
			: () => factory();

		List<EditorPropertyDescriptor> properties = [];

		foreach (PropertyInfo property in type.GetProperties(
			BindingFlags.Public |
			BindingFlags.Instance))
		{
			if (property.Name is nameof(AuthoredComponent.TypeId) or
				nameof(AuthoredComponent.DisplayName) ||
				!property.CanRead)
			{
				continue;
			}

			EditorPropertyAttribute? editorAttribute =
				property.GetCustomAttribute<EditorPropertyAttribute>();

			properties.Add(
				new EditorPropertyDescriptor(
					property,
					editorAttribute?.DisplayName ??
						SplitPascalCase(property.Name),
					ResolveKind(property.PropertyType, editorAttribute),
					editorAttribute?.Minimum ?? float.NaN,
					editorAttribute?.Maximum ?? float.NaN,
					editorAttribute?.ReadOnly ?? false));
		}

		EditorComponentDescriptor descriptor = new(
			type,
			attribute,
			componentFactory,
			properties);

		s_byType.Add(type, descriptor);
		s_byTypeId.Add(descriptor.TypeId, descriptor);
		s_ordered.Add(descriptor);

		return descriptor;
	}

	public bool TryGet(string typeId, out EditorComponentDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(typeId);
		return s_byTypeId.TryGetValue(typeId, out descriptor!);
	}

	public bool TryGet(
		Type componentType,
		out EditorComponentDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(componentType);
		return s_byType.TryGetValue(componentType, out descriptor!);
	}

	public EditorComponentDescriptor Get(string typeId)
	{
		ArgumentNullException.ThrowIfNull(typeId);

		return s_byTypeId.TryGetValue(typeId, out EditorComponentDescriptor? descriptor)
			? descriptor
			: throw new KeyNotFoundException(
				$"No authored component is registered as '{typeId}'.");
	}

	internal EditorComponentDescriptor GetForType(Type type)
	{
		return s_byType.TryGetValue(type, out EditorComponentDescriptor? descriptor)
			? descriptor
			: throw new KeyNotFoundException(
				$"Component type '{type.Name}' is not registered.");
	}

	private static EditorPropertyKind ResolveKind(
		Type type,
		EditorPropertyAttribute? attribute)
	{
		if (type == typeof(bool))
		{
			return EditorPropertyKind.Boolean;
		}

		if (type == typeof(int))
		{
			return EditorPropertyKind.Integer;
		}

		if (type == typeof(float))
		{
			return EditorPropertyKind.Float;
		}

		if (type.IsEnum)
		{
			return EditorPropertyKind.Enum;
		}

		if (type == typeof(Vector3))
		{
			return EditorPropertyKind.Vector3;
		}

		if (type == typeof(Vector4))
		{
			return EditorPropertyKind.Color;
		}

		if (type == typeof(string))
		{
			return attribute?.Kind == EditorPropertyKind.Asset
				? EditorPropertyKind.Asset
				: EditorPropertyKind.Text;
		}

		throw new NotSupportedException(
			$"Property type '{type.Name}' has no inspector " +
			"representation. Register a custom editor property or " +
			"use a supported primitive.");
	}

	private static string SplitPascalCase(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return value;
		}

		System.Text.StringBuilder builder = new(value.Length + 4);

		for (int index = 0; index < value.Length; index++)
		{
			char character = value[index];

			if (index > 0 &&
				char.IsUpper(character) &&
				!char.IsUpper(value[index - 1]))
			{
				builder.Append(' ');
			}

			builder.Append(character);
		}

		return builder.ToString();
	}
}
