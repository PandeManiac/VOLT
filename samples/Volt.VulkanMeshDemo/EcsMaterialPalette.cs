using Volt.Rendering.Resources;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Validates material handles for the ECS scene.
/// </summary>
/// <remarks>
/// The sample's shader colours vertices directly and has no material
/// pipelines, textures, or shader selection. Material handles
/// therefore prove only that the render record carries and validates
/// a complete generational handle. They do not select GPU state.
///
/// This is a sample-local table, not a rendering-system material
/// manager.
/// </remarks>
internal sealed class EcsMaterialPalette
{
	/// <summary>
	/// Gets the single vertex-colour material used by every ECS
	/// entity.
	/// </summary>
	public MaterialHandle VertexColor { get; } =
		EcsDemoScene.VertexColorMaterial;

	/// <summary>
	/// Reports whether a complete handle names the vertex-colour
	/// material.
	/// </summary>
	public bool Contains(MaterialHandle handle)
	{
		return handle == VertexColor;
	}
}
