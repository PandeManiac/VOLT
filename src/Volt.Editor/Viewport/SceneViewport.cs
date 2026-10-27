using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Viewport;

/// <summary>
/// An interactive authoring preview of the authored scene.
/// </summary>
/// <remarks>
/// This milestone projects authored transforms, selection, and
/// transform gizmos into a Volt.Ui surface. It deliberately does not
/// pretend to be the final mesh-rendered viewport: binding this widget
/// to a Vulkan offscreen render target is the next graphics milestone.
/// The data it displays and manipulates is the real authored scene.
/// </remarks>
public sealed class SceneViewport : Widget
{
	private readonly EditorSession _session;
	private readonly OrbitCamera _camera = new();
	private readonly List<EntityHit> _hits = [];
	private readonly Dictionary<Guid, Matrix4x4> _worldMatrices = [];

	private bool _worldDirty = true;
	private bool _navigating;
	private bool _dragging;
	private bool _dragMoved;
	private UiPoint _lastPointer;
	private Guid _dragEntityId;
	private TransformData? _gestureBefore;
	private bool _disposed;

	public SceneViewport(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		IsFocusable = true;
		ClipToBounds = true;
		Cursor = UiCursor.Crosshair;

		_session.SceneChanged += OnSessionChanged;
		_session.SelectionChanged += OnSessionChanged;
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

		drawList.AddRect(bounds, theme.Palette.WindowBackground);

		if (_session.Scene is not SceneDocument scene)
		{
			DrawCenteredText(
				drawList,
				"Open or create a project to begin.",
				bounds,
				theme);
			return;
		}

		EnsureWorldMatrices();
		Matrix4x4 viewProjection = _camera.CreateViewProjection(bounds);

		DrawGrid(drawList, bounds, viewProjection, theme);
		DrawEntities(drawList, scene, bounds, viewProjection, theme);
		DrawOverlay(drawList, scene, bounds, theme);
	}

	protected override void OnDetached()
	{
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
	}

