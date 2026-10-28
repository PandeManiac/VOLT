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
/// </remarks>
public sealed record UiPalette
{
	// Chrome: window background, menu bar, toolbars, status bar.

	public UiColor WindowBackground { get; init; } = UiColor.FromRgb(0x171B22);

	public UiColor Chrome { get; init; } = UiColor.FromRgb(0x1D232C);

	public UiColor ChromeBorder { get; init; } = UiColor.FromRgb(0x2C3541);

	// Panels and containers.

	public UiColor Surface { get; init; } = UiColor.FromRgb(0x202630);

	public UiColor SurfaceRaised { get; init; } = UiColor.FromRgb(0x272E39);

	public UiColor SurfaceOverlay { get; init; } = UiColor.FromRgb(0x2A3340);

	/// <summary>
	/// Gets the fill of a panel header or tab strip.
	/// </summary>
	public UiColor Header { get; init; } = UiColor.FromRgb(0x1B2029);

	/// <summary>
	/// Gets the fill of a sunken area: the scene viewport, list wells,
	/// and progress tracks.
	/// </summary>
	public UiColor Well { get; init; } = UiColor.FromRgb(0x12161C);

	public UiColor WellBorder { get; init; } = UiColor.FromRgb(0x2A323D);

	public UiColor Border { get; init; } = UiColor.FromRgb(0x333D4A);

	public UiColor BorderStrong { get; init; } = UiColor.FromRgb(0x44505F);

	// Text.

	public UiColor Text { get; init; } = UiColor.FromRgb(0xE5EAF1);

	public UiColor TextMuted { get; init; } = UiColor.FromRgb(0xA8B3C1);

	public UiColor TextDisabled { get; init; } = UiColor.FromRgb(0x748092);

	/// <summary>
	/// Gets the text color that stays legible on the accent fill.
	/// </summary>
	public UiColor TextOnAccent { get; init; } = UiColor.FromRgb(0x06141C);

	// Accent and interaction.

	public UiColor Accent { get; init; } = UiColor.FromRgb(0x35A9E0);

	public UiColor AccentHover { get; init; } = UiColor.FromRgb(0x54BCEA);

	public UiColor AccentPressed { get; init; } = UiColor.FromRgb(0x2A8FC0);

	/// <summary>
	/// Gets the fill of an activated toolbar control.
	/// </summary>
	public UiColor AccentSurface { get; init; } = UiColor.FromRgb(0x1F4A66);

	public UiColor AccentText { get; init; } = UiColor.FromRgb(0xEAF6FC);

	public UiColor Selection { get; init; } = UiColor.FromRgb(0x285B84);

	public UiColor SelectionInactive { get; init; } = UiColor.FromRgb(0x2C3947);

	public UiColor Hover { get; init; } = UiColor.FromRgb(0x313A47);

	public UiColor Pressed { get; init; } = UiColor.FromRgb(0x3B4655);

	/// <summary>
	/// Gets the focus ring color drawn around the focused control.
	/// </summary>
	public UiColor FocusRing { get; init; } = UiColor.FromRgb(0x35A9E0);

	/// <summary>
	/// Gets the fill of an asset tile.
	/// </summary>
	public UiColor Tile { get; init; } = UiColor.FromRgb(0x242B36);

	public UiColor TileHover { get; init; } = UiColor.FromRgb(0x2C3542);

	// Status.

	public UiColor Danger { get; init; } = UiColor.FromRgb(0xE46C75);

	public UiColor Warning { get; init; } = UiColor.FromRgb(0xE2B45E);

	public UiColor Success { get; init; } = UiColor.FromRgb(0x54C78A);

	public UiColor Info { get; init; } = UiColor.FromRgb(0x6FB6DC);

	// Viewport and gizmos.

	public UiColor Grid { get; init; } = UiColor.FromRgb(0x2A323D);

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
	public UiColor Overlay { get; init; } = UiColor.FromArgb(0xE61B2129);

	public UiColor OverlayBorder { get; init; } = UiColor.FromRgb(0x394450);

	public UiColor Shadow { get; init; } = UiColor.FromArgb(0x88000000);

	public UiColor ScrollThumb { get; init; } = UiColor.FromRgb(0x3B4655);

	public UiColor ScrollThumbHover { get; init; } = UiColor.FromRgb(0x4C5868);

	public static UiPalette Dark => new();
}
