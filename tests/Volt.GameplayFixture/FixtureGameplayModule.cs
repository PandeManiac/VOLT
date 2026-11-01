using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.GameplayFixture;

/// <summary>
/// An authored component declared by a project-owned assembly.
/// </summary>
/// <remarks>
/// This assembly is never referenced for compilation by the test project.
/// Tests load the built file through the same loader the editor uses, so
/// the discovery path is exercised rather than a compile-time list.
/// </remarks>
[EditorComponent(ComponentTypeId, "Fixture Spin")]
public sealed class FixtureSpinComponent : AuthoredComponent
{
	public const string ComponentTypeId = "volt.fixture.spin";

	public const float DegreesPerSecond = 90f;

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Fixture Spin";

	[EditorProperty(DisplayName = "Degrees Per Second", Minimum = -720f, Maximum = 720f)]
	public float Speed { get; set; } = DegreesPerSecond;
}

/// <summary>
/// The unmanaged ECS state a fixture system iterates.
/// </summary>
public struct FixtureSpinState
{
	public float Radians;
}

/// <summary>
/// Rotates every entity that carries <see cref="FixtureSpinState"/>.
/// </summary>
public sealed class FixtureSpinSystem : IGameplaySystem
{
	public int StartCount { get; private set; }

	public int UpdateCount { get; private set; }

	public int StopCount { get; private set; }

	public void OnStart(RuntimeScene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		StartCount++;
	}

	public void Update(RuntimeScene scene, float deltaSeconds)
	{
		ArgumentNullException.ThrowIfNull(scene);

		UpdateCount++;

		World world = scene.World;

		ComponentQuery query = new(
			required: new ComponentSignature(
				ComponentRegistry.Register<FixtureSpinState>()));

		float radians = float.DegreesToRadians(
			FixtureSpinComponent.DegreesPerSecond) * deltaSeconds;

		foreach (WorldQueryChunk chunk in world.Query(query))
		{
			Span<FixtureSpinState> states = chunk.GetSpan<FixtureSpinState>();
			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			for (int index = 0; index < states.Length; index++)
			{
				states[index].Radians += radians;

				ref RotationComponent rotation =
					ref world.GetComponent<RotationComponent>(entities[index]);

				rotation.Value = Quaternion.Normalize(
					Quaternion.Multiply(
						Quaternion.CreateFromAxisAngle(Vector3.UnitY, radians),
						rotation.Value));
			}
		}
	}

	public void OnStop(RuntimeScene scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		StopCount++;
	}
}

/// <summary>
/// Registers the fixture's component, its ECS binding, and its system.
/// </summary>
public sealed class FixtureGameplayModule : IGameplayModule
{
	public void Register(GameplayRegistrar registrar)
	{
		ArgumentNullException.ThrowIfNull(registrar);

		registrar.AddComponent<FixtureSpinComponent>();

		registrar.BindComponent<FixtureSpinComponent, FixtureSpinState>(
			component => new FixtureSpinState { Radians = component.Speed });

		registrar.AddSystem<FixtureSpinSystem>();
	}
}

/// <summary>
/// Binds a component it never registers, which must be reported instead
/// of silently doing nothing.
/// </summary>
public sealed class FixtureBindingOnlyModule : IGameplayModule
{
	public void Register(GameplayRegistrar registrar)
	{
		ArgumentNullException.ThrowIfNull(registrar);

		registrar.BindComponent<FixtureUnboundComponent, FixtureUnboundState>(
			_ => default);
	}
}

/// <summary>
/// An authored component only a binding refers to.
/// </summary>
[EditorComponent(ComponentTypeId, "Fixture Unbound")]
public sealed class FixtureUnboundComponent : AuthoredComponent
{
	public const string ComponentTypeId = "volt.fixture.unbound";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Fixture Unbound";
}

public struct FixtureUnboundState
{
	public int Value;
}

/// <summary>
/// Registers a system that throws on every step, so the runtime's
/// failure isolation is exercised as part of a normal load.
/// </summary>
public sealed class FixtureFaultySystemModule : IGameplayModule
{
	public void Register(GameplayRegistrar registrar)
	{
		ArgumentNullException.ThrowIfNull(registrar);
		registrar.AddSystem<FixtureThrowingSystem>();
	}
}

/// <summary>
/// A system that always fails, to prove a broken system does not stop
/// the others or take the editor down.
/// </summary>
public sealed class FixtureThrowingSystem : IGameplaySystem
{
	public const string FailureMessage = "fixture system failure";

	public void Update(RuntimeScene scene, float deltaSeconds)
	{
		throw new InvalidOperationException(FailureMessage);
	}
}

/// <summary>
/// Fails while registering, so the loader's recoverable path is real.
/// </summary>
public sealed class ThrowingFixtureModule : IGameplayModule
{
	public const string FailureMessage = "fixture module failure";

	public void Register(GameplayRegistrar registrar)
	{
		ArgumentNullException.ThrowIfNull(registrar);
		throw new InvalidOperationException(FailureMessage);
	}
}
