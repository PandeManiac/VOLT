using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Lifecycle.Stress;

/// <summary>
/// A deliberately simple oracle for the harness schema.
/// </summary>
/// <remarks>
/// <para>
/// The model stores only what the public contract promises: counted
/// tags, one additive attribute, one modifier per effect instance,
/// stack counts, inhibition, grants, and active ability state. It never
/// calls a gameplay store to decide what the answer should be. The
/// harness feeds it committed notifications and explicit operations,
/// then compares the public query surface against it.
/// </para>
/// <para>
/// Effects are modelled through their observable transitions rather
/// than by reproducing the effect system's linked lists, slots, or due
/// heap. That is what makes it an independent check of ownership and
/// counter bookkeeping.
/// </para>
/// </remarks>
internal sealed class LifecycleReferenceModel
{
	private readonly Dictionary<Entity, ReferenceEntity> _entities = [];
	private readonly List<ReferenceEntity> _living = [];
	private readonly Dictionary<GameplayEffectHandle, ReferenceEffect> _effects = [];
	private readonly Dictionary<GameplayAbilityHandle, ReferenceAbility> _abilities = [];
	private readonly Dictionary<GameplayModifierHandle, ReferenceModifier> _modifiers = [];

	public IReadOnlyList<ReferenceEntity> Living => _living;

	public IEnumerable<ReferenceEffect> Effects => _effects.Values;

	public IEnumerable<ReferenceAbility> Abilities => _abilities.Values;

	public int ActiveEffectCount => _effects.Count;

	/// <summary>
	/// Gets the number of active instances that must currently own a
	/// persistent presentation cue.
	/// </summary>
	/// <remarks>
	/// Cue ownership follows the instance, not the tag: an instant
	/// effect delivers a one-shot cue without keeping state, and an
	/// inhibited instance withdraws its cue until it resumes.
	/// </remarks>
	public int ExpectedCueCount
	{
		get
		{
			int count = 0;

			foreach (ReferenceEffect effect in _effects.Values)
			{
				if (effect.CueActive)
				{
					count++;
				}
			}

			return count;
		}
	}

	public int GrantedAbilityCount => _abilities.Count;

	public int ActiveAbilityCount
	{
		get
		{
			int count = 0;

			foreach (ReferenceAbility ability in _abilities.Values)
			{
				if (ability.Active)
				{
					count++;
				}
			}

			return count;
		}
	}

	public int TaggedEntityCount
	{
		get
		{
			int count = 0;

			foreach (ReferenceEntity entity in _living)
			{
				if (entity.HasAnyTag)
				{
					count++;
				}
			}

			return count;
		}
	}

	public int AttributedEntityCount
	{
		get
		{
			int count = 0;

			foreach (ReferenceEntity entity in _living)
			{
				if (entity.HasAttributeState)
				{
					count++;
				}
			}

			return count;
		}
	}

	public void AddEntity(Entity entity)
	{
		if (_entities.ContainsKey(entity))
		{
			throw new InvalidOperationException(
				$"The model already contains {entity}.");
		}

		ReferenceEntity model = new(entity);

		_entities.Add(entity, model);
		_living.Add(model);
	}

	public RemovedEntityState RemoveEntity(Entity entity)
	{
		if (!_entities.Remove(entity, out ReferenceEntity? model))
		{
			return default;
		}

		_living.Remove(model);

		GameplayEffectHandle[] effects = model.Effects
			.Select(effect => effect.Handle)
			.ToArray();

		GameplayAbilityHandle[] abilities = model.Abilities
			.Select(ability => ability.Handle)
			.ToArray();

		GameplayModifierHandle[] modifiers = model.Modifiers
			.Select(modifier => modifier.Handle)
			.ToArray();

		foreach (ReferenceEffect effect in model.Effects)
		{
			_effects.Remove(effect.Handle);
		}

		foreach (ReferenceAbility ability in model.Abilities)
		{
			_abilities.Remove(ability.Handle);
		}

		foreach (ReferenceModifier modifier in model.Modifiers)
		{
			_modifiers.Remove(modifier.Handle);
		}

		model.Effects.Clear();
		model.Abilities.Clear();
		model.Modifiers.Clear();
		model.ClearTags();
		model.HasAttributeState = false;

		return new RemovedEntityState(entity, effects, abilities, modifiers);
	}

