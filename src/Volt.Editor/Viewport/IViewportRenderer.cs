using Volt.Editor.Rendering;
using Volt.Ui;

namespace Volt.Editor.Viewport;

/// <summary>
/// The renderer contract a viewport widget needs.
/// </summary>
/// <remarks>
/// The editor's <c>EditorSceneRenderer</c> implements this. The
/// interface exists so the widgets depend on what a viewport requires
/// rather than on the Vulkan renderer itself, which keeps them
/// constructible, and testable, without a GPU: a widget whose renderer
/// is null still lays out, navigates, picks, and draws its overlays.
/// </remarks>
internal interface IViewportRenderer
{
	/// <summary>
	/// Gets the frame slot the current frame records into.
	/// </summary>
	/// <remarks>
	/// A viewport draws the image for this slot, which is the one the
	/// same submission fills.
	/// </remarks>
	int FrameSlot { get; }

	/// <summary>
	/// Gets the image texture a viewport draws for a slot, creating the
	/// target on first use.
	/// </summary>
	/// <param name="kind">The viewport kind.</param>
	/// <param name="slot">The frame slot.</param>
	/// <returns>
	/// The texture to draw, or null when there is nothing to show yet.
	/// </returns>
	UiTextureId? PrepareTexture(ViewportKind kind, int slot);

	/// <summary>
	/// Publishes what a viewport wants rendered for the current frame.
	/// </summary>
	/// <param name="request">The request to consume.</param>
	void PublishRequest(ViewportRenderRequest request);

	/// <summary>
	/// Removes a viewport's request so nothing is rendered for it.
	/// </summary>
	/// <param name="kind">The viewport kind.</param>
	void ClearRequest(ViewportKind kind);
}

/// <summary>
/// A widget that advances its own state once per frame.
/// </summary>
/// <remarks>
/// The editor shell drives these with the host's frame delta. Camera
/// movement belongs to the frame, not to painting, so a viewport moves
/// at the same rate no matter how often it repaints.
/// </remarks>
internal interface IFrameTick
{
	/// <summary>
	/// Gets whether the widget has been disposed.
	/// </summary>
	/// <remarks>
	/// The shell drops disposed widgets from its tick list, so a panel
	/// that was closed stops advancing.
	/// </remarks>
	bool IsDisposed { get; }

	/// <summary>
	/// Advances the widget by one frame.
	/// </summary>
	/// <param name="deltaSeconds">The real frame interval.</param>
	/// <param name="dpiScale">
	/// Physical pixels per logical unit, needed to size render targets.
	/// </param>
	void Tick(double deltaSeconds, float dpiScale);
}
