using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Rendering;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Viewport;

/// <summary>
/// The authoring view of the edited scene.
/// </summary>
/// <remarks>
/// <para>
/// The viewport composites an image VOLT rendered with Vulkan: the
/// scene's meshes and the ground grid are drawn into an offscreen
/// render target every frame and sampled by the UI, so what the panel
/// shows is real rendered geometry with depth testing, not a
/// projection drawn with UI primitives. Selection outlines, gizmos,
/// labels, and the heads-up readout are UI overlays drawn on top, in
/// the same coordinate space as the image.
/// </para>
/// <para>
/// The widget owns no GPU resource. It publishes what it wants
/// rendered, and the renderer that owns the targets consumes that
/// request later in the same frame. With no renderer attached the
/// viewport still lays out, navigates, picks, and draws its overlays,
/// which is what makes it testable without a device.
/// </para>
/// </remarks>
internal sealed class SceneViewport : Widget, IFrameTick
{
	/// <summary>
	/// How long the pointer must rest on a chrome control before its
	/// tooltip appears.
	/// </summary>
	private const float TooltipDelaySeconds = 0.45f;

	/// <summary>
	/// How far the pointer must travel before a press on empty space
	/// becomes a box selection rather than a click.
	/// </summary>
	private const float MarqueeThresholdPixels = 3f;

	/// <summary>
	/// The increments the snap readout cycles through.
	/// </summary>
	private static readonly float[] s_snapSteps =
		[0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f];

	private readonly EditorSession _session;
	private readonly IViewportRenderer? _renderer;
	private readonly EditorCamera _camera = new();
	private readonly ViewportGizmo _gizmo = new();
	private readonly ViewportRenderRequest _request = new()
	{
		Kind = ViewportKind.Scene
	};

	private readonly Dictionary<Guid, Matrix4x4> _worldMatrices = [];
	private readonly Dictionary<Guid, TransformData> _dragStarts = [];
	private readonly List<PickTarget> _pickTargets = [];
	private readonly ViewportOverlay _overlay = new();
	private readonly ViewportCompass _compass = new();

	private bool _worldDirty = true;
	private bool _disposed;
	private float _dpiScale = 1f;
	private bool _showGrid = true;
	private bool _chromePressed;
	private string? _hoverText;
	private float _hoverSeconds;
	private ViewportOverlayControl _hoverOverlay;
	private ViewportCompassControl _hoverCompass;
	private ViewportOverlayControl _pressedOverlay;
	private bool _marqueePending;
	private bool _marqueeActive;
	private bool _marqueeAdditive;
	private UiPoint _marqueeStart;
	private UiPoint _marqueeCurrent;
	private UiRect _selectionExtent = UiRect.Empty;

	private bool _looking;
	private bool _panning;
	private bool _moveForward;
	private bool _moveBackward;
	private bool _moveLeft;
	private bool _moveRight;
	private bool _moveUp;
	private bool _moveDown;
	private bool _sprint;

	private GizmoHandle _activeHandle;
	private Guid _dragEntityId;
	private TransformData? _dragBefore;
	private Vector3 _dragOrigin;
	private Vector3 _dragAxisWorld;
	private Vector3 _dragAxisXWorld;
	private Vector3 _dragAxisZWorld;
	private float _dragStartAlong;
	private float _dragStartAlongX;
	private float _dragStartAlongZ;
	private float _dragStartAngle;
	private float _dragAngle;
	private float _dragGizmoLength = 1f;
	private float _dragFacing = 1f;
	private bool _dragMoved;

	/// <summary>
	/// Gets the gizmo geometry built by the last painted frame.
	/// </summary>
	/// <remarks>
	/// Tests drive the same handles the developer sees, so a drag is
	/// asserted through painted geometry rather than a rebuilt copy.
	/// </remarks>
	internal ViewportGizmo Gizmo => _gizmo;

	/// <summary>
	/// Gets the rectangle bracketing the whole selection.
	/// </summary>
	/// <remarks>
	/// Empty when the selection does not need a summary: one object's
	/// own outline already is its bounds, and a running game owns its
	/// objects.
	/// </remarks>
	internal UiRect SelectionBoundsView => _selectionExtent;

	public SceneViewport(
		EditorSession session,
		IViewportRenderer? renderer = null)
	{
		ArgumentNullException.ThrowIfNull(session);

		_session = session;
		_renderer = renderer;

		IsFocusable = true;
		ClipToBounds = true;
		Cursor = UiCursor.Crosshair;

		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
		_session.PlayStateChanged += OnSessionChanged;
	}

	/// <summary>
	/// Gets the authoring camera, exposed so tests can assert
	/// navigation.
	/// </summary>
	internal EditorCamera Camera => _camera;

	/// <inheritdoc />
	public void Tick(double deltaSeconds, float dpiScale)
	{
		if (_disposed)
		{
			return;
		}

		_dpiScale = float.IsFinite(dpiScale) && dpiScale > 0f
			? dpiScale
			: 1f;

		if (_hoverText is not null && deltaSeconds > 0.0)
		{
			_hoverSeconds += (float)deltaSeconds;
		}

		// Camera movement only happens while the look button is held,
		// which is what keeps the tool keys and the movement keys from
		// fighting over W A S D.
		if (!_looking)
		{
			return;
		}

		Vector3 before = _camera.Position;

		_camera.Move(
			_moveForward,
			_moveBackward,
			_moveLeft,
			_moveRight,
			_moveUp,
			_moveDown,
			_sprint,
			(float)deltaSeconds);

		if (_camera.Position != before)
		{
			InvalidateVisual();
		}
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		return new UiSize(
			float.IsFinite(contentAvailable.Width)
				? contentAvailable.Width
				: 480f,
			float.IsFinite(contentAvailable.Height)
				? contentAvailable.Height
				: 320f);
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect bounds = GlobalVisualBounds;

		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			return;
		}

		drawList.AddRect(bounds, theme.Palette.WindowBackground);

		if (_session.Scene is not SceneDocument scene)
		{
			_renderer?.ClearRequest(ViewportKind.Scene);

			DrawCenteredText(
				drawList,
				"Open or create a project to begin.",
				bounds,
				theme);

			return;
		}

		EnsureWorldMatrices(scene);
		EnsurePickTargets(scene);

		float aspect = bounds.Width / bounds.Height;
		Matrix4x4 view = _camera.CreateView();

		// One camera, two orientations: the render target is a Vulkan
		// framebuffer and needs +Y down, while overlay geometry shares
		// the UI's top-left origin and needs the upright mapping.
		Matrix4x4 overlayProjection = _camera.CreateProjection(
			aspect,
			yDown: false);

		Matrix4x4 renderProjection = _camera.CreateProjection(
			aspect,
			yDown: true);

		Matrix4x4 viewProjection = view * overlayProjection;

		PublishRequest(scene, bounds, view, renderProjection);

		if (_renderer?.PrepareTexture(
				ViewportKind.Scene,
				_renderer.FrameSlot) is UiTextureId texture)
		{
			drawList.AddImage(
				bounds,
				texture,
				new UiRect(0f, 0f, 1f, 1f),
				UiColor.White);
		}

		DrawEntities(
			drawList,
			scene,
			bounds,
			viewProjection,
			theme);

