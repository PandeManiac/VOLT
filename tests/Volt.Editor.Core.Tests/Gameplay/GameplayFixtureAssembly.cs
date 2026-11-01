using System.Runtime.CompilerServices;

namespace Volt.Editor.Core.Tests.Gameplay;

/// <summary>
/// Locates the built gameplay fixture assembly.
/// </summary>
/// <remarks>
/// The test project builds the fixture without referencing it for
/// compilation, so loading it exercises the same discovery path a
/// developer's project takes. The path is derived from the test output
/// directory rather than hardcoded, so Debug and Release both resolve.
/// </remarks>
internal static class GameplayFixtureAssembly
{
	public static string Path { get; } = Resolve();

	public static string Configuration =>
		new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name ?? "Release";

	private static string Resolve()
	{
		DirectoryInfo output = new(AppContext.BaseDirectory);
		string framework = output.Name;

		DirectoryInfo? root = output;

		while (root is not null &&
			!File.Exists(System.IO.Path.Combine(root.FullName, "Volt.slnx")))
		{
			root = root.Parent;
		}

		if (root is null)
		{
			throw new InvalidOperationException(
				"The repository root could not be found from " +
				$"'{AppContext.BaseDirectory}'.");
		}

		return System.IO.Path.Combine(
			root.FullName,
			"tests",
			"Volt.GameplayFixture",
			"bin",
			Configuration,
			framework,
			"Volt.GameplayFixture.dll");
	}
}
