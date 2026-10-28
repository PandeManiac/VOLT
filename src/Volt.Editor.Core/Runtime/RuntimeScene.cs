using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Scenes;
using Volt.Rendering.Components;
using Volt.Rendering.Extraction;
using Volt.Rendering.Resources;

namespace Volt.Editor.Core.Runtime;

/// <summary>
/// A disposable ECS simulation built from an authored scene.
/// </summary>
/// <remarks>
/// The authored document is not stored or mutated here. Runtime
/// entities, components, resource handles, and behaviours are separate
/// values owned by this scene. Stopping Play disposes this object and
/// discards every change.
/// </remarks>
public sealed class RuntimeScene : IDisposable
{
	private static readonly MeshHandle s_demoMesh = new(1, 1);
	private static readonly MaterialHandle s_demoMaterial = new(1, 1);

	private readonly Dictionary<Guid, RuntimeEntityInfo> _entities = [];
	private readonly List<RuntimeEntityInfo> _ordered = [];
	private readonly List<IRuntimeBehaviour> _behaviours = [];
	private readonly List<string> _diagnostics = [];
	private readonly EcsRenderExtractor _extractor;
	private bool _disposed;

	public RuntimeScene(
		SceneDocument source,
		RuntimeInputState? input = null,
		IEnumerable<IRuntimeBehaviourFactory>? behaviourFactories = null,
		Func<string, bool>? assetExists = null)
	{
		ArgumentNullException.ThrowIfNull(source);

		SourceSceneId = source.Id;
		Input = input ?? new RuntimeInputState();
		World = new World();
		_extractor = new EcsRenderExtractor(World);

		IRuntimeBehaviourFactory[] factories =
			behaviourFactories?.ToArray() ?? [];

		Dictionary<Guid, SceneEntity> authored =
			source.Entities.ToDictionary(entity => entity.Id);

		Dictionary<Guid, WorldTransform> worldTransforms = [];

		foreach (SceneEntity entity in source.BuildHierarchyOrder())
		{
			Entity runtimeEntity = World.CreateEntity();

			RuntimeEntityInfo info = new(
				entity.Id,
				entity.Name,
				runtimeEntity,
				entity.ParentId);

			WorldTransform worldTransform = ResolveWorldTransform(
				entity,
				authored,
				worldTransforms);

			worldTransforms[entity.Id] = worldTransform;

			World.AddComponent(
				runtimeEntity,
				new PositionComponent(worldTransform.Position));

			World.AddComponent(
				runtimeEntity,
				new RotationComponent(worldTransform.Rotation));

			World.AddComponent(
				runtimeEntity,
				new ScaleComponent(worldTransform.Scale));

			_entities.Add(entity.Id, info);
			_ordered.Add(info);

			AddRenderer(entity, runtimeEntity, info, assetExists);
			AddBehaviours(
				entity,
				info,
				factories);
		}

		Camera = ResolveCamera(source, worldTransforms);
	}

	public Guid SourceSceneId { get; }

	public World World { get; }

	public RuntimeInputState Input { get; }

	public IReadOnlyList<RuntimeEntityInfo> Entities => _ordered;

	public IReadOnlyList<string> Diagnostics => _diagnostics;

	public RuntimeCamera? Camera { get; }

	public float ElapsedSeconds { get; private set; }

	public int TickCount { get; private set; }

	public int RenderableCount => _extractor.CountRequiredInstances();

	public RuntimeEntityInfo? Find(Guid authoredId)
	{
		return _entities.GetValueOrDefault(authoredId);
	}

	public void Update(float fixedDeltaSeconds)
	{
		ThrowIfDisposed();

		if (!float.IsFinite(fixedDeltaSeconds) ||
			fixedDeltaSeconds <= 0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(fixedDeltaSeconds));
		}

		foreach (IRuntimeBehaviour behaviour in _behaviours)
		{
			behaviour.Update(this, fixedDeltaSeconds);
		}

