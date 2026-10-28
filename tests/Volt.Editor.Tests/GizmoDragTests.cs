using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Input;

namespace Volt.Editor.Tests;

/// <summary>
/// Drives real gizmo drags through the viewport: press a painted
/// handle, move the pointer, release, and assert the authored
/// transform, the undo history, and the multi-selection result.
/// </summary>
/// <remarks>
/// The viewport is laid out and painted by a real <see cref="UiContext"/>
/// without a window or a GPU, so the handles under test are the ones
/// the developer would see rather than a rebuilt copy of them.
/// </remarks>
public sealed class GizmoDragTests : IDisposable
{
	private static readonly UiSize Surface = new(1200f, 800f);

	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-gizmo-drag-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();
	private readonly SceneViewport _viewport;

	public GizmoDragTests()
	{
		Directory.CreateDirectory(_root);

		_session.NewProject(Path.Combine(_root, "Game"), "Gizmo");
		_session.CreateScene("Scenes/Drag.volt.scene.json", "Drag");

		_viewport = new SceneViewport(_session);

		_context.Add(_viewport);
		_context.BeginFrame(0.0, Surface, UiPoint.Zero);

		// Dead ahead of the origin: +X is horizontal and +Y vertical in
		// the viewport, which makes a handle's pixels and its world
		// length directly comparable.
		_viewport.Camera.SetPose(new Vector3(0f, 0f, 10f), 0f, 0f);
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

	private UiRect Bounds => _viewport.GlobalVisualBounds;

	private SceneDocument Scene => _session.Scene!;

	[Fact]
	public void AxisDrag_MovesTheObjectByTheHandleLength_AlongThatAxisOnly()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);
		float length = _viewport.Gizmo.WorldLength;

		// Drag from the gizmo origin to where the handle's tip is drawn:
		// the object must travel the handle's world length, which is what
		// "attached to the handle" means.
		PointerDown(xAxis.StartPixel);
		PointerMove(xAxis.EndPixel);
		PointerUp(xAxis.EndPixel);

