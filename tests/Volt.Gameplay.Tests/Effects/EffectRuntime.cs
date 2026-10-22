using Volt.Ecs;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Effects;

/// <summary>
/// Owns a world and the gameplay stores layered on it, disposed in
/// reverse construction order.
/// </summary>
internal sealed class EffectRuntime : IDisposable
{
	public EffectRuntime()
	{
		World = new World();
		Attributes = new GameplayAttributeStore(World);
		Tags = new GameplayTagStore(World);
		Effects = new GameplayEffectSystem(World, Attributes, Tags);
	}

	public World World { get; }

	public GameplayAttributeStore Attributes { get; }

	public GameplayTagStore Tags { get; }

	public GameplayEffectSystem Effects { get; }

	public Entity CreateAffectedEntity()
	{
		return World.CreateEntity();
	}

	public void Dispose()
	{
		Effects.Dispose();
		Tags.Dispose();
		Attributes.Dispose();
		World.Dispose();
	}
}

/// <summary>
/// Records gameplay effect notifications in delivery order.
/// </summary>
internal sealed class EffectRecorder
{
	private readonly List<GameplayEffectChange> _changes = [];

	public EffectRecorder(GameplayEffectSystem effects)
	{
		effects.Changed += _changes.Add;
	}

	public IReadOnlyList<GameplayEffectChange> Changes => _changes;

	public int Count(GameplayEffectChangeKind kind)
	{
		int count = 0;

		foreach (GameplayEffectChange change in _changes)
		{
			if (change.Kind == kind)
			{
				count++;
			}
		}

		return count;
	}

	public GameplayEffectChange Last(GameplayEffectChangeKind kind)
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
}
