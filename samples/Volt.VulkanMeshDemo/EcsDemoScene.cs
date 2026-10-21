using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;

namespace Volt.VulkanMeshDemo;

/// <summary>
/// Owns the opt-in ECS demonstration scene.
/// </summary>
/// <remarks>
/// <para>
/// The scene builds a real <see cref="World"/> whose entities
/// describe the same perspective arrangement as the fixed camera
/// scene. Both triangle entities reference one mesh handle, so the
/// sample uploads a single triangle buffer and draws it twice. The
/// camera is an entity with projection, position, and rotation
/// components; the renderer reads its matrices every frame.
/// </para>
/// <para>
/// The world has application lifetime and is deliberately independent
/// of swapchain generations: a resize recreates GPU resources but
/// never rebuilds or resets the scene. Entities are mutated in place
/// through their components; they are never replaced or re-uploaded.
/// </para>
/// <para>
/// The instance buffer is caller-owned storage reused for every
/// extraction, so steady-state frames do not allocate.
/// </para>
/// </remarks>
internal sealed class EcsDemoScene : IEcsRenderScene, IDisposable
{
	/// <summary>
	/// Handle of the uploaded non-indexed triangle mesh.
	/// </summary>
	public static readonly MeshHandle TriangleMesh = new(0, 1);

	/// <summary>
	/// Handle of the uploaded 16-bit indexed quad mesh.
	/// </summary>
	public static readonly MeshHandle QuadMesh = new(1, 1);

	/// <summary>
	/// Handle of the uploaded 32-bit indexed hexagon mesh.
	/// </summary>
	public static readonly MeshHandle HexagonMesh = new(2, 1);

	/// <summary>
	/// Complete material handle shared by every ECS entity.
	/// </summary>
	public static readonly MaterialHandle VertexColorMaterial =
		new(0, 1);

	/// <summary>
	/// Horizontal shift applied to the first triangle by the script.
	/// </summary>
	public const float ScriptedTriangleShiftX = 0.9f;

	/// <summary>
	/// Horizontal shift applied to the camera by the script.
	/// </summary>
	public const float ScriptedCameraShiftX = 0.3f;

	private const int InstanceCapacity = 8;

	private readonly World _world;
	private readonly EcsRenderExtractor _extractor;
	private readonly RenderInstance[] _instances =
		new RenderInstance[InstanceCapacity];

	private bool _disposed;

	public EcsDemoScene(EcsMeshResolver meshes)
	{
		ArgumentNullException.ThrowIfNull(meshes);

		Meshes = meshes;
		_world = new World();

		TriangleAEntity = CreateMeshEntity(
			CameraTransforms.TriangleATransform,
			TriangleMesh);

		TriangleBEntity = CreateMeshEntity(
			CameraTransforms.TriangleBTransform,
			TriangleMesh);

		QuadEntity = CreateMeshEntity(
			CameraTransforms.QuadTransform,
			QuadMesh);

		HexagonEntity = CreateMeshEntity(
			CameraTransforms.HexagonTransform,
			HexagonMesh);

		CameraEntity = _world.CreateEntity();

		_world.AddComponent(
			CameraEntity,
			CameraTransforms.PerspectiveCamera);

		_world.AddComponent(
			CameraEntity,
			new PositionComponent(CameraTransforms.CameraPosition));

		_world.AddComponent(
			CameraEntity,
			RotationComponent.Identity);

		_extractor = new EcsRenderExtractor(_world);
	}

	/// <summary>
	/// Gets the world that owns the scene entities.
	/// </summary>
	public World World => _world;

	/// <summary>
	/// Gets the generational mesh handle resolver.
	/// </summary>
	public EcsMeshResolver Meshes { get; }

	/// <summary>
	/// Gets the material handle palette.
	/// </summary>
	public EcsMaterialPalette Materials { get; } = new();

	/// <summary>
	/// Gets the camera entity.
	/// </summary>
	public Entity CameraEntity { get; }

