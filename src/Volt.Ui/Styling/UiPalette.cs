namespace Volt.Ui.Styling;

/// <summary>
/// The colors of the VOLT interface theme.
/// </summary>
/// <remarks>
/// <para>
/// The palette is an original charcoal/slate scheme with a restrained
/// cyan-blue accent. It is data, not widget logic: an application can
/// replace any color without subclassing a widget.
/// </para>
/// <para>
/// Roles are grouped rather than named after the widgets that use
/// them, so a shell, a dialog, and an editor panel can stay visually
/// consistent. Chrome sits slightly darker than panel surfaces,
/// sunken areas (wells) darker still, and raised controls lighter
/// than the panels that host them; that gradient is what gives the
/// interface depth without shadows or gradients.
/// </para>
/// <para>
/// Values are authored in sRGB, matching how they are measured against
/// a screenshot. The renderer decodes them when the framebuffer format
/// applies the sRGB transfer function, so these numbers are the bytes
/// a developer sees rather than linear-light intermediates.
/// </para>
/// </remarks>
public sealed record UiPalette
{
	// Chrome: window background, menu bar, toolbars, status bar.

	public UiColor WindowBackground { get; init; } = UiColor.FromRgb(0x11151C);

	public UiColor Chrome { get; init; } = UiColor.FromRgb(0x151A22);

	public UiColor ChromeBorder { get; init; } = UiColor.FromRgb(0x262E3A);

	// Panels and containers.

	public UiColor Surface { get; init; } = UiColor.FromRgb(0x1B212B);

	public UiColor SurfaceRaised { get; init; } = UiColor.FromRgb(0x252D39);

	public UiColor SurfaceOverlay { get; init; } = UiColor.FromRgb(0x29313D);

	/// <summary>
	/// Gets the fill of a panel header or tab strip.
	/// </summary>
	public UiColor Header { get; init; } = UiColor.FromRgb(0x151A22);

	/// <summary>
	/// Gets the fill of a sunken area: the scene viewport, list wells,
	/// and progress tracks.
	/// </summary>
	public UiColor Well { get; init; } = UiColor.FromRgb(0x10151D);

	public UiColor WellBorder { get; init; } = UiColor.FromRgb(0x252D39);

	public UiColor Border { get; init; } = UiColor.FromRgb(0x303947);

	public UiColor BorderStrong { get; init; } = UiColor.FromRgb(0x42505F);

	// Text.

	public UiColor Text { get; init; } = UiColor.FromRgb(0xE7EDF5);

	public UiColor TextMuted { get; init; } = UiColor.FromRgb(0xA8B4C3);

	public UiColor TextDisabled { get; init; } = UiColor.FromRgb(0x788595);

	/// <summary>
	/// Gets the text color that stays legible on the accent fill.
	/// </summary>
	public UiColor TextOnAccent { get; init; } = UiColor.FromRgb(0x06141C);

	// Accent and interaction.

	public UiColor Accent { get; init; } = UiColor.FromRgb(0x38B6E8);

	public UiColor AccentHover { get; init; } = UiColor.FromRgb(0x55C6F2);

	public UiColor AccentPressed { get; init; } = UiColor.FromRgb(0x2B93BF);

	/// <summary>
	/// Gets the fill of an activated toolbar control.
	/// </summary>
	public UiColor AccentSurface { get; init; } = UiColor.FromRgb(0x1B4258);

	public UiColor AccentText { get; init; } = UiColor.FromRgb(0xEAF7FD);

	public UiColor Selection { get; init; } = UiColor.FromRgb(0x245777);

	public UiColor SelectionInactive { get; init; } = UiColor.FromRgb(0x2A3340);

	public UiColor Hover { get; init; } = UiColor.FromRgb(0x303B49);

	public UiColor Pressed { get; init; } = UiColor.FromRgb(0x3A4656);

	/// <summary>
	/// Gets the focus ring color drawn around the focused control.
	/// </summary>
	public UiColor FocusRing { get; init; } = UiColor.FromRgb(0x38B6E8);

	/// <summary>
	/// Gets the fill of an asset tile.
	/// </summary>
	public UiColor Tile { get; init; } = UiColor.FromRgb(0x1F2631);

	public UiColor TileHover { get; init; } = UiColor.FromRgb(0x28313E);

	// Status.

	public UiColor Danger { get; init; } = UiColor.FromRgb(0xE46C75);

	public UiColor Warning { get; init; } = UiColor.FromRgb(0xE2B45E);

	public UiColor Success { get; init; } = UiColor.FromRgb(0x54C78A);

	public UiColor Info { get; init; } = UiColor.FromRgb(0x6FB6DC);

	// Viewport and gizmos.

	public UiColor Grid { get; init; } = UiColor.FromRgb(0x252D39);

	public UiColor GridAxisX { get; init; } = UiColor.FromRgb(0xC86A6A);

	public UiColor GridAxisZ { get; init; } = UiColor.FromRgb(0x5A8FD0);

	/// <summary>
	/// Gets the muted red of a transform axis, used for X hints.
	/// </summary>
	public UiColor AxisX { get; init; } = UiColor.FromRgb(0xD96A6A);

	public UiColor AxisY { get; init; } = UiColor.FromRgb(0x7CC98A);

	public UiColor AxisZ { get; init; } = UiColor.FromRgb(0x6AA2E0);

	/// <summary>
	/// Gets the translucent fill behind floating viewport controls.
	/// </summary>
	public UiColor Overlay { get; init; } = UiColor.FromArgb(0xE810141A);

	public UiColor OverlayBorder { get; init; } = UiColor.FromRgb(0x38424F);

	public UiColor Shadow { get; init; } = UiColor.FromArgb(0x88000000);

	public UiColor ScrollThumb { get; init; } = UiColor.FromRgb(0x38414E);

	public UiColor ScrollThumbHover { get; init; } = UiColor.FromRgb(0x4A5563);

	public static UiPalette Dark => new();
}
