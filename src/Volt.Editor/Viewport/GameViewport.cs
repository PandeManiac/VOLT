using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Rendering;
using Volt.Rendering.Components;
using Volt.Rendering.Transforms;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;
using Volt.Ui.Text;
using Volt.Ui.Widgets;

namespace Volt.Editor.Viewport;

/// <summary>
/// The running game's view, rendered from the runtime world.
/// </summary>
/// <remarks>
/// <para>
/// This view renders the disposable ECS world the same way the scene
/// view renders the authored scene: meshes and depth into an offscreen
/// target, sampled by the UI. The difference is where the data comes
/// from and which camera looks at it, which is exactly what makes the
/// two tabs different views rather than two copies of one image.
/// </para>
/// <para>
/// Gameplay input is only consumed while this view has focus, so
/// typing in the inspector can never drive the game.
/// </para>
/// </remarks>
internal sealed class GameViewport : Widget, IFrameTick
{
	private readonly EditorSession _session;
	private readonly IViewportRenderer? _renderer;
	private readonly ViewportRenderRequest _request = new()
	{
		Kind = ViewportKind.Game,
		ShowGrid = false
	};

	private bool _disposed;
	private float _dpiScale = 1f;

	public GameViewport(
		EditorSession session,
		IViewportRenderer? renderer = null)
	{
		ArgumentNullException.ThrowIfNull(session);

		_session = session;
		_renderer = renderer;

		IsFocusable = true;
		FocusOnPointerPress = true;
		ClipToBounds = true;
		Cursor = UiCursor.Crosshair;

		_session.RuntimeUpdated += OnSessionChanged;
		_session.PlayStateChanged += OnSessionChanged;
	}

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

		PlayState state = _session.PlayState;
		RuntimeScene? runtime = _session.Play?.Runtime;

		if (runtime is null)
		{
			_renderer?.ClearRequest(ViewportKind.Game);

			DrawCenteredText(
				drawList,
				state == PlayState.Stopped
					? "Press Play to run the scene."
					: "No runtime world is available.",
				bounds,
				theme);

			return;
		}

		float aspect = bounds.Width / bounds.Height;

		(CameraComponent camera, Matrix4x4 view) = ResolveCamera(
			runtime,
			aspect);

		PublishRequest(runtime, bounds, view, camera, aspect);

		if (_renderer?.PrepareTexture(
				ViewportKind.Game,
				_renderer.FrameSlot) is UiTextureId texture)
		{
			drawList.AddImage(
				bounds,
				texture,
				new UiRect(0f, 0f, 1f, 1f),
				UiColor.White);
		}