	/// <summary>
	/// Gets no overlay mesh: the fixed scene has no reticle.
	/// </summary>
	public MeshHandle? OverlayMesh => null;

	/// <summary>
	/// Resolves a complete generational mesh handle.
	/// </summary>
	public bool TryResolveMesh(
		MeshHandle handle,
		out EcsMeshBinding binding)
	{
		return Meshes.TryResolve(handle, out binding);
	}

	/// <summary>
	/// Reports whether the scene's palette knows a material handle.
	/// </summary>
	public bool ContainsMaterial(MaterialHandle handle)
	{
		return Materials.Contains(handle);
	}

	/// <summary>
	/// Creates the overlay transform. The fixed scene draws no overlay,
	/// so this is the identity and the renderer never records it.
	/// </summary>
	public Matrix4x4 CreateOverlayTransform(
		int framebufferWidth,
		int framebufferHeight)
	{
		return Matrix4x4.Identity;
	}

	public Entity TriangleAEntity { get; }

	public Entity TriangleBEntity { get; }

	public Entity QuadEntity { get; }

	public Entity HexagonEntity { get; }

	/// <summary>
	/// Gets the camera entity's current world position.
	/// </summary>
	public Vector3 CameraPosition =>
		_world.GetComponent<PositionComponent>(CameraEntity).Value;

	/// <summary>
	/// Gets the first triangle entity's current world position.
	/// </summary>
	public Vector3 TriangleAPosition =>
		_world.GetComponent<PositionComponent>(TriangleAEntity).Value;

	/// <summary>
	/// Extracts the renderable entities into the scene's own storage.
	/// </summary>
	/// <remarks>
	/// The returned span is valid until the next extraction. The
	/// scene owns enough capacity for every entity it creates; an
	/// overflow is a defect rather than a silently truncated frame.
	/// </remarks>
	public ReadOnlySpan<RenderInstance> Extract()
	{
		RenderExtractionResult result =
			_extractor.Extract(_instances);

		if (!result.Succeeded)
		{
			throw new InvalidOperationException(
				$"The ECS scene produced {result.RequiredCount} " +
				$"render instances, but its storage holds only " +
				$"{_instances.Length}.");
		}

		return _instances.AsSpan(0, result.WrittenCount);
	}

	/// <summary>
	/// Counts the Vulkan draw commands the extracted records produce.
	/// </summary>
	/// <remarks>
	/// The sample's 16-bit indexed quad keeps its two partial index
	/// ranges, so four renderable entities produce five draws. This
	/// mirrors the recording rule and is reported at startup so a
	/// headless run can prove the split survived the ECS migration.
	/// </remarks>
	public int CountDrawCommands()
	{
		int draws = 0;

		foreach (RenderInstance instance in Extract())
		{
			if (!Meshes.TryResolve(instance.Mesh, out EcsMeshBinding binding))
			{
				throw new InvalidOperationException(
					$"The ECS scene references an unbound mesh " +
					$"handle {instance.Mesh}.");
			}

			draws += binding.Geometry.IndexFormat ==
				MeshIndexFormat.UInt16
				? 2
				: 1;
		}

		return draws;
	}

	/// <summary>
	/// Applies the deterministic scripted component changes.
	/// </summary>
	/// <remarks>
	/// One mesh entity and the camera entity are mutated in place.
	/// Repeating the call sets the same values, so the script remains
	/// deterministic if it advances more than once. No structural
	/// change occurs, and no GPU resource is touched.
	/// </remarks>
	public void ApplyScriptedChange()
	{
		ref PositionComponent triangle =
			ref _world.GetComponent<PositionComponent>(TriangleAEntity);

		triangle.Value = CameraTransforms.TriangleATransform
			.Position.Value +
			new Vector3(ScriptedTriangleShiftX, 0.0f, 0.0f);

		ref PositionComponent camera =
			ref _world.GetComponent<PositionComponent>(CameraEntity);

		camera.Value = CameraTransforms.CameraPosition +
			new Vector3(ScriptedCameraShiftX, 0.0f, 0.0f);
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
