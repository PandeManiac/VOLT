namespace Volt.Editor.Panels;

/// <summary>
/// View state the inspector keeps across an interface-scale or
/// preferences rebuild, when the panel widget itself is recreated.
/// </summary>
/// <remarks>
/// Only the property grid's offset is kept: the bound object, the
/// component rows and the typed values all come back from the session,
/// but how far the author had scrolled through a long component list
/// exists nowhere else.
/// </remarks>
internal sealed class InspectorPanelState
{
	public float ScrollOffset { get; init; }
}
