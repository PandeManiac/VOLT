using System.Numerics;

namespace Volt.Ui.Widgets.Properties;

/// <summary>
/// Identifies the editor control a property uses.
/// </summary>
public enum UiPropertyKind
{
	Boolean = 0,
	Integer = 1,
	Float = 2,
	String = 3,
	Enumeration = 4,
	Vector3 = 5,
	Color = 6,
	Asset = 7,
	ReadOnlyText = 8
}

/// <summary>
/// A cached, typed description of one inspectable value.
/// </summary>
/// <remarks>
/// Descriptors are created once when a selection changes and reused
/// across frames. Values are read through typed delegates; the
/// inspector performs no reflection while painting.
/// </remarks>
public abstract class UiProperty
{
	protected UiProperty(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException(
				"A property requires a name.",
				nameof(name));
		}

		Name = name;
	}

	public string Name { get; }

	public string? Tooltip { get; set; }

	public bool IsReadOnly { get; set; }

	public abstract UiPropertyKind Kind { get; }
}

public sealed class BoolProperty : UiProperty
{
	public BoolProperty(string name, Func<bool> get, Action<bool> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<bool> Get { get; }

	public Action<bool> Set { get; }

	public override UiPropertyKind Kind => UiPropertyKind.Boolean;
}

public sealed class IntProperty : UiProperty
{
	public IntProperty(string name, Func<int> get, Action<int> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<int> Get { get; }

	public Action<int> Set { get; }

	public int Minimum { get; set; } = int.MinValue;

	public int Maximum { get; set; } = int.MaxValue;

	public int Step { get; set; } = 1;

	public override UiPropertyKind Kind => UiPropertyKind.Integer;
}

public sealed class FloatProperty : UiProperty
{
	public FloatProperty(string name, Func<float> get, Action<float> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<float> Get { get; }

	public Action<float> Set { get; }

	public float Minimum { get; set; } = float.MinValue;

	public float Maximum { get; set; } = float.MaxValue;

	public float Step { get; set; } = 0.1f;

	public string Format { get; set; } = "0.###";

	public override UiPropertyKind Kind => UiPropertyKind.Float;
}

public sealed class StringProperty : UiProperty
{
	public StringProperty(
		string name,
		Func<string> get,
		Action<string> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<string> Get { get; }

	public Action<string> Set { get; }

	public override UiPropertyKind Kind => UiPropertyKind.String;
}

public sealed class EnumProperty : UiProperty
{
	public EnumProperty(
		string name,
		IReadOnlyList<string> options,
		Func<int> get,
		Action<int> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);

		if (options.Count == 0)
		{
			throw new ArgumentException(
				"An enumeration property requires at least one option.",
				nameof(options));
		}

		Options = options;
		Get = get;
		Set = set;
	}

	public IReadOnlyList<string> Options { get; }

	public Func<int> Get { get; }

	public Action<int> Set { get; }

	public override UiPropertyKind Kind => UiPropertyKind.Enumeration;
}

public sealed class Vector3Property : UiProperty
{
	public Vector3Property(
		string name,
		Func<Vector3> get,
		Action<Vector3> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<Vector3> Get { get; }

	public Action<Vector3> Set { get; }

	public override UiPropertyKind Kind => UiPropertyKind.Vector3;
}

public sealed class ColorProperty : UiProperty
{
	public ColorProperty(
		string name,
		Func<UiColor> get,
		Action<UiColor> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<UiColor> Get { get; }

	public Action<UiColor> Set { get; }

	public override UiPropertyKind Kind => UiPropertyKind.Color;
}

public sealed class AssetProperty : UiProperty
{
	public AssetProperty(
		string name,
		Func<string?> get,
		Action<string?> set)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		ArgumentNullException.ThrowIfNull(set);
		Get = get;
		Set = set;
	}

	public Func<string?> Get { get; }

	public Action<string?> Set { get; }

	public override UiPropertyKind Kind => UiPropertyKind.Asset;
}

public sealed class ReadOnlyTextProperty : UiProperty
{
	public ReadOnlyTextProperty(string name, Func<string> get)
		: base(name)
	{
		ArgumentNullException.ThrowIfNull(get);
		Get = get;
		IsReadOnly = true;
	}

	public Func<string> Get { get; }

	public override UiPropertyKind Kind => UiPropertyKind.ReadOnlyText;
}

/// <summary>
/// A titled group of properties.
/// </summary>
public sealed class UiPropertyGroup
{
	public UiPropertyGroup(string name, IReadOnlyList<UiProperty> properties)
	{
		Name = name ?? string.Empty;
		Properties = properties ??
			throw new ArgumentNullException(nameof(properties));
	}

	public string Name { get; }

	public IReadOnlyList<UiProperty> Properties { get; }
}
