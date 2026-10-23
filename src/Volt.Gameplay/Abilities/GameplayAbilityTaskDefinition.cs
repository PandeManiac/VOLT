namespace Volt.Gameplay.Abilities;

/// <summary>
/// A named custom task that abilities of one ability system can wait
/// on.
/// </summary>
/// <remarks>
/// <para>
/// A definition is registered once per ability system and shared by
/// every activation that uses it: the definition itself holds no
/// per-activation state. What lives per activation is one compact task
/// slot in <see cref="Tasks.GameplayAbilityTaskSystem"/> with the same
/// generation, activation, and cancellation guarantees as the built-in
/// waits.
/// </para>
/// <para>
/// Custom tasks are completed by application code. The application
/// subscribes to whatever signal it already owns, such as an attribute
/// change, an interaction finishing, or a project-specific event, and
/// calls <see cref="Tasks.GameplayAbilityTaskSystem.Complete"/> for the
/// task handle. Nothing is polled and the runtime never scans pending
/// tasks, so a dormant custom task costs only its slot.
/// </para>
/// <para>
/// The definition is an identity, not a callback. There is no virtual
/// dispatch and no managed object per pending task; the name exists for
/// diagnostics and for finding a task through
/// <see cref="Tasks.GameplayAbilityTaskInfo"/>.
/// </para>
/// </remarks>
public sealed class GameplayAbilityTaskDefinition
{
	private readonly GameplayAbilitySystem _system;

	internal GameplayAbilityTaskDefinition(
		GameplayAbilitySystem system,
		int id,
		string name)
	{
		_system = system;
		Id = id;
		Name = name;
	}

	/// <summary>
	/// Gets the name supplied at registration.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the identifier that is unique within the ability system.
	/// </summary>
	public int Id { get; }

	/// <summary>
	/// Gets the ability system that owns this definition.
	/// </summary>
	internal GameplayAbilitySystem System => _system;

	/// <inheritdoc />
	public override string ToString()
	{
		return $"GameplayAbilityTaskDefinition({Name})";
	}
}
