namespace Volt.Ui.Rendering;

/// <summary>
/// The built-in vector icon vocabulary.
/// </summary>
/// <remarks>
/// Icons are drawn from geometry rather than font glyphs or image
/// assets. They therefore stay sharp at any scale, need no texture
/// atlas space, and can be tinted by the theme like any other shape.
/// The vocabulary is intentionally small and functional: it covers
/// the tools, panels, asset kinds, and diagnostics an editor needs.
/// </remarks>
public enum UiIcon
{
	None = 0,
	Select,
	Move,
	Rotate,
	Scale,
	Play,
	Pause,
	Step,
	Stop,
	Sphere,
	Cube,
	Plane,
	Camera,
	Light,
	Folder,
	Scene,
	Script,
	Texture,
	Material,
	Mesh,
	Search,
	Plus,
	Minus,
	Duplicate,
	Trash,
	Refresh,
	Check,
	Close,
	ChevronRight,
	ChevronDown,
	Grid,
	Snap,
	World,
	Local,
	Settings,
	Undo,
	Redo,
	Save,
	Warning,
	Error,
	Info,
	Layers,
	ListView,
	TileView,
	Hierarchy,
	Inspector,
	Console,
	Profiler,
	Project,
	Visibility,
	Dot,
	Frame
}