		Assert.Equal(length, box.Transform.Position.X, 3);
		Assert.Equal(0f, box.Transform.Position.Y, 4);
		Assert.Equal(0f, box.Transform.Position.Z, 4);
	}

	[Fact]
	public void AxisDrag_IsOneUndoStep_ThatRestoresTheExactTransform()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;
		_session.Undo.Clear();

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		PointerDown(xAxis.StartPixel);
		PointerMove(xAxis.EndPixel);
		PointerMove(Lerp(xAxis.StartPixel, xAxis.EndPixel, 0.9f));
		PointerMove(xAxis.EndPixel);
		PointerUp(xAxis.EndPixel);

		Assert.NotEqual(Vector3.Zero, box.Transform.Position);
		Assert.Equal(1, _session.Undo.UndoCount);

		float moved = box.Transform.Position.X;

		_session.UndoLast();

		Assert.Equal(Vector3.Zero, box.Transform.Position);

		_session.RedoLast();

		Assert.Equal(moved, box.Transform.Position.X, 4);
	}

	[Fact]
	public void CanceledDrag_RestoresTheTransformAndAddsNoUndoStep()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;
		_session.Undo.Clear();

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		PointerDown(xAxis.StartPixel);
		PointerMove(xAxis.EndPixel);

		Assert.NotEqual(Vector3.Zero, box.Transform.Position);

		Assert.True(_context.KeyDown(UiKey.Escape));

		Assert.Equal(Vector3.Zero, box.Transform.Position);
		Assert.False(_session.Undo.CanUndo);
	}

	[Fact]
	public void LocalMode_DragsAlongTheObjectsOwnAxis()
	{
		SceneEntity box = CreateBox();

		// A quarter turn about +Y sends the object's own Z axis to +X
		// while its own X axis points into the screen.
		_session.SetTransform(box.Id, new TransformData
		{
			EulerAngles = new Vector3(0f, 90f, 0f)
		});

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = true;
		_session.Undo.Clear();

		Paint();

		GizmoHandleHit zAxis = Handle(GizmoHandle.AxisZ);

		// The local X handle is aimed at the camera, so the press has to
		// be taken on the visible Z handle rather than at the origin
		// where both start.
		UiPoint press = Lerp(zAxis.StartPixel, zAxis.EndPixel, 0.5f);
		UiPoint moved = new(
			zAxis.EndPixel.X +
				((zAxis.EndPixel.X - zAxis.StartPixel.X) * 0.5f),
			zAxis.EndPixel.Y +
				((zAxis.EndPixel.Y - zAxis.StartPixel.Y) * 0.5f));

		PointerDown(press);
		PointerMove(moved);
		PointerUp(moved);

		Vector3 delta = box.Transform.Position;
		float length = _viewport.Gizmo.WorldLength;

		Assert.Equal(length, delta.X, 3);
		Assert.Equal(0f, delta.Z, 3);
	}

	[Fact]
	public void WorldMode_DragsAlongTheWorldAxis_ForARotatedObject()
	{
		SceneEntity box = CreateBox();

		box.Transform.EulerAngles = new Vector3(0f, 90f, 0f);

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		PointerDown(xAxis.StartPixel);
		PointerMove(xAxis.EndPixel);
		PointerUp(xAxis.EndPixel);

		// World mode ignores the object's own quarter turn: the drag
		// still runs along world +X, which is the bug this test pins.
		Assert.Equal(_viewport.Gizmo.WorldLength, box.Transform.Position.X, 3);
		Assert.Equal(0f, box.Transform.Position.Z, 3);
	}

	[Fact]
	public void PlaneDrag_FollowsThePointerOntoThePlane()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;

		Paint();

		GizmoHandleHit plane = Handle(GizmoHandle.PlaneXY);

		Assert.True(plane.Visible);

		// The camera looks straight down -Z, so the XY plane is square
		// to the view and a world offset covers the same pixels
		// wherever the drag starts on it.
		Vector3 from = Vector3.Zero;
		Vector3 to = new(2f, 1f, 0f);

		Matrix4x4 viewProjection =
			_viewport.Camera.CreateView() *
			_viewport.Camera.CreateProjection(
				Bounds.Width / Bounds.Height,
				yDown: false);

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			from,
			Bounds,
			out UiPoint fromPixel));

		Assert.True(ViewportMath.TryProject(
			viewProjection,
			to,
			Bounds,
			out UiPoint toPixel));

		UiPoint press = plane.Quad.Center;
		UiPoint moved = new(
			press.X + (toPixel.X - fromPixel.X),
			press.Y + (toPixel.Y - fromPixel.Y));

		PointerDown(press);
		PointerMove(moved);
		PointerUp(moved);

		Assert.Equal(2f, box.Transform.Position.X, 2);
		Assert.Equal(1f, box.Transform.Position.Y, 2);
		Assert.Equal(0f, box.Transform.Position.Z, 2);
	}

	[Fact]
	public void ScaleDrag_DoublesTheScaledAxisOnly()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Scale;
		_session.LocalTransform = false;

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		PointerDown(Lerp(xAxis.StartPixel, xAxis.EndPixel, 0.5f));

		// Moving by one handle length turns factor 1 into factor 2.
		UiPoint along = new(
			xAxis.StartPixel.X +
				((xAxis.EndPixel.X - xAxis.StartPixel.X) * 1.5f),
			xAxis.StartPixel.Y +
				((xAxis.EndPixel.Y - xAxis.StartPixel.Y) * 1.5f));

		PointerMove(along);
		PointerUp(along);

		Assert.Equal(2f, box.Transform.Scale.X, 2);
		Assert.Equal(1f, box.Transform.Scale.Y, 4);
		Assert.Equal(1f, box.Transform.Scale.Z, 4);
	}

	[Fact]
	public void ScaleDrag_StopsAtTheMinimumInsteadOfCollapsing()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Scale;
		_session.LocalTransform = false;

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		UiPoint start = Lerp(xAxis.StartPixel, xAxis.EndPixel, 0.5f);

		PointerDown(start);

		// Drag far past the origin: a negative factor must not flip or
		// zero the axis.
		UiPoint beyond = new(
			xAxis.StartPixel.X -
				((xAxis.EndPixel.X - xAxis.StartPixel.X) * 6f),
			xAxis.StartPixel.Y -
				((xAxis.EndPixel.Y - xAxis.StartPixel.Y) * 6f));

		PointerMove(beyond);
		PointerUp(beyond);

		Assert.True(
			box.Transform.Scale.X > 0f,
			"Scale must stay positive instead of collapsing to zero.");
	}

	[Fact]
	public void RotationDrag_TurnsTheWorldOrientationAboutTheHandleAxis()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Rotate;
		_session.LocalTransform = false;

		Paint();

		GizmoHandleHit yAxis = Handle(GizmoHandle.AxisY);

		UiPoint press = Lerp(yAxis.StartPixel, yAxis.EndPixel, 0.9f);
		UiPoint centre = Handle(GizmoHandle.AxisX).StartPixel;

		// Sweep the pointer a quarter turn around the gizmo centre.
		float radius = Distance(press, centre);

		UiPoint moved = new(
			centre.X + radius,
			centre.Y);

		PointerDown(press);
		PointerMove(moved);
		PointerUp(moved);

		// The drag rotates about world Y, so X and Z must stay at zero
		// and the object must actually have turned.
		Assert.Equal(0f, box.Transform.EulerAngles.X, 2);
		Assert.Equal(0f, box.Transform.EulerAngles.Z, 2);
		Assert.True(
			MathF.Abs(box.Transform.EulerAngles.Y) > 1f,
			"The drag must turn the object about its handle's axis.");
	}

	[Fact]
	public void RotationWithARotatedParent_KeepsTheWorldAxis()
	{
		SceneEntity parent = CreateBox();
		parent.Transform.EulerAngles = new Vector3(30f, 0f, 20f);

		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		_session.Tool = EditorTool.Rotate;
		_session.LocalTransform = false;
		_session.Select(child.Id);

		Paint();

		Matrix4x4 before = WorldRotation(Scene, child);

		GizmoHandleHit yAxis = Handle(GizmoHandle.AxisY);
		UiPoint press = Lerp(yAxis.StartPixel, yAxis.EndPixel, 0.9f);
		UiPoint centre = Handle(GizmoHandle.AxisX).StartPixel;

		float radius = Distance(press, centre);

		UiPoint moved = new(centre.X + radius, centre.Y);

		PointerDown(press);
		PointerMove(moved);
		PointerUp(moved);

		Matrix4x4 after = WorldRotation(Scene, child);
		Matrix4x4 delta = RotationDelta(before, after);

		Assert.True(
			MathF.Abs(delta.M12) < 0.05f &&
				MathF.Abs(delta.M21) < 0.05f &&
				MathF.Abs(delta.M32) < 0.05f &&
				MathF.Abs(delta.M23) < 0.05f,
			$"A world-Y drag must not tilt the object: {delta}");
	}

	[Fact]
	public void Snapping_StepsTheWorldDelta()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;
		_session.GridSnapping = true;
		_session.SnapStep = 0.5f;

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		UiPoint press = Lerp(xAxis.StartPixel, xAxis.EndPixel, 0.5f);
		UiPoint moved = new(press.X + 47f, press.Y);

		PointerDown(press);
		PointerMove(moved);
		PointerUp(moved);

		float stepped = box.Transform.Position.X / 0.5f;

		Assert.Equal(
			MathF.Round(stepped),
			stepped,
			3);
	}

	[Fact]
	public void GroupDrag_MovesEveryRootByTheSameWorldDelta()
	{
		SceneEntity first = CreateBox();
		SceneEntity second = CreateBox();

		second.Transform.Position = new Vector3(0f, 2f, 0f);

		_session.SelectMany([first.Id, second.Id], first.Id);

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;
		_session.Undo.Clear();

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		PointerDown(xAxis.StartPixel);
		PointerMove(xAxis.EndPixel);
		PointerUp(xAxis.EndPixel);

		float length = _viewport.Gizmo.WorldLength;

		Assert.Equal(length, first.Transform.Position.X, 3);
		Assert.Equal(length, second.Transform.Position.X, 3);
		Assert.Equal(2f, second.Transform.Position.Y, 4);
		Assert.Equal(1, _session.Undo.UndoCount);
	}

	[Fact]
	public void ParentAndChildSelection_TransformsTheParentOnlyOnce()
	{
		SceneEntity parent = CreateBox();
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		_session.SelectMany([parent.Id, child.Id], parent.Id);

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;
		_session.Undo.Clear();

		Paint();

		GizmoHandleHit xAxis = Handle(GizmoHandle.AxisX);

		PointerDown(xAxis.StartPixel);
		PointerMove(xAxis.EndPixel);
		PointerUp(xAxis.EndPixel);

		// The child keeps its own authored position; it moves because
		// its parent moved, not because it was transformed twice.
		Assert.Equal(Vector3.Zero, child.Transform.Position);
		Assert.NotEqual(Vector3.Zero, parent.Transform.Position);
		Assert.Equal(1, _session.Undo.UndoCount);
	}

	[Fact]
	public void PressingEmptySpace_DoesNotStartATransformDrag()
	{
		SceneEntity box = CreateBox();

		_session.Tool = EditorTool.Translate;
		_session.LocalTransform = false;
		_session.Undo.Clear();

		Paint();

		// Far from the gizmo and from any object.
		UiPoint empty = new(Bounds.X + 12f, Bounds.Bottom - 12f);

		PointerDown(empty);
		PointerMove(new UiPoint(empty.X + 80f, empty.Y - 40f));
		PointerUp(new UiPoint(empty.X + 80f, empty.Y - 40f));

		Assert.Equal(Vector3.Zero, box.Transform.Position);
		Assert.False(_session.Undo.CanUndo);
	}

	private SceneEntity CreateBox()
	{
		SceneEntity entity = _session.CreateEntity("Box")!;

		Paint();

		return entity;
	}

	private GizmoHandleHit Handle(GizmoHandle handle)
	{
		return _viewport.Gizmo.Handles.First(hit => hit.Handle == handle);
	}

	private void Paint()
	{
		_context.BeginFrame(0.0, Surface, UiPoint.Zero);
		_context.Draw();
	}

	private void PointerDown(UiPoint point)
	{
		_context.PointerDown(UiMouseButton.Left, point);
	}

	private void PointerMove(UiPoint point)
	{
		_context.PointerMove(
			point,
			new UiPoint(1f, 1f));
	}

	private void PointerUp(UiPoint point)
	{
		_context.PointerUp(UiMouseButton.Left, point);
	}

	/// <summary>
	/// Builds an object's world rotation straight from rotation
	/// matrices, so the expected value does not depend on the quaternion
	/// multiplication order the engine uses.
	/// </summary>
	private static Matrix4x4 WorldRotation(
		SceneDocument scene,
		SceneEntity entity)
	{
		Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(
			entity.Transform.Rotation);

		HashSet<Guid> visited = [];

		while (entity.ParentId is Guid parentId &&
			visited.Add(parentId) &&
			scene.Find(parentId) is SceneEntity parent)
		{
			rotation *=
				Matrix4x4.CreateFromQuaternion(parent.Transform.Rotation);

			entity = parent;
		}

		return rotation;
	}

	private static Matrix4x4 RotationDelta(
		Matrix4x4 before,
		Matrix4x4 after)
	{
		Assert.True(Matrix4x4.Invert(before, out Matrix4x4 inverse));

		return inverse * after;
	}

	private static UiPoint Lerp(UiPoint start, UiPoint end, float t)
	{
		return new UiPoint(
			start.X + ((end.X - start.X) * t),
			start.Y + ((end.Y - start.Y) * t));
	}

	private static float Distance(UiPoint start, UiPoint end)
	{
		float dx = end.X - start.X;
		float dy = end.Y - start.Y;

		return MathF.Sqrt((dx * dx) + (dy * dy));
	}
}
