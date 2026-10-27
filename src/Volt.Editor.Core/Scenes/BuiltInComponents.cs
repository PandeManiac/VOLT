using System.Numerics;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Describes whether and how an entity is rendered.
/// </summary>
[EditorComponent(
	MeshRendererData.ComponentTypeId,
	"Mesh Renderer")]
public sealed class MeshRendererData : AuthoredComponent
{
	public const string ComponentTypeId = "volt.meshRenderer";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Mesh Renderer";

	[EditorProperty(DisplayName = "Mesh Asset", Kind = EditorPropertyKind.Asset)]
	public string Mesh { get; set; } = string.Empty;

	[EditorProperty(DisplayName = "Material Asset", Kind = EditorPropertyKind.Asset)]
	public string Material { get; set; } = string.Empty;

	public bool Visible { get; set; } = true;
}

/// <summary>
/// Continuously rotates an entity around one local axis.
/// </summary>
[EditorComponent(
	SpinBehaviourData.ComponentTypeId,
	"Spin Behaviour")]
public sealed class SpinBehaviourData : AuthoredComponent
{
	public const string ComponentTypeId = "volt.behaviour.spin";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Spin Behaviour";

	public bool Enabled { get; set; } = true;

	public Vector3 Axis { get; set; } = Vector3.UnitY;

	[EditorProperty(Minimum = -720f, Maximum = 720f)]
	public float DegreesPerSecond { get; set; } = 45f;
}

/// <summary>
/// Moves an entity along one local axis, either automatically or from
/// game input.
/// </summary>
[EditorComponent(
	MoveBehaviourData.ComponentTypeId,
	"Move Behaviour")]
public sealed class MoveBehaviourData : AuthoredComponent
{
	public const string ComponentTypeId = "volt.behaviour.move";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Move Behaviour";

	public bool Enabled { get; set; } = true;

	public Vector3 Axis { get; set; } = Vector3.UnitX;

	[EditorProperty(Minimum = -100f, Maximum = 100f)]
	public float Speed { get; set; } = 3f;

	[EditorProperty(
		DisplayName = "Auto Range",
		Minimum = 0f,
		Maximum = 100f)]
	public float Range { get; set; } = 2f;

	/// <summary>
	/// Gets or sets whether automatic ping-pong motion is used. When
	/// false the behaviour reads the active runtime input instead.
	/// </summary>
	public bool Automatic { get; set; } = true;

	/// <summary>
	/// Gets or sets the starting distance along <see cref="Axis"/>.
	/// </summary>
	public float Phase { get; set; }
}

/// <summary>
/// Marks the primary camera used by the editor preview and a running
/// game.
/// </summary>
[EditorComponent(
	CameraData.ComponentTypeId,
	"Camera")]
public sealed class CameraData : AuthoredComponent
{
	public const string ComponentTypeId = "volt.camera";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Camera";

	public bool Primary { get; set; } = true;

	[EditorProperty(Minimum = 1f, Maximum = 179f)]
	public float FieldOfView { get; set; } = 60f;

	public float Near { get; set; } = 0.1f;

	public float Far { get; set; } = 1000f;
}
