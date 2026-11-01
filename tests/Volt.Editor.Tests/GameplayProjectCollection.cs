namespace Volt.Editor.Tests;

/// <summary>
/// Tests that build and play a scaffolded project, run one at a time.
/// </summary>
/// <remarks>
/// Component registration is process-wide and keyed by the project's stable
/// type id, so two tests that load their own scaffolded assembly at the same
/// moment can replace each other's catalog entry. That is not what an
/// editor does - it loads one project's code - but it is exactly what a
/// parallel test run does, and the symptom is a system that sees no
/// entities. Sharing a collection makes the tests serialize instead of
/// making the failure look like a gameplay bug.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class GameplayProjectCollection
{
	public const string Name = "Gameplay project";
}
