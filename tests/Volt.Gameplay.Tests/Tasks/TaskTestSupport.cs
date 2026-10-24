using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Tasks;
using Volt.Gameplay.Tests.Abilities;

namespace Volt.Gameplay.Tests.Tasks;

internal static class TaskTestSupport
{
	public static GameplayAbilityActivation Activate(
		AbilityRuntime runtime,
		Entity actor,
		GameplayAbilityDefinition definition,
		out GameplayAbilityHandle handle)
	{
		handle = runtime.Abilities.GiveAbility(actor, definition);

		GameplayAbilityActivationResult result =
			runtime.Abilities.TryActivate(handle);

		Assert.True(result.Succeeded);
		Assert.False(result.Activation.IsNone);

		return result.Activation;
	}

	public static GameplayAbilityActivation ActivateChannel(
		AbilityRuntime runtime,
		Entity actor,
		out GameplayAbilityHandle handle)
	{
		return Activate(
			runtime,
			actor,
			AbilitySchema.Channel,
			out handle);
	}
}

internal sealed class TaskRecorder
{
	private readonly List<GameplayAbilityTaskChange> _changes = [];

	public TaskRecorder(GameplayAbilityTaskSystem tasks)
	{
		tasks.Changed += _changes.Add;
	}

	public IReadOnlyList<GameplayAbilityTaskChange> Changes => _changes;

	public int Count(GameplayAbilityTaskChangeKind kind)
	{
		int count = 0;

		foreach (GameplayAbilityTaskChange change in _changes)
		{
			if (change.Kind == kind)
			{
				count++;
			}
		}

		return count;
	}

	public GameplayAbilityTaskChange Last(GameplayAbilityTaskChangeKind kind)
	{
		for (int index = _changes.Count - 1; index >= 0; index--)
		{
			if (_changes[index].Kind == kind)
			{
				return _changes[index];
			}
		}

		throw new InvalidOperationException(
			$"No {kind} notification was recorded.");
	}

	public string Sequence()
	{
		return string.Join(
			",",
			_changes.Select(change => change.Kind.ToString()));
	}
}
