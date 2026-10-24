using System.Text;
using Volt.Ecs;
using Volt.Gameplay.Abilities;
using Volt.Gameplay.Attributes;
using Volt.Gameplay.Cues;
using Volt.Gameplay.Effects;
using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Lifecycle.Stress;

public enum StressNotificationBoundary
{
	TagsChanged,
	AttributeChanged,
	EffectChanged,
	AbilityChanged
}

internal sealed record StressOptions
{
	public ulong Seed { get; init; }

	public int Operations { get; init; } = 250;

	public bool DestroyCallbackBeforeStores { get; init; } = true;

	public int ExceptionProbeOperation { get; init; } = -1;

	public StressNotificationBoundary ExceptionBoundary { get; init; } =
		StressNotificationBoundary.EffectChanged;
}

internal readonly record struct StressTraceEntry(
	int Index,
	int Depth,
	string Operation,
	string Outcome)
{
	public override string ToString()
	{
		string indent = new(' ', Depth * 2);

		return $"{Index,4} {indent}{Operation} => {Outcome}";
	}
}

internal sealed record StressRunResult(
	ulong Seed,
	int OperationsExecuted,
	int InjectedExceptions,
	string TraceDigest,
	IReadOnlyList<StressTraceEntry> Trace)
{
	public string Describe()
	{
		return $"seed {Seed}, {OperationsExecuted} operations, " +
			$"{InjectedExceptions} injected exception(s), trace {TraceDigest}";
	}
}

internal sealed class StressInjectedException(string boundary)
	: Exception($"The stress harness injected an exception at {boundary}.")
{
	public string Boundary { get; } = boundary;
}

internal sealed class LifecycleStressFailureException : Exception
{
	public LifecycleStressFailureException(
		ulong seed,
		int operation,
		int traceIndex,
		string state,
		IReadOnlyList<StressTraceEntry> trace,
		string message,
		Exception? innerException = null)
		: base(message, innerException)
	{
		Seed = seed;
		Operation = operation;
		TraceIndex = traceIndex;
		State = state;
		Trace = trace;
	}

	public ulong Seed { get; }

	public int Operation { get; }

	public int TraceIndex { get; }

	public string State { get; }

	public IReadOnlyList<StressTraceEntry> Trace { get; }
}

/// <summary>
/// Runs a deterministic, seed-replayable state machine over every
/// gameplay store.
/// </summary>
/// <remarks>
/// <para>
/// Operations are generated at the top level and from committed
/// notifications. A single <see cref="SplitMix64"/> stream drives both,
/// so replaying a seed and an operation limit reproduces the same
/// callback choices. The one exception is an explicit exception probe,
/// which is selected by operation index and boundary rather than by
/// chance.
/// </para>
/// <para>
/// Invariants are checked after every top-level operation. Checks are
/// deferred while callbacks are nested because a store is allowed to be
/// in the middle of one committed transition; the deferral point is the
/// return of the outermost mutation, not the end of the run.
/// </para>
/// <para>
/// Tag callbacks are not selected while an effect or ability call is
/// mutating the stores. Effect grants and withdrawals raise tag
/// notifications midway through their own transition, so allowing a
/// callback to mutate tags there would compare against a model that has
/// not observed the enclosing effect change yet. Effect, attribute, and
/// ability notifications still generate callbacks.
/// </para>
/// </remarks>
internal sealed class LifecycleStressHarness : IDisposable
{
	private const int MaxCallbackDepth = 2;
	private const int MaxCallbackOperationsPerTopLevel = 8;
	private const int MaxReplacementEntities = 16;
	private const int StaleHistoryLimit = 48;

	private readonly StressOptions _options;
	private readonly LifecycleReferenceModel _model = new();
	private readonly List<StressTraceEntry> _trace = [];
	private readonly List<GameplayEffectHandle> _staleEffects = [];
	private readonly List<GameplayAbilityHandle> _staleAbilities = [];
	private readonly List<GameplayModifierHandle> _staleModifiers = [];
	private readonly List<Entity> _staleEntities = [];
	private readonly GameplayCueBuffer _cueBuffer = new(128, 65536);

	private SplitMix64 _random;
	private int _traceSequence;
	private int _topLevelOperation = -1;
	private int _callDepth;
	private int _callbackBudget;
	private int _storeMutationDepth;
	private int _callbacksSuppressed;
	private int _injectedExceptions;
	private bool _probeActive;
	private bool _entityDestructionInProgress;
	private bool _probeFired;
	private StressNotificationBoundary _probeBoundary;

	public LifecycleStressHarness(StressOptions options)
	{
		_options = options;
		_random = new SplitMix64(options.Seed);

		World = new World();

		if (options.DestroyCallbackBeforeStores)
		{
			World.EntityDestroyed += HandleEntityDestroyed;
		}

		Attributes = new GameplayAttributeStore(World);
		Tags = new GameplayTagStore(World);
		Effects = new GameplayEffectSystem(World, Attributes, Tags);
		Cues = new GameplayCueSystem(World);
		Cues.BindEffects(Effects);
		Cues.PresentationBuffer = _cueBuffer;
		Abilities = new GameplayAbilitySystem(World, Tags, Effects, Attributes);

		if (!options.DestroyCallbackBeforeStores)
		{
			World.EntityDestroyed += HandleEntityDestroyed;
		}

		Attributes.AttributeChanged += HandleAttributeChanged;
		Tags.TagsChanged += HandleTagsChanged;
		Effects.Changed += HandleEffectChanged;
		Cues.Changed += HandleCueChanged;
		Abilities.Changed += HandleAbilityChanged;

		SeedInitialState();
	}

	public World World { get; }

	public GameplayAttributeStore Attributes { get; }

	public GameplayTagStore Tags { get; }

	public GameplayEffectSystem Effects { get; }

	public GameplayCueSystem Cues { get; }

	public GameplayAbilitySystem Abilities { get; }

	public StressRunResult Run()
	{
		for (int operation = 0; operation < _options.Operations; operation++)
		{
			_topLevelOperation = operation;
			_callbackBudget = MaxCallbackOperationsPerTopLevel;
			_callDepth = 0;
			Trace($"op:{operation}", "begin");

			try
			{
				if (operation == _options.ExceptionProbeOperation)
				{
					RunExceptionProbe();
				}
				else
				{
					RunTopLevelOperation();
				}
			}
			catch (StressInjectedException injected)
			{
				_injectedExceptions++;
				Trace(
					$"inject:{injected.Boundary}",
					"propagated");
			}
			catch (LifecycleStressFailureException)
			{
				throw;
			}
			catch (Exception exception)
			{
				throw CreateFailure(
					$"Unexpected {exception.GetType().Name} from operation: " +
					exception.Message,
					exception);
			}

			try
			{
				Verify();
			}
			catch (LifecycleStressFailureException)
			{
				throw;
			}
			catch (Exception exception)
			{
				throw CreateFailure(
					$"Invariant check threw {exception.GetType().Name}: " +
					exception.Message,
					exception);
			}
		}

		return new StressRunResult(
			_options.Seed,
			_options.Operations,
			_injectedExceptions,
			ComputeTraceDigest(),
			_trace.ToArray());
	}

