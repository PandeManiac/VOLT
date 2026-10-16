
using Volt.Rendering.Resources;

namespace Volt.Rendering.Components;

/// <summary>
/// Associates an entity with a mesh and material for rendering.
/// </summary>
/// <remarks>
/// Meshes and materials are referenced using generational handles.
///
/// This component does not own either resource. Resource lifetime
/// and handle validation are managed by the rendering system.
///
/// A default component contains invalid resource handles.
/// </remarks>
public struct MeshRendererComponent(
    MeshHandle mesh,
    MaterialHandle material)
{
    public MeshHandle Mesh = mesh;
    public MaterialHandle Material = material;
}
