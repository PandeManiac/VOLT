namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Declares the stable type identifier and display name of a component
/// that can be authored in a scene.
/// </summary>
[AttributeUsage(
	AttributeTargets.Class,
	Inherited = false,
	AllowMultiple = false)]
public sealed class EditorComponentAttribute : Attribute
{
	public EditorComponentAttribute(string typeId, string displayName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
		ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

		ComponentTypeId = typeId;
		DisplayName = displayName;
	}

	public string ComponentTypeId { get; }

	public string DisplayName { get; }
}

public enum EditorPropertyKind
{
	Boolean = 0,
	Integer = 1,
	Float = 2,
	Text = 3,
	Enum = 4,
	Vector3 = 5,
	Color = 6,
	Asset = 7
}

/// <summary>
/// Overrides how one component property appears in the inspector.
/// </summary>
[AttributeUsage(
	AttributeTargets.Property,
	Inherited = true,
	AllowMultiple = false)]
public sealed class EditorPropertyAttribute : Attribute
{
	public string? DisplayName { get; set; }

	public EditorPropertyKind Kind { get; set; } =
		EditorPropertyKind.Text;

	public float Minimum { get; set; } = float.NaN;

	public float Maximum { get; set; } = float.NaN;

	/// <summary>
	/// Gets or sets whether the property is read-only in the
	/// inspector.
	/// </summary>
	public bool ReadOnly { get; set; }
}
