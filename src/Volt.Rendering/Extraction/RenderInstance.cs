using System.Numerics;
using Volt.Ecs;
using Volt.Rendering.Resources;

namespace Volt.Rendering.Extraction;

/// <summary>
/// Describes one renderable entity extracted from the ECS world.
/// </summary>
/// <remarks>
/// <para>
/// This is derived render data, not an ECS component. It is a
/// snapshot of the entity's component values at extraction time:
/// later component mutations do not update an existing record, and a
/// new extraction must be performed to observe them.
/// </para>
/// <para>
/// The record carries complete generational resource handles and a
/// world-space model matrix. It contains no Vulkan objects, command
/// buffers, shader objects, or windowing types; resolving handles to
/// GPU resources belongs to a later rendering stage.
/// </para>
/// <para>
/// The type is a value type with no managed references, so records
/// can be stored in caller-owned contiguous storage.
/// </para>
/// </remarks>
public readonly record struct RenderInstance(
	Entity Entity,
	MeshHandle Mesh,
	MaterialHandle Material,
	Matrix4x4 Model);