	public override void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_session.SceneChanged -= OnSessionChanged;
		_session.SelectionChanged -= OnSessionChanged;
		base.Dispose();
	}

	protected override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();
		_lastPointer = pointer.GlobalPosition;

		if (pointer.Button is UiMouseButton.Middle or UiMouseButton.Right)
		{
			_navigating = true;
			CapturePointer(pointer.Button);
			return true;
		}

		if (pointer.Button != UiMouseButton.Left ||
			_session.Scene is not SceneDocument scene)
		{
			return false;
		}

		EnsureHits();
		EntityHit? hit = HitTest(pointer.GlobalPosition);

		if (hit is null)
		{
			_session.Select(null);
			return true;
		}

		if (!pointer.Modifiers.HasFlag(UiKeyModifiers.Shift) &&
			_session.ActiveEntityId != hit.Value.EntityId)
		{
			_session.Select(hit.Value.EntityId);
			return true;
		}

		SceneEntity? entity = scene.Find(hit.Value.EntityId);

		if (entity is null ||
			_session.Tool == EditorTool.Select ||
			_session.ActiveEntityId != entity.Id)
		{
			return true;
		}

		_dragging = true;
		_dragMoved = false;
		_dragEntityId = entity.Id;
		_gestureBefore = entity.Transform.Clone();
		CapturePointer(pointer.Button);

		return true;
	}

	protected override bool OnPointerMove(UiPointerEvent pointer)
	{
		if (_navigating)
		{
			_camera.Orbit(pointer.Delta.X, pointer.Delta.Y);
			InvalidateVisual();
			return true;
		}

		if (!_dragging ||
			_session.Scene is not SceneDocument scene)
		{
			return false;
		}

		SceneEntity? entity = scene.Find(_dragEntityId);

		if (entity is null)
		{
			return false;
		}

		float dx = pointer.Delta.X;
		float dy = pointer.Delta.Y;

		if (MathF.Abs(dx) + MathF.Abs(dy) < 0.01f)
		{
			return true;
		}

		_dragMoved = true;

		switch (_session.Tool)
		{
			case EditorTool.Translate:
			{
				float scale = _camera.Distance * 0.0025f;
				Vector3 delta =
					(_camera.Right * dx) +
					(_camera.Up * -dy);

				delta *= scale;

				if (_session.GridSnapping)
				{
					delta = Snap(delta, _session.SnapStep);
				}

				entity.Transform.Position += delta;
				break;
			}

			case EditorTool.Rotate:
			{
				Vector3 euler = entity.Transform.EulerAngles;

				euler.Y += dx * 0.6f;
				euler.X = Math.Clamp(
					euler.X - (dy * 0.6f),
					-360f,
					360f);

				if (_session.GridSnapping && _session.SnapStep > 0f)
				{
					float step = MathF.Max(1f, _session.SnapStep * 45f);
					euler.Y = MathF.Round(euler.Y / step) * step;
					euler.X = MathF.Round(euler.X / step) * step;
				}

				entity.Transform.EulerAngles = euler;
				break;
			}

			case EditorTool.Scale:
			{
				float factor = 1f + (dx * 0.01f);

				entity.Transform.Scale = new Vector3(
					MathF.Max(0.001f, entity.Transform.Scale.X * factor),
					MathF.Max(0.001f, entity.Transform.Scale.Y * factor),
					MathF.Max(0.001f, entity.Transform.Scale.Z * factor));
				break;
			}
		}

		_worldDirty = true;
		scene.NotifyTransformChanged(entity.Id);
		InvalidateVisual();

		return true;
	}

	protected override bool OnPointerUp(UiPointerEvent pointer)
	{
		if (_navigating)
		{
			_navigating = false;
			ReleasePointer();
			return true;
		}

		if (!_dragging)
		{
			return false;
		}

		_dragging = false;
		ReleasePointer();

		if (_dragMoved && _gestureBefore is not null)
		{
			_session.CommitTransform(_dragEntityId, _gestureBefore);
		}

		_gestureBefore = null;
		_dragMoved = false;

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
		if (key.Key == UiKey.F &&
			_session.ActiveEntityId is Guid id &&
			_session.Scene is SceneDocument scene)
		{
			SceneEntity? entity = scene.Find(id);

			if (entity is not null)
			{
				EnsureWorldMatrices();

				if (_worldMatrices.TryGetValue(
					entity.Id,
					out Matrix4x4 world))
				{
					_camera.Focus(Vector3.Transform(
						Vector3.Zero,
						world));
					InvalidateVisual();
				}
			}

			return true;
		}

		return false;
	}

	private void DrawGrid(
		UiDrawList drawList,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		const int Extent = 10;
		UiColor minor = theme.Palette.Grid;
		UiColor major = theme.Palette.BorderStrong;

		for (int index = -Extent; index <= Extent; index++)
		{
			UiColor color = index == 0
				? theme.Palette.GridAxisZ
				: index % 5 == 0
					? major
					: minor;

			if (ViewportMath.TryProject(
					viewProjection,
					new Vector3(index, 0f, -Extent),
					bounds,
					out UiPoint start) &&
				ViewportMath.TryProject(
					viewProjection,
					new Vector3(index, 0f, Extent),
					bounds,
					out UiPoint end))
			{
				drawList.AddLine(start, end, 1f, color);
			}

			color = index == 0
				? theme.Palette.GridAxisX
				: index % 5 == 0
					? major
					: minor;

			if (ViewportMath.TryProject(
					viewProjection,
					new Vector3(-Extent, 0f, index),
					bounds,
					out start) &&
				ViewportMath.TryProject(
					viewProjection,
					new Vector3(Extent, 0f, index),
					bounds,
					out end))
			{
				drawList.AddLine(start, end, 1f, color);
			}
		}
	}

	private void DrawEntities(
		UiDrawList drawList,
		SceneDocument scene,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		_hits.Clear();

		foreach (SceneEntity entity in scene.Entities)
		{
			if (!_worldMatrices.TryGetValue(
				entity.Id,
				out Matrix4x4 world))
			{
				continue;
			}

			Vector3 origin = Vector3.Transform(Vector3.Zero, world);

			if (!ViewportMath.TryProject(
				viewProjection,
				origin,
				bounds,
				out UiPoint screen))
			{
				continue;
			}

			bool selected = _session.Selection.Contains(entity.Id);
			MeshRendererData? renderer =
				entity.GetComponent<MeshRendererData>();

			if (renderer is not null && renderer.Visible)
			{
				ViewportMath.DrawWireCube(
					drawList,
					world,
					viewProjection,
					bounds,
					selected
						? theme.Palette.Accent
						: theme.Palette.TextMuted,
					1f);
			}

			else
			{
				drawList.AddRoundedRect(
					new UiRect(
						screen.X - 4f,
						screen.Y - 4f,
						8f,
						8f),
					2f,
					selected
						? theme.Palette.Accent
						: theme.Palette.BorderStrong);
			}

			_hits.Add(new EntityHit(
				entity.Id,
				screen,
				selected));

			if (selected || scene.Entities.Count <= 12)
			{
				UiTextLayoutResult label = UiTextLayout.Create(
					entity.Name,
					theme.Fonts.Small,
					MathF.Max(0f, bounds.Width - 8f),
					UiTextWrap.NoWrap,
					UiTextAlignment.Start);

				drawList.AddText(
					label,
					new UiPoint(
						screen.X + 7f,
						screen.Y - label.Height - 2f),
					selected
						? theme.Palette.Accent
						: theme.Palette.TextMuted);
			}
		}

		if (_session.ActiveEntityId is Guid activeId &&
			scene.Find(activeId) is SceneEntity active &&
			_worldMatrices.TryGetValue(active.Id, out Matrix4x4 activeWorld))
		{
			DrawGizmo(
				drawList,
				active,
				activeWorld,
				bounds,
				viewProjection,
				theme);
		}
	}

	private void DrawGizmo(
		UiDrawList drawList,
		SceneEntity entity,
		Matrix4x4 world,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		Vector3 origin = Vector3.Transform(Vector3.Zero, world);

		if (!ViewportMath.TryProject(
			viewProjection,
			origin,
			bounds,
			out UiPoint center))
		{
			return;
		}

		float length = MathF.Max(
			0.5f,
			_camera.Distance * 0.12f);

		DrawAxis(
			drawList,
			viewProjection,
			bounds,
			center,
			origin,
			Vector3.UnitX,
			length,
			theme.Palette.GridAxisX);

		DrawAxis(
			drawList,
			viewProjection,
			bounds,
			center,
			origin,
			Vector3.UnitY,
			length,
			theme.Palette.Success);

		DrawAxis(
			drawList,
			viewProjection,
			bounds,
			center,
			origin,
			Vector3.UnitZ,
			length,
			theme.Palette.GridAxisZ);

		drawList.AddRoundedRect(
			new UiRect(center.X - 3f, center.Y - 3f, 6f, 6f),
			2f,
			theme.Palette.Warning);
	}

	private static void DrawAxis(
		UiDrawList drawList,
		Matrix4x4 viewProjection,
		UiRect bounds,
		UiPoint center,
		Vector3 origin,
		Vector3 direction,
		float length,
		UiColor color)
	{
		if (!ViewportMath.TryProject(
			viewProjection,
			origin + (direction * length),
			bounds,
			out UiPoint end))
		{
			return;
		}

		drawList.AddLine(center, end, 2f, color);

		float angle = MathF.Atan2(end.Y - center.Y, end.X - center.X);
		const float Arrow = 5f;

		drawList.AddLine(
			end,
			new UiPoint(
				end.X - (MathF.Cos(angle - 0.5f) * Arrow),
				end.Y - (MathF.Sin(angle - 0.5f) * Arrow)),
			2f,
			color);

		drawList.AddLine(
			end,
			new UiPoint(
				end.X - (MathF.Cos(angle + 0.5f) * Arrow),
				end.Y - (MathF.Sin(angle + 0.5f) * Arrow)),
			2f,
			color);
	}

	private void DrawOverlay(
		UiDrawList drawList,
		SceneDocument scene,
		UiRect bounds,
		UiTheme theme)
	{
		string mode = _session.Tool switch
		{
			EditorTool.Translate => "Translate",
			EditorTool.Rotate => "Rotate",
			EditorTool.Scale => "Scale",
			_ => "Select"
		};

		UiTextLayoutResult title = UiTextLayout.Create(
			$"{scene.Name}  |  {mode}" +
			(_session.LocalTransform ? " Local" : " World"),
			theme.Fonts.Small,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			title,
			new UiPoint(bounds.X + 10f, bounds.Y + 8f),
			theme.Palette.TextMuted);

		UiTextLayoutResult hint = UiTextLayout.Create(
			"RMB orbit  ·  Wheel zoom  ·  LMB select/drag",
			theme.Fonts.Small,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			hint,
			new UiPoint(
				bounds.X + 10f,
				bounds.Bottom - hint.Height - 8f),
			theme.Palette.TextDisabled);

		if (_session.ActiveEntity is SceneEntity active)
		{
			UiTextLayoutResult selected = UiTextLayout.Create(
				active.Name,
				theme.Fonts.Body,
				300f,
				UiTextWrap.NoWrap,
				UiTextAlignment.Start);

			UiRect chip = new(
				bounds.X + 8f,
				bounds.Y + 28f,
				selected.Width + 12f,
				selected.Height + 6f);

			drawList.AddRoundedRect(
				chip,
				theme.Metrics.SmallCornerRadius,
				theme.Palette.Selection);

			drawList.AddText(
				selected,
				new UiPoint(chip.X + 6f, chip.Y + 3f),
				theme.Palette.Accent);
		}
	}

	private void OnSessionChanged(EditorSession session)
	{
		_worldDirty = true;
		InvalidateVisual();
	}

	private void EnsureHits()
	{
		if (_session.Scene is not SceneDocument scene)
		{
			_hits.Clear();
			return;
		}

		EnsureWorldMatrices();
		_hits.Clear();
		UiRect bounds = GlobalVisualBounds;
		Matrix4x4 viewProjection = _camera.CreateViewProjection(bounds);

		foreach (SceneEntity entity in scene.Entities)
		{
			if (!_worldMatrices.TryGetValue(
				entity.Id,
				out Matrix4x4 world))
			{
				continue;
			}

			Vector3 origin = Vector3.Transform(Vector3.Zero, world);

			if (ViewportMath.TryProject(
				viewProjection,
				origin,
				bounds,
				out UiPoint screen))
			{
				_hits.Add(new EntityHit(
					entity.Id,
					screen,
					_session.Selection.Contains(entity.Id)));
			}
		}
	}

	private void EnsureWorldMatrices()
	{
		if (!_worldDirty || _session.Scene is not SceneDocument scene)
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

	private EntityHit? HitTest(UiPoint point)
	{
		EntityHit? best = null;
		float bestDistance = 22f;

		foreach (EntityHit hit in _hits)
		{
			float dx = hit.Screen.X - point.X;
			float dy = hit.Screen.Y - point.Y;
			float distance = (dx * dx) + (dy * dy);

			if (distance <= bestDistance * bestDistance)
			{
				bestDistance = MathF.Sqrt(distance);
				best = hit;
			}
		}

		return best;
	}

	private static Vector3 Snap(Vector3 value, float step)
	{
		if (step <= 0f)
		{
			return value;
		}

		return new Vector3(
			MathF.Round(value.X / step) * step,
			MathF.Round(value.Y / step) * step,
			MathF.Round(value.Z / step) * step);
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

	private readonly record struct EntityHit(
		Guid EntityId,
		UiPoint Screen,
		bool Selected);
}
