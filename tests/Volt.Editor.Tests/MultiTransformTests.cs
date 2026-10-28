using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Editor.Viewport;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers editing a multi-selection: one gesture or one field edit
/// changes every selected object, lands in the history as a single
/// step, and leaves unselected objects alone.
/// </summary>
public sealed class MultiTransformTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-multi-transform-{Guid.NewGuid():N}");

	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public MultiTransformTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "Multi");
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
	public void Delta_ReplaysTheEditWithoutReplacingValues()
	{
		TransformData before = new()
		{
			Position = new Vector3(1f, 2f, 3f),
			EulerAngles = new Vector3(0f, 170f, 0f),
			Scale = new Vector3(2f)
		};

		TransformData after = before.Clone();
		after.Position += new Vector3(0f, 1f, 0f);
		after.EulerAngles = new Vector3(0f, 190f, 0f);
		after.Scale = new Vector3(4f);

		TransformDelta delta = TransformDelta.Between(before, after);

		// The shortest turn is +20 degrees, not -340.
		Assert.Equal(20f, delta.EulerAngles.Y, 3);

		TransformData other = new()
		{
			Position = new Vector3(5f, 0f, 0f),
			EulerAngles = new Vector3(0f, 179f, 0f),
			Scale = Vector3.One
		};

		delta.ApplyTo(other);

		Assert.Equal(new Vector3(5f, 1f, 0f), other.Position);
		Assert.Equal(-161f, other.EulerAngles.Y, 3);
		Assert.Equal(new Vector3(2f), other.Scale);
	}

	[Fact]
	public void SelectionEdit_ChangesEverySelectedObjectInOneUndoStep()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		cube.Transform.Position = Vector3.Zero;
		partner.Transform.Position = new Vector3(2f, 0f, 0f);

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		int history = _session.Undo.UndoCount;

		TransformData edited = cube.Transform.Clone();
		edited.Position = new Vector3(0f, 3f, 0f);

		int changed = _session.SetSelectionTransform(cube.Id, edited);

		Assert.Equal(2, changed);
		Assert.Equal(new Vector3(0f, 3f, 0f), cube.Transform.Position);
		Assert.Equal(new Vector3(2f, 3f, 0f), partner.Transform.Position);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.Equal(Vector3.Zero, cube.Transform.Position);
		Assert.Equal(new Vector3(2f, 0f, 0f), partner.Transform.Position);
	}

	[Fact]
	public void SelectionEdit_LeavesUnselectedObjectsAlone()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;
		SceneEntity bystander = _session.CreateEntity("Bystander")!;

		bystander.Transform.Position = new Vector3(0f, 5f, 0f);

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		TransformData edited = cube.Transform.Clone();
		edited.Position += new Vector3(1f, 0f, 0f);

		_session.SetSelectionTransform(cube.Id, edited);

		Assert.Equal(new Vector3(0f, 5f, 0f), bystander.Transform.Position);
	}

	[Fact]
	public void SelectionEdit_DoesNotMoveAChildTwice()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity parent = _session.CreateEntity("Parent")!;
		SceneEntity child = _session.CreateEntity("Child", parent.Id)!;

		parent.Transform.Position = Vector3.Zero;
		child.Transform.Position = new Vector3(1f, 0f, 0f);

		_session.SelectMany([parent.Id, child.Id], parent.Id);

		TransformData edited = parent.Transform.Clone();
		edited.Position = new Vector3(0f, 2f, 0f);

		int changed = _session.SetSelectionTransform(parent.Id, edited);

		Assert.Equal(1, changed);
		Assert.Equal(new Vector3(0f, 2f, 0f), parent.Transform.Position);
		Assert.Equal(new Vector3(1f, 0f, 0f), child.Transform.Position);
	}

	[Fact]
	public void ViewportDrag_MovesEverySelectedObjectAsOneUndoStep()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;
		SceneEntity bystander = _session.CreateEntity("Bystander")!;

		cube.Transform.Position = Vector3.Zero;
		partner.Transform.Position = new Vector3(0f, 2f, 0f);
		bystander.Transform.Position = new Vector3(0f, -6f, 0f);

		_session.Tool = EditorTool.Translate;
		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		SceneViewport viewport = BuildViewport();

		GizmoHandleHit handle = viewport.Gizmo.Handles.First(
			candidate => candidate.Handle == GizmoHandle.AxisX &&
				candidate.Visible);

		UiPoint from = Lerp(handle.StartPixel, handle.EndPixel, 0.85f);

		UiPoint to = Along(
			from,
			handle.StartPixel,
			handle.EndPixel,
			48f);

		Vector3 cubeStart = cube.Transform.Position;
		Vector3 partnerStart = partner.Transform.Position;
		Vector3 bystanderStart = bystander.Transform.Position;
		int history = _session.Undo.UndoCount;

		_context.PointerDown(UiMouseButton.Left, from);
		_context.PointerMove(
			to,
			new UiPoint(to.X - from.X, to.Y - from.Y));
		_context.PointerUp(UiMouseButton.Left, to);

		Vector3 cubeDelta = cube.Transform.Position - cubeStart;
		Vector3 partnerDelta = partner.Transform.Position - partnerStart;

		Assert.True(
			cubeDelta.Length() > 0.05f,
			"The drag must move the object it is aimed at.");

		Assert.Equal(cubeDelta.X, partnerDelta.X, 3);
		Assert.Equal(cubeDelta.Y, partnerDelta.Y, 3);
		Assert.Equal(cubeDelta.Z, partnerDelta.Z, 3);

		Assert.Equal(bystanderStart, bystander.Transform.Position);
		Assert.Equal(history + 1, _session.Undo.UndoCount);

		_session.UndoLast();

		Assert.Equal(cubeStart, cube.Transform.Position);
		Assert.Equal(partnerStart, partner.Transform.Position);
	}

	[Fact]
	public void Inspector_SaysThatEditsApplyToTheWholeSelection()
	{
		SceneDocument scene = _session.Scene!;
		SceneEntity cube = FindMesh(scene);
		SceneEntity partner = _session.CreateEntity("Partner")!;

		InspectorPanel inspector = new(_session);

		_context.Add(inspector);
		_context.BeginFrame(
			0.0,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		_session.SelectMany([cube.Id, partner.Id], cube.Id);

		_context.BeginFrame(
			0.1,
			new UiSize(380f, 800f),
			UiPoint.Zero);

		Assert.Contains(
			Descendants(inspector).OfType<Label>(),
			label => label.Text.Contains(
				"edits apply to all",
				StringComparison.Ordinal));
	}

	private SceneViewport BuildViewport()
	{
		SceneViewport viewport = new(_session);

		_context.Add(viewport);
		_context.BeginFrame(
			0.0,
			new UiSize(960f, 600f),
			UiPoint.Zero);

		// The gizmo is built while painting, exactly as it is in the
		// running editor, so a frame is drawn before it is read.
		_context.Draw();

		return viewport;
	}

	private static SceneEntity FindMesh(SceneDocument scene)
	{
		return scene.Entities.First(
			entity => entity.GetComponent<MeshRendererData>() is not null);
	}

	private static UiPoint Lerp(UiPoint start, UiPoint end, float t)
	{
		return new UiPoint(
			start.X + ((end.X - start.X) * t),
			start.Y + ((end.Y - start.Y) * t));
	}

	private static UiPoint Along(
		UiPoint from,
		UiPoint start,
		UiPoint end,
		float pixels)
	{
		float dx = end.X - start.X;
		float dy = end.Y - start.Y;
		float length = MathF.Max(
			0.001f,
			MathF.Sqrt((dx * dx) + (dy * dy)));

		return new UiPoint(
			from.X + ((dx / length) * pixels),
			from.Y + ((dy / length) * pixels));
	}

	private static IEnumerable<Widget> Descendants(Widget root)
	{
		foreach (Widget child in root.Children)
		{
			yield return child;

			foreach (Widget descendant in Descendants(child))
			{
				yield return descendant;
			}
		}
	}
}
