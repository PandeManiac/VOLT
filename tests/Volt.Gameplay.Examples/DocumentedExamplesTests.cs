using Xunit;

namespace Volt.Gameplay.Examples;

/// <summary>
/// Executes the documented examples so a snippet compiled against the
/// current API is also proven to do what the prose claims.
/// </summary>
public sealed class DocumentedExamplesTests
{
	[Fact]
	public void CreateServices() => DocumentedExamples.CreateServices();

	[Fact]
	public void RegisterAndApplyEffects() =>
		DocumentedExamples.RegisterAndApplyEffects();

	[Fact]
	public void DefineAnAbility() => DocumentedExamples.DefineAnAbility();

	[Fact]
	public void AuthorAConditionalPlan() =>
		DocumentedExamples.AuthorAConditionalPlan();

	[Fact]
	public void RegisterACustomTask() =>
		DocumentedExamples.RegisterACustomTask();

	[Fact]
	public void AuthorACompositeWait() =>
		DocumentedExamples.AuthorACompositeWait();

	[Fact]
	public void IntegrateWithTheFixedStep() =>
		DocumentedExamples.IntegrateWithTheFixedStep();

	[Fact]
	public void SubscribeAndBufferCues() =>
		DocumentedExamples.SubscribeAndBufferCues();

	[Fact]
	public void DriveCuesFromEffectsAndActivations() =>
		DocumentedExamples.DriveCuesFromEffectsAndActivations();
}