	public ReferenceEntity GetEntity(Entity entity)
	{
		if (!_entities.TryGetValue(entity, out ReferenceEntity? model))
		{
			throw new InvalidOperationException(
				$"The model does not contain live entity {entity}.");
		}

		return model;
	}

	public bool TryGetEntity(Entity entity, out ReferenceEntity? model)
	{
		return _entities.TryGetValue(entity, out model);
	}

	public void AddTag(Entity entity, GameplayTag tag)
	{
		GetEntity(entity).AddTag(tag);
	}

	public bool RemoveTag(Entity entity, GameplayTag tag)
	{
		return GetEntity(entity).RemoveTag(tag);
	}

	public bool ClearTags(Entity entity)
	{
		return GetEntity(entity).ClearTags();
	}

	public void SetBaseValue(Entity entity, float value)
	{
		ReferenceEntity model = GetEntity(entity);

		model.BaseValue = value;
		model.HasAttributeState = true;
	}

	public void AddDirectModifier(
		Entity entity,
		GameplayModifierHandle handle,
		float magnitude)
	{
		ReferenceEntity model = GetEntity(entity);
		ReferenceModifier modifier = new(handle, magnitude);

		model.Modifiers.Add(modifier);
		model.HasAttributeState = true;

		_modifiers.Add(handle, modifier);
	}

	public bool RemoveDirectModifier(GameplayModifierHandle handle)
	{
		if (!_modifiers.Remove(handle, out ReferenceModifier? modifier))
		{
			return false;
		}

		if (_entities.TryGetValue(handle.Entity, out ReferenceEntity? entity))
		{
			entity.Modifiers.Remove(modifier);
		}

		return true;
	}

	public void ClearAttributes(Entity entity)
	{
		ReferenceEntity model = GetEntity(entity);

		model.BaseValue = LifecycleStressSchema.Score.DefaultBaseValue;
		model.HasAttributeState = false;
		model.Modifiers.Clear();

		foreach (ReferenceEffect effect in model.Effects)
		{
			// Clearing the attribute removes the storage the effect's
			// modifier handles pointed at. The effect remains active,
			// but those modifiers are detached until the effect is
			// inhibited and restored again.
			effect.ModifiersAttached = false;
		}
	}

	public void AddEffect(
		GameplayEffectHandle handle,
		GameplayEffectDefinition definition,
		Entity target,
		int stackCount,
		bool inhibited)
	{
		ReferenceEntity entity = GetEntity(target);
		ReferenceEffect effect = new(
			handle,
			definition,
			target,
			stackCount,
			inhibited);

		entity.Effects.Add(effect);
		_effects.Add(handle, effect);

		if (!inhibited)
		{
			effect.TagsGranted = true;
			GrantTags(target, definition.GrantedTags);

			if (definition.Modifiers.Length > 0)
			{
				effect.ModifiersAttached = true;
				entity.HasAttributeState = true;
			}
		}
	}

	public void StackEffect(GameplayEffectHandle handle, int stackCount)
	{
		ReferenceEffect effect = GetEffect(handle);

		effect.StackCount = stackCount;
	}

	public void InhibitEffect(GameplayEffectHandle handle)
	{
		ReferenceEffect effect = GetEffect(handle);

		if (effect.Inhibited)
		{
			return;
		}

		effect.Inhibited = true;

		if (effect.TagsGranted)
		{
			RemoveTags(effect.Target, effect.Definition.GrantedTags);
			effect.TagsGranted = false;
		}

		effect.ModifiersAttached = false;
	}

	public void UninhibitEffect(GameplayEffectHandle handle)
	{
		ReferenceEffect effect = GetEffect(handle);

		if (!effect.Inhibited)
		{
			return;
		}

		effect.Inhibited = false;
		effect.TagsGranted = true;

		GrantTags(effect.Target, effect.Definition.GrantedTags);

		if (effect.Definition.Modifiers.Length > 0)
		{
			effect.ModifiersAttached = true;
			GetEntity(effect.Target).HasAttributeState = true;
		}
	}