		DrawSelectionBounds(drawList, bounds, viewProjection, theme);
		DrawGizmo(drawList, scene, bounds, viewProjection, theme);
		DrawMarquee(drawList, bounds, theme);
		DrawInfoStrip(drawList, scene, bounds, theme);
		DrawChrome(drawList, bounds, theme);
	}

	protected override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();

		// The floating chrome and the compass belong to the viewport,
		// so they are offered the click before navigation and the
		// picking tools claim it.
		if (TryUseChrome(pointer))
		{
			InvalidateVisual();
			return true;
		}

		_session.ViewportNavigating = true;
		InvalidateVisual();

		if (pointer.Button == UiMouseButton.Right)
		{
			_looking = true;
			CapturePointer(pointer.Button);
			return true;
		}

		if (pointer.Button == UiMouseButton.Middle)
		{
			_panning = true;
			CapturePointer(pointer.Button);
			return true;
		}

		if (pointer.Button != UiMouseButton.Left ||
			_session.Scene is not SceneDocument scene)
		{
			return false;
		}

		UiRect bounds = GlobalVisualBounds;

		if (TryBeginGizmoDrag(scene, bounds, pointer))
		{
			CapturePointer(pointer.Button);
			return true;
		}

		if (TryPick(scene, bounds, pointer.GlobalPosition, out Guid entityId))
		{
			_session.Select(
				entityId,
				pointer.Modifiers.HasFlag(UiKeyModifiers.Shift));

			return true;
		}

		// Empty space starts a box selection. The press cannot decide
		// between a box and a clearing click until the pointer moves, so
		// the selection is left alone until the release.
		_marqueePending = true;
		_marqueeActive = false;
		_marqueeAdditive = pointer.Modifiers.HasFlag(UiKeyModifiers.Shift);
		_marqueeStart = ClampToViewport(pointer.GlobalPosition);
		_marqueeCurrent = _marqueeStart;

		CapturePointer(pointer.Button);

		return true;
	}

	protected override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (_looking)
		{
			_camera.Look(pointer.Delta.X, pointer.Delta.Y);
			InvalidateVisual();
			return true;
		}

		if (_panning)
		{
			_camera.Pan(pointer.Delta.X, pointer.Delta.Y);
			InvalidateVisual();
			return true;
		}

		if (_marqueePending)
		{
			_marqueeCurrent = ClampToViewport(pointer.GlobalPosition);

			if (!_marqueeActive &&
				DistanceBetween(_marqueeStart, _marqueeCurrent) >=
					MarqueeThresholdPixels)
			{
				_marqueeActive = true;
			}

			if (_marqueeActive)
			{
				// A lingering hover tooltip would sit on top of the box.
				ClearHover();
				InvalidateVisual();

				return true;
			}
		}

		if (_activeHandle == GizmoHandle.None ||
			pointer.Button != UiMouseButton.Left)
		{
			UpdateHover(pointer.GlobalPosition);

			GizmoHandle hovered = _gizmo
				.Pick(
					pointer.GlobalPosition,
					ViewportMath.GizmoPickTolerancePixels)
				.Handle;

			if (hovered != _hoveredHandle)
			{
				_hoveredHandle = hovered;
				InvalidateVisual();
			}

			return false;
		}

		return ApplyDrag(pointer);
	}

	protected override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (_chromePressed)
		{
			_chromePressed = false;
			_pressedOverlay = ViewportOverlayControl.None;
			ReleasePointer();
			InvalidateVisual();

			return true;
		}

		if (_looking && pointer.Button == UiMouseButton.Right)
		{
			_looking = false;
			_session.ViewportNavigating = false;
			ReleasePointer();
			return true;
		}

		if (_panning && pointer.Button == UiMouseButton.Middle)
		{
			_panning = false;
			_session.ViewportNavigating = false;
			ReleasePointer();
			return true;
		}

		if (pointer.Button == UiMouseButton.Left)
		{
			// A left press owns the camera keys only until it is
			// released, which is what gives the shell its tool hotkeys
			// back after a click in the view.
			_session.ViewportNavigating = false;

			if (_marqueePending)
			{
				EndMarquee();
				ReleasePointer();
				InvalidateVisual();

				return true;
			}
		}

		if (_activeHandle == GizmoHandle.None)
		{
			return false;
		}

		EndDrag();
		ReleasePointer();

		return true;
	}

	protected override bool OnWheel(UiWheelEvent wheel)
	{
		_camera.Zoom(wheel.Delta.Y);
		InvalidateVisual();

		return true;
	}

	protected override bool OnKeyDown(UiKeyEvent key)
	{
		// Modified keys belong to the shell's shortcuts and to text
		// entry, never to the camera.
		if (key.Modifiers.HasFlag(UiKeyModifiers.Control))
		{
			return false;
		}

		switch (key.Key)
		{
			case UiKey.W:
				_moveForward = true;
				break;

			case UiKey.S:
				_moveBackward = true;
				break;

			case UiKey.A:
				_moveLeft = true;
				break;

			case UiKey.D:
				_moveRight = true;
				break;

			case UiKey.Space:
			case UiKey.E:
				_moveUp = true;
				break;

			case UiKey.Q:
				_moveDown = true;
				break;

			case UiKey.F:
				return FocusSelection();

			default:
				return false;
		}

		// Sprint follows the modifier on the movement key itself, because
		// a shift key press is delivered as a modifier rather than as its
		// own key event.
		_sprint = key.Modifiers.HasFlag(UiKeyModifiers.Shift);

		// Movement keys are only consumed while the look button is
		// held, so the same letters still reach the shell's tool
		// shortcuts in ordinary use.
		return _looking;
	}

	protected override bool OnKeyUp(UiKeyEvent key)
	{
		switch (key.Key)
		{
			case UiKey.W:
				_moveForward = false;
				break;

			case UiKey.S:
				_moveBackward = false;
				break;

			case UiKey.A:
				_moveLeft = false;
				break;

			case UiKey.D:
				_moveRight = false;
				break;

			case UiKey.Space:
			case UiKey.E:
				_moveUp = false;
				break;

			case UiKey.Q:
				_moveDown = false;
				break;

			default:
				return false;
		}

		return _looking;
	}

	protected override void OnDetached()
	{
		Unsubscribe();
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Unsubscribe();
		base.Dispose();
	}

	/// <summary>
	/// Frames the selection, or the whole scene when nothing is
	/// selected.
	/// </summary>
	/// <returns>True when the camera moved.</returns>
	internal bool FocusSelection()
	{
		if (_session.Scene is not SceneDocument scene)
		{
			return false;
		}

		EnsureWorldMatrices(scene);
		EnsurePickTargets(scene);

		if (_session.ActiveEntityId is Guid activeId)
		{
			foreach (PickTarget target in _pickTargets)
			{
				if (target.EntityId != activeId)
				{
					continue;
				}

				_camera.Focus(target.BoundsCenter, target.BoundsRadius);
				InvalidateVisual();

				return true;
			}
		}

		_camera.Focus(Vector3.Zero, 4f);
		InvalidateVisual();

		return true;
	}

	private void Unsubscribe()
	{
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
		_session.PlayStateChanged -= OnSessionChanged;
		_session.ViewportNavigating = false;
	}

	private void OnSessionChanged(EditorSession session)
	{
		_worldDirty = true;
		InvalidateVisual();
	}

	private void EnsureWorldMatrices(SceneDocument scene)
	{
		if (!_worldDirty)
		{
			return;
		}

		_worldMatrices.Clear();

		foreach (KeyValuePair<Guid, Matrix4x4> pair in
			ViewportMath.BuildWorldMatrices(scene))
		{
			_worldMatrices.Add(pair.Key, pair.Value);
		}

		_worldDirty = false;
	}

	/// <summary>
	/// Rebuilds the picking boxes for the current scene.
	/// </summary>
	/// <remarks>
	/// Every entity is pickable: one with a mesh uses that mesh's
	/// bounds, and anything else, such as a camera or an empty
	/// transform, uses a small box so it can still be selected in the
	/// viewport.
	/// </remarks>
	private void EnsurePickTargets(SceneDocument scene)
	{
		_pickTargets.Clear();

		foreach (SceneEntity entity in scene.Entities)
		{
			if (!_worldMatrices.TryGetValue(
				entity.Id,
				out Matrix4x4 world))
			{
				continue;
			}

			MeshRendererData? renderer =
				entity.GetComponent<MeshRendererData>();

			EditorMeshInfo? mesh = renderer is not null && renderer.Visible
				? EditorMeshLibrary.Resolve(renderer.Mesh)
				: null;

			Vector3 center = mesh is EditorMeshInfo info
				? info.Center
				: Vector3.Zero;

			Vector3 halfExtent = mesh is EditorMeshInfo bounds
				? bounds.HalfExtent
				: new Vector3(0.25f);

			Vector3 worldCenter = Vector3.Transform(center, world);

			// The bounding radius follows the transform's own axes, so
			// a rotated or scaled object keeps the same fit.
			float radius = MathF.Max(
				Vector3.TransformNormal(
					new Vector3(halfExtent.X, 0f, 0f),
					world).Length(),
				MathF.Max(
					Vector3.TransformNormal(
						new Vector3(0f, halfExtent.Y, 0f),
						world).Length(),
					Vector3.TransformNormal(
						new Vector3(0f, 0f, halfExtent.Z),
						world).Length()));

			_pickTargets.Add(new PickTarget(
				entity.Id,
				world,
				center,
				halfExtent,
				worldCenter,
				MathF.Max(0.05f, radius)));
		}
	}

	private void PublishRequest(
		SceneDocument scene,
		UiRect bounds,
		Matrix4x4 view,
		Matrix4x4 projection)
	{
		if (_renderer is null)
		{
			return;
		}

		_request.PixelWidth = Math.Max(
			1,
			(int)MathF.Round(bounds.Width * _dpiScale));

		_request.PixelHeight = Math.Max(
			1,
			(int)MathF.Round(bounds.Height * _dpiScale));

		_request.View = view;
		_request.Projection = projection;
		_request.ShowGrid = _showGrid;
		_request.HasContent = true;

		_request.Instances.Clear();

		foreach (PickTarget target in _pickTargets)
		{
			SceneEntity? entity = scene.Find(target.EntityId);

			MeshRendererData? renderer =
				entity?.GetComponent<MeshRendererData>();

			if (renderer is null || !renderer.Visible)
			{
				continue;
			}

			if (EditorMeshLibrary.Resolve(renderer.Mesh) is not
				EditorMeshInfo mesh)
			{
				continue;
			}

			_request.Instances.Add(
				new ViewportMeshInstance(mesh.Id, target.World));
		}

		_renderer.PublishRequest(_request);
	}

	private bool TryPick(
		SceneDocument scene,
		UiRect bounds,
		UiPoint pointer,
		out Guid entityId)
	{
		entityId = Guid.Empty;

		if (!TryCreateOverlayRay(
			scene,
			bounds,
			pointer,
			out Vector3 rayOrigin,
			out Vector3 rayDirection))
		{
			return false;
		}

		float nearest = float.MaxValue;
		bool found = false;

		foreach (PickTarget target in _pickTargets)
		{
			if (!ViewportMath.TryIntersectBox(
					target.World,
					target.Center,
					target.HalfExtent,
					rayOrigin,
					rayDirection,
					out float distance))
			{
				continue;
			}

			if (distance >= nearest)
			{
				continue;
			}

			nearest = distance;
			entityId = target.EntityId;
			found = true;
		}

		return found;
	}

	private bool TryCreateOverlayRay(
		SceneDocument scene,
		UiRect bounds,
		UiPoint pointer,
		out Vector3 origin,
		out Vector3 direction)
	{
		origin = default;
		direction = default;

		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			return false;
		}

		float aspect = bounds.Width / bounds.Height;

		Matrix4x4 viewProjection =
			_camera.CreateView() *
			_camera.CreateProjection(aspect, yDown: false);

		return ViewportMath.TryCreateRay(
			viewProjection,
			bounds,
			pointer,
			out origin,
			out direction);
	}

	private bool TryBeginGizmoDrag(
		SceneDocument scene,
		UiRect bounds,
		UiPointerEvent pointer)
	{
		if (!_gizmo.IsVisible ||
			_session.ActiveEntityId is not Guid activeId ||
			scene.Find(activeId) is not SceneEntity entity ||
			!_worldMatrices.TryGetValue(activeId, out Matrix4x4 world))
		{
			return false;
		}

		GizmoHandleHit hit = _gizmo.Pick(
			pointer.GlobalPosition,
			ViewportMath.GizmoPickTolerancePixels);

		if (hit.Handle == GizmoHandle.None)
		{
			return false;
		}

		if (!TryCreateOverlayRay(
			scene,
			bounds,
			pointer.GlobalPosition,
			out Vector3 rayOrigin,
			out Vector3 rayDirection))
		{
			return false;
		}

		Vector3 origin = Vector3.Transform(Vector3.Zero, world);

		// Every object the gesture will move is captured up front, so
		// the whole drag commits as one undo step. A child of another
		// selected object is left out: its parent already carries it.
		_dragStarts.Clear();
		_dragEntityId = activeId;
		_dragBefore = entity.Transform.Clone();

		foreach (SceneEntity selected in SceneSelection.Roots(
			scene,
			_session.Selection))
		{
			_dragStarts[selected.Id] = selected.Transform.Clone();
		}

		_dragOrigin = origin;
		_dragGizmoLength = _gizmo.WorldLength;
		_dragMoved = false;

		Quaternion rotation = ResolveWorldRotation(scene, entity);

		_dragAxisWorld = AxisDirection(hit.Handle, rotation);
		_dragAxisXWorld = AxisDirection(GizmoHandle.AxisX, rotation);
		_dragAxisZWorld = AxisDirection(GizmoHandle.AxisZ, rotation);

		float facing = Vector3.Dot(_dragAxisWorld, _camera.Forward);
		_dragFacing = facing > 0f ? 1f : -1f;

		// Rotation is driven by the angle the pointer sweeps around the
		// gizmo centre, so its own start value is that angle.
		if (_session.Tool == EditorTool.Rotate)
		{
			if (!ViewportMath.TryProject(
					_camera.CreateView() * _camera.CreateProjection(
						bounds.Width / MathF.Max(1f, bounds.Height),
						yDown: false),
					origin,
					bounds,
					out UiPoint centerPixel))
			{
				return false;
			}

			_dragStartAngle = AngleAround(
				pointer.GlobalPosition,
				centerPixel);

			_dragAngle = 0f;
			_activeHandle = hit.Handle;

			return true;
		}

		if (hit.Handle == GizmoHandle.PlaneXZ)
		{
			if (!ViewportMath.TryProjectOntoAxis(
					rayOrigin,
					rayDirection,
					_camera.Position,
					origin,
					_dragAxisXWorld,
					out _dragStartAlongX) ||
				!ViewportMath.TryProjectOntoAxis(
					rayOrigin,
					rayDirection,
					_camera.Position,
					origin,
					_dragAxisZWorld,
					out _dragStartAlongZ))
			{
				return false;
			}
		}

		else if (!ViewportMath.TryProjectOntoAxis(
			rayOrigin,
			rayDirection,
			_camera.Position,
			origin,
			_dragAxisWorld,
			out _dragStartAlong))
		{
			return false;
		}

		_activeHandle = hit.Handle;

		return true;
	}

	private bool ApplyDrag(UiPointerEvent pointer)
	{
		if (_session.Scene is not SceneDocument scene ||
			_dragBefore is not TransformData before ||
			scene.Find(_dragEntityId) is not SceneEntity entity)
		{
			return false;
		}

		UiRect bounds = GlobalVisualBounds;

		if (!TryCreateOverlayRay(
			scene,
			bounds,
			pointer.GlobalPosition,
			out Vector3 rayOrigin,
			out Vector3 rayDirection))
		{
			return false;
		}

		TransformData next = before.Clone();

		bool applied = _session.Tool switch
		{
			EditorTool.Translate => ApplyTranslate(
				scene,
				entity,
				before,
				next,
				rayOrigin,
				rayDirection),
			EditorTool.Rotate => ApplyRotate(
				scene,
				before,
				next,
				bounds,
				pointer.GlobalPosition),
			EditorTool.Scale => ApplyScale(
				before,
				next,
				rayOrigin,
				rayDirection),
			_ => false
		};

		if (!applied)
		{
			return false;
		}

		_dragMoved = true;
		_worldDirty = true;

		ApplyGesture(scene, before, next);

		return true;
	}

	/// <summary>
	/// Replays one object's drag delta on the rest of the selection.
	/// </summary>
	/// <remarks>
	/// The edited object lands on the exact transform its own drag math
	/// produced, snapping included, while every other object keeps its
	/// own authored transform plus that delta. The result is previewed
	/// directly and committed once the drag ends.
	/// </remarks>
	private void ApplyGesture(
		SceneDocument scene,
		TransformData before,
		TransformData after)
	{
		TransformDelta delta = TransformDelta.Between(before, after);

		foreach (KeyValuePair<Guid, TransformData> start in _dragStarts)
		{
			if (scene.Find(start.Key) is not SceneEntity entity)
			{
				continue;
			}

			if (start.Key == _dragEntityId)
			{
				entity.Transform.CopyFrom(after);
			}

			else
			{
				TransformData next = start.Value.Clone();
				delta.ApplyTo(next);
				entity.Transform.CopyFrom(next);
			}

			scene.NotifyTransformChanged(entity.Id);
		}

		InvalidateVisual();
	}

	private bool ApplyTranslate(
		SceneDocument scene,
		SceneEntity entity,
		TransformData before,
		TransformData next,
		Vector3 rayOrigin,
		Vector3 rayDirection)
	{
		Vector3 worldDelta;

		if (_activeHandle == GizmoHandle.PlaneXZ)
		{
			if (!ViewportMath.TryProjectOntoAxis(
					rayOrigin,
					rayDirection,
					_camera.Position,
					_dragOrigin,
					_dragAxisXWorld,
					out float alongX) ||
				!ViewportMath.TryProjectOntoAxis(
					rayOrigin,
					rayDirection,
					_camera.Position,
					_dragOrigin,
					_dragAxisZWorld,
					out float alongZ))
			{
				return false;
			}

			worldDelta =
				(_dragAxisXWorld * (alongX - _dragStartAlongX)) +
				(_dragAxisZWorld * (alongZ - _dragStartAlongZ));
		}

		else
		{
			if (!ViewportMath.TryProjectOntoAxis(
					rayOrigin,
					rayDirection,
					_camera.Position,
					_dragOrigin,
					_dragAxisWorld,
					out float along))
			{
				return false;
			}

			worldDelta = _dragAxisWorld * (along - _dragStartAlong);
		}

		Vector3 localDelta = worldDelta;

		if (entity.ParentId is Guid parentId &&
			_worldMatrices.TryGetValue(parentId, out Matrix4x4 parentWorld) &&
			Matrix4x4.Invert(parentWorld, out Matrix4x4 parentInverse))
		{
			localDelta = Vector3.TransformNormal(worldDelta, parentInverse);
		}

		Vector3 position = before.Position;

		if (_session.GridSnapping)
		{
			position = SnapVector(localDelta, _session.SnapStep) + position;
		}

		else
		{
			position += localDelta;
		}

		next.Position = position;

		return true;
	}

	private bool ApplyRotate(
		SceneDocument scene,
		TransformData before,
		TransformData next,
		UiRect bounds,
		UiPoint pointer)
	{
		float aspect = bounds.Width / MathF.Max(1f, bounds.Height);

		if (!ViewportMath.TryProject(
				_camera.CreateView() *
				_camera.CreateProjection(aspect, yDown: false),
				Vector3.Transform(Vector3.Zero, _worldMatrices[_dragEntityId]),
				bounds,
				out UiPoint centerPixel))
		{
			return false;
		}

		float angle = AngleAround(pointer, centerPixel);
		float step = angle - _dragStartAngle;

		// Unwrap so a drag that crosses the -X axis keeps turning in
		// the direction the pointer moved.
		if (step > MathF.PI)
		{
			step -= MathF.Tau;
		}

		else if (step < -MathF.PI)
		{
			step += MathF.Tau;
		}

		_dragStartAngle = angle;
		_dragAngle += step;

		float degrees = _dragAngle * _dragFacing * (180f / MathF.PI);

		if (_session.GridSnapping)
		{
			float increment = MathF.Max(1f, _session.SnapStep * 45f);
			degrees = MathF.Round(degrees / increment) * increment;
		}

		Vector3 euler = before.EulerAngles;

		// A drag turns the Euler angle its axis corresponds to. Local
		// and world mode therefore differ only in which axis the handle
		// names, which keeps a single drag predictable.
		euler = _activeHandle switch
		{
			GizmoHandle.AxisX => new Vector3(
				WrapDegrees(euler.X + degrees),
				euler.Y,
				euler.Z),

			GizmoHandle.AxisY => new Vector3(
				euler.X,
				WrapDegrees(euler.Y + degrees),
				euler.Z),

			GizmoHandle.AxisZ => new Vector3(
				euler.X,
				euler.Y,
				WrapDegrees(euler.Z + degrees)),

			_ => euler
		};

		next.EulerAngles = euler;

		return true;
	}

	private bool ApplyScale(
		TransformData before,
		TransformData next,
		Vector3 rayOrigin,
		Vector3 rayDirection)
	{
		if (!ViewportMath.TryProjectOntoAxis(
				rayOrigin,
				rayDirection,
				_camera.Position,
				_dragOrigin,
				_dragAxisWorld,
				out float along))
		{
			return false;
		}

		float factor = 1f + ((along - _dragStartAlong) /
			MathF.Max(0.001f, _dragGizmoLength));

		if (_session.GridSnapping)
		{
			factor = MathF.Round(factor * 10f) / 10f;
		}

		factor = MathF.Max(0.01f, factor);

		Vector3 scale = before.Scale;

		scale = _activeHandle switch
		{
			GizmoHandle.AxisX => new Vector3(
				MathF.Max(0.01f, scale.X * factor),
				scale.Y,
				scale.Z),

			GizmoHandle.AxisY => new Vector3(
				scale.X,
				MathF.Max(0.01f, scale.Y * factor),
				scale.Z),

			GizmoHandle.AxisZ => new Vector3(
				scale.X,
				scale.Y,
				MathF.Max(0.01f, scale.Z * factor)),

			_ => scale
		};

		next.Scale = scale;

		return true;
	}

	private void EndDrag()
	{
		bool moved = _dragMoved;

		_activeHandle = GizmoHandle.None;
		_dragMoved = false;

		if (moved && _dragStarts.Count > 0)
		{
			// A whole gesture becomes one undo step: the preview edited
			// the authored transforms directly, and committing records
			// where each of them started.
			_session.CommitTransforms(_dragStarts);
		}

		_dragStarts.Clear();
		_dragBefore = null;
		_worldDirty = true;
	}

	private void DrawEntities(
		UiDrawList drawList,
		SceneDocument scene,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		foreach (PickTarget target in _pickTargets)
		{
			SceneEntity? entity = scene.Find(target.EntityId);

			if (entity is null)
			{
				continue;
			}

			bool selected = _session.Selection.Contains(entity.Id);

			MeshRendererData? renderer =
				entity.GetComponent<MeshRendererData>();

			bool renderable = renderer is not null &&
				renderer.Visible &&
				EditorMeshLibrary.Resolve(renderer.Mesh) is not null;

			if (selected)
			{
				ViewportMath.DrawWireBox(
					drawList,
					target.World,
					viewProjection,
					bounds,
					theme.Palette.Accent,
					target.Center,
					target.HalfExtent,
					1.2f);
			}

			else if (!renderable)
			{
				// Objects the render target cannot show, such as a
				// camera or an empty, still get a marker and a label so
				// they can be seen and picked.
				ViewportMath.DrawWireBox(
					drawList,
					target.World,
					viewProjection,
					bounds,
					theme.Palette.Warning,
					target.Center,
					target.HalfExtent,
					1f);
			}

			if (selected || !renderable)
			{
				DrawEntityLabel(
					drawList,
					viewProjection,
					bounds,
					target,
					entity.Name,
					selected
						? theme.Palette.Accent
						: theme.Palette.TextMuted,
					theme);
			}
		}
	}

	/// <summary>
	/// Paints one frame around the whole selection.
	/// </summary>
	/// <remarks>
	/// The per-object wire boxes answer which objects are selected; this
	/// answers how far the selection reaches, which is what a group
	/// move, rotate, or scale is about to act on. It is drawn as corner
	/// brackets over a faint wash rather than another solid box, so a
	/// summary reads differently from the outlines it encloses, and one
	/// selected object needs no summary at all.
	/// </remarks>
	private void DrawSelectionBounds(
		UiDrawList drawList,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		_selectionExtent = UiRect.Empty;

		if (_session.Selection.Count < 2 || _session.IsPlaying)
		{
			return;
		}

		bool any = false;
		float left = float.MaxValue;
		float top = float.MaxValue;
		float right = float.MinValue;
		float bottom = float.MinValue;

		foreach (PickTarget target in _pickTargets)
		{
			if (!_session.Selection.Contains(target.EntityId) ||
				!TryProjectBounds(
					target,
					viewProjection,
					bounds,
					out UiRect projected))
			{
				continue;
			}

			left = MathF.Min(left, projected.X);
			top = MathF.Min(top, projected.Y);
			right = MathF.Max(right, projected.Right);
			bottom = MathF.Max(bottom, projected.Bottom);
			any = true;
		}

		if (!any)
		{
			return;
		}

		const float padding = 6f;
		const float bracket = 14f;

		UiRect extent = UiRect.FromLtrb(
			left - padding,
			top - padding,
			right + padding,
			bottom + padding).Intersect(bounds);

		if (extent.IsEmpty)
		{
			return;
		}

		UiColor color = theme.Palette.Accent;

		drawList.AddRect(extent, color.ScaleAlpha(0.05f));

		DrawCorner(drawList, new UiPoint(extent.X, extent.Y), 1f, 1f, bracket, color);
		DrawCorner(drawList, new UiPoint(extent.Right, extent.Y), -1f, 1f, bracket, color);
		DrawCorner(drawList, new UiPoint(extent.X, extent.Bottom), 1f, -1f, bracket, color);
		DrawCorner(drawList, new UiPoint(extent.Right, extent.Bottom), -1f, -1f, bracket, color);

		UiTextLayoutResult label = UiTextLayout.Create(
			$"{_session.Selection.Count} objects",
			theme.Fonts.Small,
			MathF.Max(0f, extent.Width - 8f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			label,
			new UiPoint(
				extent.X + 2f,
				MathF.Max(bounds.Y + 1f, extent.Y - label.Height - 2f)),
			theme.Palette.TextMuted);

		_selectionExtent = extent;
	}

	/// <summary>
	/// Draws one L-shaped bracket of a selection frame.
	/// </summary>
	private static void DrawCorner(
		UiDrawList drawList,
		UiPoint corner,
		float xDirection,
		float yDirection,
		float length,
		UiColor color)
	{
		drawList.AddLine(
			corner,
			new UiPoint(corner.X + (xDirection * length), corner.Y),
			1.6f,
			color);

		drawList.AddLine(
			corner,
			new UiPoint(corner.X, corner.Y + (yDirection * length)),
			1.6f,
			color);
	}

	private static void DrawEntityLabel(
		UiDrawList drawList,
		in Matrix4x4 viewProjection,
		UiRect bounds,
		PickTarget target,
		string name,
		UiColor color,
		UiTheme theme)
	{
		if (!ViewportMath.TryProject(
			viewProjection,
			target.BoundsCenter,
			bounds,
			out UiPoint screen))
		{
			return;
		}

		UiTextLayoutResult label = UiTextLayout.Create(
			name,
			theme.Fonts.Small,
			180f,
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			label,
			new UiPoint(
				screen.X + 8f,
				screen.Y - label.Height - 2f),
			color);
	}

	private void DrawGizmo(
		UiDrawList drawList,
		SceneDocument scene,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		if (_session.ActiveEntityId is not Guid activeId ||
			scene.Find(activeId) is not SceneEntity active ||
			!_worldMatrices.TryGetValue(activeId, out Matrix4x4 world) ||
			_session.IsPlaying)
		{
			// A running game owns its objects; the gizmo would only
			// fight the simulation for them.
			_gizmo.Build(
				EditorTool.Select,
				Vector3.Zero,
				Quaternion.Identity,
				local: false,
				scale: 1f,
				viewProjection,
				bounds,
				worldPerPixel: 0f);

			return;
		}

		Vector3 origin = Vector3.Transform(Vector3.Zero, world);
		Quaternion rotation = ResolveWorldRotation(scene, active);

		float distance = Vector3.Distance(_camera.Position, origin);

		float worldPerPixel = ViewportMath.WorldPerPixel(
			_camera.FieldOfView,
			bounds.Height,
			distance);

		float scale = MathF.Max(
			MathF.Abs(active.Transform.Scale.X),
			MathF.Max(
				MathF.Abs(active.Transform.Scale.Y),
				MathF.Abs(active.Transform.Scale.Z)));

		if (!_gizmo.Build(
				_session.Tool,
				origin,
				rotation,
				_session.LocalTransform,
				scale,
				viewProjection,
				bounds,
				worldPerPixel))
		{
			return;
		}

		foreach (GizmoHandleHit handle in _gizmo.Handles)
		{
			if (!handle.Visible)
			{
				continue;
			}

			UiColor color = handle.Handle switch
			{
				GizmoHandle.AxisX => theme.Palette.GridAxisX,
				GizmoHandle.AxisY => theme.Palette.Success,
				GizmoHandle.AxisZ => theme.Palette.GridAxisZ,
				_ => theme.Palette.Warning
			};

			bool isActive =
				handle.Handle == _activeHandle ||
				handle.Handle == _hoveredHandle;

			if (isActive)
			{
				color = theme.Palette.AccentHover;
			}

			float thickness = isActive ? 3.5f : 2f;

			if (handle.Handle == GizmoHandle.PlaneXZ)
			{
				drawList.AddRoundedRect(
					new UiRect(
						MathF.Min(handle.StartPixel.X, handle.EndPixel.X),
						MathF.Min(handle.StartPixel.Y, handle.EndPixel.Y),
						MathF.Abs(handle.EndPixel.X - handle.StartPixel.X) + 4f,
						MathF.Abs(handle.EndPixel.Y - handle.StartPixel.Y) + 4f),
					2f,
					color);

				continue;
			}

			drawList.AddLine(
				handle.StartPixel,
				handle.EndPixel,
				thickness,
				color);

			DrawGizmoTip(
				drawList,
				handle,
				_session.Tool,
				color);
		}

		if (ViewportMath.TryProject(
			viewProjection,
			origin,
			bounds,
			out UiPoint center))
		{
			drawList.AddRoundedRect(
				new UiRect(center.X - 3f, center.Y - 3f, 6f, 6f),
				2f,
				theme.Palette.Warning);
		}
	}

	private static void DrawGizmoTip(
		UiDrawList drawList,
		GizmoHandleHit handle,
		EditorTool tool,
		UiColor color)
	{
		float size = tool == EditorTool.Scale ? 4f : 3f;

		// Translation ends in a cone, rotation in a circle, and scale
		// in a square, which is what makes the active tool readable
		// without reading the toolbar.
		if (tool == EditorTool.Scale)
		{
			drawList.AddRect(
				new UiRect(
					handle.EndPixel.X - size,
					handle.EndPixel.Y - size,
					size * 2f,
					size * 2f),
				color);

			return;
		}

		if (tool == EditorTool.Rotate)
		{
			drawList.AddRoundedRect(
				new UiRect(
					handle.EndPixel.X - size,
					handle.EndPixel.Y - size,
					size * 2f,
					size * 2f),
				size,
				color);

			return;
		}

		drawList.AddRoundedRect(
			new UiRect(
				handle.EndPixel.X - 2f,
				handle.EndPixel.Y - 2f,
				5f,
				5f),
			2f,
			color);
	}

	/// <summary>
	/// Draws the readout that hugs the bottom edge: the scene, the
	/// active mode, and the hint line.
	/// </summary>
	private void DrawInfoStrip(
		UiDrawList drawList,
		SceneDocument scene,
		UiRect bounds,
		UiTheme theme)
	{
		string mode = _session.Tool switch
		{
			EditorTool.Translate => "Move",
			EditorTool.Rotate => "Rotate",
			EditorTool.Scale => "Scale",
			_ => "Select"
		};

		string header =
			$"{scene.Name}   {mode}" +
			(_session.LocalTransform ? " (local)" : " (world)") +
			(_session.GridSnapping
				? $"   snap {_session.SnapStep:0.###}"
				: string.Empty);

		if (_session.IsPlaying)
		{
			header = $"PLAY MODE  ·  {header}";
		}

		UiTextLayoutResult hint = UiTextLayout.Create(
			"RMB look + WASD/QE fly  ·  MMB pan  ·  wheel zoom  ·  " +
			"F frame  ·  LMB select or box select  ·  " +
			"compass snaps the view",
			theme.Fonts.Small,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		UiTextLayoutResult title = UiTextLayout.Create(
			header,
			theme.Fonts.Small,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			title,
			new UiPoint(
				bounds.X + 10f,
				bounds.Bottom - hint.Height - title.Height - 12f),
			_session.IsPlaying
				? theme.Palette.Warning
				: theme.Palette.TextMuted);

		drawList.AddText(
			hint,
			new UiPoint(
				bounds.X + 10f,
				bounds.Bottom - hint.Height - 8f),
			theme.Palette.TextDisabled);
	}

	/// <summary>
	/// Gets whether the ground grid is shown beneath the scene.
	/// </summary>
	internal bool ShowGrid => _showGrid;

	/// <summary>
	/// Gets the floating chrome's current layout.
	/// </summary>
	internal ViewportOverlay OverlayView => _overlay;

	/// <summary>
	/// Gets the orientation compass's current geometry.
	/// </summary>
	internal ViewportCompass CompassView => _compass;

	/// <summary>
	/// Gets the label of the chrome control under the pointer.
	/// </summary>
	internal string? HoverText => _hoverText;

	/// <summary>
	/// Gets whether the hovered control has earned its tooltip.
	/// </summary>
	internal bool IsTooltipVisible =>
		_hoverText is not null && _hoverSeconds >= TooltipDelaySeconds;

	/// <summary>
	/// Gets whether the view has an active camera look or drag.
	/// </summary>
	private bool IsInteracting =>
		_looking ||
		_panning ||
		_marqueeActive ||
		_activeHandle != GizmoHandle.None;

	/// <summary>
	/// Finishes a press on empty space: a drag selects what the box
	/// covers, and a click without motion clears the selection.
	/// </summary>
	private void EndMarquee()
	{
		bool dragged = _marqueeActive;
		bool additive = _marqueeAdditive;

		_marqueePending = false;
		_marqueeActive = false;
		_marqueeAdditive = false;

		if (!dragged)
		{
			if (!additive)
			{
				_session.ClearSelection();
			}

			return;
		}

		SelectWithinMarquee(additive);
	}

	/// <summary>
	/// Selects every object whose projected bounds overlap the box.
	/// </summary>
	/// <param name="additive">
	/// True to keep the objects that were already selected.
	/// </param>
	/// <remarks>
	/// Overlap rather than full containment is what makes the gesture
	/// usable in a scene with large objects: a box drawn around the
	/// objects someone means to group should catch the ones that extend
	/// past it, which is also how the box reads on screen.
	/// </remarks>
	private void SelectWithinMarquee(bool additive)
	{
		if (_session.Scene is not SceneDocument scene)
		{
			return;
		}

		UiRect bounds = GlobalVisualBounds;

		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			return;
		}

		EnsureWorldMatrices(scene);
		EnsurePickTargets(scene);

		Matrix4x4 viewProjection =
			_camera.CreateView() *
			_camera.CreateProjection(
				bounds.Width / MathF.Max(1f, bounds.Height),
				yDown: false);

		UiRect box = MarqueeBounds();
		List<Guid> selected = additive ? [.. _session.Selection] : [];

		foreach (PickTarget target in _pickTargets)
		{
			if (selected.Contains(target.EntityId))
			{
				continue;
			}

			MeshRendererData? renderer = scene
				.Find(target.EntityId)?
				.GetComponent<MeshRendererData>();

			// A hidden renderer is not on screen, so a box cannot cover
			// it; objects the render target cannot draw at all, such as
			// cameras and empties, keep their wire marker and stay
			// selectable.
			if (renderer is { Visible: false })
			{
				continue;
			}

			if (!TryProjectBounds(
					target,
					viewProjection,
					bounds,
					out UiRect projected))
			{
				continue;
			}

			if (!projected.Intersect(box).IsEmpty)
			{
				selected.Add(target.EntityId);
			}
		}

		if (selected.Count == 0)
		{
			if (!additive)
			{
				_session.ClearSelection();
			}

			_session.SetStatus("Box selection: nothing inside the box");
			return;
		}

		_session.SelectMany(selected, selected[^1]);

		_session.SetStatus(
			selected.Count == 1
				? "Selected 1 object"
				: $"Selected {selected.Count} objects");
	}

	/// <summary>
	/// Projects an object's oriented bounds into the viewport.
	/// </summary>
	/// <returns>
	/// False when no corner of the box is in front of the camera.
	/// </returns>
	/// <remarks>
	/// Corners behind the camera are dropped rather than wrapped, so a
	/// box straddling the near plane reports the part that is visible.
	/// </remarks>
	private static bool TryProjectBounds(
		in PickTarget target,
		in Matrix4x4 viewProjection,
		UiRect bounds,
		out UiRect projected)
	{
		projected = UiRect.Empty;

		float left = float.MaxValue;
		float top = float.MaxValue;
		float right = float.MinValue;
		float bottom = float.MinValue;
		int corners = 0;

		for (int index = 0; index < 8; index++)
		{
			Vector3 corner = target.Center + new Vector3(
				(index & 1) == 0 ? -target.HalfExtent.X : target.HalfExtent.X,
				(index & 2) == 0 ? -target.HalfExtent.Y : target.HalfExtent.Y,
				(index & 4) == 0 ? -target.HalfExtent.Z : target.HalfExtent.Z);

			if (!ViewportMath.TryProject(
				viewProjection,
				Vector3.Transform(corner, target.World),
				bounds,
				out UiPoint pixel))
			{
				continue;
			}

			left = MathF.Min(left, pixel.X);
			top = MathF.Min(top, pixel.Y);
			right = MathF.Max(right, pixel.X);
			bottom = MathF.Max(bottom, pixel.Y);
			corners++;
		}

		if (corners == 0)
		{
			return false;
		}

		projected = UiRect.FromLtrb(left, top, right, bottom);
		return true;
	}

	/// <summary>
	/// Gets the box being dragged, in viewport pixels.
	/// </summary>
	private UiRect MarqueeBounds()
	{
		return UiRect.FromLtrb(
			MathF.Min(_marqueeStart.X, _marqueeCurrent.X),
			MathF.Min(_marqueeStart.Y, _marqueeCurrent.Y),
			MathF.Max(_marqueeStart.X, _marqueeCurrent.X),
			MathF.Max(_marqueeStart.Y, _marqueeCurrent.Y));
	}

	/// <summary>
	/// Keeps a marquee corner inside the view, so a drag that leaves
	/// the panel still describes a box over the image.
	/// </summary>
	private UiPoint ClampToViewport(UiPoint point)
	{
		UiRect bounds = GlobalVisualBounds;

		return new UiPoint(
			Math.Clamp(point.X, bounds.X, bounds.Right),
			Math.Clamp(point.Y, bounds.Y, bounds.Bottom));
	}

	private static float DistanceBetween(UiPoint left, UiPoint right)
	{
		float dx = right.X - left.X;
		float dy = right.Y - left.Y;

		return MathF.Sqrt((dx * dx) + (dy * dy));
	}

	/// <summary>
	/// Paints the box selection rectangle.
	/// </summary>
	private void DrawMarquee(UiDrawList drawList, UiRect bounds, UiTheme theme)
	{
		if (!_marqueeActive)
		{
			return;
		}

		UiRect box = MarqueeBounds().Intersect(bounds);

		if (box.IsEmpty)
		{
			return;
		}

		drawList.AddRect(box, theme.Palette.Accent.ScaleAlpha(0.14f));
		drawList.AddBorder(box, theme.Palette.Accent, 1.2f);
	}

	/// <summary>
	/// Rebuilds the chrome for the current viewport rectangle.
	/// </summary>
	/// <remarks>
	/// A pointer event can arrive before the frame's paint, so the
	/// layout is derived on demand rather than cached: the controls a
	/// click can hit are always the controls the user can see.
	/// </remarks>
	private void EnsureChrome()
	{
		UiRect bounds = GlobalVisualBounds;

		if (bounds.Width <= 0f || bounds.Height <= 0f)
		{
			return;
		}

		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();

		_overlay.Build(
			bounds,
			_session,
			theme.Metrics,
			theme.Fonts.Body,
			_showGrid,
			_session.IsPlaying);

		_compass.Build(
			bounds,
			_camera,
			MathF.Max(28f, theme.Metrics.ToolButtonSize * 1.5f));
	}

	/// <summary>
	/// Offers a press to the chrome.
	/// </summary>
	/// <returns>
	/// True when the chrome owns the click, which keeps a click on a
	/// control from also selecting whatever the scene has behind it.
	/// </returns>
	private bool TryUseChrome(UiPointerEvent pointer)
	{
		if (pointer.Button != UiMouseButton.Left)
		{
			return false;
		}

		EnsureChrome();

		ViewportCompassControl compass = _compass.Pick(pointer.GlobalPosition);

		if (compass != ViewportCompassControl.None)
		{
			_chromePressed = true;
			CapturePointer(pointer.Button);
			ApplyCompassControl(compass);

			return true;
		}

		ViewportOverlayItem item = _overlay.Pick(pointer.GlobalPosition);

		if (item.Control == ViewportOverlayControl.None)
		{
			return false;
		}

		if (!item.IsEnabled)
		{
			// A control that is shown but inert still owns its click.
			return true;
		}

		_chromePressed = true;
		_pressedOverlay = item.Control;
		CapturePointer(pointer.Button);
		ApplyOverlayControl(item);

		return true;
	}

	private void ApplyOverlayControl(ViewportOverlayItem item)
	{
		switch (item.Control)
		{
			case ViewportOverlayControl.Tool:
				_session.Tool = item.Tool;
				_session.SetStatus($"{EditorToolInfo.Label(item.Tool)} tool");
				break;

			case ViewportOverlayControl.ToggleSpace:
				_session.LocalTransform = !_session.LocalTransform;
				_session.SetStatus(
					_session.LocalTransform
						? "Local gizmo space"
						: "World gizmo space");
				break;

			case ViewportOverlayControl.ToggleSnap:
				_session.GridSnapping = !_session.GridSnapping;
				_session.SetStatus(
					_session.GridSnapping
						? $"Grid snapping on ({_session.SnapStep:0.###})"
						: "Grid snapping off");
				break;

			case ViewportOverlayControl.CycleSnapStep:
				_session.SnapStep = NextSnapStep(_session.SnapStep);
				_session.SetStatus($"Snap increment {_session.SnapStep:0.###}");
				break;

			case ViewportOverlayControl.ToggleGrid:
				_showGrid = !_showGrid;
				_session.SetStatus(
					_showGrid ? "Ground grid shown" : "Ground grid hidden");
				break;

			case ViewportOverlayControl.FrameSelection:
				_session.SetStatus(
					FocusSelection()
						? "Framed the selection"
						: "Nothing to frame");
				break;
		}

		ClearHover();
		InvalidateVisual();
	}

	private void ApplyCompassControl(ViewportCompassControl control)
	{
		Vector3 side = control switch
		{
			ViewportCompassControl.ViewFromX => Vector3.UnitX,
			ViewportCompassControl.ViewFromNegX => -Vector3.UnitX,
			ViewportCompassControl.ViewFromY => Vector3.UnitY,
			ViewportCompassControl.ViewFromNegY => -Vector3.UnitY,
			ViewportCompassControl.ViewFromZ => Vector3.UnitZ,
			ViewportCompassControl.ViewFromNegZ => -Vector3.UnitZ,
			_ => Vector3.Normalize(EditorCamera.DefaultPosition)
		};

		_camera.FrameSide(side, ChromePivot());

		_session.SetStatus(
			control == ViewportCompassControl.Home
				? "Default view"
				: _compass.LabelOf(control));

		_worldDirty = true;
		ClearHover();
		InvalidateVisual();
	}

	/// <summary>
	/// Tracks what the pointer is over, resetting the tooltip delay
	/// whenever the target changes.
	/// </summary>
	private void UpdateHover(UiPoint pointer)
	{
		if (IsInteracting)
		{
			return;
		}

		EnsureChrome();

		ViewportOverlayItem item = _overlay.Pick(pointer);
		bool overControl = item.Control != ViewportOverlayControl.None;

		ViewportOverlayControl overlay = overControl && item.IsEnabled
			? item.Control
			: ViewportOverlayControl.None;

		ViewportCompassControl compass =
			overControl ? ViewportCompassControl.None : _compass.Pick(pointer);

		string? text = overControl
			? item.IsEnabled
				? item.Label
				: $"{item.Label}  ·  unavailable while the scene runs"
			: compass != ViewportCompassControl.None
				? _compass.LabelOf(compass)
				: null;

		Cursor = overlay != ViewportOverlayControl.None ||
			compass != ViewportCompassControl.None
				? UiCursor.Hand
				: UiCursor.Crosshair;

		if (overlay == _hoverOverlay &&
			compass == _hoverCompass &&
			text == _hoverText)
		{
			return;
		}

		_hoverOverlay = overlay;
		_hoverCompass = compass;
		_hoverText = text;
		_hoverSeconds = 0f;

		InvalidateVisual();
	}

	private void ClearHover()
	{
		_hoverOverlay = ViewportOverlayControl.None;
		_hoverCompass = ViewportCompassControl.None;
		_hoverText = null;
		_hoverSeconds = 0f;
	}

	/// <summary>
	/// Gets the point the compass frames: the selection when there is
	/// one, and the scene origin otherwise.
	/// </summary>
	private Vector3 ChromePivot()
	{
		if (_session.ActiveEntityId is Guid activeId)
		{
			foreach (PickTarget target in _pickTargets)
			{
				if (target.EntityId == activeId)
				{
					return target.BoundsCenter;
				}
			}
		}

		return Vector3.Zero;
	}

	/// <summary>
	/// Draws the floating chrome: the tool and display clusters, the
	/// orientation compass, and a tooltip once the pointer rests.
	/// </summary>
	private void DrawChrome(UiDrawList drawList, UiRect bounds, UiTheme theme)
	{
		EnsureChrome();

		_overlay.Render(drawList, theme, _hoverOverlay, _pressedOverlay);
		_compass.Render(drawList, theme, _hoverCompass);

		if (!IsTooltipVisible || _hoverText is null)
		{
			return;
		}

		UiRect anchor = _hoverOverlay != ViewportOverlayControl.None
			? _overlay.BoundsOf(_hoverOverlay)
			: _compass.BoundsOf(_hoverCompass);

		ViewportTooltip.Draw(drawList, theme, anchor, bounds, _hoverText);
	}

	private static float NextSnapStep(float current)
	{
		foreach (float step in s_snapSteps)
		{
			if (step > current + 1e-4f)
			{
				return step;
			}
		}

		return s_snapSteps[0];
	}

	private static void DrawCenteredText(
		UiDrawList drawList,
		string text,
		UiRect bounds,
		UiTheme theme)
	{
		UiTextLayoutResult layout = UiTextLayout.Create(
			text,
			theme.Fonts.Body,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Center);

		drawList.AddText(
			layout,
			new UiPoint(
				bounds.Center.X - (layout.Width * 0.5f),
				bounds.Center.Y - (layout.Height * 0.5f)),
			theme.Palette.TextMuted);
	}

	private static Quaternion ResolveWorldRotation(
		SceneDocument scene,
		SceneEntity entity)
	{
		Quaternion rotation = entity.Transform.Rotation;
		HashSet<Guid> visited = [];

		while (entity.ParentId is Guid parentId &&
			visited.Add(parentId) &&
			scene.Find(parentId) is SceneEntity parent)
		{
			rotation = Quaternion.Normalize(
				Quaternion.Multiply(
					rotation,
					parent.Transform.Rotation));

			entity = parent;
		}

		return rotation;
	}

	private static Vector3 AxisDirection(
		GizmoHandle handle,
		Quaternion rotation)
	{
		Vector3 axis = handle switch
		{
			GizmoHandle.AxisY => Vector3.UnitY,
			GizmoHandle.AxisZ => Vector3.UnitZ,
			_ => Vector3.UnitX
		};

		return Vector3.Normalize(Vector3.Transform(axis, rotation));
	}

	private static float AngleAround(UiPoint pointer, UiPoint center)
	{
		return MathF.Atan2(
			pointer.Y - center.Y,
			pointer.X - center.X);
	}

	private static float WrapDegrees(float degrees)
	{
		float wrapped = degrees % 360f;

		if (wrapped > 180f)
		{
			wrapped -= 360f;
		}

		else if (wrapped < -180f)
		{
			wrapped += 360f;
		}

		return wrapped;
	}

	private static Vector3 SnapVector(Vector3 value, float step)
	{
		if (!float.IsFinite(step) || step <= 0f)
		{
			return value;
		}

		return new Vector3(
			MathF.Round(value.X / step) * step,
			MathF.Round(value.Y / step) * step,
			MathF.Round(value.Z / step) * step);
	}

	/// <summary>
	/// One entity's picking box: its transform, its local bounds, and
	/// the world-space centre and radius used for framing.
	/// </summary>
	private readonly record struct PickTarget(
		Guid EntityId,
		Matrix4x4 World,
		Vector3 Center,
		Vector3 HalfExtent,
		Vector3 BoundsCenter,
		float BoundsRadius);

	/// <summary>
	/// The handle the pointer is over, highlighted while hovered.
	/// </summary>
	private GizmoHandle _hoveredHandle;
}
