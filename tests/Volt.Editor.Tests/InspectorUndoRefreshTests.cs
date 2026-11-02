using Volt.Editor.Panels;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Input;
using Volt.Ui.Layout;
using Volt.Ui.Widgets;

namespace Volt.Editor.Tests;

public sealed class InspectorUndoRefreshTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(), $"volt-inspector-undo-{Guid.NewGuid():N}");
	private readonly EditorSession _session = new();
	private readonly UiContext _context = new();

	public InspectorUndoRefreshTests()
	{
		Directory.CreateDirectory(_root);
		_session.NewProject(Path.Combine(_root, "Game"), "UndoRefresh");
		_session.CreateEntity("Target");
	}

	[Fact]
	public void UndoWhileNumericInspectorFieldIsFocused_RefreshesCommittedValue()
	{
		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);
		NumericField positionX = Descendants(inspector)
			.OfType<NumericField>()
			.First();
		Assert.True(positionX.Focus());

		positionX.Value = 12f;
		Assert.Equal(12f, _session.ActiveEntity!.Transform.Position.X);

		_session.UndoLast();

		Assert.Equal(0f, _session.ActiveEntity!.Transform.Position.X);
		Assert.Equal(0f, positionX.Value);
		Assert.True(positionX.HasFocus);
	}

	[Fact]
	public void UndoWhileCommittedAssetFieldRemainsFocused_RefreshesInspectorValue()
	{
		SceneEntity entity = _session.Scene!.Entities.Single(
			candidate => candidate.GetComponent<MeshRendererData>() is not null);
		MeshRendererData renderer = entity.GetComponent<MeshRendererData>()!;
		renderer.Mesh = "Assets/Meshes/old.mesh";
		_session.Select(entity.Id);

		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);
		TextField meshAsset = Descendants(inspector)
			.OfType<TextField>()
			.Single(field => field.Text == "Assets/Meshes/old.mesh");

		Assert.True(meshAsset.Focus());
		meshAsset.Text = "Assets/Meshes/new.mesh";
		meshAsset.Commit();
		Assert.Equal("Assets/Meshes/new.mesh", renderer.Mesh);
		Assert.True(meshAsset.HasFocus);

		_session.UndoLast();

		Assert.Equal("Assets/Meshes/old.mesh", renderer.Mesh);
		Assert.Equal("Assets/Meshes/old.mesh", meshAsset.Text);
		Assert.True(meshAsset.HasFocus);
	}

	[Fact]
	public void UndoWhileInspectorCheckboxRemainsFocused_RefreshesValue()
	{
		SceneEntity entity = _session.Scene!.Entities.Single(
			candidate => candidate.GetComponent<MeshRendererData>() is not null);
		_session.Select(entity.Id);

		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);
		CheckBox visible = Descendants(inspector)
			.OfType<CheckBox>()
			.Single(field => field.Text == "Visible");
		Assert.True(visible.IsChecked);

		Assert.True(visible.Focus());
		Assert.True(_context.KeyDown(UiKey.Space));
		Assert.False(visible.IsChecked);
		Assert.False(entity.GetComponent<MeshRendererData>()!.Visible);

		_session.UndoLast();

		Assert.True(entity.GetComponent<MeshRendererData>()!.Visible);
		Assert.True(visible.IsChecked);
		Assert.True(visible.HasFocus);
	}

	[Fact]
	public void LeavingUnchangedAssetField_DoesNotCreateUndoOrDirtyScene()
	{
		SceneEntity entity = _session.Scene!.Entities.Single(
			candidate => candidate.GetComponent<MeshRendererData>() is not null);
		MeshRendererData renderer = entity.GetComponent<MeshRendererData>()!;
		renderer.Mesh = "Assets/Meshes/mesh.mesh";
		_session.SaveScene();
		_session.Select(entity.Id);

		using InspectorPanel inspector = new(_session);
		_context.Add(inspector);
		_context.BeginFrame(0, new UiSize(800, 600), UiPoint.Zero);
		TextField meshAsset = Descendants(inspector)
			.OfType<TextField>()
			.Single(field => field.Text == renderer.Mesh);
		int undoCount = _session.Undo.UndoCount;

		Assert.True(meshAsset.Focus());
		meshAsset.Commit();

		Assert.Equal(undoCount, _session.Undo.UndoCount);
		Assert.False(_session.IsSceneDirty);
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
		}
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
