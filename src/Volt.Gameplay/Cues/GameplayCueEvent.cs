using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Events;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Cues;

/// <summary>Immutable gameplay-to-presentation cue transition.</summary>
public readonly struct GameplayCueEvent
{
	public GameplayCueEvent(
		GameplayCueKind kind,
		GameplayTag tag,
		GameplayCueHandle handle = default,
		Entity source = default,
		Entity target = default,
		GameplayEventPayload parameters = default,
		float magnitude = 1f,
		int stackCount = 0,
		GameplayAbilityActivation activation = default,
		GameplayEffectHandle effect = default)
	{
		if (!float.IsFinite(magnitude))
		{
			throw new ArgumentOutOfRangeException(nameof(magnitude));
		}

		if (stackCount < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(stackCount));
		}

		Kind = kind;
		Tag = tag;
		Handle = handle;
		Source = source;
		Target = target;
		Parameters = parameters;
		Magnitude = magnitude;
		StackCount = stackCount;
		Activation = activation;
		Effect = effect;
	}

	/// <summary>Transition kind.</summary>
	public GameplayCueKind Kind { get; }
	/// <summary>Interned cue identity.</summary>
	public GameplayTag Tag { get; }
	/// <summary>Persistent identity; empty for execute cues.</summary>
	public GameplayCueHandle Handle { get; }
	/// <summary>Gameplay source identity, possibly default or destroyed.</summary>
	public Entity Source { get; }
	/// <summary>Gameplay target identity, possibly destroyed on removal.</summary>
	public Entity Target { get; }
	/// <summary>Optional stable typed value parameters.</summary>
	public GameplayEventPayload Parameters { get; }
	/// <summary>Optional finite presentation intensity.</summary>
	public float Magnitude { get; }
	/// <summary>Effect stack count, or zero for cues without stacks.</summary>
	public int StackCount { get; }
	/// <summary>Ability activation owner, when applicable.</summary>
	public GameplayAbilityActivation Activation { get; }
	/// <summary>Effect owner, when applicable; dead after removal.</summary>
	public GameplayEffectHandle Effect { get; }
}
