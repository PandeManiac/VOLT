using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace SampleGame;

/// <summary>
/// Authored data for the demo's player: what the inspector edits and the
/// scene file saves.
/// </summary>
[EditorComponent(PlayerControlComponent.ComponentTypeId, "Player Control")]
public sealed class PlayerControlComponent : AuthoredComponent
{
	/// <summary>
	/// The stable identifier a scene file refers to.
	/// </summary>
	/// <remarks>
	/// Identity is this string, not the CLR type name, so a rebuild that
	/// changes the type does not invalidate saved scenes.
	/// </remarks>
	public const string ComponentTypeId = "playdemo.playerControl";

	public override string TypeId => ComponentTypeId;

	public override string DisplayName => "Player Control";

	[EditorProperty(DisplayName = "Speed", Minimum = 0f, Maximum = 20f)]
	public float Speed { get; set; } = 3f;

	/// <summary>
	/// Gets or sets how far from the origin the player may travel.
	/// </summary>
	/// <remarks>
	/// The demo projects a fixed camera into its view, so a bound keeps
	/// the player on screen without needing a camera controller.
	/// </remarks>
	[EditorProperty(DisplayName = "Range", Minimum = 0f, Maximum = 12f)]
	public float Range { get; set; } = 5f;
}

/// <summary>
/// The native state the system iterates, laid out contiguously.
/// </summary>
public struct PlayerControlState
{
	public float Speed;

	public float Range;
}

/// <summary>
/// Moves every player from game input on each fixed step.
/// </summary>
public sealed class PlayerControlSystem : IGameplaySystem
{
	/// <summary>
	/// Announces the system in the editor's Console.
	/// </summary>
	/// <remarks>
	/// Writing through the world it runs against is the whole logging
	/// contract: no static logger and no editor reference, so the same
	/// code works in a standalone run.
	/// </remarks>
	public void OnStart(RuntimeScene scene)
	{
		scene.Log.Info("Player control ready");
	}

	public void Update(RuntimeScene scene, float deltaSeconds)
	{
		float horizontal = scene.Input.Horizontal;
		float vertical = scene.Input.Vertical;

		if (horizontal == 0f && vertical == 0f)
		{
			return;
		}

		// Only entities that carry the bound component are selected: the
		// camera and the spinning cube share the world and must not move.
		ComponentQuery query = new(
			required: new ComponentSignature(
				ComponentRegistry.Register<PlayerControlState>()));

		foreach (WorldQueryChunk chunk in scene.World.Query(query))
		{
			Span<PlayerControlState> states =
				chunk.GetSpan<PlayerControlState>();

			ReadOnlySpan<Entity> entities = chunk.GetEntities();

			for (int index = 0; index < states.Length; index++)
			{
				PlayerControlState state = states[index];

				ref PositionComponent position =
					ref scene.World.GetComponent<PositionComponent>(
						entities[index]);

				Vector3 next = position.Value +
					(new Vector3(horizontal, 0f, -vertical) *
						(state.Speed * deltaSeconds));

				if (state.Range > 0f)
				{
					next.X = Math.Clamp(next.X, -state.Range, state.Range);
					next.Z = Math.Clamp(next.Z, -state.Range, state.Range);
				}

				position.Value = next;
			}
		}
	}
}

/// <summary>
/// The entry point VOLT discovers when it loads this assembly.
/// </summary>
public sealed class SampleGameModule : IGameplayModule
{
	public void Register(GameplayRegistrar registrar)
	{
		registrar.AddComponent<PlayerControlComponent>();

		registrar.BindComponent<PlayerControlComponent, PlayerControlState>(
			component => new PlayerControlState
			{
				Speed = component.Speed,
				Range = component.Range
			});

		// Without this the runtime inspector can only say the component
		// exists and how large it is: native storage has no field names.
		registrar.DescribeComponent<PlayerControlState>(
			"Player Control",
			state =>
			[
				RuntimeFieldSnapshot.Number("Speed", state.Speed),
				RuntimeFieldSnapshot.Number("Range", state.Range)
			]);

		registrar.AddSystem<PlayerControlSystem>();
	}
}
