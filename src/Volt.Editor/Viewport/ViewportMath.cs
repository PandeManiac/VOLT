using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Ui;

namespace Volt.Editor.Viewport;

/// <summary>
/// An orbit camera used by the scene authoring preview.
/// </summary>
internal sealed class OrbitCamera
{
	private Vector3 _target = Vector3.Zero;
	private float _yaw = 45f;
	private float _pitch = -24f;
	private float _distance = 14f;

	public Vector3 Target => _target;

	public float Distance => _distance;

	public Vector3 Position
	{
		get
		{
			float yaw = _yaw * (MathF.PI / 180f);
			float pitch = _pitch * (MathF.PI / 180f);
			float horizontal = MathF.Cos(pitch) * _distance;
			float vertical = MathF.Sin(-pitch) * _distance;

			return _target + new Vector3(
				MathF.Sin(yaw) * horizontal,
				vertical,
				MathF.Cos(yaw) * horizontal);
		}
	}

	public void Orbit(float deltaX, float deltaY)
	{
		_yaw += deltaX * 0.3f;
		_pitch = Math.Clamp(_pitch - (deltaY * 0.3f), -85f, 85f);
	}

	public void Zoom(float wheel)
	{
		_distance = Math.Clamp(
			_distance * MathF.Pow(0.9f, wheel),
			1.5f,
			150f);
	}

	public void Focus(Vector3 target)
	{
		_target = target;
	}

	public Vector3 Forward => Vector3.Normalize(_target - Position);

	public Vector3 Right => Vector3.Normalize(
		Vector3.Cross(Forward, Vector3.UnitY));

	public Vector3 Up => Vector3.Normalize(
		Vector3.Cross(Right, Forward));

	public Matrix4x4 CreateViewProjection(UiRect bounds)
	{
		Matrix4x4 view = Matrix4x4.CreateLookAt(
			Position,
			_target,
			Vector3.UnitY);

		float aspect = bounds.Width > 0f && bounds.Height > 0f
			? bounds.Width / bounds.Height
			: 1f;

		Matrix4x4 projection =
			Matrix4x4.CreatePerspectiveFieldOfView(
				MathF.PI / 3f,
				MathF.Max(0.01f, aspect),
				0.05f,
				1000f);

		return view * projection;
	}
}

internal static class ViewportMath
{
	public static bool TryProject(
		in Matrix4x4 viewProjection,
		Vector3 world,
		UiRect bounds,
		out UiPoint screen)
	{
		Vector4 clip = Vector4.Transform(
			new Vector4(world, 1f),
			viewProjection);

		if (clip.W <= 0.0001f || !float.IsFinite(clip.W))
		{
			screen = default;
			return false;
		}

		float x = clip.X / clip.W;
		float y = clip.Y / clip.W;

		if (x < -1.5f || x > 1.5f || y < -1.5f || y > 1.5f)
		{
			screen = default;
			return false;
		}

		screen = new UiPoint(
			bounds.X + ((x * 0.5f) + 0.5f) * bounds.Width,
			bounds.Y + ((1f - ((y * 0.5f) + 0.5f)) * bounds.Height));

		return true;
	}

	public static Dictionary<Guid, Matrix4x4> BuildWorldMatrices(
		SceneDocument scene)
	{
		Dictionary<Guid, Matrix4x4> matrices = [];

		foreach (SceneEntity entity in scene.BuildHierarchyOrder())
		{
			Matrix4x4 local = CreateLocalMatrix(entity.Transform);
			matrices[entity.Id] = entity.ParentId is Guid parentId &&
				matrices.TryGetValue(parentId, out Matrix4x4 parent)
					? local * parent
					: local;
		}

		return matrices;
	}

	public static Matrix4x4 CreateLocalMatrix(TransformData transform)
	{
		return Matrix4x4.CreateScale(transform.Scale) *
			Matrix4x4.CreateFromQuaternion(transform.Rotation) *
			Matrix4x4.CreateTranslation(transform.Position);
	}

	public static void DrawWireCube(
		Volt.Ui.Rendering.UiDrawList drawList,
		Matrix4x4 world,
		in Matrix4x4 viewProjection,
		UiRect bounds,
		Volt.Ui.UiColor color,
		float size = 1f)
	{
		const int VertexCount = 8;

		Span<Vector3> local = stackalloc Vector3[VertexCount]
		{
			new(-0.5f, -0.5f, -0.5f),
			new(0.5f, -0.5f, -0.5f),
			new(0.5f, 0.5f, -0.5f),
			new(-0.5f, 0.5f, -0.5f),
			new(-0.5f, -0.5f, 0.5f),
			new(0.5f, -0.5f, 0.5f),
			new(0.5f, 0.5f, 0.5f),
			new(-0.5f, 0.5f, 0.5f)
		};

		Span<UiPoint> projected = stackalloc UiPoint[VertexCount];
		Span<bool> visible = stackalloc bool[VertexCount];

		for (int index = 0; index < VertexCount; index++)
		{
			Vector3 worldPoint = Vector3.Transform(
				local[index] * size,
				world);

			visible[index] = TryProject(
				viewProjection,
				worldPoint,
				bounds,
				out projected[index]);
		}

		ReadOnlySpan<int> edges =
		[
			0, 1, 1, 2, 2, 3, 3, 0,
			4, 5, 5, 6, 6, 7, 7, 4,
			0, 4, 1, 5, 2, 6, 3, 7
		];

		for (int index = 0; index < edges.Length; index += 2)
		{
			int from = edges[index];
			int to = edges[index + 1];

			if (!visible[from] || !visible[to])
			{
				continue;
			}

			drawList.AddLine(
				projected[from],
				projected[to],
				1.2f,
				color);
		}
	}
}
