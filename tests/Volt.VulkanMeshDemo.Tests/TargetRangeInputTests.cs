using Volt.Rendering.Cameras;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class TargetRangeInputTests
{
	private static readonly CameraInputSnapshot Idle = new(
		KeyboardFocused: true,
		FireRequested: false,
		RestartRequested: false,
		Movement: CameraMovementInput.None);

	[Fact]
	public void FromInput_WithoutEdges_RequestsNothing()
	{
		Assert.Equal(
			TargetRangeCommand.None,
			TargetRangeCommand.FromInput(Idle));
	}

	[Fact]
	public void FromInput_FireEdge_FiresOnce()
	{
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with { FireRequested = true });

		Assert.True(command.Fire);
		Assert.False(command.Restart);
	}

	[Fact]
	public void FromInput_RestartEdge_Restarts()
	{
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with { RestartRequested = true });

		Assert.False(command.Fire);
		Assert.True(command.Restart);
	}

	[Fact]
	public void FromInput_RestartAndFireTogether_DropsTheShot()
	{
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with
			{
				FireRequested = true,
				RestartRequested = true
			});

		Assert.True(command.Restart);
		Assert.False(command.Fire);
	}

	[Fact]
	public void FromInput_LosingFocus_RequestsNothing()
	{
		// The sampler already suppresses both edges without focus; this
		// pins the contract that unfocused input cannot score or reset.
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with { KeyboardFocused = false });

		Assert.False(command.Fire);
		Assert.False(command.Restart);
	}

	[Fact]
	public void FromInput_UnfocusedFireEdge_RequestsNothing()
	{
		// A malformed or synthesized snapshot must not be able to shoot
		// from a window that does not own the keyboard.
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with
			{
				KeyboardFocused = false,
				FireRequested = true
			});

		Assert.Equal(TargetRangeCommand.None, command);
		Assert.False(command.Fire);
		Assert.False(command.Restart);
	}

	[Fact]
	public void FromInput_UnfocusedRestartEdge_RequestsNothing()
	{
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with
			{
				KeyboardFocused = false,
				RestartRequested = true
			});

		Assert.Equal(TargetRangeCommand.None, command);
		Assert.False(command.Fire);
		Assert.False(command.Restart);
	}

	[Fact]
	public void FromInput_UnfocusedSnapshotWithBothEdges_RequestsNothing()
	{
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with
			{
				KeyboardFocused = false,
				FireRequested = true,
				RestartRequested = true
			});

		Assert.Equal(TargetRangeCommand.None, command);
		Assert.False(command.Fire);
		Assert.False(command.Restart);
	}

	[Fact]
	public void FromInput_FocusedSnapshotWithBothEdges_StillRestarts()
	{
		// The focus guard must not change the focused precedence rule.
		TargetRangeCommand command = TargetRangeCommand.FromInput(
			Idle with
			{
				FireRequested = true,
				RestartRequested = true
			});

		Assert.True(command.Restart);
		Assert.False(command.Fire);
	}
}