	/// <summary>
	/// Re-runs only the prefix through the first failing operation.
	/// </summary>
	/// <remarks>
	/// The generator is state-dependent, so an operation cannot simply
	/// be deleted from the middle of a trace. The shortest faithful
	/// replay is the prefix that still contains the first failing
	/// invariant. This method returns that limit when a failure is
	/// deterministic; arbitrary operation deletion would need saved
	/// store snapshots and is intentionally out of scope.
	/// </remarks>
	public static StressResultSummary ReplayFailingPrefix(
		StressOptions options,
		int failingOperation)
	{
		StressOptions replay = options with
		{
			Operations = failingOperation + 1,
			ExceptionProbeOperation = options.ExceptionProbeOperation
				<= failingOperation
				? options.ExceptionProbeOperation
				: -1
		};

		try
		{
			using LifecycleStressHarness harness = new(replay);
			StressRunResult result = harness.Run();

			return new StressResultSummary(
				replay,
				false,
				result.Describe(),
				null);
		}
		catch (LifecycleStressFailureException failure)
		{
			return new StressResultSummary(
				replay,
				true,
				failure.Message,
				failure);
		}
	}

	public void Dispose()
	{
		World.EntityDestroyed -= HandleEntityDestroyed;
		Attributes.AttributeChanged -= HandleAttributeChanged;
		Tags.TagsChanged -= HandleTagsChanged;
		Effects.Changed -= HandleEffectChanged;
		Cues.Changed -= HandleCueChanged;
		Abilities.Changed -= HandleAbilityChanged;

		Abilities.Dispose();
		Cues.PresentationBuffer = null;
		Cues.Dispose();
		_cueBuffer.Dispose();
		Effects.Dispose();
		Tags.Dispose();
		Attributes.Dispose();
		World.Dispose();
	}

	private void SeedInitialState()
	{
		_callbacksSuppressed++;

		try
		{
			for (int index = 0; index < 3; index++)
			{
				Entity entity = CreateEntityCore();

				SetBase(
					entity,
					_random.NextInt(121) + 20f);

				if (_random.Chance(60))
				{
					AddTag(entity, _random.Pick(LifecycleStressSchema.Tags));
				}

				if (_random.Chance(60))
				{
					ApplyEffect(entity, _random.Pick(LifecycleStressSchema.Effects));
				}

				if (_random.Chance(50))
				{
					GiveAbility(entity, _random.Pick(LifecycleStressSchema.Abilities));
				}
			}
		}
		finally
		{
			_callbacksSuppressed--;
		}

		_trace.Clear();
		_traceSequence = 0;
		_topLevelOperation = -1;
	}

	private Entity CreateEntityCore()
	{
		Entity entity = World.CreateEntity();

		_model.AddEntity(entity);

		return entity;
	}

	private void RunTopLevelOperation()
	{
		int choice = _random.NextInt(182);

		Trace("dispatch", choice.ToString());

		if (choice < 10)
		{
			ExecuteCreateEntity();
		}
		else if (choice < 22)
		{
			ExecuteDestroyEntity();
		}
		else if (choice < 36)
		{
			ExecuteAddTag();
		}
		else if (choice < 46)
		{
			ExecuteRemoveTag();
		}
		else if (choice < 50)
		{
			ExecuteClearTags();
		}
		else if (choice < 62)
		{
			ExecuteSetBase();
		}
		else if (choice < 69)
		{
			ExecuteAddModifier();
		}
		else if (choice < 76)
		{
			ExecuteRemoveModifier();
		}
		else if (choice < 80)
		{
			ExecuteClearAttributes();
		}
		else if (choice < 98)
		{
			ExecuteApplyEffect();
		}
		else if (choice < 108)
		{
			ExecuteRemoveEffect();
		}
		else if (choice < 112)
		{
			ExecuteRemoveAllEffects();
		}
		else if (choice < 124)
		{
			ExecuteAdvance();
		}
		else if (choice < 134)
		{
			ExecuteGiveAbility();
		}
		else if (choice < 140)
		{
			ExecuteRevokeAbility();
		}
		else if (choice < 154)
		{
			ExecuteActivateAbility();
		}
		else if (choice < 159)
		{
			ExecuteEndAbility();
		}
		else if (choice < 163)
		{
			ExecuteCancelAbility();
		}
		else if (choice < 168)
		{
			ExecuteRemoveStaleEffect();
		}
		else if (choice < 172)
		{
			ExecuteActivateStaleAbility();
		}
		else if (choice < 175)
		{
			ExecuteRemoveStaleModifier();
		}
		else if (choice < 179)
		{
			ExecuteQueryStaleEntity();
		}
		else
		{
			Trace("query", DescribeCounts());
		}
	}

	private void RunExceptionProbe()
	{
		Entity entity = EnsureEntity();

		_probeActive = true;
		_probeFired = false;
		_probeBoundary = _options.ExceptionBoundary;

		try
		{
			switch (_options.ExceptionBoundary)
			{
				case StressNotificationBoundary.TagsChanged:
					AddTag(entity, _random.Pick(LifecycleStressSchema.Tags));
					break;

				case StressNotificationBoundary.AttributeChanged:
					SetBase(
						entity,
						Attributes.GetBaseValue(
							entity,
							LifecycleStressSchema.Score) + 1f);
					break;

				case StressNotificationBoundary.EffectChanged:
					// No tags and no modifiers means the Applied event is
					// the first effect notification from this application.
					ApplyEffect(entity, LifecycleStressSchema.InertInfinite);
					break;

				case StressNotificationBoundary.AbilityChanged:
					GiveAbility(entity, LifecycleStressSchema.Dash);
					break;

				default:
					throw new ArgumentOutOfRangeException(
						nameof(_options.ExceptionBoundary));
			}
		}
		finally
		{
			_probeActive = false;
		}

		if (!_probeFired)
		{
			throw CreateFailure(
				$"The {_options.ExceptionBoundary} probe did not deliver " +
				"its notification.");
		}
	}

	private void ExecuteCreateEntity()
	{
		Entity entity = CreateEntityCore();

		Trace("create", Describe(entity));
	}

	private void ExecuteDestroyEntity(Entity? selected = null)
	{
		Entity target;

		if (selected is { } candidate && _model.TryGetEntity(candidate, out _))
		{
			target = candidate;
		}
		else if (_model.Living.Count == 0)
		{
			target = CreateEntityCore();
		}
		else
		{
			target = _random.Pick(_model.Living).Entity;
		}

		_entityDestructionInProgress = true;
		bool destroyed;
		try
		{
			destroyed = World.DestroyEntity(target);
		}
		finally
		{
			_entityDestructionInProgress = false;
		}

		Trace("destroy", $"{Describe(target)} => {destroyed}");
	}

