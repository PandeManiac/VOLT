using System.Numerics;
using BenchmarkDotNet.Attributes;
using Volt.Ui;
using Volt.Ui.Docking;
using Volt.Ui.Layout;
using Volt.Ui.Styling;
using Volt.Ui.Widgets;
using Volt.Ui.Widgets.Properties;

namespace Volt.Benchmarks;

/// <summary>
/// Measures the Volt.Ui operations that dominate editor cost: widget
/// churn, layout of deep and wide trees, idle frames, hit testing,
/// resize, large tree views, inspector refresh, and dock rebuilding.
/// </summary>
/// <remarks>
/// <para>
/// Every benchmark runs against a headless <see cref="UiContext"/>.
/// There is no window, no GPU, and no font atlas upload, so the
/// numbers cover the CPU half of the UI: measure, arrange, hit test,
/// paint, and draw-list generation. Renderer cost is measured
/// separately and is not asserted here.
/// </para>
/// <para>
/// <see cref="WidgetChurn64Widgets"/> creates and destroys 64 widgets
/// per invocation, so divide its time and allocation numbers by 64 for
/// a per-widget figure. The remaining benchmarks report one frame, one
/// layout, one hit test, or one structural tree update.
/// </para>
/// <para>
/// <see cref="TreeViewFrame5000Items"/> measures the steady state of a
/// virtualized tree holding 5000 visible rows.
/// <see cref="TreeViewRebuild5000Rows"/> records a structural change
/// plus the frame that flattens it into its visible-row list, so the
/// difference between the two is the flatten pass; the tree only marks
/// itself dirty on a structural change and flattens lazily in the next
/// frame.
/// </para>
/// <para>
/// The two inspector benchmarks differ only in whether the property
/// values change between frames.
/// <see cref="InspectorFrame256Properties"/> models a drag, and
/// <see cref="InspectorIdleRefresh256Properties"/> models an editor
/// sitting on a selection whose values do not change.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class UiBenchmarks
{
	private const int ChurnWidth = 64;
	private const int WideChildCount = 256;
	private const int DeepDepth = 24;
	private const int TreeRootCount = 50;
	private const int TreeChildrenPerRoot = 99;
	private const int InspectorPropertyCount = 256;

	private static readonly UiSize Surface = new(1920f, 1080f);
	private static readonly UiSize HalfSurface = new(960f, 540f);
	private static readonly UiPoint Pointer = new(420f, 260f);

	private UiContext _churn = null!;
	private StackPanel _churnHost = null!;

	private UiContext _deep = null!;
	private UiContext _wide = null!;

	private UiContext _hit = null!;

	private UiContext _tree = null!;
	private TreeView _treeView = null!;
	private TreeViewItem _treeRebuildAnchor = null!;

	private UiContext _inspector = null!;
	private PropertyGrid _propertyGrid = null!;
	private UiContext _inspectorIdle = null!;
	private PropertyGrid _idlePropertyGrid = null!;
	private float _floatValue;
	private Vector3 _vectorValue;
	private bool _boolValue;

	private DockLayout _dockLayout = null!;
	private string _dockJson = string.Empty;
	private UiContext _dock = null!;
	private DockHost _dockHost = null!;
	private bool _activateScene = true;

	[GlobalSetup]
	public void Setup()
	{
		SetupChurn();
		SetupDeepTree();
		SetupWideTree();
		SetupTreeView();
		SetupInspector();
		SetupIdleInspector();
		SetupDock();
	}

	[Benchmark]
	public void WidgetChurn64Widgets()
	{
		for (int index = 0; index < ChurnWidth; index++)
		{
			_churnHost.Add(new Label($"Item {index}"));
		}

		while (_churnHost.Children.Count > 0)
		{
			Widget child = _churnHost.Children[^1];
			_churnHost.Remove(child);
			child.Dispose();
		}
	}

	[Benchmark]
	public void LayoutDeepTree()
	{
		_deep.BeginFrame(0.0, Surface, Pointer);
	}

	[Benchmark]
	public void LayoutWideTree()
	{
		_wide.BeginFrame(0.0, Surface, Pointer);
	}

	[Benchmark]
	public void FrameIdle()
	{
		_wide.BeginFrame(0.0, Surface, Pointer);
		_wide.Draw();
	}

	[Benchmark]
	public void FrameAfterResize()
	{
		_wide.BeginFrame(
			0.0,
			_activateScene ? HalfSurface : Surface,
			Pointer);

		_wide.Draw();

		_activateScene = !_activateScene;
	}

	[Benchmark]
	public void HitTestDeepestWidget()
	{
		_hit.FindWidgetAt(Pointer);
	}

	[Benchmark]
	public void TreeViewRebuild5000Rows()
	{
		_treeView.RemoveRoot(_treeRebuildAnchor);
		_treeView.AddRoot(_treeRebuildAnchor);

		_tree.BeginFrame(0.0, Surface, Pointer);
		_tree.Draw();
	}

	[Benchmark]
	public void TreeViewFrame5000Items()
	{
		_tree.BeginFrame(0.0, Surface, Pointer);
		_tree.Draw();
	}

	[Benchmark]
	public void InspectorFrame256Properties()
	{
		_inspector.BeginFrame(0.0, Surface, Pointer);
		_propertyGrid.RefreshValues();
		_inspector.Draw();

		_floatValue += 0.25f;
		_vectorValue.X += 0.25f;
		_boolValue = !_boolValue;
	}

	[Benchmark]
	public void InspectorIdleRefresh256Properties()
	{
		_inspectorIdle.BeginFrame(0.0, Surface, Pointer);
		_idlePropertyGrid.RefreshValues();
		_inspectorIdle.Draw();
	}

	[Benchmark]
	public void DockLayoutSerialize()
	{
		_dockLayout.Serialize();
	}

	[Benchmark]
	public void DockLayoutDeserialize()
	{
		DockLayout.Deserialize(_dockJson).ClosePanel("profiler");
	}

	[Benchmark]
	public void DockHostRebuildAndFrame()
	{
		_dockLayout.ActivatePanel(
			_activateScene ? "game" : "scene");

		_activateScene = !_activateScene;

		_dock.BeginFrame(0.0, Surface, Pointer);
		_dock.Draw();
	}

	private void SetupChurn()
	{
		_churn = new UiContext();
		_churnHost = new StackPanel { Spacing = 2f };

		_churn.Add(_churnHost);
		_churn.BeginFrame(0.0, Surface, Pointer);
	}

	private void SetupDeepTree()
	{
		_deep = new UiContext();

		_deep.Add(BuildDeepBranch(DeepDepth));
		_deep.BeginFrame(0.0, Surface, Pointer);
	}

	private void SetupWideTree()
	{
		_wide = new UiContext();

		StackPanel column = new() { Spacing = 2f };

		for (int index = 0; index < WideChildCount; index++)
		{
			StackPanel row = new(UiOrientation.Horizontal)
			{
				Spacing = 6f,
				Padding = new UiThickness(4f, 2f)
			};

			row.Add(
				new Label($"Field {index}")
				{
					Width = UiLength.Points(160f)
				});

			row.Add(
				new Button($"Apply {index}")
				{
					FlexGrow = 1f
				});

			column.Add(row);
		}

		_wide.Add(
			new ScrollView
			{
				Content = column
			});

		_wide.BeginFrame(0.0, Surface, Pointer);

		_hit = _wide;
	}

	private void SetupTreeView()
	{
		_tree = new UiContext();
		_treeView = new TreeView();

		for (int root = 0; root < TreeRootCount; root++)
		{
			TreeViewItem item = new(
				$"root-{root}",
				$"Group {root}")
			{
				IsExpanded = true
			};

			for (int child = 0; child < TreeChildrenPerRoot; child++)
			{
				item.AddChild(
					new TreeViewItem(
						$"root-{root}/child-{child}",
						$"Object {root}.{child}"));
			}

			_treeView.AddRoot(item);
		}

		_treeRebuildAnchor = _treeView.Roots[^1];

		_tree.Add(_treeView);
		_tree.BeginFrame(0.0, Surface, Pointer);
		_tree.Draw();
	}

	private void SetupInspector()
	{
		_inspector = new UiContext();
		_propertyGrid = new PropertyGrid();

		_inspector.Add(_propertyGrid);
		_propertyGrid.SetProperties(BuildProperties(mutating: true));
		_inspector.BeginFrame(0.0, Surface, Pointer);
	}

	private void SetupIdleInspector()
	{
		_inspectorIdle = new UiContext();
		_idlePropertyGrid = new PropertyGrid();

		_inspectorIdle.Add(_idlePropertyGrid);
		_idlePropertyGrid.SetProperties(BuildProperties(mutating: false));
		_inspectorIdle.BeginFrame(0.0, Surface, Pointer);
	}

	private List<UiProperty> BuildProperties(bool mutating)
	{
		List<UiProperty> properties = []; for (int index = 0; index < InspectorPropertyCount; index++)
		{
			int stable = index;

			UiProperty property = (index % 4) switch
			{
				0 => new FloatProperty(
					$"Speed {index}",
					() => mutating ? _floatValue : 1.5f,
					value => _floatValue = value),
				1 => new Vector3Property(
					$"Position {index}",
					() => mutating ? _vectorValue : Vector3.One,
					value => _vectorValue = value),
				2 => new IntProperty(
					$"Layer {index}",
					() => stable,
					_ => { }),
				_ => new BoolProperty(
					$"Enabled {index}",
					() => mutating && _boolValue,
					value => _boolValue = value)
			};

			properties.Add(property);
		}

		return properties;
	}

	private void SetupDock()
	{
		_dock = new UiContext();
		_dockLayout = DockLayout.CreateEditorDefault();

		_dockHost = new DockHost(
			_dockLayout,
			static panelId => new Label($"Panel {panelId}"));

		_dock.Add(_dockHost);
		_dock.BeginFrame(0.0, Surface, Pointer);
		_dock.Draw();

		_dockJson = _dockLayout.Serialize();
	}

	private static Widget BuildDeepBranch(int depth)
	{
		StackPanel panel = new(UiOrientation.Vertical)
		{
			Spacing = 2f,
			Padding = new UiThickness(2f)
		};

		panel.Add(new Label($"Level {depth}"));

		if (depth > 0)
		{
			panel.Add(BuildDeepBranch(depth - 1));
		}

		return panel;
	}
}
