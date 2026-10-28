using System.Numerics;
using System.Runtime.InteropServices;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Vertex layout shared by meshes drawn through
/// <see cref="VulkanSceneMeshPipeline"/>.
/// </summary>
/// <remarks>
/// Position and albedo are each three tightly packed floats. There is
/// no normal attribute: the scene pipeline derives face normals from
/// the world-space position, which keeps every generated primitive on
/// one vertex format.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly struct VulkanSceneVertex
{
	/// <summary>
	/// Gets the local-space position.
	/// </summary>
	public readonly Vector3 Position;

	/// <summary>
	/// Gets the local-space albedo colour.
	/// </summary>
	public readonly Vector3 Color;

	/// <summary>
	/// Initializes one vertex.
	/// </summary>
	public VulkanSceneVertex(Vector3 position, Vector3 color)
	{
		Position = position;
		Color = color;
	}
}