	public void RemoveEffect(GameplayEffectHandle handle)
	{
		if (!_effects.Remove(handle, out ReferenceEffect? effect))
		{
			return;
		}

		if (effect.TagsGranted)
		{
			RemoveTags(effect.Target, effect.Definition.GrantedTags);
			effect.TagsGranted = false;
		}

		if (_entities.TryGetValue(effect.Target, out ReferenceEntity? entity))
		{
			entity.Effects.Remove(effect);
		}
	}

	public bool TryGetEffect(
		GameplayEffectHandle handle,
		out ReferenceEffect? effect)
	{
		return _effects.TryGetValue(handle, out effect);
	}

	public ReferenceEffect GetEffect(GameplayEffectHandle handle)
	{
		if (!_effects.TryGetValue(handle, out ReferenceEffect? effect))
		{
			throw new InvalidOperationException(
				$"The model does not contain effect {handle}.");
		}

		return effect;
	}

	public void AddAbility(
		GameplayAbilityHandle handle,
		GameplayAbilityDefinition definition,
		Entity owner)
	{
		ReferenceEntity entity = GetEntity(owner);
		ReferenceAbility ability = new(handle, definition, owner);

		entity.Abilities.Add(ability);
		_abilities.Add(handle, ability);
	}

	public void RemoveAbility(GameplayAbilityHandle handle)
	{
		if (!_abilities.Remove(handle, out ReferenceAbility? ability))
		{
			return;
		}

		if (_entities.TryGetValue(ability.Owner, out ReferenceEntity? entity))
		{
			entity.Abilities.Remove(ability);
		}
	}

	public ReferenceAbility GetAbility(GameplayAbilityHandle handle)
	{
		if (!_abilities.TryGetValue(handle, out ReferenceAbility? ability))
		{
			throw new InvalidOperationException(
				$"The model does not contain ability {handle}.");
		}

		return ability;
	}

	public void ActivateAbility(GameplayAbilityHandle handle, Entity target)
	{
		ReferenceAbility ability = GetAbility(handle);

		ability.Active = true;
		ability.Target = target;
	}

	public void DeactivateAbility(GameplayAbilityHandle handle)
	{
		GetAbility(handle).Active = false;
	}

	public int TagCount(Entity entity, GameplayTag tag)
	{
		return _entities.TryGetValue(entity, out ReferenceEntity? model)
			? model.TagCount(tag)
			: 0;
	}

	public int UnownedTagCount(Entity entity, GameplayTag tag)
	{
		return _entities.TryGetValue(entity, out ReferenceEntity? model)
			? model.UnownedTagCount(tag)
			: 0;
	}

	public bool HasTag(Entity entity, GameplayTag tag)
	{
		return TagCount(entity, tag) > 0;
	}

	public float ExpectedCurrentValue(Entity entity)
	{
		ReferenceEntity model = GetEntity(entity);
		float value = model.BaseValue;

		foreach (ReferenceModifier modifier in model.Modifiers)
		{
			value += modifier.Magnitude;
		}

		foreach (ReferenceEffect effect in model.Effects)
		{
			if (effect.Inhibited || !effect.ModifiersAttached)
			{
				continue;
			}

			ReadOnlySpan<GameplayEffectModifier> modifiers =
				effect.Definition.Modifiers;

			for (int index = 0; index < modifiers.Length; index++)
			{
				GameplayEffectModifier modifier = modifiers[index];

				if (modifier.Operation != GameplayModifierOperation.AddBase)
				{
					throw new InvalidOperationException(
						"The stress schema only supports additive effect " +
						"modifiers.");
				}

				float magnitude = effect.Definition.FactorInStackCount
					? modifier.Magnitude * effect.StackCount
					: modifier.Magnitude;

				value += magnitude;
			}
		}

		return value;
	}

	public int ExpectedModifierCount(Entity entity)
	{
		ReferenceEntity model = GetEntity(entity);
		int count = model.Modifiers.Count;

		foreach (ReferenceEffect effect in model.Effects)
		{
			if (!effect.Inhibited
				&& effect.ModifiersAttached
				&& effect.Definition.Modifiers.Length > 0)
			{
				count++;
			}
		}

		return count;
	}

