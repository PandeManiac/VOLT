using Volt.Editor.Core.Runtime;

namespace Volt.Editor.Core.Gameplay;

/// <summary>
/// The entry point of a project-owned gameplay assembly.
/// </summary>
/// <remarks>
/// <para>
/// A game project implements this interface in its own assembly. VOLT
/// discovers every concrete implementation that has a parameterless
/// constructor when the assembly is loaded through an explicit build or
/// Play workflow. Opening a folder or listing an asset never loads code,
/// and a scene is always authored before any assembly runs.
/// </para>
/// <para>
/// Registration is where a module declares the parts the editor and the
/// runtime need to know about: the authored components the inspector can
/// add and edit, the mapping from those values onto real ECS components,
/// and the systems that run each fixed step. Nothing in the module should
/// start simulating; that happens when Play Mode starts a world.
/// </para>
/// </remarks>
public interface IGameplayModule
{
	/// <summary>
	/// Declares this module's components, bindings, and systems.
	/// </summary>
	/// <remarks>
	/// This runs once per load, before any runtime world exists. An
	/// exception here is reported as a diagnostic and leaves the editor
	/// usable, so a half-written module does not crash the session.
	/// </remarks>
	void Register(GameplayRegistrar registrar);
}

/// <summary>
/// A project-owned game system that runs once per fixed simulation step.
/// </summary>
/// <remarks>
/// <para>
/// Systems run in the order they were registered, on the thread that
/// owns the world, after the engine's built-in entity behaviours. That is
/// a sequential model: a system may read and write any component it can
/// reach, and no thread-safety guarantee is implied. Parallel system
/// execution needs declared access sets, which this contract does not
/// yet express, so it is deliberately not offered.
/// </para>
/// <para>
/// A system owns no per-entity state: entity state lives in ECS
/// components, which is what makes it inspectable and serializable. The
/// world is recreated every Play session, so a system instance is created
/// per load and must not cache entity handles.
/// </para>
/// </remarks>
public interface IGameplaySystem
{
	/// <summary>
	/// Runs once before the first fixed step of a world.
	/// </summary>
	/// <remarks>
	/// A game that creates its own entities does so here rather than in a
	/// constructor, so the work belongs to one world's lifetime.
	/// </remarks>
	void OnStart(RuntimeScene scene)
	{
	}

	/// <summary>
	/// Advances this system by one fixed step.
	/// </summary>
	/// <param name="scene">The world this step belongs to.</param>
	/// <param name="deltaSeconds">
	/// The fixed step in seconds. It is always
	/// <see cref="PlaySession.FixedDeltaSeconds"/>.
	/// </param>
	/// <remarks>
	/// An exception is reported through the runtime's diagnostics and
	/// Console instead of unwinding the whole simulation, and the system is
	/// skipped for the rest of that session: a system that throws every
	/// tick would otherwise bury every other message.
	/// </remarks>
	void Update(RuntimeScene scene, float deltaSeconds);

	/// <summary>
	/// Runs once as the world shuts down, before it is disposed.
	/// </summary>
	void OnStop(RuntimeScene scene)
	{
	}
}
