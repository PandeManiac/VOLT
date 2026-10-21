using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Owns the playable target range's ECS world and its GPU-independent
/// state.
/// </summary>
/// <remarks>
/// <para>
/// Five target entities and one floor entity carry the same spatial and
/// render components as the fixed ECS scene, so the existing extractor
/// turns them into draw records without knowing anything about
/// gameplay. The camera is a seventh entity with projection, position,
/// and rotation components.
/// </para>
/// <para>
/// This type mutates component values only. It never changes the
/// world's structure after construction, so extraction can run every
/// frame against stable archetypes, and it never touches a GPU
/// resource: a hit swaps the entity's mesh handle to an already
/// uploaded alternative and shrinks its scale, and the next extraction
/// reports both.
/// </para>
/// <para>
/// The scene is deliberately free of scoring, round rules, and hit
/// detection. Those live in <see cref="TargetRangeController"/>, which
/// reads the same components this type writes, so gameplay and
/// rendering cannot disagree about where a target is.
/// </para>
/// <para>
/// The world and its entities outlive swapchain generations: a resize
/// recreates GPU resources but never rebuilds or resets the scene.
/// </para>
/// </remarks>
internal sealed class TargetRangeScene : IEcsRenderScene, IDisposable
{
	/// <summary>
	/// Number of targets in one round.
	/// </summary>
	public const int TargetCount = 5;

	/// <summary>
	/// Points awarded for hitting one target.
	/// </summary>
	public const int PointsPerTarget = 100;

	/// <summary>
	/// Score and number of hits when every target is complete.
	/// </summary>
	public const int MaximumScore = TargetCount * PointsPerTarget;

	/// <summary>
	/// Scale multiplier applied to a target that has been hit.
	/// </summary>
	/// <remarks>
	/// The entity's scale is both the authored target radius and the
	/// collision radius, so a completed target is drawn at 55% of its
	/// original size while its scene position is unchanged.
	/// </remarks>
	public const float HitScaleFactor = 0.55f;

	public const float FieldOfViewRadians = MathF.PI / 3.0f;
	public const float NearPlane = 0.1f;
	public const float FarPlane = 100.0f;

	/// <summary>
	/// Handle of the uploaded unhit target sphere.
	/// </summary>
	public static readonly MeshHandle UnhitTargetMesh = new(0, 1);

	/// <summary>
	/// Handle of the uploaded hit target sphere.
	/// </summary>
	public static readonly MeshHandle HitTargetMesh = new(1, 1);

	/// <summary>
	/// Handle of the uploaded range floor.
	/// </summary>
	public static readonly MeshHandle FloorMesh = new(2, 1);

	/// <summary>
	/// Handle of the uploaded aiming reticle.
	/// </summary>
	public static readonly MeshHandle ReticleMesh = new(3, 1);

	/// <summary>
	/// Complete material handle shared by every range entity.
	/// </summary>
	/// <remarks>
	/// As in the ECS scene, this handle proves generational validation
	/// and does not select GPU state: the sample shader colours vertices
	/// directly.
	/// </remarks>
	public static readonly MaterialHandle VertexColorMaterial = new(0, 1);

	/// <summary>
	/// Gets the documented initial camera position.
	/// </summary>
	public static Vector3 InitialCameraPosition { get; } =
		new(0.0f, 1.7f, 8.0f);

	/// <summary>
	/// Gets the documented initial camera yaw, in radians.
	/// </summary>
	public const float InitialCameraYaw = 0.0f;

	/// <summary>
	/// Gets the documented initial camera pitch, in radians.
	/// </summary>
	public const float InitialCameraPitch = 0.0f;

	/// <summary>
	/// Gets the camera entity's projection settings.
	/// </summary>
	public static CameraComponent PerspectiveCamera { get; } =
		new(FieldOfViewRadians, NearPlane, FarPlane);

