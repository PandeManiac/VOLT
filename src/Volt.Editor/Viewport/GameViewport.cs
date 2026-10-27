using System.Numerics;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Runtime;
using Volt.Rendering.Transforms;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Viewport;

/// <summary>
/// Presents the disposable runtime ECS world in the Game tab.
/// </summary>
/// <remarks>
/// As with the Scene preview, this milestone draws runtime transforms
/// and bounds with Volt.Ui geometry rather than binding the scene to
/// a mesh render target. The data is the real runtime world: pausing,
/// stepping, and stopping visibly affect only this view.
/// </remarks>
public sealed class GameViewport : Widget
{
	private readonly EditorSession _session;
	private bool _disposed;

	public GameViewport(EditorSession session)
	{
		ArgumentNullException.ThrowIfNull(session);
		_session = session;

		IsFocusable = true;
		FocusOnPointerPress = true;
		ClipToBounds = true;
		Cursor = UiCursor.Crosshair;

		_session.RuntimeUpdated += OnRuntimeUpdated;
		_session.PlayStateChanged += OnRuntimeUpdated;
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

		RuntimeScene? runtime = _session.Play?.Runtime;

		if (runtime is null)
		{
			DrawMessage(
				drawList,
				"Press Play to run the scene.",
				bounds,
				theme,
				theme.Palette.TextMuted);
			return;
		}

		Matrix4x4 viewProjection = CreateViewProjection(
			runtime.Camera,
			bounds);

		DrawGround(drawList, bounds, viewProjection, theme);

		foreach (RuntimeEntityInfo info in runtime.Entities)
		{
			Matrix4x4 world = GetWorld(runtime, info);

			Vector3 origin = Vector3.Transform(Vector3.Zero, world);

			if (!ViewportMath.TryProject(
				viewProjection,
				origin,
				bounds,
				out UiPoint screen))
			{
				continue;
			}

			bool selected = _session.Selection.Contains(info.AuthoredId);

			if (runtime.World.HasComponent<Volt.Rendering.Components.MeshRendererComponent>(
					info.Entity))
			{
				ViewportMath.DrawWireCube(
					drawList,
					world,
					viewProjection,
					bounds,
					selected
						? theme.Palette.Accent
						: theme.Palette.TextMuted);
			}

			else
			{
				drawList.AddRoundedRect(
					new UiRect(screen.X - 3f, screen.Y - 3f, 6f, 6f),
					2f,
					selected
						? theme.Palette.Accent
						: theme.Palette.BorderStrong);
			}

			UiTextLayoutResult label = UiTextLayout.Create(
				info.Name,
				theme.Fonts.Small,
				160f,
				UiTextWrap.NoWrap,
				UiTextAlignment.Start);

			drawList.AddText(
				label,
				new UiPoint(screen.X + 6f, screen.Y - label.Height),
				selected
					? theme.Palette.Accent
					: theme.Palette.TextMuted);
		}

		PlayState state = _session.PlayState;
		UiColor stateColor = state switch
		{
			PlayState.Playing => theme.Palette.Success,
			PlayState.Paused => theme.Palette.Warning,
			_ => theme.Palette.TextMuted
		};

		UiTextLayoutResult status = UiTextLayout.Create(
			$"{state.ToString().ToUpperInvariant()}  " +
			$"tick {runtime.TickCount}  " +
			$"{runtime.Entities.Count} entities  " +
			$"{runtime.RenderableCount} renderable",
			theme.Fonts.Small,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			status,
			new UiPoint(bounds.X + 10f, bounds.Y + 8f),
			stateColor);

		UiTextLayoutResult controls = UiTextLayout.Create(
			"Click the viewport, then WASD to move input-driven objects.",
			theme.Fonts.Small,
			MathF.Max(0f, bounds.Width - 20f),
			UiTextWrap.NoWrap,
			UiTextAlignment.Start);

		drawList.AddText(
			controls,
			new UiPoint(
				bounds.X + 10f,
				bounds.Bottom - controls.Height - 8f),
			theme.Palette.TextDisabled);
	}