		DrawOverlay(drawList, runtime, bounds, theme);
	}

	protected override bool OnPointerDown(UiPointerEvent pointer)
	{
		Focus();

		// The running game owns the primary action only while this view
		// is the focused one.
		_session.Input.PrimaryAction = true;
		_session.ViewportNavigating = true;

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
		if (key.Modifiers.HasFlag(UiKeyModifiers.Control))
		{
			return false;
		}

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

	private void Unsubscribe()
	{
		_session.RuntimeUpdated -= OnSessionChanged;
		_session.PlayStateChanged -= OnSessionChanged;
	}

	private void OnSessionChanged(EditorSession session)
	{
		InvalidateVisual();
	}

	private bool ApplyKey(UiKey key, bool pressed)
	{
		switch (key)
		{
			case UiKey.A:
				_session.Input.MoveLeft = pressed;
				break;

			case UiKey.D:
				_session.Input.MoveRight = pressed;
				break;

			case UiKey.W:
				_session.Input.MoveForward = pressed;
				break;

			case UiKey.S:
				_session.Input.MoveBackward = pressed;
				break;

			default:
				return false;
		}

		if (!pressed &&
			!_session.Input.MoveLeft &&
			!_session.Input.MoveRight &&
			!_session.Input.MoveForward &&
			!_session.Input.MoveBackward)
		{
			// Once no game key is held, the tool shortcuts are free to
			// use the same letters again.
			_session.ViewportNavigating = false;
		}

		return true;
	}

	private void PublishRequest(
		RuntimeScene runtime,
		UiRect bounds,
		Matrix4x4 view,
		in CameraComponent camera,
		float aspect)
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

		_request.Projection = Volt.Rendering.Cameras.CameraProjection
			.CreatePerspective(
				camera,
				aspect,
				Volt.Rendering.Cameras.ViewportYOrientation.YDown);

		_request.HasContent = true;
		_request.Instances.Clear();

		SceneDocument? scene = _session.Scene;

		_request.Light = ViewportMath.ResolveLight(scene);

		foreach (RuntimeEntityInfo info in runtime.Entities)
		{
			if (!runtime.World.HasComponent<MeshRendererComponent>(
					info.Entity))
			{
				continue;
			}

			_request.Instances.Add(new ViewportMeshInstance(
				ResolveMeshId(scene, info.AuthoredId),
				CreateWorld(runtime, info)));
		}

		_renderer.PublishRequest(_request);
	}

	/// <summary>
	/// Resolves the mesh a runtime entity should be drawn with.
	/// </summary>
	/// <remarks>
	/// The runtime world stores a resource handle, not an asset
	/// reference, so the drawn mesh comes from the authored component
	/// the runtime entity was built from. An authored reference this
	/// build cannot draw falls back to the cube, which keeps a running
	/// game visible instead of silently empty.
	/// </remarks>
	private static string ResolveMeshId(SceneDocument? scene, Guid authoredId)
	{
		if (scene?.Find(authoredId) is not SceneEntity entity)
		{
			return EditorMeshLibrary.Cube;
		}

		MeshRendererData? renderer =
			entity.GetComponent<MeshRendererData>();

		if (renderer is null)
		{
			return EditorMeshLibrary.Cube;
		}

		return EditorMeshLibrary.Resolve(renderer.Mesh) is
			EditorMeshInfo mesh
				? mesh.Id
				: EditorMeshLibrary.Cube;
	}

	private static Matrix4x4 CreateWorld(
		RuntimeScene runtime,
		RuntimeEntityInfo info)
	{
		PositionComponent position =
			runtime.World.GetComponent<PositionComponent>(info.Entity);

		RotationComponent rotation =
			runtime.World.GetComponent<RotationComponent>(info.Entity);

		ScaleComponent scale =
			runtime.World.GetComponent<ScaleComponent>(info.Entity);

		return TransformMatrices.CreateModelMatrix(
			in position,
			in rotation,
			in scale);
	}

	private static (CameraComponent Camera, Matrix4x4 View) ResolveCamera(
		RuntimeScene runtime,
		float aspect)
	{
		if (runtime.Camera is RuntimeCamera authored)
		{
			// Authored field of view is degrees; a projection component
			// works in radians.
			CameraComponent component = new(
				authored.FieldOfView * (MathF.PI / 180f),
				MathF.Max(0.001f, authored.Near),
				MathF.Max(0.002f, authored.Far));

			return (
				component,
				CameraComponent.CreateViewMatrix(
					new PositionComponent(authored.Position),
					new RotationComponent(authored.Rotation)));
		}

		CameraComponent fallback = CameraComponent.Default;

		return (
			fallback,
			CameraComponent.CreateViewMatrix(
				new PositionComponent(new Vector3(6f, 5f, -10f)),
				new RotationComponent(
					Quaternion.CreateFromYawPitchRoll(0.5f, -0.2f, 0f))));
	}

	private void DrawOverlay(
		UiDrawList drawList,
		RuntimeScene runtime,
		UiRect bounds,
		UiTheme theme)
	{
		PlayState state = _session.PlayState;

		UiColor stateColor = state switch
		{
			PlayState.Playing => theme.Palette.Success,
			PlayState.Paused => theme.Palette.Warning,
			_ => theme.Palette.TextMuted
		};

		UiTextLayoutResult status = UiTextLayout.Create(
			$"{state.ToString().ToUpperInvariant()}   " +
			$"tick {runtime.TickCount}   " +
			$"{runtime.Entities.Count} entities   " +
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
			"Click this view, then W A S D to drive input behaviours.",
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
}
