namespace Volt.Editor.Tests;

/// <summary>
/// Covers the persisted editor settings and the session setting they
/// feed: a file must never put the editor into a state its own
/// controls cannot represent.
/// </summary>
public sealed class EditorSettingsTests
{
	[Fact]
	public void Defaults_UseSixtyFramesPerSecond()
	{
		EditorSettings settings = new();

		Assert.Equal(EditorSettings.CurrentVersion, settings.Version);
		Assert.Equal(60, settings.TargetFrameRate);
		Assert.Equal(1f, settings.InterfaceScale, 3);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-60)]
	[InlineData(9)]
	[InlineData(481)]
	[InlineData(int.MaxValue)]
	public void Normalize_FallsBackForRatesTheEditorCannotRepresent(int rate)
	{
		EditorSettings settings = new()
		{
			TargetFrameRate = rate
		};

		EditorSettings normalized = settings.Normalize();

		Assert.Equal(60, normalized.TargetFrameRate);
	}

	[Theory]
	[InlineData(10)]
	[InlineData(60)]
	[InlineData(144)]
	[InlineData(480)]
	public void Normalize_KeepsSupportedRates(int rate)
	{
		EditorSettings settings = new()
		{
			TargetFrameRate = rate
		};

		Assert.Equal(rate, settings.Normalize().TargetFrameRate);
	}

	[Theory]
	[InlineData(5, 10)]
	[InlineData(-1, 10)]
	[InlineData(90, 90)]
	[InlineData(999, 480)]
	public void Clamp_BringsTypedRatesIntoTheSupportedRange(
		int typed,
		int expected)
	{
		Assert.Equal(
			expected,
			EditorSettings.ClampTargetFrameRate(typed));
	}

	[Theory]
	[InlineData(0f, 0.75f)]
	[InlineData(0.5f, 0.75f)]
	[InlineData(1.25f, 1.25f)]
	[InlineData(3f, 2f)]
	[InlineData(float.NaN, 1f)]
	[InlineData(float.PositiveInfinity, 1f)]
	public void Clamp_BringsInterfaceScalesIntoTheSupportedRange(
		float typed,
		float expected)
	{
		Assert.Equal(
			expected,
			EditorSettings.ClampInterfaceScale(typed),
			3);
	}

	[Fact]
	public void Serialize_RoundTripsThroughDeserialize()
	{
		EditorSettings settings = new()
		{
			TargetFrameRate = 144,
			InterfaceScale = 1.25f
		};

		EditorSettings restored = EditorSettings.Deserialize(
			settings.Serialize());

		Assert.Equal(144, restored.TargetFrameRate);
		Assert.Equal(1.25f, restored.InterfaceScale, 3);
		Assert.Equal(EditorSettings.CurrentVersion, restored.Version);
	}

	[Fact]
	public void Deserialize_RejectsMalformedJson()
	{
		Assert.Throws<InvalidDataException>(
			() => EditorSettings.Deserialize("{ not json"));
	}

	[Fact]
	public void Deserialize_RejectsAnotherFormatVersion()
	{
		Assert.Throws<InvalidDataException>(
			() => EditorSettings.Deserialize(
				"{ \"version\": 99, \"targetFrameRate\": 144 }"));
	}

	[Fact]
	public void Session_TargetFrameRateFiresSettingsChanged()
	{
		using EditorSession session = new();

		int changed = 0;
		session.SettingsChanged += _ => changed++;

		Assert.Equal(60, session.TargetFrameRate);
		Assert.Equal(1000.0 / 60.0, session.TargetFrameMilliseconds, 6);

		session.TargetFrameRate = 144;

		Assert.Equal(144, session.TargetFrameRate);
		Assert.Equal(1000.0 / 144.0, session.TargetFrameMilliseconds, 6);
		Assert.Equal(1, changed);

		// Re-applying the same rate is not a change.
		session.TargetFrameRate = 144;

		Assert.Equal(1, changed);

		// A rate the editor cannot represent falls back to the default,
		// which is still a real change from 144.
		session.TargetFrameRate = 0;

		Assert.Equal(60, session.TargetFrameRate);
		Assert.Equal(2, changed);
	}
}
