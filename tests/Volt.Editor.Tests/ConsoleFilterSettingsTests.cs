namespace Volt.Editor.Tests;

/// <summary>
/// Covers the Console filter as a session setting: what it hides, that a
/// change is announced, and that it survives a workspace save and load.
/// </summary>
public sealed class ConsoleFilterSettingsTests
{
	[Fact]
	public void SeverityAndSourceFiltersAreIndependent()
	{
		EditorConsoleFilter filter = new(
			ShowInfo: false,
			ShowWarnings: true,
			ShowErrors: true,
			Source: EditorLogSource.Game);

		Assert.False(filter.Shows(EditorLogLevel.Info, EditorLogSource.Game));
		Assert.True(filter.Shows(EditorLogLevel.Warning, EditorLogSource.Game));
		Assert.True(filter.Shows(EditorLogLevel.Error, EditorLogSource.Game));
		Assert.False(filter.Shows(EditorLogLevel.Error, EditorLogSource.Editor));
	}

	[Fact]
	public void TheDefaultFilterShowsEverything()
	{
		EditorConsoleFilter filter = EditorConsoleFilter.Default;

		foreach (EditorLogLevel level in Enum.GetValues<EditorLogLevel>())
		{
			foreach (EditorLogSource source in Enum.GetValues<EditorLogSource>())
			{
				Assert.True(filter.Shows(level, source));
			}
		}
	}

	[Fact]
	public void TheSessionOwnsTheFilterAndAnnouncesChanges()
	{
		using EditorSession session = new();
		int changes = 0;

		session.SettingsChanged += _ => changes++;

		session.ConsoleFilter = new EditorConsoleFilter(ShowInfo: false);

		Assert.Equal(1, changes);
		Assert.False(session.ConsoleFilter.ShowInfo);

		// Setting the same value again is not a change.
		session.ConsoleFilter = new EditorConsoleFilter(ShowInfo: false);
		Assert.Equal(1, changes);
	}

	[Fact]
	public void TheFilterSurvivesASaveAndLoad()
	{
		EditorSettings settings = new()
		{
			ConsoleShowInfo = false,
			ConsoleShowWarnings = true,
			ConsoleShowErrors = false,
			ConsoleSource = "validation"
		};

		EditorSettings restored = EditorSettings.Deserialize(
			settings.Serialize());

		Assert.False(restored.ConsoleShowInfo);
		Assert.True(restored.ConsoleShowWarnings);
		Assert.False(restored.ConsoleShowErrors);
		Assert.Equal("validation", restored.ConsoleSource);

		EditorConsoleFilter filter =
			EditorConsoleFilter.FromSettings(restored);

		Assert.False(filter.ShowInfo);
		Assert.True(filter.ShowWarnings);
		Assert.False(filter.ShowErrors);
		Assert.Equal(EditorLogSource.Validation, filter.Source);
	}

	[Fact]
	public void AnUnknownSourceFallsBackToAll()
	{
		EditorSettings settings = EditorSettings.Deserialize(
			"""
			{
			  "version": 1,
			  "consoleSource": "telepathy"
			}
			""");

		Assert.Equal("all", settings.ConsoleSource);

		EditorConsoleFilter filter =
			EditorConsoleFilter.FromSettings(settings);

		Assert.Null(filter.Source);
	}

	[Fact]
	public void OmittedConsoleSettingsDefaultToShowingEverything()
	{
		EditorSettings settings = EditorSettings.Deserialize(
			"""
			{
			  "version": 1
			}
			""");

		Assert.True(settings.ConsoleShowInfo);
		Assert.True(settings.ConsoleShowWarnings);
		Assert.True(settings.ConsoleShowErrors);
		Assert.Equal("all", settings.ConsoleSource);
	}

	[Fact]
	public void EverySourceRoundTripsThroughItsSettingSpelling()
	{
		foreach (EditorLogSource source in Enum.GetValues<EditorLogSource>())
		{
			EditorSettings settings = new()
			{
				ConsoleSource = new EditorConsoleFilter(Source: source)
					.SourceSetting
			};

			EditorConsoleFilter filter =
				EditorConsoleFilter.FromSettings(settings.Normalize());

			Assert.Equal(source, filter.Source);
		}
	}
}
