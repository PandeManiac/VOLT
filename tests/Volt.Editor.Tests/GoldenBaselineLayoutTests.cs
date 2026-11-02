namespace Volt.Editor.Tests;

/// <summary>
/// Covers where the golden capture files live.
/// </summary>
/// <remarks>
/// A capture the guide publishes is compared against the guide's own
/// image, so the same interface state is stored once. Keeping a copy
/// under the test baselines as well would make every interface change a
/// two-file edit, and the copies could disagree while each still looking
/// plausible.
/// </remarks>
public sealed class GoldenBaselineLayoutTests
{
	[Fact]
	public void BaselinesDoNotDuplicateAPublishedGuideImage()
	{
		string baselines = BaselinesDirectory();
		string images = Path.Combine(
			Directory.GetParent(baselines)!.Parent!.Parent!.FullName,
			"docs",
			"images");

		Assert.True(
			Directory.Exists(images),
			$"The guide image folder '{images}' must exist.");

		HashSet<string> published = [.. Directory
			.EnumerateFiles(images, "*.png")
			.Select(path => Path.GetFileName(path))];

		Assert.NotEmpty(published);

		List<string> duplicated = [.. Directory
			.EnumerateFiles(baselines, "*.png")
			.Select(path => Path.GetFileName(path))
			.Where(name => published.Contains(name))];

		Assert.True(
			duplicated.Count == 0,
			"These baselines duplicate a guide image; the guide's copy is " +
			$"the one the tests read: {string.Join(", ", duplicated)}");

		Assert.False(
			File.Exists(Path.Combine(baselines, "editor-default.png")),
			"The default workspace capture is compared against " +
			"docs/images/editor-workspace.png, so a private copy would " +
			"drift from it.");
	}

	private static string BaselinesDirectory()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (Directory.Exists(
				Path.Combine(directory.FullName, "src", "Volt.Ui")))
			{
				return Path.Combine(
					directory.FullName,
					"tests",
					"Volt.Editor.Tests",
					"Baselines");
			}

			directory = directory.Parent;
		}

		throw new InvalidOperationException(
			"Could not locate the repository root from the test output folder.");
	}
}
