using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Widgets;

namespace Volt.Ui.Tests.Rendering;

public sealed class GeometryTests
{
	[Fact]
	public void DrawList_IntersectsNestedClips()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(100f, 100f));
		list.PushClip(new UiRect(10f, 10f, 40f, 40f));
		list.PushClip(new UiRect(20f, 20f, 100f, 100f));
		list.AddRect(new UiRect(0f, 0f, 100f, 100f), UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list);

		Assert.Equal(1, geometry.BatchCount);
		UiBatch batch = geometry.Batches[0];

		Assert.Equal(20, batch.Scissor.X);
		Assert.Equal(20, batch.Scissor.Y);
		Assert.Equal(30, batch.Scissor.Width);
		Assert.Equal(30, batch.Scissor.Height);
	}

	[Fact]
	public void Geometry_BatchesByTextureChange()
	{
		using UiTestHost host = new(300f, 100f);
		StackPanel stack = host.Add(new StackPanel());

		stack.Add(new Panel
		{
			Width = UiLength.Points(50f),
			Height = UiLength.Points(20f)
		});

		stack.Add(new Label("Hello"));

		host.Layout();

		UiGeometry geometry = new();
		geometry.Build(host.Context.Draw(), 1f);

		Assert.True(geometry.BatchCount >= 2);
		Assert.True(geometry.Vertices.Length >= 8);
		Assert.True(geometry.Indices.Length >= 12);

		bool hasWhite = false;
		bool hasFont = false;

		foreach (UiBatch batch in geometry.Batches)
		{
			hasWhite |= batch.Texture.Kind == UiTextureKind.White;
			hasFont |= batch.Texture.Kind == UiTextureKind.Font;
		}

		Assert.True(hasWhite);
		Assert.True(hasFont);
	}

	[Fact]
	public void Geometry_AppliesDisplayScaleToPositionsAndScissor()
	{
		UiDrawList list = new();
		list.Begin(new UiSize(100f, 100f));
		list.AddRect(new UiRect(10f, 20f, 30f, 40f), UiColor.White);

		UiGeometry geometry = new();
		geometry.Build(list, 2f);

		Assert.Equal(1, geometry.BatchCount);
		UiBatch batch = geometry.Batches[0];

		Assert.Equal(0, batch.Scissor.X);
		Assert.Equal(0, batch.Scissor.Y);
		Assert.Equal(200, batch.Scissor.Width);
		Assert.Equal(200, batch.Scissor.Height);
		Assert.Equal(20f, geometry.Vertices[0].Position.X);
		Assert.Equal(40f, geometry.Vertices[0].Position.Y);
	}
}
