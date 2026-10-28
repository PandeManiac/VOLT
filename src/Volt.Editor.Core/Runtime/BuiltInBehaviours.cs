using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Runtime;

internal sealed class SpinRuntimeBehaviour : IRuntimeBehaviour
{
	private readonly Entity _entity;
	private readonly Vector3 _axis;
	private readonly float _radiansPerSecond;

	public SpinRuntimeBehaviour(
		Entity entity,
		SpinBehaviourData data)
	{
		_entity = entity;
		_axis = data.Axis.LengthSquared() > 1e-8f
			? Vector3.Normalize(data.Axis)
			: Vector3.UnitY;

		_radiansPerSecond =
			data.DegreesPerSecond * (MathF.PI / 180f);
	}

	public void Update(RuntimeScene scene, float deltaSeconds)
	{
		ref RotationComponent rotation =
			ref scene.World.GetComponent<RotationComponent>(_entity);

		Quaternion delta = Quaternion.CreateFromAxisAngle(
			_axis,
			_radiansPerSecond * deltaSeconds);

		rotation.Value = Quaternion.Normalize(
			Quaternion.Multiply(delta, rotation.Value));
	}
}

internal sealed class MoveRuntimeBehaviour : IRuntimeBehaviour
{
	private readonly Entity _entity;
	private readonly Vector3 _axis;
	private readonly Vector3 _basePosition;
	private readonly float _speed;
	private readonly float _range;
	private readonly float _phase;
	private readonly bool _automatic;
	private readonly RuntimeInputState _input;
	private float _distance;

	public MoveRuntimeBehaviour(
		Entity entity,
		MoveBehaviourData data,
		RuntimeInputState input,
		Vector3 initialPosition)
	{
		_entity = entity;
		_axis = data.Axis.LengthSquared() > 1e-8f
			? Vector3.Normalize(data.Axis)
			: Vector3.UnitX;

		_basePosition = initialPosition;

		_speed = data.Speed;
		_range = MathF.Max(0f, data.Range);
		_phase = data.Phase;
		_automatic = data.Automatic;
		_input = input;
		_distance = 0f;
	}

	public void Update(RuntimeScene scene, float deltaSeconds)
	{
		ref PositionComponent position =
			ref scene.World.GetComponent<PositionComponent>(_entity);

		if (_automatic)
		{
			_distance += deltaSeconds;
			float travel = _range > 0f
				? MathF.Sin(
					(_distance * MathF.Max(0.01f, _speed)) + _phase) *
					_range
				: 0f;

			position.Value = _basePosition + (_axis * travel);
			return;
		}

		float input = _axis.X != 0f
			? _input.Horizontal
			: _axis.Z != 0f
				? _input.Vertical
				: 0f;

		position.Value += _axis * (input * _speed * deltaSeconds);
	}
}