	/// <summary>
	/// Gets the floor entity's spatial components.
	/// </summary>
	public static SceneTransform FloorTransform { get; } =
		new(
			new PositionComponent(new Vector3(0.0f, 0.0f, -9.0f)),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(20.0f, 1.0f, 21.0f)));

	/// <summary>
	/// Authored target placements and radii.
	/// </summary>
	/// <remarks>
	/// The targets span about 8 to 19 world units from the initial
	/// pose and cover left, right, centre, high, and low positions. All
	/// five are inside the 60-degree vertical field of view from the
	/// start, so a target can be acquired immediately after capturing
	/// the mouse. Radii grow with distance so every target keeps a
	/// projected radius above two degrees, which is about 24 pixels at
	/// 1280x720 and well above the camera's 0.126-degree-per-pixel
	/// look sensitivity.
	/// </remarks>
	private static readonly TargetDefinition[] TargetDefinitions =
	[
		new(new Vector3(-3.0f, 1.7f, 0.0f), 0.55f),
		new(new Vector3(3.2f, 1.7f, -1.0f), 0.55f),
		new(new Vector3(0.0f, 1.6f, -6.0f), 0.60f),
		new(new Vector3(-2.0f, 3.2f, -8.0f), 0.65f),
		new(new Vector3(2.4f, 0.6f, -11.0f), 0.70f)
	];

	private readonly World _world;
	private readonly Entity[] _targetEntities;
	private readonly EcsRenderExtractor _extractor;
	private readonly RenderInstance[] _instances =
		new RenderInstance[TargetCount + 1];

	private bool _disposed;

	public TargetRangeScene(EcsMeshResolver meshes)
	{
		ArgumentNullException.ThrowIfNull(meshes);

		Meshes = meshes;
		_world = new World();
		_targetEntities = new Entity[TargetCount];

		for (int index = 0; index < TargetCount; index++)
		{
			_targetEntities[index] = CreateTargetEntity(index);
		}

		FloorEntity = CreateMeshEntity(FloorTransform, FloorMesh);

		CameraEntity = _world.CreateEntity();

		_world.AddComponent(CameraEntity, PerspectiveCamera);
		_world.AddComponent(
			CameraEntity,
			new PositionComponent(InitialCameraPosition));

		_world.AddComponent(CameraEntity, RotationComponent.Identity);

		_extractor = new EcsRenderExtractor(_world);
	}

	public World World => _world;

	public Entity CameraEntity { get; }

	public Entity FloorEntity { get; }

	/// <summary>
	/// Gets the generational mesh handle resolver.
	/// </summary>
	public EcsMeshResolver Meshes { get; }

	public MeshHandle? OverlayMesh => ReticleMesh;

	/// <summary>
	/// Gets the world-space camera position as the ECS world sees it.
	/// </summary>
	public Vector3 CameraPosition =>
		_world.GetComponent<PositionComponent>(CameraEntity).Value;

	/// <summary>
	/// Gets the camera's world-space forward direction.
	/// </summary>
	/// <remarks>
	/// VOLT's convention is right-handed with -Z forward, so forward is
	/// the entity's rotation applied to -Z. The quaternion is already
	/// normalized by the camera controller; a degenerate rotation falls
	/// back to -Z rather than producing a NaN direction.
	/// </remarks>
	public Vector3 CameraForward
	{
		get
		{
			Quaternion rotation =
				_world.GetComponent<RotationComponent>(CameraEntity).Value;

			Vector3 forward = Vector3.Transform(
				-Vector3.UnitZ,
				rotation);

			return forward.LengthSquared() > 0.0f
				? Vector3.Normalize(forward)
				: -Vector3.UnitZ;
		}
	}

	/// <summary>
	/// Gets one target's authored definition.
	/// </summary>
	public static TargetDefinition GetDefinition(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		if (index >= TargetDefinitions.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return TargetDefinitions[index];
	}

	/// <summary>
	/// Gets one target entity.
	/// </summary>
	public Entity GetTargetEntity(int index)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		if (index >= _targetEntities.Length)
		{
			throw new ArgumentOutOfRangeException(nameof(index));
		}

		return _targetEntities[index];
	}

	/// <summary>
	/// Reads one target's collision sphere from its components.
	/// </summary>
	/// <remarks>
	/// The center is the entity's position and the radius is its
	/// uniform scale. The drawn mesh is a faceted unit sphere whose
	/// vertices reach radius one, so this radius tightly bounds the
	/// visible target instead of being smaller than it.
	/// </remarks>
	public TargetSphere GetTargetSphere(int index)
	{
		Entity entity = GetTargetEntity(index);

		return new TargetSphere(
			Entity: entity,
			Center: _world.GetComponent<PositionComponent>(entity).Value,
			Radius: _world.GetComponent<ScaleComponent>(entity).Value.X,
			Completed: IsTargetCompleted(index));
	}

	/// <summary>
	/// Reports whether one target has already been completed.
	/// </summary>
	public bool IsTargetCompleted(int index)
	{
		Entity entity = GetTargetEntity(index);

		return _world.GetComponent<TargetStateComponent>(entity).State ==
			TargetStateComponent.Completed;
	}

	/// <summary>
	/// Gets one target's current world-space position.
	/// </summary>
	public Vector3 GetTargetPosition(int index)
	{
		Entity entity = GetTargetEntity(index);

		return _world.GetComponent<PositionComponent>(entity).Value;
	}

	/// <summary>
	/// Gets one target's current mesh handle.
	/// </summary>
	public MeshHandle GetTargetMesh(int index)
	{
		Entity entity = GetTargetEntity(index);

		return _world.GetComponent<MeshRendererComponent>(entity).Mesh;
	}

	/// <summary>
	/// Gets one target's current uniform scale.
	/// </summary>
	public float GetTargetScale(int index)
	{
		Entity entity = GetTargetEntity(index);

		return _world.GetComponent<ScaleComponent>(entity).Value.X;
	}

	/// <summary>
	/// Counts the targets completed in this round.
	/// </summary>
	public int CountCompletedTargets()
	{
		int completed = 0;

		for (int index = 0; index < _targetEntities.Length; index++)
		{
			if (IsTargetCompleted(index))
			{
				completed++;
			}
		}

		return completed;
	}

	/// <summary>
	/// Writes the active camera controller's pose into the camera
	/// entity.
	/// </summary>
	/// <remarks>
	/// This runs before anything reads the camera, so the ray used for a
	/// shot is always derived from the components the renderer will use
	/// for the same frame. Writing component values allocates nothing
	/// and performs no structural change.
	/// </remarks>
	public void SyncCamera(CameraController camera)
	{
		ArgumentNullException.ThrowIfNull(camera);

		_world.SetComponent(
			CameraEntity,
			new PositionComponent(camera.Position));

		_world.SetComponent(
			CameraEntity,
			new RotationComponent(camera.CreateRotation()));
	}

	/// <summary>
	/// Applies a hit to one target.
	/// </summary>
	/// <returns>
	/// True when the target changed from live to completed; false when
	/// it was already completed.
	/// </returns>
	/// <remarks>
	/// The visible reaction is component data: the renderer swaps to
	/// the pre-uploaded blue sphere and the scale shrinks about the
	/// target's own centre. No geometry is uploaded, no pipeline is
	/// created, and no structural change occurs.
	/// </remarks>
	public bool TryMarkTargetHit(int index)
	{
		Entity entity = GetTargetEntity(index);

		ref TargetStateComponent state =
			ref _world.GetComponent<TargetStateComponent>(entity);

		if (state.State == TargetStateComponent.Completed)
		{
			return false;
		}

		state.State = TargetStateComponent.Completed;

		_world.SetComponent(
			entity,
			new MeshRendererComponent(
				HitTargetMesh,
				VertexColorMaterial));

		float radius = TargetDefinitions[index].Radius;

		_world.SetComponent(
			entity,
			new ScaleComponent(
				new Vector3(radius * HitScaleFactor)));

		return true;
	}

	/// <summary>
	/// Restores every target to its authored position, size, mesh, and
	/// live state.
	/// </summary>
	/// <remarks>
	/// Restart is a value reset, not a world rebuild: entity identities,
	/// archetypes, and handles survive, so extraction and rendering keep
	/// using the same storage.
	/// </remarks>
	public void RestoreTargets()
	{
		for (int index = 0; index < _targetEntities.Length; index++)
		{
			TargetDefinition definition = TargetDefinitions[index];
			Entity entity = _targetEntities[index];

			_world.SetComponent(entity, new PositionComponent(
				definition.Position));

			_world.SetComponent(entity, RotationComponent.Identity);

			_world.SetComponent(
				entity,
				new ScaleComponent(new Vector3(definition.Radius)));

			_world.SetComponent(
				entity,
				new MeshRendererComponent(
					UnhitTargetMesh,
					VertexColorMaterial));

			_world.SetComponent(
				entity,
				new TargetStateComponent
				{
					State = TargetStateComponent.Live
				});
		}
	}

	/// <summary>
	/// Extracts the renderable entities into the scene's own storage.
	/// </summary>
	/// <remarks>
	/// The returned span is valid until the next extraction. The scene
	/// owns enough capacity for every entity it creates; an overflow is
	/// a defect rather than a silently truncated frame.
	/// </remarks>
	public ReadOnlySpan<RenderInstance> Extract()
	{
		RenderExtractionResult result =
			_extractor.Extract(_instances);

		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"The target range produced {result.RequiredCount} " +
				$"render instances, but its storage holds only " +
				$"{_instances.Length}.");
		}

		return _instances.AsSpan(0, result.WrittenCount);
	}

	/// <summary>
	/// Counts the Vulkan draw commands the extracted records produce.
	/// </summary>
	public int CountDrawCommands()
	{
		int draws = 0;

		foreach (RenderInstance instance in Extract())
		{
			if (!TryResolveMesh(instance.Mesh, out EcsMeshBinding binding))
			{
				throw new InvalidOperationException(
					$"The target range references an unbound mesh " +
					$"handle {instance.Mesh}.");
			}

			draws += binding.Geometry.IndexFormat ==
				MeshIndexFormat.UInt16
				? 2
				: 1;
		}

		return draws;
	}

	public bool TryResolveMesh(
		MeshHandle handle,
		out EcsMeshBinding binding)
	{
		return Meshes.TryResolve(handle, out binding);
	}

	public bool ContainsMaterial(MaterialHandle handle)
	{
		return handle == VertexColorMaterial;
	}

	public Matrix4x4 CreateOverlayTransform(
		int framebufferWidth,
		int framebufferHeight)
	{
		return TargetRangeReticle.CreateOverlayTransform(
			framebufferWidth,
			framebufferHeight);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_world.Dispose();
		_disposed = true;
	}

	private Entity CreateTargetEntity(int index)
	{
		TargetDefinition definition = TargetDefinitions[index];

		Entity entity = _world.CreateEntity();

		_world.AddComponent(
			entity,
			new PositionComponent(definition.Position));

		_world.AddComponent(entity, RotationComponent.Identity);

		_world.AddComponent(
			entity,
			new ScaleComponent(new Vector3(definition.Radius)));

		_world.AddComponent(
			entity,
			new MeshRendererComponent(
				UnhitTargetMesh,
				VertexColorMaterial));

		_world.AddComponent(
			entity,
			new TargetStateComponent
			{
				State = TargetStateComponent.Live
			});

		return entity;
	}

	private Entity CreateMeshEntity(
		in SceneTransform transform,
		MeshHandle mesh)
	{
		Entity entity = _world.CreateEntity();

		_world.AddComponent(entity, transform.Position);
		_world.AddComponent(entity, transform.Rotation);
		_world.AddComponent(entity, transform.Scale);

		_world.AddComponent(entity, new MeshRendererComponent(
			mesh,
			VertexColorMaterial));

		return entity;
	}
}
