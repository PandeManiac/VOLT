using System.Text.Json;

namespace Volt.Editor;

/// <summary>
/// Settings that belong to the editor rather than to a project or a
/// scene, persisted with the workspace so they survive a restart.
/// </summary>
/// <remarks>
/// The type is deliberately small and validated on the way in: a
/// hand-edited or older file must never put the editor into a state
/// its own controls cannot represent. Unknown JSON members are
/// ignored, so a file written by a newer build still loads what this
/// one understands.
/// </remarks>
public sealed class EditorSettings
{
	/// <summary>
	/// The format version this build writes and reads.
	/// </summary>
	public const int CurrentVersion = 1;

	/// <summary>
	/// The frame rate the profiler assumes when nothing else is set.
	/// </summary>
	public const int DefaultTargetFrameRate = 60;

	/// <summary>
	/// The lowest target frame rate the editor accepts.
	/// </summary>
	public const int MinimumTargetFrameRate = 10;

	/// <summary>
	/// The highest target frame rate the editor accepts.
	/// </summary>
	public const int MaximumTargetFrameRate = 480;

	/// <summary>
	/// The interface scale a workspace starts at.
	/// </summary>
	public const float DefaultInterfaceScale = 1f;

	/// <summary>
	/// The smallest interface scale the editor persists.
	/// </summary>
	public const float MinimumInterfaceScale = 0.75f;

	/// <summary>
	/// The largest interface scale the editor persists.
	/// </summary>
	public const float MaximumInterfaceScale = 2f;

	/// <summary>
	/// The window width a workspace opens at when none is saved.
	/// </summary>
	public const int DefaultWindowWidth = 1600;

	/// <summary>
	/// The window height a workspace opens at when none is saved.
	/// </summary>
	public const int DefaultWindowHeight = 960;

	/// <summary>
	/// The narrowest window width the editor restores.
	/// </summary>
	public const int MinimumWindowWidth = 640;

	/// <summary>
	/// The shortest window height the editor restores.
	/// </summary>
	public const int MinimumWindowHeight = 480;

	/// <summary>
	/// The largest window dimension the editor persists.
	/// </summary>
	/// <remarks>
	/// This is a sanity bound for hand-edited files, not a display
	/// limit: the window manager still decides what fits.
	/// </remarks>
	public const int MaximumWindowDimension = 8192;

	private static readonly JsonSerializerOptions s_options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	/// <summary>
	/// Gets or sets the persisted format version.
	/// </summary>
	public int Version { get; set; } = CurrentVersion;

	/// <summary>
	/// Gets or sets the frame rate the editor aims for, which is the
	/// budget the profiler graphs against.
	/// </summary>
	public int TargetFrameRate { get; set; } = DefaultTargetFrameRate;

	/// <summary>
	/// Gets or sets the interface scale the editor restores at startup,
	/// which is the same setting the Preferences modal offers.
	/// </summary>
	public float InterfaceScale { get; set; } = DefaultInterfaceScale;

	/// <summary>
	/// Gets or sets the logical width the editor window reopens at.
	/// </summary>
	public int WindowWidth { get; set; } = DefaultWindowWidth;

	/// <summary>
	/// Gets or sets the logical height the editor window reopens at.
	/// </summary>
	public int WindowHeight { get; set; } = DefaultWindowHeight;

	/// <summary>
	/// Gets whether a frame rate is one the editor accepts.
	/// </summary>
	public static bool IsValidTargetFrameRate(int frameRate)
	{
		return frameRate is >= MinimumTargetFrameRate
			and <= MaximumTargetFrameRate;
	}

	/// <summary>
	/// Brings a typed frame rate into the supported range.
	/// </summary>
	/// <remarks>
	/// A preference field clamps rather than falls back: typing 5 fps
	/// should give the slowest supported rate, not silently reset the
	/// setting to the default.
	/// </remarks>
	public static int ClampTargetFrameRate(int frameRate)
	{
		return Math.Clamp(
			frameRate,
			MinimumTargetFrameRate,
			MaximumTargetFrameRate);
	}

	/// <summary>
	/// Brings a typed interface scale into the supported range.
	/// </summary>
	public static float ClampInterfaceScale(float scale)
	{
		return float.IsFinite(scale)
			? Math.Clamp(
				scale,
				MinimumInterfaceScale,
				MaximumInterfaceScale)
			: DefaultInterfaceScale;
	}

	/// <summary>
	/// Brings a typed window width into the supported range.
	/// </summary>
	public static int ClampWindowWidth(int width)
	{
		return Math.Clamp(
			width,
			MinimumWindowWidth,
			MaximumWindowDimension);
	}

	/// <summary>
	/// Brings a typed window height into the supported range.
	/// </summary>
	public static int ClampWindowHeight(int height)
	{
		return Math.Clamp(
			height,
			MinimumWindowHeight,
			MaximumWindowDimension);
	}

	/// <summary>
	/// Returns a copy whose values are all representable, falling back
	/// to defaults rather than trusting a file.
	/// </summary>
	public EditorSettings Normalize()
	{
		return new EditorSettings
		{
			Version = CurrentVersion,
			TargetFrameRate = IsValidTargetFrameRate(TargetFrameRate)
				? TargetFrameRate
				: DefaultTargetFrameRate,
			InterfaceScale = ClampInterfaceScale(InterfaceScale),
			WindowWidth = ClampWindowWidth(WindowWidth),
			WindowHeight = ClampWindowHeight(WindowHeight)
		};
	}

	/// <summary>
	/// Serializes the settings as indented camel-case JSON.
	/// </summary>
	public string Serialize()
	{
		return JsonSerializer.Serialize(Normalize(), s_options);
	}

	/// <summary>
	/// Reads settings from JSON.
	/// </summary>
	/// <param name="json">The persisted JSON.</param>
	/// <returns>The normalized settings.</returns>
	/// <exception cref="InvalidDataException">
	/// The JSON is malformed, empty, or from an unsupported version.
	/// </exception>
	public static EditorSettings Deserialize(string json)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(json);

		EditorSettings? settings;

		try
		{
			settings = JsonSerializer.Deserialize<EditorSettings>(
				json,
				s_options);
		}

		catch (JsonException exception)
		{
			throw new InvalidDataException(
				"The editor settings JSON is malformed.",
				exception);
		}

		if (settings is null)
		{
			throw new InvalidDataException(
				"The editor settings document is empty.");
		}

		if (settings.Version != CurrentVersion)
		{
			throw new InvalidDataException(
				$"Unsupported editor settings version {settings.Version}. " +
				$"This build reads version {CurrentVersion}.");
		}

		return settings.Normalize();
	}
}
