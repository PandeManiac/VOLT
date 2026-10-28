using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the frame the scene view draws around a multi-selection: it
/// appears only for more than one object and encloses every one of
/// them.
/// </summary>
public sealed class SelectionBoundsOverlayTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-selection-bounds-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public SelectionBoundsOverlayTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Bounds");
		_session.Tool = EditorTool.Translate;
	}

	public void Dispose()
	{
		_context.Dispose();
		_session.Dispose();

		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file must not fail a completed test.
		}
	}

	[Fact]
	public void SelectionBounds_AppearOnlyForMoreThanOneObject()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		partner.Transform.Position = new Vector3(2f, 0f, 0f);

		SceneViewport viewport = BuildViewport();

		_session.Select(cube.Id);
		Paint();

		Assert.True(
			viewport.SelectionBoundsView.IsEmpty,
			"One object's own outline already is its bounds.");

		_session.SelectMany([cube.Id, partner.Id], cube.Id);
		Paint();

		Assert.False(viewport.SelectionBoundsView.IsEmpty);
	}

	[Fact]
	public void SelectionBounds_EncloseEverySelectedObject()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		partner.Transform.Position = new Vector3(2f, 0f, 0f);

		SceneViewport viewport = BuildViewport();

		_session.SelectMany([cube.Id, partner.Id], cube.Id);
		Paint();

		UiRect extent = viewport.SelectionBoundsView;
		UiPoint cubePixel = ActiveOriginPixel(viewport);

		// The gizmo follows the active object, so swapping which one is
		// active is how the test learns where the other object projects
		// without rebuilding the camera.
		_session.SelectMany([cube.Id, partner.Id], partner.Id);
		Paint();

		UiPoint partnerPixel = ActiveOriginPixel(viewport);

		Assert.False(extent.IsEmpty);
		Assert.True(
			Contains(extent, cubePixel),
			$"The frame {extent} must enclose the first object at {cubePixel}.");

		Assert.True(
			Contains(extent, partnerPixel),
			$"The frame {extent} must enclose the second object at {partnerPixel}.");
	}

	[Fact]
	public void SelectionBounds_AreEmptyWhilePlaying()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		SceneViewport viewport = BuildViewport();

		_session.SelectMany([cube.Id, partner.Id], cube.Id);
		Paint();

		Assert.False(viewport.SelectionBoundsView.IsEmpty);

		_session.StartPlay();

		try
		{
			Paint();

			Assert.True(
				viewport.SelectionBoundsView.IsEmpty,
				"A running game owns its objects, so the editor does not frame them.");
		}

		finally
		{
			_session.Stop();
		}
	}

	private SceneViewport BuildViewport()
	{
		SceneViewport viewport = new(_session);

		_context.Add(viewport);
		_context.BeginFrame(
			0.0,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		_context.Draw();

		return viewport;
	}

	private void Paint()
	{
		_context.BeginFrame(
			0.1,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		_context.Draw();
	}

	private static UiPoint ActiveOriginPixel(SceneViewport viewport)
	{
		GizmoHandleHit handle = viewport.Gizmo.Handles.First(
			candidate => candidate.Handle == GizmoHandle.AxisX);

		return handle.StartPixel;
	}

	private static bool Contains(UiRect rect, UiPoint point)
	{
		const float tolerance = 0.5f;

		return point.X >= rect.X - tolerance &&
			point.X <= rect.Right + tolerance &&
			point.Y >= rect.Y - tolerance &&
			point.Y <= rect.Bottom + tolerance;
	}

	private static SceneEntity FindMesh(SceneDocument scene)
	{
		return scene.Entities.First(
			entity => entity.GetComponent<MeshRendererData>() is not null);
	}
}
