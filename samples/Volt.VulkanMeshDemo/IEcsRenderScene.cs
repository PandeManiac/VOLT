using System.Numerics;
using Volt.Ecs;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Describes a scene whose draw records come from an ECS world.
/// </summary>
/// <remarks>
/// The mesh renderer owns the pipeline and framebuffers, not the
/// scene. Two sample scenes are ECS-driven: the fixed ECS scene and the
/// playable target range. This interface is the small set of queries
/// the renderer needs from either of them, so the renderer keeps one
/// extraction path instead of duplicating the GPU recording rules. It
/// is deliberately sample-local; it is not an engine scene framework.
/// </remarks>
internal interface IEcsRenderScene
{
	/// <summary>
	/// Gets the world that owns the scene's entities.
	/// </summary>
	World World { get; }

	/// <summary>
	/// Gets the entity whose camera components drive the view and
	/// projection.
	/// </summary>
	Entity CameraEntity { get; }

	/// <summary>
	/// Gets the clip-space overlay mesh drawn after the scene, or null
	/// when the scene has no overlay.
	/// </summary>
	MeshHandle? OverlayMesh { get; }

	/// <summary>
	/// Extracts the scene's renderable entities into storage the scene
	/// owns and reuses.
	/// </summary>
	ReadOnlySpan<RenderInstance> Extract();

	/// <summary>
	/// Resolves a complete generational mesh handle.
	/// </summary>
	bool TryResolveMesh(MeshHandle handle, out EcsMeshBinding binding);

	/// <summary>
	/// Reports whether a material handle is known to the scene.
	/// </summary>
	bool ContainsMaterial(MaterialHandle handle);

	/// <summary>
	/// Counts the Vulkan draw commands one extracted frame produces.
	/// </summary>
	int CountDrawCommands();

	/// <summary>
	/// Creates the clip-space transform for the overlay mesh.
	/// </summary>
	Matrix4x4 CreateOverlayTransform(
		int framebufferWidth,
		int framebufferHeight);
}