	protected override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();
		_session.Input.PrimaryAction = true;
		CapturePointer(pointer.Button);
		return true;
	}

	protected override bool OnPointerUp(UiPointerEvent pointer)
	{
		_session.Input.PrimaryAction = false;
		ReleasePointer();
		return true;
	}

	protected override bool OnKeyDown(UiKeyEvent key)
	{
		return ApplyKey(key.Key, pressed: true);
	}

	protected override bool OnKeyUp(UiKeyEvent key)
	{
		return ApplyKey(key.Key, pressed: false);
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

	private bool ApplyKey(UiKey key, bool pressed)
	{
		switch (key)
		{
			case UiKey.A:
				_session.Input.MoveLeft = pressed;
				return true;

			case UiKey.D:
				_session.Input.MoveRight = pressed;
				return true;

			case UiKey.W:
				_session.Input.MoveForward = pressed;
				return true;

			case UiKey.S:
				_session.Input.MoveBackward = pressed;
				return true;

			case UiKey.Space:
				_session.Input.PrimaryAction = pressed;
				return true;

			default:
				return false;
		}
	}

	private static Matrix4x4 CreateViewProjection(
		RuntimeCamera? camera,
		UiRect bounds)
	{
		Vector3 position = camera?.Position ??
			new Vector3(0f, 3f, 8f);

		Quaternion rotation = camera?.Rotation ??
			Quaternion.Identity;

		Vector3 forward = Vector3.Transform(
			-Vector3.UnitZ,
			rotation);

		Vector3 up = Vector3.Transform(Vector3.UnitY, rotation);

		if (up.LengthSquared() < 1e-6f)
		{
			up = Vector3.UnitY;
		}

		Matrix4x4 view = Matrix4x4.CreateLookAt(
			position,
			position + forward,
			up);

		float aspect = bounds.Height > 0f
			? bounds.Width / bounds.Height
			: 1f;

		Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
			(camera?.FieldOfView ?? 60f) * (MathF.PI / 180f),
			MathF.Max(0.01f, aspect),
			MathF.Max(0.001f, camera?.Near ?? 0.1f),
			MathF.Max(1f, camera?.Far ?? 1000f));

		return view * projection;
	}

	private static Matrix4x4 GetWorld(
		RuntimeScene runtime,
		RuntimeEntityInfo info)
	{
		PositionComponent position = runtime.World
			.GetComponent<PositionComponent>(info.Entity);

		RotationComponent rotation = runtime.World
			.GetComponent<RotationComponent>(info.Entity);

		ScaleComponent scale = runtime.World
			.GetComponent<ScaleComponent>(info.Entity);

		return TransformMatrices.CreateModelMatrix(
			in position,
			in rotation,
			in scale);
	}

	private static void DrawGround(
		UiDrawList drawList,
		UiRect bounds,
		Matrix4x4 viewProjection,
		UiTheme theme)
	{
		for (int index = -5; index <= 5; index++)
		{
			UiColor color = index == 0
				? theme.Palette.GridAxisZ
				: theme.Palette.Grid;

			if (ViewportMath.TryProject(
					viewProjection,
					new Vector3(index, 0f, -5f),
					bounds,
					out UiPoint start) &&
				ViewportMath.TryProject(
					viewProjection,
					new Vector3(index, 0f, 5f),
					bounds,
					out UiPoint end))
			{
				drawList.AddLine(start, end, 1f, color);
			}

			color = index == 0
				? theme.Palette.GridAxisX
				: theme.Palette.Grid;

			if (ViewportMath.TryProject(
					viewProjection,
					new Vector3(-5f, 0f, index),
					bounds,
					out start) &&
				ViewportMath.TryProject(
					viewProjection,
					new Vector3(5f, 0f, index),
					bounds,
					out end))
			{
				drawList.AddLine(start, end, 1f, color);
			}
		}
	}

	private static void DrawMessage(
		UiDrawList drawList,
		string text,
		UiRect bounds,
		UiTheme theme,
		UiColor color)
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
			color);
	}

	private void OnRuntimeUpdated(EditorSession session)
	{
		InvalidateVisual();
	}

	private void Unsubscribe()
	{
		_session.RuntimeUpdated -= OnRuntimeUpdated;
		_session.PlayStateChanged -= OnRuntimeUpdated;
	}
}
