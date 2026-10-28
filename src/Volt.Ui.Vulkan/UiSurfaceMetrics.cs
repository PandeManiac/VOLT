using Volt.Platform.Windows;

namespace Volt.Ui.Vulkan;

/// <summary>
/// The logical and physical geometry of a UI window, plus the display
/// scale that relates them.
/// </summary>
/// <remarks>
/// <para>
/// SDL reports the logical size in points and the drawable size in
/// pixels; a high-density display reports more pixels than points. The
/// UI lays out in logical units and the swapchain is created in
/// physical pixels, so both numbers come from one snapshot and cannot
/// disagree.
/// </para>
/// <para>
/// Logical geometry is never zero, because a zero-sized layout surface
/// cannot be arranged. Physical geometry may be zero while a window is
/// minimized, which is why <see cref="HasPhysicalSurface"/> exists: a
/// minimized window has nothing to present and must not be treated as a
/// very small one.
/// </para>
/// </remarks>
public readonly record struct UiSurfaceMetrics(
	int PhysicalWidth,
	int PhysicalHeight,
	int LogicalWidth,
	int LogicalHeight,
	float DisplayScale)
{
	/// <summary>
	/// Gets the logical surface size used for layout.
	/// </summary>
	public UiSize LogicalSize => new(LogicalWidth, LogicalHeight);

	/// <summary>
	/// Gets whether the window currently has pixels to present.
	/// </summary>
	public bool HasPhysicalSurface =>
		PhysicalWidth > 0 && PhysicalHeight > 0;

	/// <summary>
	/// Derives the layout geometry and display scale of one window.
	/// </summary>
	/// <param name="logical">The window size in points.</param>
	/// <param name="physical">The drawable size in pixels.</param>
	/// <returns>
	/// Logical dimensions of at least one, the reported physical
	/// dimensions, and physical pixels per logical unit.
	/// </returns>
	public static UiSurfaceMetrics FromWindowSizes(
		WindowSize logical,
		WindowSize physical)
	{
		int physicalWidth = Math.Max(0, physical.Width);
		int physicalHeight = Math.Max(0, physical.Height);

		int logicalWidth = logical.Width > 0
			? logical.Width
			: Math.Max(1, physicalWidth);

		int logicalHeight = logical.Height > 0
			? logical.Height
			: Math.Max(1, physicalHeight);

		float displayScale = MathF.Max(
			0.01f,
			physicalWidth / (float)logicalWidth);

		return new UiSurfaceMetrics(
			physicalWidth,
			physicalHeight,
			logicalWidth,
			logicalHeight,
			displayScale);
	}
}
