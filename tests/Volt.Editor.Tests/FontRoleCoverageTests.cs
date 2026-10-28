using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Text;

namespace Volt.Editor.Tests;

/// <summary>
/// Verifies that every font the editor actually paints with belongs to
/// the font set the UI context exposes.
/// </summary>
/// <remarks>
/// The Vulkan renderer maps exactly the context theme's fonts to atlas
/// layers; any other font object has no layer and samples the opaque
/// white texel, which paints solid blocks instead of letters. This test
/// is the editor-level guard for that contract, so it must fail if a
/// panel starts capturing fonts from a separate theme.
/// </remarks>
public sealed class FontRoleCoverageTests
{
	[Fact]
	public void EveryPaintedFont_BelongsToTheContextTheme()
	{
		using EditorSession session = new();
		using UiContext context = new();
		using EditorShell shell = new(session);

		context.Add(shell);
		context.BeginFrame(0.0, new UiSize(1600f, 960f), UiPoint.Zero);

		UiDrawList drawList = context.Draw();

		HashSet<UiFont> themeFonts = [.. context.Theme.Fonts.Enumerate()];

		Assert.Equal(8, themeFonts.Count);

		List<UiFont> painted = [];

		foreach (UiDrawCommand command in drawList.Commands)
		{
			if (command.Kind == UiDrawCommandKind.Text &&
				command.Text is not null)
			{
				painted.Add(command.Text.Font);
			}
		}

		Assert.NotEmpty(painted);

		foreach (UiFont font in painted)
		{
			Assert.Contains(font, themeFonts);
		}

		// The start screen heading and its caption are the shapes that
		// rendered as solid blocks when the shell kept a second font set.
		Assert.Contains(context.Theme.Fonts.Title, painted);
		Assert.Contains(context.Theme.Fonts.Small, painted);
	}

	[Fact]
	public void EveryPaintedFont_BelongsToTheContextTheme_WithAProjectOpen()
	{
		string root = Path.Combine(
			Path.GetTempPath(),
			$"volt-font-roles-{Guid.NewGuid():N}");

		try
		{
			using EditorSession session = new();

			session.NewProject(Path.Combine(root, "Game"), "Font Roles");

			using UiContext context = new();
			using EditorShell shell = new(session);

			context.Add(shell);
			context.BeginFrame(0.0, new UiSize(1600f, 960f), UiPoint.Zero);

			UiDrawList drawList = context.Draw();

			HashSet<UiFont> themeFonts = [.. context.Theme.Fonts.Enumerate()];
			int inspected = 0;

			foreach (UiDrawCommand command in drawList.Commands)
			{
				if (command.Kind != UiDrawCommandKind.Text ||
					command.Text is null)
				{
					continue;
				}

				inspected++;

				Assert.Contains(command.Text.Font, themeFonts);
			}

			Assert.True(inspected > 0);
		}

		finally
		{
			try
			{
				Directory.Delete(root, recursive: true);
			}

			catch (IOException)
			{
				// A leftover temporary folder must not fail the test.
			}
		}
	}
}
