namespace Volt.Ui.Styling;

/// <summary>
/// The colors of the VOLT interface theme.
/// </summary>
/// <remarks>
/// The default palette is an original charcoal/slate scheme with
/// restrained cyan accents. It is data, not widget logic: an
/// application can replace any color without subclassing a widget.
/// </remarks>
public sealed record UiPalette
{
	public UiColor WindowBackground { get; init; } = UiColor.FromRgb(0x101318);

	public UiColor Surface { get; init; } = UiColor.FromRgb(0x181C23);

	public UiColor SurfaceRaised { get; init; } = UiColor.FromRgb(0x1E232C);

	public UiColor SurfaceOverlay { get; init; } = UiColor.FromRgb(0x232933);

	public UiColor Header { get; init; } = UiColor.FromRgb(0x1B2028);

	public UiColor Border { get; init; } = UiColor.FromRgb(0x2C333E);

	public UiColor BorderStrong { get; init; } = UiColor.FromRgb(0x3B4553);

	public UiColor Text { get; init; } = UiColor.FromRgb(0xD9E0E9);

	public UiColor TextMuted { get; init; } = UiColor.FromRgb(0x8C99AA);

	public UiColor TextDisabled { get; init; } = UiColor.FromRgb(0x5B6574);

	public UiColor Accent { get; init; } = UiColor.FromRgb(0x45C2E6);

	public UiColor AccentHover { get; init; } = UiColor.FromRgb(0x6AD2F0);

	public UiColor AccentPressed { get; init; } = UiColor.FromRgb(0x2FA8CC);

	public UiColor AccentText { get; init; } = UiColor.FromRgb(0x081217);

	public UiColor Selection { get; init; } = UiColor.FromRgb(0x1B3B4A);

	public UiColor SelectionInactive { get; init; } = UiColor.FromRgb(0x262D38);

	public UiColor Hover { get; init; } = UiColor.FromRgb(0x252C36);

	public UiColor Pressed { get; init; } = UiColor.FromRgb(0x2D3541);

	public UiColor Danger { get; init; } = UiColor.FromRgb(0xF07178);

	public UiColor Warning { get; init; } = UiColor.FromRgb(0xE6B450);

	public UiColor Success { get; init; } = UiColor.FromRgb(0x4FD1A1);

	public UiColor Grid { get; init; } = UiColor.FromRgb(0x2E3540);

	public UiColor GridAxisX { get; init; } = UiColor.FromRgb(0xB65E5E);

	public UiColor GridAxisZ { get; init; } = UiColor.FromRgb(0x4E7FB8);

	public UiColor Shadow { get; init; } = UiColor.FromArgb(0x88000000);

	public static UiPalette Dark => new();
}
