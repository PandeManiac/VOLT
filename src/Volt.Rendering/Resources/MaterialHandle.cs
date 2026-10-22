
namespace Volt.Rendering.Resources;

/// <summary>
/// Identifies a material resource managed by the rendering system.
/// </summary>
/// <remarks>
/// The index identifies a resource slot, while the generation
/// distinguishes successive resources occupying that slot.
///
/// A handle does not own a resource or guarantee that it is
/// currently alive. Resource lifetime is managed separately.
/// </remarks>
public readonly record struct MaterialHandle(uint Index, uint Generation)
{
	/// <summary>
	/// Gets whether the handle has a nonzero generation.
	/// </summary>
	public bool IsValid => Generation != 0;
}