	private void ExecuteAddTag(Entity? selected = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		GameplayTag tag = _random.Pick(LifecycleStressSchema.Tags);

		AddTag(entity, tag);
	}

	private void AddTag(Entity entity, GameplayTag tag)
	{
		_model.AddTag(entity, tag);

		bool added = Tags.AddTag(entity, tag);

		Trace(
			"tag.add",
			$"{Describe(entity)} {TagName(tag)} => {added}");

		if (!added)
		{
			throw CreateFailure(
				$"AddTag returned false for live {Describe(entity)}.");
		}
	}

	private void ExecuteRemoveTag(Entity? selected = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		GameplayTag tag = _random.Pick(LifecycleStressSchema.Tags);

		// Only an unowned grant is removable from outside an effect. A
		// tag held solely by active effects must make RemoveTag report
		// false rather than steal the owner's grant, so the generator
		// makes sure an unowned grant exists first.
		if (_model.UnownedTagCount(entity, tag) == 0)
		{
			AddTag(entity, tag);
			return;
		}

		RemoveTag(entity, tag);
	}

	private void RemoveTag(Entity entity, GameplayTag tag)
	{
		_model.RemoveTag(entity, tag);

		bool removed = Tags.RemoveTag(entity, tag);

		Trace(
			"tag.remove",
			$"{Describe(entity)} {TagName(tag)} => {removed}");

		if (!removed)
		{
			throw CreateFailure(
				$"RemoveTag returned false for a counted grant on " +
				$"{Describe(entity)}.");
		}
	}

	private void ExecuteClearTags(Entity? selected = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		bool modeled = _model.ClearTags(entity);

		bool removed = Tags.Clear(entity);

		Trace("tag.clear", $"{Describe(entity)} => {removed}");

		if (modeled != removed)
		{
			throw CreateFailure(
				$"Clear returned {removed} for {Describe(entity)} but the " +
				$"reference model expected {modeled}.");
		}
	}

	private void ExecuteSetBase(Entity? selected = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		float value = _random.NextInt(161) - 20f;

		SetBase(entity, value);
	}

	private void SetBase(Entity entity, float value)
	{
		bool changed = Attributes.SetBaseValue(
			entity,
			LifecycleStressSchema.Score,
			value);

		Trace(
			"attr.base",
			$"{Describe(entity)} {value} => {changed}");
	}

	private void ExecuteAddModifier(Entity? selected = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		float magnitude = _random.NextInt(13) - 6f;

		_callbacksSuppressed++;

		GameplayModifierHandle handle;

		try
		{
			handle = Attributes.AddModifier(
				entity,
				LifecycleStressSchema.Score,
				GameplayModifierOperation.AddBase,
				magnitude);
		}
		finally
		{
			_callbacksSuppressed--;
		}

		if (handle.IsNone)
		{
			throw CreateFailure("AddModifier returned None.");
		}

		if (_model.TryGetEntity(entity, out _))
		{
			_model.AddDirectModifier(entity, handle, magnitude);
		}

		Trace(
			"attr.addModifier",
			$"{Describe(entity)} {magnitude} => {Describe(handle)}");
	}

	private void ExecuteRemoveModifier(Entity? selected = null)
	{
		ReferenceEntity? entity = selected is { } candidate
			? _model.TryGetEntity(candidate, out ReferenceEntity? exact)
				? exact
				: null
			: _model.Living.Count == 0
				? null
				: _random.Pick(_model.Living);

		entity ??= _model.Living.Count == 0
			? null
			: _random.Pick(_model.Living);

		if (entity is null || entity.Modifiers.Count == 0)
		{
			ExecuteAddModifier(entity?.Entity);
			return;
		}

		ReferenceModifier modifier = _random.Pick(entity.Modifiers);

		bool removed = Attributes.RemoveModifier(modifier.Handle);

		if (removed)
		{
			_model.RemoveDirectModifier(modifier.Handle);
			RememberStale(_staleModifiers, modifier.Handle);
		}

		Trace(
			"attr.removeModifier",
			$"{Describe(entity.Entity)} {Describe(modifier.Handle)} => {removed}");
	}

	private void ExecuteClearAttributes(Entity? selected = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		bool hadState = Attributes.Clear(entity);

		_model.ClearAttributes(entity);

		Trace("attr.clear", $"{Describe(entity)} => {hadState}");
	}

	private void ExecuteApplyEffect(
		Entity? selected = null,
		GameplayEffectDefinition? selectedDefinition = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		GameplayEffectDefinition definition = selectedDefinition
			?? _random.Pick(LifecycleStressSchema.Effects);

		Entity source = _random.Chance(50)
			? default
			: EntityOrSelf(entity);

		GameplayEffectApplyResult result;

		_storeMutationDepth++;

		try
		{
			result = Effects.Apply(entity, definition, source);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"effect.apply",
			$"{Describe(entity)} {definition.Name} => {result.Status}" +
			$" x{result.StackCount}");
	}

	private void ApplyEffect(
		Entity entity,
		GameplayEffectDefinition definition)
	{
		_storeMutationDepth++;

		try
		{
			Effects.Apply(entity, definition);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"effect.apply",
			$"{Describe(entity)} {definition.Name}");
	}

	private void ExecuteRemoveEffect()
	{
		ReferenceEffect[] effects = _model.Effects.ToArray();

		if (effects.Length == 0)
		{
			ExecuteApplyEffect();
			return;
		}

		ReferenceEffect effect = _random.Pick(effects);
		bool removed;

		_storeMutationDepth++;

		try
		{
			removed = Effects.Remove(effect.Handle);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"effect.remove",
			$"{Describe(effect.Target)} {effect.Definition.Name} => {removed}");
	}