	private void GrantTags(Entity entity, GameplayTagSet tags)
	{
		foreach (GameplayTag tag in tags.Tags)
		{
			GetEntity(entity).GrantOwnedTag(tag);
		}
	}

	private void RemoveTags(Entity entity, GameplayTagSet tags)
	{
		if (!_entities.TryGetValue(entity, out ReferenceEntity? model))
		{
			return;
		}

		foreach (GameplayTag tag in tags.Tags)
		{
			model.ReleaseOwnedTag(tag);
		}
	}
}

internal sealed class ReferenceEntity(Entity entity)
{
	private readonly Dictionary<GameplayTag, int> _unowned = [];
	private readonly Dictionary<GameplayTag, int> _owned = [];

	public Entity Entity { get; } = entity;

	public float BaseValue { get; set; } =
		LifecycleStressSchema.Score.DefaultBaseValue;

	public bool HasAttributeState { get; set; }

	public List<ReferenceEffect> Effects { get; } = [];

	public List<ReferenceAbility> Abilities { get; } = [];

	public List<ReferenceModifier> Modifiers { get; } = [];

	public bool HasAnyTag => _unowned.Count > 0 || _owned.Count > 0;

	public int TagCount(GameplayTag tag)
	{
		return UnownedTagCount(tag) + OwnedTagCount(tag);
	}

	public int UnownedTagCount(GameplayTag tag)
	{
		return _unowned.TryGetValue(tag, out int count) ? count : 0;
	}

	public int OwnedTagCount(GameplayTag tag)
	{
		return _owned.TryGetValue(tag, out int count) ? count : 0;
	}

	public void AddTag(GameplayTag tag)
	{
		_unowned[tag] = UnownedTagCount(tag) + 1;
	}

	public bool RemoveTag(GameplayTag tag)
	{
		int count = UnownedTagCount(tag);

		if (count == 0)
		{
			return false;
		}

		if (count == 1)
		{
			_unowned.Remove(tag);
		}
		else
		{
			_unowned[tag] = count - 1;
		}

		return true;
	}

	public void GrantOwnedTag(GameplayTag tag)
	{
		_owned[tag] = OwnedTagCount(tag) + 1;
	}

	public void ReleaseOwnedTag(GameplayTag tag)
	{
		int count = OwnedTagCount(tag);

		if (count <= 1)
		{
			_owned.Remove(tag);
		}
		else
		{
			_owned[tag] = count - 1;
		}
	}

	public bool ClearTags()
	{
		bool removed = _unowned.Count > 0;

		_unowned.Clear();

		return removed;
	}
}

internal sealed class ReferenceEffect(
	GameplayEffectHandle handle,
	GameplayEffectDefinition definition,
	Entity target,
	int stackCount,
	bool inhibited)
{
	public GameplayEffectHandle Handle { get; } = handle;

	public GameplayEffectDefinition Definition { get; } = definition;

	public Entity Target { get; } = target;

	public int StackCount { get; set; } = stackCount;

	public bool Inhibited { get; set; } = inhibited;

	public bool TagsGranted { get; set; }

	public bool ModifiersAttached { get; set; }

	/// <summary>
	/// Gets whether this instance should own a persistent cue right
	/// now.
	/// </summary>
	public bool CueActive => !Inhibited
		&& Definition.CueTag.IsValid
		&& !Definition.IsInstant;
}

internal sealed class ReferenceAbility(
	GameplayAbilityHandle handle,
	GameplayAbilityDefinition definition,
	Entity owner)
{
	public GameplayAbilityHandle Handle { get; } = handle;

	public GameplayAbilityDefinition Definition { get; } = definition;

	public Entity Owner { get; } = owner;

	public bool Active { get; set; }

	public Entity Target { get; set; }
}

internal sealed record ReferenceModifier(
	GameplayModifierHandle Handle,
	float Magnitude);

internal readonly record struct RemovedEntityState(
	Entity Entity,
	GameplayEffectHandle[] Effects,
	GameplayAbilityHandle[] Abilities,
	GameplayModifierHandle[] Modifiers);
