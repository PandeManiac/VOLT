using StbTrueTypeSharp;

namespace Volt.Ui.Text;

/// <summary>
/// Owns the parsed TrueType data for one face.
/// </summary>
/// <remarks>
/// A face is shared by every <see cref="UiFont"/> size created from
/// it. Parsing happens once; sizing is applied per font instance.
/// The type is not thread-safe and is intended for the UI thread.
/// </remarks>
internal sealed unsafe class UiFontFace
{
	private static readonly Dictionary<UiFontKind, UiFontFace> s_faces = [];
	private static readonly Lock s_gate = new();

	private readonly byte[] _data;

	private UiFontFace(
		UiFontKind kind,
		string familyName,
		byte[] data,
		StbTrueType.stbtt_fontinfo info)
	{
		Kind = kind;
		FamilyName = familyName;
		_data = data;
		Info = info;
	}

	public UiFontKind Kind { get; }

	public string FamilyName { get; }

	public StbTrueType.stbtt_fontinfo Info { get; }

	public static UiFontFace Load(UiFontKind kind)
	{
		lock (s_gate)
		{
			if (s_faces.TryGetValue(kind, out UiFontFace? cached))
			{
				return cached;
			}

			(string resourceName, string familyName) = kind switch
			{
				UiFontKind.Sans => (
					"Volt.Ui.Fonts.IBMPlexSans-Regular.ttf",
					"IBM Plex Sans"),
				UiFontKind.Monospace => (
					"Volt.Ui.Fonts.JetBrainsMono-Regular.ttf",
					"JetBrains Mono"),
				_ => throw new ArgumentOutOfRangeException(nameof(kind))
			};

			using Stream stream = typeof(UiFontFace)
				.Assembly
				.GetManifestResourceStream(resourceName)
				?? throw new InvalidOperationException(
					$"The embedded font '{resourceName}' is missing.");

			byte[] data = new byte[stream.Length];

			using MemoryStream memory = new(data);
			stream.CopyTo(memory);

			StbTrueType.stbtt_fontinfo info = StbTrueType.CreateFont(data, 0)
				?? throw new InvalidOperationException(
					$"The embedded font '{resourceName}' could not be parsed.");

			UiFontFace face = new(kind, familyName, data, info);
			s_faces.Add(kind, face);

			return face;
		}
	}

	// The data must stay reachable for the lifetime of stbtt_fontinfo,
	// which holds pointers into it. This method documents that
	// requirement for readers and the garbage collector.
	public ReadOnlySpan<byte> Data => _data;
}
