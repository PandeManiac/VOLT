using Volt.Ui.Rendering;

namespace Volt.Editor;

/// <summary>
/// The name, icon, and hotkey of each editor tool.
/// </summary>
/// <remarks>
/// The toolbar and the viewport's floating chrome both present the
/// tools, so the table lives here rather than inside either surface:
/// a tool that is renamed or given a new icon changes in one place.
/// </remarks>
internal static class EditorToolInfo
{
	public static UiIcon Icon(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.Select => UiIcon.Select,
			EditorTool.Rotate => UiIcon.Rotate,
			EditorTool.Scale => UiIcon.Scale,
			_ => UiIcon.Move
		};
	}

	public static string Label(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.Select => "Select",
			EditorTool.Rotate => "Rotate",
			EditorTool.Scale => "Scale",
			_ => "Move"
		};
	}

	public static string Shortcut(EditorTool tool)
	{
		return tool switch
		{
			EditorTool.Select => "Q",
			EditorTool.Rotate => "E",
			EditorTool.Scale => "R",
			_ => "W"
		};
	}
}
