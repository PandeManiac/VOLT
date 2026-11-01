using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace GameScripts;

/// <summary>
/// Authored data: what the inspector shows and the scene saves.
/// </summary>
[EditorComponent(PlayerMotorComponent.ComponentTypeId, "Player Motor")]
public sealed class PlayerMotorComponent : AuthoredComponent
{
	public const string ComponentTypeId = "samplegame.playerMotor";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Player Motor";

	[EditorProperty(Minimum = 0f, Maximum = 20f)]
	public float Speed { get; set; } = 3f;

	public Vector3 Axis { get; set; } = Vector3.UnitX;
}

/// <summary>
/// Native state: what a system iterates in contiguous storage.
/// </summary>
public struct PlayerMotorState
{
	public Vector3 Axis;

	public float Speed;
}

/// <summary>
/// Moves every entity with a motor from game input each fixed step.
/// </summary>
public sealed class PlayerMotorSystem : IGameplaySystem
{
	public void Update(RuntimeScene scene, float deltaSeconds)
	{
		ComponentQuery query = new(
			required: new ComponentSignature(
				ComponentRegistry.Register<PlayerMotorState>()));

		float input = scene.Input.Horizontal;

		foreach (WorldQueryChunk chunk in scene.World.Query(query))
		{
			Span<PlayerMotorState> states =
				chunk.GetSpan<PlayerMotorState>();

			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			for (int index = 0; index < states.Length; index++)
			{
				PlayerMotorState state = states[index];

				ref PositionComponent position =
					ref scene.World.GetComponent<PositionComponent>(
						entities[index]);

				position.Value +=
					state.Axis * (input * state.Speed * deltaSeconds);
			}
		}
	}
}

/// <summary>
/// The entry point VOLT discovers when it loads this assembly.
/// </summary>
public sealed class GameModule : IGameplayModule
{
	public void Register(GameplayRegistrar registrar)
	{
		registrar.AddComponent<PlayerMotorComponent>();

		registrar.BindComponent<PlayerMotorComponent, PlayerMotorState>(
			component => new PlayerMotorState
			{
				Axis = component.Axis,
				Speed = component.Speed
			});

		registrar.AddSystem<PlayerMotorSystem>();
	}
}
