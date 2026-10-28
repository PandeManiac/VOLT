using System.Numerics;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// The authored directional light uploaded to the scene pipeline.
/// </summary>
/// <param name="Direction">
/// The world-space direction toward the light. It need not be unit
/// length; the fragment shader normalizes it.
/// </param>
/// <param name="DiffuseColor">
/// The light color in linear RGB, already multiplied by intensity.
/// </param>
/// <remarks>
/// This is a renderer input, not an authored scene type. The editor
/// resolves authored light data into this value once per viewport and
/// the pipeline packs it into the frame slot's uniform buffer.
/// </remarks>
public readonly record struct VulkanSceneLight(
	Vector3 Direction,
	Vector3 DiffuseColor);