	private void ExecuteRemoveAllEffects()
	{
		Entity entity = EnsureEntity();
		int removed;

		_storeMutationDepth++;

		try
		{
			removed = Effects.RemoveAll(entity);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace("effect.removeAll", $"{Describe(entity)} => {removed}");
	}

	private void ExecuteAdvance()
	{
		float delta = 0.25f + _random.NextInt(12) * 0.25f;

		_storeMutationDepth++;

		try
		{
			Effects.Advance(delta);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace("advance", delta.ToString("0.00"));
	}

	private void ExecuteGiveAbility(
		Entity? selected = null,
		GameplayAbilityDefinition? selectedDefinition = null)
	{
		Entity entity = selected is { } candidate
			&& _model.TryGetEntity(candidate, out _)
			? candidate
			: EnsureEntity();

		GameplayAbilityDefinition definition = selectedDefinition
			?? _random.Pick(LifecycleStressSchema.Abilities);

		GameplayAbilityHandle handle;

		_storeMutationDepth++;

		try
		{
			handle = Abilities.GiveAbility(
				entity,
				definition,
				inputId: _random.NextInt(3));
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"ability.give",
			$"{Describe(entity)} {definition.Name} => {Describe(handle)}");
	}

	private void GiveAbility(
		Entity entity,
		GameplayAbilityDefinition definition)
	{
		_storeMutationDepth++;

		try
		{
			Abilities.GiveAbility(entity, definition);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace("ability.give", $"{Describe(entity)} {definition.Name}");
	}

	private void ExecuteRevokeAbility()
	{
		ReferenceAbility[] abilities = _model.Abilities.ToArray();

		if (abilities.Length == 0)
		{
			ExecuteGiveAbility();
			return;
		}

		ReferenceAbility ability = _random.Pick(abilities);
		bool revoked;

		_storeMutationDepth++;

		try
		{
			revoked = Abilities.RevokeAbility(ability.Handle);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"ability.revoke",
			$"{Describe(ability.Owner)} {ability.Definition.Name} => {revoked}");
	}

	private void ExecuteActivateAbility()
	{
		ReferenceAbility[] abilities = _model.Abilities.ToArray();

		if (abilities.Length == 0)
		{
			ExecuteGiveAbility();
			return;
		}

		ReferenceAbility ability = _random.Pick(abilities);
		Entity target = _random.Chance(70)
			? EnsureEntity()
			: default;

		GameplayAbilityActivationResult result;

		_storeMutationDepth++;

		try
		{
			result = Abilities.TryActivate(ability.Handle, target);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"ability.activate",
			$"{Describe(ability.Owner)} {ability.Definition.Name} => " +
			result.Status);
	}

	private void ExecuteEndAbility()
	{
		ReferenceAbility[] active = _model.Abilities
			.Where(candidate => candidate.Active)
			.ToArray();

		ReferenceAbility[] abilities = active.Length > 0
			? active
			: _model.Abilities.ToArray();

		if (abilities.Length == 0)
		{
			ExecuteGiveAbility();
			return;
		}

		ReferenceAbility ability = _random.Pick(abilities);
		bool ended;

		_storeMutationDepth++;

		try
		{
			ended = Abilities.End(ability.Handle);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"ability.end",
			$"{Describe(ability.Owner)} {ability.Definition.Name} => {ended}");
	}

	private void ExecuteCancelAbility()
	{
		ReferenceAbility[] active = _model.Abilities
			.Where(candidate => candidate.Active)
			.ToArray();

		ReferenceAbility[] abilities = active.Length > 0
			? active
			: _model.Abilities.ToArray();

		if (abilities.Length == 0)
		{
			ExecuteGiveAbility();
			return;
		}

		ReferenceAbility ability = _random.Pick(abilities);
		bool cancelled;

		_storeMutationDepth++;

		try
		{
			cancelled = Abilities.Cancel(ability.Handle);
		}
		finally
		{
			_storeMutationDepth--;
		}

		Trace(
			"ability.cancel",
			$"{Describe(ability.Owner)} {ability.Definition.Name} => " +
			cancelled);
	}

	private void ExecuteRemoveStaleEffect()
	{
		if (_staleEffects.Count == 0)
		{
			ExecuteRemoveEffect();
			return;
		}

		GameplayEffectHandle handle = _random.Pick(_staleEffects);

		if (Effects.TryGetEffect(handle, out _))
		{
			throw CreateFailure(
				$"Stale effect {Describe(handle)} still resolves.");
		}

		bool removed = Effects.Remove(handle);

		if (removed)
		{
			throw CreateFailure(
				$"Stale effect {Describe(handle)} was removed as live state.");
		}

		Trace("stale.effect.remove", Describe(handle));
	}

	private void ExecuteActivateStaleAbility()
	{
		if (_staleAbilities.Count == 0)
		{
			ExecuteRevokeAbility();
			return;
		}

		GameplayAbilityHandle handle = _random.Pick(_staleAbilities);

		if (Abilities.TryGetAbility(handle, out _))
		{
			throw CreateFailure(
				$"Stale ability {Describe(handle)} still resolves.");
		}

		GameplayAbilityActivationResult result = Abilities.TryActivate(handle);

		if (result.Status != GameplayAbilityActivationStatus.NotGranted)
		{
			throw CreateFailure(
				$"Stale ability {Describe(handle)} returned {result.Status}.");
		}

		if (Abilities.End(handle) || Abilities.Cancel(handle))
		{
			throw CreateFailure(
				$"Stale ability {Describe(handle)} was ended or cancelled.");
		}

		Trace("stale.ability.activate", Describe(handle));
	}

	private void ExecuteRemoveStaleModifier()
	{
		if (_staleModifiers.Count == 0)
		{
			ExecuteRemoveModifier();
			return;
		}

		GameplayModifierHandle handle = _random.Pick(_staleModifiers);

		if (Attributes.RemoveModifier(handle))
		{
			throw CreateFailure(
				$"Stale modifier {Describe(handle)} removed live state.");
		}

		Trace("stale.modifier.remove", Describe(handle));
	}

	private void ExecuteQueryStaleEntity()
	{
		if (_staleEntities.Count == 0)
		{
			Trace("stale.entity.query", "none");
			return;
		}

		Entity entity = _random.Pick(_staleEntities);

		if (World.IsAlive(entity))
		{
			throw CreateFailure(
				$"Stale entity {Describe(entity)} is alive.");
		}

		foreach (GameplayTag tag in LifecycleStressSchema.Tags)
		{
			if (Tags.GetTagCount(entity, tag) != 0
				|| Tags.HasTag(entity, tag))
			{
				throw CreateFailure(
					$"Stale entity {Describe(entity)} retained {TagName(tag)}.");
			}
		}

		if (Attributes.HasAttribute(
				entity,
				LifecycleStressSchema.Score)
			|| Attributes.GetBaseValue(
				entity,
				LifecycleStressSchema.Score)
				!= LifecycleStressSchema.Score.DefaultBaseValue)
		{
			throw CreateFailure(
				$"Stale entity {Describe(entity)} retained attributes.");
		}

		if (Effects.GetEffectCount(entity) != 0)
		{
			throw CreateFailure(
				$"Stale entity {Describe(entity)} retained effects.");
		}

		if (Abilities.GetAbilityCount(entity) != 0)
		{
			throw CreateFailure(
				$"Stale entity {Describe(entity)} retained abilities.");
		}

		Trace("stale.entity.query", Describe(entity));
	}

	private void HandleEntityDestroyed(Entity entity)
	{
		RemovedEntityState removed = _model.RemoveEntity(entity);

		foreach (GameplayEffectHandle effect in removed.Effects)
		{
			RememberStale(_staleEffects, effect);
		}

		foreach (GameplayAbilityHandle ability in removed.Abilities)
		{
			RememberStale(_staleAbilities, ability);
		}

		foreach (GameplayModifierHandle modifier in removed.Modifiers)
		{
			RememberStale(_staleModifiers, modifier);
		}

		RememberStale(_staleEntities, entity);

		Trace("callback.destroyed", Describe(entity));

		MaybeCreateReplacement();
	}

	private void MaybeCreateReplacement()
	{
		if (_callDepth >= MaxCallbackDepth
			|| World.Count >= MaxReplacementEntities
			|| !_random.Chance(70))
		{
			return;
		}

		_callDepth++;

		try
		{
			Entity replacement = CreateEntityCore();

			Trace("callback.replacement", Describe(replacement));

			int actions = _random.NextInt(3) + 1;

			for (int index = 0; index < actions; index++)
			{
				if (!_model.TryGetEntity(replacement, out _))
				{
					break;
				}

				switch (_random.NextInt(4))
				{
					case 0:
						AddTag(
							replacement,
							_random.Pick(LifecycleStressSchema.Tags));
						break;

					case 1:
						SetBase(replacement, _random.NextInt(121) + 20f);
						break;

					case 2:
						ApplyEffect(
							replacement,
							_random.Pick(LifecycleStressSchema.Effects));
						break;

					default:
						GiveAbility(
							replacement,
							_random.Pick(LifecycleStressSchema.Abilities));
						break;
				}
			}

			if (_callDepth < MaxCallbackDepth
				&& _random.Chance(20)
				&& _model.TryGetEntity(replacement, out _))
			{
				// Leave the next generation to the nested destruction
				// callback. No store has observed this generation yet.
				World.DestroyEntity(replacement);
			}
		}
		finally
		{
			_callDepth--;
		}
	}

	private void HandleTagsChanged(Entity entity)
	{
		RunNotification(StressNotificationBoundary.TagsChanged, entity);
	}

	private void HandleAttributeChanged(AttributeChange change)
	{
		if (_model.TryGetEntity(change.Entity, out _))
		{
			_model.SetBaseValue(change.Entity, change.NewBaseValue);
		}

		RunNotification(StressNotificationBoundary.AttributeChanged, change.Entity);
	}

	private void HandleEffectChanged(GameplayEffectChange change)
	{
		switch (change.Kind)
		{
			case GameplayEffectChangeKind.Applied:
				{
					// The model observes individual tag grants before the
					// effect's committed Applied notification.
					bool inhibited = Effects.TryGetEffect(
						change.Handle,
						out ActiveGameplayEffect snapshot)
						&& snapshot.IsInhibited;

					_model.AddEffect(
						change.Handle,
						change.Definition,
						change.Target,
						change.StackCount,
						inhibited);

					break;
				}

			case GameplayEffectChangeKind.StackChanged:
				// An effect is active and linked before its Applied
				// notification is raised. A nested application or tag
				// callback can therefore stack or inhibit it first. Its
				// eventual Applied snapshot carries the final stack and
				// inhibition state, so intermediate transitions for a
				// handle the model has not seen yet are ignored.
				if (!_model.TryGetEffect(change.Handle, out _))
				{
					return;
				}

				_model.StackEffect(change.Handle, change.StackCount);
				break;

			case GameplayEffectChangeKind.Inhibited:
				if (!_model.TryGetEffect(change.Handle, out _))
				{
					return;
				}

				_model.InhibitEffect(change.Handle);
				break;

			case GameplayEffectChangeKind.Uninhibited:
				if (!_model.TryGetEffect(change.Handle, out _))
				{
					return;
				}

				_model.UninhibitEffect(change.Handle);
				break;

			case GameplayEffectChangeKind.Removed:
			case GameplayEffectChangeKind.Expired:
				_model.RemoveEffect(change.Handle);
				RememberStale(_staleEffects, change.Handle);
				break;

			case GameplayEffectChangeKind.TargetDestroyed:
				// The target may already have been replaced at the same index.
				// Do not mutate the new entity model while recording retirement.
				if (_model.TryGetEffect(change.Handle, out _))
				{
					_model.RemoveEffect(change.Handle);
				}
				RememberStale(_staleEffects, change.Handle);
				return;

			case GameplayEffectChangeKind.PeriodExecuted:
				if (!_model.TryGetEffect(change.Handle, out _))
				{
					return;
				}

				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(change.Kind),
					change.Kind,
					"The stress harness does not understand this effect " +
					"change kind.");
		}

		RunNotification(StressNotificationBoundary.EffectChanged, change.Target);
	}

	private void HandleAbilityChanged(GameplayAbilityChange change)
	{
		switch (change.Kind)
		{
			case GameplayAbilityChangeKind.Granted:
				_model.AddAbility(
					change.Handle,
					change.Definition,
					change.Owner);
				break;

			case GameplayAbilityChangeKind.Revoked:
				_model.RemoveAbility(change.Handle);
				RememberStale(_staleAbilities, change.Handle);
				break;

			case GameplayAbilityChangeKind.Activated:
				_model.ActivateAbility(change.Handle, change.Target);
				break;

			case GameplayAbilityChangeKind.Ended:
			case GameplayAbilityChangeKind.Cancelled:
				_model.DeactivateAbility(change.Handle);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(change.Kind),
					change.Kind,
					"The stress harness does not understand this ability " +
					"change kind.");
		}

		RunNotification(StressNotificationBoundary.AbilityChanged, change.Owner);
	}

	private void RunNotification(
		StressNotificationBoundary boundary,
		Entity entity)
	{
		if (_probeActive
			&& _probeBoundary == boundary
			&& !_probeFired)
		{
			_probeFired = true;
			throw new StressInjectedException(boundary.ToString());
		}

		if (_probeActive
			|| _entityDestructionInProgress
			|| _callbacksSuppressed > 0
			|| _callDepth >= MaxCallbackDepth
			|| _callbackBudget <= 0)
		{
			return;
		}

		if (boundary == StressNotificationBoundary.TagsChanged
			&& _storeMutationDepth > 0)
		{
			// An effect or ability transition is midway through its
			// own tag bookkeeping. The enclosing change has not been
			// committed to the reference model yet.
			return;
		}

		if (!_random.Chance(65))
		{
			return;
		}

		_callbackBudget--;
		_callDepth++;

		try
		{
			RunCallback(entity);
		}
		finally
		{
			_callDepth--;
		}
	}

	private void RunCallback(Entity subject)
	{
		int choice = _random.NextInt(132);

		bool tagOperationAllowed = _storeMutationDepth == 0;

		if (choice < 20 && tagOperationAllowed)
		{
			AddTag(
				ModelOrRandom(subject),
				_random.Pick(LifecycleStressSchema.Tags));
		}
		else if (choice < 32 && tagOperationAllowed)
		{
			Entity entity = ModelOrRandom(subject);
			GameplayTag tag = _random.Pick(LifecycleStressSchema.Tags);

			if (_model.UnownedTagCount(entity, tag) > 0)
			{
				RemoveTag(entity, tag);
			}
			else
			{
				AddTag(entity, tag);
			}
		}
		else if (choice < 44)
		{
			SetBase(
				ModelOrRandom(subject),
				_random.NextInt(121) + 20f);
		}
		else if (choice < 52)
		{
			ExecuteAddModifier(ModelOrRandom(subject));
		}
		else if (choice < 72)
		{
			ExecuteApplyEffect(ModelOrRandom(subject));
		}
		else if (choice < 84)
		{
			ExecuteRemoveEffect();
		}
		else if (choice < 96)
		{
			ExecuteGiveAbility(ModelOrRandom(subject));
		}
		else if (choice < 110)
		{
			ExecuteActivateAbility();
		}
		else if (choice < 120)
		{
			ExecuteEndAbility();
		}
		else if (choice < 128)
		{
			ExecuteCancelAbility();
		}
		else if (_callDepth == 1 && _random.Chance(35))
		{
			Entity entity = ModelOrRandom(subject);

			if (_model.TryGetEntity(entity, out _))
			{
				ExecuteDestroyEntity(entity);
			}
			else
			{
				Trace("callback.destroy.skipped", Describe(entity));
			}
		}
		else
		{
			Trace("callback.query", DescribeCounts());
		}
	}

	private Entity ModelOrRandom(Entity preferred)
	{
		return _model.TryGetEntity(preferred, out _)
			? preferred
			: EnsureEntity();
	}

	private Entity EntityOrSelf(Entity preferred)
	{
		return _model.TryGetEntity(preferred, out _)
			? preferred
			: EnsureEntity();
	}

	private Entity EnsureEntity()
	{
		if (_model.Living.Count == 0)
		{
			return CreateEntityCore();
		}

		return _random.Pick(_model.Living).Entity;
	}

	/// <summary>
	/// Records cue transitions without generating further operations.
	/// The events are part of the trace that a failing seed reproduces,
	/// which is what makes a cue ownership mismatch diagnosable.
	/// </summary>
	private void HandleCueChanged(GameplayCueEvent change)
	{
		Trace(
			$"cue.{change.Kind}",
			$"{GameplayTagRegistry.Shared.GetName(change.Tag)} " +
			$"{Describe(change.Target)} {Describe(change.Effect)}");
	}

	private void Verify()
	{
		Check(
			World.Count == _model.Living.Count,
			$"World.Count {World.Count} != model " +
			$"{_model.Living.Count}");

		Check(
			Tags.TaggedEntityCount == _model.TaggedEntityCount,
			$"TaggedEntityCount {Tags.TaggedEntityCount} != model " +
			$"{_model.TaggedEntityCount}");

		Check(
			Attributes.AttributedEntityCount
				== _model.AttributedEntityCount,
			$"AttributedEntityCount {Attributes.AttributedEntityCount} " +
			$"!= model {_model.AttributedEntityCount}");

		Check(
			Effects.ActiveEffectCount == _model.ActiveEffectCount,
			$"ActiveEffectCount {Effects.ActiveEffectCount} != model " +
			$"{_model.ActiveEffectCount}");

		Check(
			Abilities.GrantedAbilityCount == _model.GrantedAbilityCount,
			$"GrantedAbilityCount {Abilities.GrantedAbilityCount} != model " +
			$"{_model.GrantedAbilityCount}");

		Check(
			Abilities.ActiveAbilityCount == _model.ActiveAbilityCount,
			$"ActiveAbilityCount {Abilities.ActiveAbilityCount} != model " +
			$"{_model.ActiveAbilityCount}");

		int expectedCues = _model.ExpectedCueCount;

		Check(
			Cues.ActiveCueCount == expectedCues,
			$"ActiveCueCount {Cues.ActiveCueCount} != model {expectedCues}");

		foreach (ReferenceEffect effect in _model.Effects)
		{
			bool hasCue = Cues.TryFindEffectForTesting(effect.Handle, out _);

			Check(
				hasCue == effect.CueActive,
				$"{Describe(effect.Handle)} cue {hasCue} != model " +
				$"{effect.CueActive}.");
		}

		DrainCueBuffer();

		foreach (ReferenceEntity entity in _model.Living)
		{
			VerifyEntity(entity);
		}

		VerifyTagDenseIndex();
		VerifyAttributeDenseIndex();
		VerifyStaleHandles();
	}

	/// <summary>
	/// Empties the presentation handoff so the bounded buffer cannot
	/// overflow across a long run. The cue events themselves are
	/// validated through the model comparison; the buffer is only a
	/// consumer path here.
	/// </summary>
	private void DrainCueBuffer()
	{
		Span<GameplayCueEvent> events = stackalloc GameplayCueEvent[64];
		int drained;

		do
		{
			drained = _cueBuffer.Drain(events);
		}
		while (drained == events.Length);
	}

	private void VerifyEntity(ReferenceEntity entity)
	{
		Entity handle = entity.Entity;

		Check(
			World.IsAlive(handle),
			$"Model entity {Describe(handle)} is not alive.");

		int nonZeroTags = 0;

		foreach (GameplayTag tag in LifecycleStressSchema.Tags)
		{
			int expected = entity.TagCount(tag);
			int actual = Tags.GetTagCount(handle, tag);

			Check(
				actual == expected,
				$"{Describe(handle)} {TagName(tag)} count {actual} != " +
				$"model {expected}");

			Check(
				Tags.HasTagExact(handle, tag) == (expected > 0),
				$"{Describe(handle)} HasTagExact({TagName(tag)}) disagrees " +
				"with the model.");

			if (expected > 0)
			{
				nonZeroTags++;

				Check(
					Tags.HasTag(handle, tag),
					$"{Describe(handle)} HasTag({TagName(tag)}) is false.");
			}
		}

		Check(
			Tags.GetTagSet(handle).Count == nonZeroTags,
			$"{Describe(handle)} tag-set count " +
			$"{Tags.GetTagSet(handle).Count} != model {nonZeroTags}");

		float baseValue = Attributes.GetBaseValue(
			handle,
			LifecycleStressSchema.Score);
		float currentValue = Attributes.GetCurrentValue(
			handle,
			LifecycleStressSchema.Score);
		float expectedCurrent = _model.ExpectedCurrentValue(handle);

		Check(
			MathF.Abs(baseValue - entity.BaseValue) <= 1e-3f,
			$"{Describe(handle)} base {baseValue} != model " +
			$"{entity.BaseValue}");

		Check(
			MathF.Abs(currentValue - expectedCurrent) <= 1e-3f,
			$"{Describe(handle)} current {currentValue} != model " +
			$"{expectedCurrent}");

		Check(
			Attributes.HasAttribute(handle, LifecycleStressSchema.Score)
				== entity.HasAttributeState,
			$"{Describe(handle)} HasAttribute is " +
			$"{Attributes.HasAttribute(handle, LifecycleStressSchema.Score)}" +
			$" but model says {entity.HasAttributeState}.");

		int modifierCount = Attributes.GetModifierCount(
			handle,
			LifecycleStressSchema.Score);
		int expectedModifierCount = _model.ExpectedModifierCount(handle);

		Check(
			modifierCount == expectedModifierCount,
			$"{Describe(handle)} modifier count {modifierCount} != model " +
			$"{expectedModifierCount}");

		VerifyEffects(entity);
		VerifyAbilities(entity);
	}

	private void VerifyEffects(ReferenceEntity entity)
	{
		Entity handle = entity.Entity;
		int count = Effects.GetEffectCount(handle);

		Check(
			count == entity.Effects.Count,
			$"{Describe(handle)} effect count {count} != model " +
			$"{entity.Effects.Count}");

		Span<GameplayEffectHandle> buffer = stackalloc GameplayEffectHandle[64];
		int written = Effects.CopyEffects(handle, buffer);
		HashSet<GameplayEffectHandle> actual = [];

		for (int index = 0; index < written; index++)
		{
			GameplayEffectHandle effectHandle = buffer[index];

			Check(
				actual.Add(effectHandle),
				$"{Describe(handle)} effect enumeration repeated " +
				$"{Describe(effectHandle)}.");

			Check(
				Effects.TryGetEffect(effectHandle, out _),
				$"{Describe(handle)} enumerated dead effect " +
				$"{Describe(effectHandle)}.");
		}

		Check(
			actual.Count == entity.Effects.Count,
			$"{Describe(handle)} enumerated {actual.Count} effects but " +
			$"model has {entity.Effects.Count}.");

		foreach (ReferenceEffect expected in entity.Effects)
		{
			Check(
				actual.Contains(expected.Handle),
				$"{Describe(handle)} enumeration omitted " +
				$"{Describe(expected.Handle)}.");

			Check(
				Effects.TryGetEffect(
					expected.Handle,
					out ActiveGameplayEffect snapshot),
				$"{Describe(expected.Handle)} does not resolve.");

			Check(
				snapshot.Target == expected.Target,
				$"{Describe(expected.Handle)} target " +
				$"{Describe(snapshot.Target)} != " +
				$"{Describe(expected.Target)}.");

			Check(
				ReferenceEquals(snapshot.Definition, expected.Definition),
				$"{Describe(expected.Handle)} definition " +
				$"{snapshot.Definition.Name} != " +
				$"{expected.Definition.Name}.");

			Check(
				snapshot.StackCount == expected.StackCount,
				$"{Describe(expected.Handle)} stacks " +
				$"{snapshot.StackCount} != {expected.StackCount}.");

			Check(
				snapshot.IsInhibited == expected.Inhibited,
				$"{Describe(expected.Handle)} inhibited " +
				$"{snapshot.IsInhibited} != {expected.Inhibited}.");
		}
	}

	private void VerifyAbilities(ReferenceEntity entity)
	{
		Entity handle = entity.Entity;
		int count = Abilities.GetAbilityCount(handle);

		Check(
			count == entity.Abilities.Count,
			$"{Describe(handle)} ability count {count} != model " +
			$"{entity.Abilities.Count}");

		int active = Abilities.GetActiveAbilityCount(handle);
		int expectedActive = entity.Abilities.Count(
			ability => ability.Active);

		Check(
			active == expectedActive,
			$"{Describe(handle)} active abilities {active} != model " +
			$"{expectedActive}.");

		Span<GameplayAbilityHandle> buffer = stackalloc GameplayAbilityHandle[64];
		int written = Abilities.CopyAbilities(handle, buffer);
		HashSet<GameplayAbilityHandle> actual = [];

		for (int index = 0; index < written; index++)
		{
			GameplayAbilityHandle abilityHandle = buffer[index];

			Check(
				actual.Add(abilityHandle),
				$"{Describe(handle)} ability enumeration repeated " +
				$"{Describe(abilityHandle)}.");

			Check(
				Abilities.TryGetAbility(abilityHandle, out _),
				$"{Describe(handle)} enumerated dead ability " +
				$"{Describe(abilityHandle)}.");
		}

		Check(
			actual.Count == entity.Abilities.Count,
			$"{Describe(handle)} enumerated {actual.Count} abilities but " +
			$"model has {entity.Abilities.Count}.");

		foreach (ReferenceAbility expected in entity.Abilities)
		{
			Check(
				actual.Contains(expected.Handle),
				$"{Describe(handle)} enumeration omitted " +
				$"{Describe(expected.Handle)}.");

			Check(
				Abilities.TryGetAbility(
					expected.Handle,
					out GrantedGameplayAbility snapshot),
				$"{Describe(expected.Handle)} does not resolve.");

			Check(
				snapshot.Owner == expected.Owner,
				$"{Describe(expected.Handle)} owner " +
				$"{Describe(snapshot.Owner)} != " +
				$"{Describe(expected.Owner)}.");

			Check(
				ReferenceEquals(snapshot.Definition, expected.Definition),
				$"{Describe(expected.Handle)} definition " +
				$"{snapshot.Definition.Name} != " +
				$"{expected.Definition.Name}."); Check(
					snapshot.IsActive == expected.Active,
					$"{Describe(expected.Handle)} active {snapshot.IsActive} != " +
					$"{expected.Active}.");

			Check(
				Abilities.IsActive(expected.Handle) == expected.Active,
				$"{Describe(expected.Handle)} IsActive disagrees with " +
				"the model.");

			if (expected.Definition.CooldownEffect is not null)
			{
				bool expectedCooldown = entity.TagCount(
					LifecycleStressSchema.Cooldown) > 0;

				Check(
					Abilities.IsOnCooldown(
						handle,
						expected.Definition)
						== expectedCooldown,
					$"{Describe(handle)} cooldown state for " +
					$"{expected.Definition.Name} disagrees with the model.");
			}
		}
	}

	private void VerifyTagDenseIndex()
	{
		Span<Entity> buffer = stackalloc Entity[64];
		int written = Tags.CopyTaggedEntities(buffer);
		int expected = _model.TaggedEntityCount;

		Check(
			written == expected,
			$"Tag dense enumeration wrote {written}, expected {expected}.");

		HashSet<Entity> seen = [];

		for (int index = 0; index < written; index++)
		{
			Entity entity = buffer[index];

			Check(
				seen.Add(entity),
				$"Tag dense enumeration repeated {Describe(entity)}."); Check(
					_model.TryGetEntity(entity, out ReferenceEntity? model)
						&& model is not null
						&& model.HasAnyTag,
					$"Tag dense enumeration contains {Describe(entity)} " +
					"without model tags.");
		}

		foreach (ReferenceEntity entity in _model.Living)
		{
			if (entity.HasAnyTag)
			{
				Check(
					seen.Contains(entity.Entity),
					$"Tag dense enumeration omitted " +
					$"{Describe(entity.Entity)}.");
			}
		}
	}

	private void VerifyAttributeDenseIndex()
	{
		Span<Entity> buffer = stackalloc Entity[64];
		int written = Attributes.CopyAttributedEntities(buffer);
		int expected = _model.AttributedEntityCount;

		Check(
			written == expected,
			$"Attribute dense enumeration wrote {written}, expected " +
			$"{expected}.");

		HashSet<Entity> seen = [];

		for (int index = 0; index < written; index++)
		{
			Entity entity = buffer[index];

			Check(
				seen.Add(entity),
				$"Attribute dense enumeration repeated {Describe(entity)}."); Check(
					_model.TryGetEntity(entity, out ReferenceEntity? model)
						&& model is not null
						&& model.HasAttributeState,
					$"Attribute dense enumeration contains {Describe(entity)} " +
					"without model attribute state.");
		}

		foreach (ReferenceEntity entity in _model.Living)
		{
			if (entity.HasAttributeState)
			{
				Check(
					seen.Contains(entity.Entity),
					$"Attribute dense enumeration omitted " +
					$"{Describe(entity.Entity)}.");
			}
		}
	}

	private void VerifyStaleHandles()
	{
		foreach (GameplayEffectHandle effect in _staleEffects)
		{
			Check(
				!Effects.TryGetEffect(effect, out _),
				$"Stale effect {Describe(effect)} resolves.");
		}

		foreach (GameplayAbilityHandle ability in _staleAbilities)
		{
			Check(
				!Abilities.TryGetAbility(ability, out _),
				$"Stale ability {Describe(ability)} resolves.");
		}

		foreach (Entity entity in _staleEntities)
		{
			Check(
				!World.IsAlive(entity),
				$"Stale entity {Describe(entity)} is alive.");
		}
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private LifecycleStressFailureException CreateFailure(
		string reason,
		Exception? innerException = null)
	{
		int traceStart = Math.Max(0, _trace.Count - 160);

		StringBuilder trace = new();

		for (int index = traceStart; index < _trace.Count; index++)
		{
			trace.AppendLine(_trace[index].ToString());
		}

		string message =
			$"Lifecycle stress failure.{Environment.NewLine}" +
			$"Seed: {_options.Seed}{Environment.NewLine}" +
			$"Operation: {_topLevelOperation}{Environment.NewLine}" +
			$"Trace index: {_traceSequence}{Environment.NewLine}" +
			$"Destroy callback before stores: " +
			$"{_options.DestroyCallbackBeforeStores}{Environment.NewLine}" +
			$"Reason: {reason}{Environment.NewLine}" +
			$"Store state:{Environment.NewLine}{DumpState()}" +
			$"Trace:{Environment.NewLine}{trace}";

		return new LifecycleStressFailureException(
			_options.Seed,
			_topLevelOperation,
			_traceSequence,
			DumpState(),
			_trace.ToArray(),
			message,
			innerException);
	}

	private string DumpState()
	{
		StringBuilder builder = new();

		builder.AppendLine(
			$"entities={_model.Living.Count}, effects=" +
			$"{_model.ActiveEffectCount}, grants=" +
			$"{_model.GrantedAbilityCount}, activeAbilities=" +
			$"{_model.ActiveAbilityCount}");

		foreach (ReferenceEntity entity in _model.Living)
		{
			builder.Append(
				$"  {Describe(entity.Entity)} base={entity.BaseValue} " +
				$"attrState={entity.HasAttributeState} tags=");

			bool first = true;

			foreach (GameplayTag tag in LifecycleStressSchema.Tags)
			{
				int count = entity.TagCount(tag);

				if (count == 0)
				{
					continue;
				}

				if (!first)
				{
					builder.Append(',');
				}

				first = false;
				builder.Append($"{TagName(tag)}x{count}");
			}

			builder.Append(" effects=[");

			for (int index = 0; index < entity.Effects.Count; index++)
			{
				if (index > 0)
				{
					builder.Append(',');
				}

				ReferenceEffect effect = entity.Effects[index];

				builder.Append(
					$"{effect.Definition.Name}#{effect.Handle.Slot}." +
					$"{effect.Handle.Generation}x{effect.StackCount}" +
					$"{(effect.Inhibited ? "I" : string.Empty)}");
			}

			builder.Append("] abilities=[");

			for (int index = 0; index < entity.Abilities.Count; index++)
			{
				if (index > 0)
				{
					builder.Append(',');
				}

				ReferenceAbility ability = entity.Abilities[index];

				builder.Append(
					$"{ability.Definition.Name}#{ability.Handle.Slot}." +
					$"{ability.Handle.Generation}" +
					$"{(ability.Active ? "A" : string.Empty)}");
			}

			builder.AppendLine("]");
		}

		return builder.ToString();
	}

	private string DescribeCounts()
	{
		return $"e{_model.Living.Count} fx{_model.ActiveEffectCount} " +
			$"ab{_model.GrantedAbilityCount}/{_model.ActiveAbilityCount}";
	}

	private void Trace(string operation, string outcome)
	{
		_trace.Add(new StressTraceEntry(
			_traceSequence++,
			_callDepth,
			operation,
			outcome));
	}

	private string ComputeTraceDigest()
	{
		ulong hash = 14695981039346656037UL;

		foreach (StressTraceEntry entry in _trace)
		{
			string line = $"{entry.Depth}:{entry.Operation}:{entry.Outcome}";

			foreach (char character in line)
			{
				hash ^= character;
				hash *= 1099511628211UL;
			}

			hash ^= '\n';
			hash *= 1099511628211UL;
		}

		return $"0x{hash:X16}";
	}

	private static void RememberStale<T>(List<T> history, T value)
		where T : struct
	{
		if (history.Count >= StaleHistoryLimit)
		{
			history.RemoveAt(0);
		}

		history.Add(value);
	}

	private static string Describe(Entity entity)
	{
		return $"e{entity.Index}g{entity.Generation}";
	}

	private static string Describe(GameplayEffectHandle handle)
	{
		return handle.IsNone
			? "fx.none"
			: $"fx[{Describe(handle.Target)}#{handle.Slot}." +
				$"{handle.Generation}]";
	}

	private static string Describe(GameplayAbilityHandle handle)
	{
		return handle.IsNone
			? "ab.none"
			: $"ab[{Describe(handle.Owner)}#{handle.Slot}." +
				$"{handle.Generation}]";
	}

	private static string Describe(GameplayModifierHandle handle)
	{
		return handle.IsNone
			? "mod.none"
			: $"mod[{Describe(handle.Entity)}#{handle.Slot}." +
				$"{handle.Generation}]";
	}

	private static string TagName(GameplayTag tag)
	{
		return GameplayTagRegistry.Shared.GetName(tag);
	}
}

internal readonly record struct StressResultSummary(
	StressOptions Options,
	bool Failed,
	string Description,
	LifecycleStressFailureException? Failure);