		ElapsedSeconds += fixedDeltaSeconds;
		TickCount++;
	}

	public RenderExtractionResult ExtractRenderInstances(
		Span<RenderInstance> destination)
	{
		ThrowIfDisposed();
		return _extractor.Extract(destination);
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_behaviours.Clear();
		_entities.Clear();
		_ordered.Clear();
		World.Dispose();
	}

	private void AddRenderer(
		SceneEntity authored,
		Entity runtimeEntity,
		RuntimeEntityInfo info,
		Func<string, bool>? assetExists)
	{
		MeshRendererData? renderer =
			authored.GetComponent<MeshRendererData>();

		if (renderer is null || !renderer.Visible)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(renderer.Mesh))
		{
			_diagnostics.Add(
				$"{authored.Name}: mesh renderer has no mesh asset.");
			return;
		}

		if (assetExists is not null &&
			!renderer.Mesh.StartsWith(
				"builtin:",
				StringComparison.OrdinalIgnoreCase) &&
			!assetExists(renderer.Mesh))
		{
			_diagnostics.Add(
				$"{authored.Name}: missing mesh asset " +
				$"'{renderer.Mesh}'.");
			return;
		}

		World.AddComponent(
			runtimeEntity,
			new MeshRendererComponent(s_demoMesh, s_demoMaterial));
	}

	private void AddBehaviours(
		SceneEntity authored,
		RuntimeEntityInfo info,
		IReadOnlyList<IRuntimeBehaviourFactory> factories)
	{
		foreach (AuthoredComponent component in authored.Components)
		{
			IRuntimeBehaviour? behaviour = null;

			if (component is SpinBehaviourData spin && spin.Enabled)
			{
				behaviour = new SpinRuntimeBehaviour(
					info.Entity,
					spin);
			}

			else if (component is MoveBehaviourData move && move.Enabled)
			{
				behaviour = new MoveRuntimeBehaviour(
					info.Entity,
					move,
					Input,
					World.GetComponent<PositionComponent>(info.Entity).Value);
			}

			else if (component is UnknownComponentData)
			{
				_diagnostics.Add(
					$"{authored.Name}: unknown component " +
					$"'{component.TypeId}' is preserved but not " +
					"simulated.");
			}

			if (behaviour is null)
			{
				foreach (IRuntimeBehaviourFactory factory in factories)
				{
					if (factory.TryCreate(this, info, component, out behaviour))
					{
						break;
					}
				}
			}

			if (behaviour is null)
			{
				continue;
			}

			info.Behaviours.Add(behaviour);
			_behaviours.Add(behaviour);
		}
	}

	private static WorldTransform ResolveWorldTransform(
		SceneEntity entity,
		IReadOnlyDictionary<Guid, SceneEntity> authored,
		IReadOnlyDictionary<Guid, WorldTransform> resolved)
	{
		if (entity.ParentId is not Guid parentId)
		{
			return new WorldTransform(
				entity.Transform.Position,
				entity.Transform.Rotation,
				entity.Transform.Scale);
		}

		WorldTransform parent = resolved.TryGetValue(
			parentId,
			out WorldTransform cached)
			? cached
			: throw new InvalidOperationException(
				$"Parent '{parentId}' was not resolved before " +
				$"'{entity.Name}'.");

		return new WorldTransform(
			Vector3.Transform(entity.Transform.Position, parent.CreateMatrix()),
			Quaternion.Normalize(
				Quaternion.Multiply(parent.Rotation, entity.Transform.Rotation)),
			entity.Transform.Scale * parent.Scale);
	}

	private static RuntimeCamera? ResolveCamera(
		SceneDocument source,
		IReadOnlyDictionary<Guid, WorldTransform> transforms)
	{
		foreach (SceneEntity entity in source.Entities)
		{
			CameraData? camera = entity.GetComponent<CameraData>();

			if (camera is null || !camera.Primary)
			{
				continue;
			}

			WorldTransform transform = transforms[entity.Id];

			return new RuntimeCamera(
				entity.Id,
				transform.Position,
				transform.Rotation,
				camera.FieldOfView,
				camera.Near,
				camera.Far);
		}

		return null;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	private readonly record struct WorldTransform(
		Vector3 Position,
		Quaternion Rotation,
		Vector3 Scale)
	{
		public Matrix4x4 CreateMatrix()
		{
			return Matrix4x4.CreateScale(Scale) *
				Matrix4x4.CreateFromQuaternion(Rotation) *
				Matrix4x4.CreateTranslation(Position);
		}
	}
}

public sealed record RuntimeCamera(
	Guid EntityId,
	Vector3 Position,
	Quaternion Rotation,
	float FieldOfView,
	float Near,
	float Far);
