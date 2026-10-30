using BenchmarkDotNet.Attributes;
using Volt.Editor;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Panels;
using Volt.Ui;
using Volt.Ui.Layout;

namespace Volt.Benchmarks;

/// <summary>
/// Compares the ECS inspector's two refresh paths: one that finds the
/// snapshot unchanged and keeps its rows, and one that rewrites them.
/// </summary>
/// <remarks>
/// The panel refreshes four times a second. Rows are rebuilt only when
/// what they display changes, so the steady path is the one a running
/// editor usually takes, and the rebuilding path is what an edit or a
/// world change costs.
/// </remarks>
[MemoryDiagnoser]
[InProcess]
public class EcsInspectorRefreshBenchmarks
{
	private EditorSession _session = null!;
	private EcsInspectorPanel _panel = null!;
	private UiContext _context = null!;
	private Guid _meshId;
	private Guid _otherId;
	private string _root = string.Empty;
	private bool _flip;

	[GlobalSetup]
	public void Setup()
	{
		_root = Path.Combine(
			Path.GetTempPath(),
			$"volt-ecs-refresh-bench-{Guid.NewGuid():N}");

		Directory.CreateDirectory(_root);

		_session = new EditorSession();
		_session.NewProject(Path.Combine(_root, "Game"), "Benchmarks");

		_meshId = _session.Scene!.Entities
			.First(entity =>
				entity.GetComponent<MeshRendererData>() is not null)
			.Id;

		_otherId = _session.Scene!.Entities
			.First(entity => entity.Id != _meshId)
			.Id;

		_session.Select(_meshId);
		_session.StartPlay();

		_panel = new EcsInspectorPanel(_session);
		_context = new UiContext();

		_context.Add(_panel);
		_context.BeginFrame(0.0, new UiSize(1140f, 660f), UiPoint.Zero);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		_context.Dispose();
		_panel.Dispose();
		_session.Dispose();

		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file must not fail the run.
		}
	}

	/// <summary>
	/// A refresh of a world that has not changed, which keeps every row.
	/// </summary>
	[Benchmark(Baseline = true)]
	public void SteadyRefresh()
	{
		_panel.Tick(0.3, 1f);
	}

	/// <summary>
	/// A refresh that rebuilds the selected entity's rows, as a selection
	/// change or a moved world produces.
	/// </summary>
	[Benchmark]
	public void RebuildingRefresh()
	{
		_flip = !_flip;
		_session.Select(_flip ? _otherId : _meshId);
	}
}
