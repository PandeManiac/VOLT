using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;
using Volt.Gameplay.Tasks;

namespace Volt.Gameplay.Tests.Abilities;

/// <summary>
/// Owns a world and the gameplay stores layered on it, disposed in
/// reverse construction order.
/// </summary>
internal sealed class AbilityRuntime : IDisposable
{
	public AbilityRuntime()
	{
		World = new World();
		Attributes = new GameplayAttributeStore(World);
		Tags = new GameplayTagStore(World);
		Effects = new GameplayEffectSystem(World, Attributes, Tags);
		Events = new GameplayEventSystem(World);
		Abilities = new GameplayAbilitySystem(
			World,
			Tags,
			Effects,
			Attributes,
			Events);
	}

	public World World { get; }

	public GameplayAttributeStore Attributes { get; }

	public GameplayTagStore Tags { get; }

	public GameplayEffectSystem Effects { get; }

	public GameplayEventSystem Events { get; }

	public GameplayAbilitySystem Abilities { get; }

	public GameplayAbilityTaskSystem Tasks => Abilities.Tasks;

	public Entity CreateActor()
	{
		return World.CreateEntity();
	}

	public void Dispose()
	{
		Abilities.Dispose();
		Effects.Dispose();
		Events.Dispose();
		Tags.Dispose();
		Attributes.Dispose();
		World.Dispose();
	}
}

/// <summary>
/// Records ability notifications in delivery order.
/// </summary>
internal sealed class AbilityRecorder
{
	private readonly List<GameplayAbilityChange> _changes = [];

	public AbilityRecorder(GameplayAbilitySystem abilities)
	{
		abilities.Changed += _changes.Add;
	}

	public IReadOnlyList<GameplayAbilityChange> Changes => _changes;

	public int Count(GameplayAbilityChangeKind kind)
	{
		int count = 0;

		foreach (GameplayAbilityChange change in _changes)
		{
			if (change.Kind == kind)
			{
				count++;
			}
		}

		return count;
	}

	public GameplayAbilityChange Last(GameplayAbilityChangeKind kind)
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
