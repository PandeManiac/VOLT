using Volt.Editor.Rendering;
using Volt.Ui;
using Volt.Ui.Docking;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Editor;

/// <summary>
/// Renders the editor's chrome to an image without a window or a GPU.
/// </summary>
/// <remarks>
/// The shell lays out and paints exactly as it does in a window; the
/// only difference is who consumes the draw list and who fills the
/// viewport images. A few frames are simulated first so the dock, the
/// hierarchy, and the viewport chrome reach their steady state, which
/// is what the first presented frame would show. The scene and game
/// views are then rasterized by <see cref="SoftwareSceneRenderer"/>,
/// so the capture includes depth-tested meshes, the scene's lighting,
/// and the ground grid instead of a placeholder.
/// </remarks>
internal static class EditorSnapshot
{
	/// <summary>
	/// Frames simulated before the image is taken.
	/// </summary>
	private const int SettleFrames = 3;

	public static UiRasterImage Capture(
		EditorSession session,
		string path,
		int width,
		int height,
		float uiScale = 1f,
		DockLayout? layout = null,
		string? defaultProjectFolder = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentException.ThrowIfNullOrWhiteSpace(path);

		if (width < 320 || height < 240)
		{
			throw new ArgumentOutOfRangeException(
				nameof(width),
				"A snapshot needs at least 320 by 240 logical units.");
		}

		using UiContext context = new();
		SoftwareSceneRenderer viewportRenderer = new();
		using EditorShell shell = new(
			session,
			defaultProjectFolder ?? session.Project?.RootPath,
			layout ?? DockLayout.CreateEditorDefault(),
			viewportRenderer);

		if (float.IsFinite(uiScale) &&
			uiScale > 0f &&
			MathF.Abs(uiScale - 1f) > 0.0001f)
		{
			shell.ApplyUiScale(uiScale);
		}

		context.Add(shell);

		UiDrawList? frame = null;
		UiSize size = new(width, height);

		for (int index = 0; index < SettleFrames; index++)
		{
			context.BeginFrame(
				index / 60.0,
				size,
				UiPoint.Zero);

			shell.Update(1.0 / 60.0, 1f);

			frame = context.Draw();
		}

		// The viewports published their requests while the final frame
		// painted, so those are the requests this capture rasterizes.
		viewportRenderer.RenderFrame();

		UiTheme theme = UiTheme.CreateDark();

		UiRasterImage image = UiRasterizer.Render(
			frame ?? throw new InvalidOperationException(
				"The editor produced no frame to capture."),
			1f,
			theme.Palette.WindowBackground,
			viewportRenderer.Textures);

		image.SavePng(path);

		return image;
	}
}
