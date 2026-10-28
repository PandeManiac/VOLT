using System.Numerics;
using Volt.Ecs.Components.Rendering;
using Volt.Editor.Rendering;
using Volt.Editor.Viewport;
using Volt.Rendering.Cameras;
using Volt.Ui;
using Volt.Ui.Rendering;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers the CPU viewport renderer used by the headless snapshot.
/// </summary>
public sealed class SoftwareSceneRendererTests
{
	private const int Width = 240;

	private const int Height = 160;

	private static readonly Vector3 ClearColor =
		new(0.1f, 0.11f, 0.12f);

	[Fact]
	public void GridAndMeshes_AreRasterizedIntoTheViewportImage()
	{
		SoftwareSceneRenderer renderer = new();

		renderer.PublishRequest(CreateRequest(
			new Vector3(4f, 3f, 6f),
			Vector3.Zero,
			showGrid: true,
			new ViewportMeshInstance(
				EditorMeshLibrary.Cube,
				Matrix4x4.Identity)));

		UiTextureId? texture = renderer.PrepareTexture(
			ViewportKind.Scene,
			0);

		Assert.NotNull(texture);
		Assert.Equal(
			SoftwareSceneRenderer.TextureIdFor(ViewportKind.Scene),
			texture.Value);

		renderer.RenderFrame();

		Assert.True(renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image));

		UiRasterImage capture = image!;
		UiColor background = capture.GetPixel(1, 1);
		UiColor center = capture.GetPixel(Width / 2, Height / 2);

		Assert.NotEqual(background, center);
		Assert.True(
			CountDifferent(capture, background) > 2000,
			"Grid and cube must cover a meaningful part of the viewport.");
	}

	[Fact]
	public void GridOnly_DrawsLinesOverTheClearColour()
	{
		SoftwareSceneRenderer renderer = new();

		renderer.PublishRequest(CreateRequest(
			new Vector3(6f, 5f, -10f),
			Vector3.Zero,
			showGrid: true));

		renderer.RenderFrame();

		Assert.True(renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image));

		UiRasterImage capture = image!;
		UiColor background = capture.GetPixel(1, 1);

		Assert.True(
			CountDifferent(capture, background) > 300,
			"The ground grid must be visible on its own.");
	}

	[Fact]
	public void NoGridAndNoInstances_LeavesOneClearColour()
	{
		SoftwareSceneRenderer renderer = new();

		renderer.PublishRequest(CreateRequest(
			new Vector3(4f, 3f, 6f),
			Vector3.Zero,
			showGrid: false));

		renderer.RenderFrame();

		Assert.True(renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image));

		UiRasterImage capture = image!;
		UiColor expected = ClearPixel();

		Assert.Equal(expected, capture.GetPixel(1, 1));
		Assert.Equal(expected, capture.GetPixel(Width / 2, Height / 2));
		Assert.Equal(expected, capture.GetPixel(Width - 2, Height - 2));
		Assert.Equal(1, CountColors(capture));
	}

	[Fact]
	public void FullyHiddenGeometry_DoesNotChangeTheImage()
	{
		UiRasterImage nearOnly = Render(
			new ViewportMeshInstance(
				EditorMeshLibrary.Cube,
				Matrix4x4.Identity));

		// A small cube behind the near one projects entirely inside its
		// silhouette, so the depth test must discard every fragment.
		Matrix4x4 hidden =
			Matrix4x4.CreateScale(0.2f) *
			Matrix4x4.CreateTranslation(0f, 0f, 10f);

		UiRasterImage withHidden = Render(
			new ViewportMeshInstance(
				EditorMeshLibrary.Cube,
				Matrix4x4.Identity),
			new ViewportMeshInstance(
				EditorMeshLibrary.Cube,
				hidden));

		Assert.Equal(nearOnly.Width, withHidden.Width);
		Assert.Equal(nearOnly.Height, withHidden.Height);

		for (int y = 0; y < nearOnly.Height; y++)
		{
			for (int x = 0; x < nearOnly.Width; x++)
			{
				Assert.Equal(
					nearOnly.GetPixel(x, y),
					withHidden.GetPixel(x, y));
			}
		}
	}

	[Fact]
	public void Rendering_IsDeterministic()
	{
		UiRasterImage first = Render(
			new ViewportMeshInstance(
				EditorMeshLibrary.Sphere,
				Matrix4x4.CreateTranslation(0f, 0.5f, 0f)));

		UiRasterImage second = Render(
			new ViewportMeshInstance(
				EditorMeshLibrary.Sphere,
				Matrix4x4.CreateTranslation(0f, 0.5f, 0f)));

		Assert.Equal(first.EncodePng(), second.EncodePng());
	}

	[Fact]
	public void AuthoredLight_TintsTheRenderedSurface()
	{
		SoftwareSceneRenderer renderer = new();
		ViewportRenderRequest request = CreateRequest(
			new Vector3(4f, 3f, 6f),
			Vector3.Zero,
			showGrid: false,
			new ViewportMeshInstance(
				EditorMeshLibrary.Cube,
				Matrix4x4.Identity));

		request.Light = new ViewportLight(
			Vector3.UnitZ,
			new Vector4(1f, 0.05f, 0.05f, 1f),
			1f);

		renderer.PublishRequest(request);
		renderer.RenderFrame();

		Assert.True(renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image));

		UiColor center = image!.GetPixel(Width / 2, Height / 2);

		Assert.True(
			center.R > center.G + 20,
			"An authored red light must dominate the lit surface.");
	}

	[Fact]
	public void CameraInsideAMesh_ClipsWithoutLosingTheFrame()
	{
		SoftwareSceneRenderer renderer = new();

		renderer.PublishRequest(CreateRequest(
			Vector3.Zero,
			new Vector3(0f, 0f, 1f),
			showGrid: false,
			new ViewportMeshInstance(
				EditorMeshLibrary.Cube,
				Matrix4x4.Identity)));

		renderer.RenderFrame();

		Assert.True(renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image));

		UiRasterImage capture = image!;

		// The interior face can cover the whole frame, so the background
		// reference must be the clear colour rather than a screen pixel.
		Assert.True(
			CountDifferent(capture, ClearPixel()) > 10_000,
			"A camera inside the cube must still see its interior.");
	}

	[Fact]
	public void ClearingARequest_StopsTheViewportImage()
	{
		SoftwareSceneRenderer renderer = new();

		renderer.PublishRequest(CreateRequest(
			new Vector3(4f, 3f, 6f),
			Vector3.Zero,
			showGrid: true));

		Assert.NotNull(renderer.PrepareTexture(ViewportKind.Scene, 0));

		renderer.ClearRequest(ViewportKind.Scene);

		Assert.Null(renderer.PrepareTexture(ViewportKind.Scene, 0));
	}

	private static UiRasterImage Render(
		params ViewportMeshInstance[] instances)
	{
		SoftwareSceneRenderer renderer = new();

		renderer.PublishRequest(CreateRequest(
			new Vector3(0f, 0f, -6f),
			Vector3.Zero,
			showGrid: false,
			instances));

		renderer.RenderFrame();

		return renderer.TryGetImage(
			ViewportKind.Scene,
			out UiRasterImage? image)
				? image!
				: throw new InvalidOperationException(
					"The renderer produced no image.");
	}

	private static ViewportRenderRequest CreateRequest(
		Vector3 position,
		Vector3 target,
		bool showGrid,
		params ViewportMeshInstance[] instances)
	{
		ViewportRenderRequest request = new()
		{
			Kind = ViewportKind.Scene,
			HasContent = true,
			PixelWidth = Width,
			PixelHeight = Height,
			View = Matrix4x4.CreateLookAt(
				position,
				target,
				Vector3.UnitY),
			Projection = CameraProjection.CreatePerspective(
				new CameraComponent(
					MathF.PI / 3f,
					0.05f,
					500f),
				Width / (float)Height,
				ViewportYOrientation.YDown),
			ShowGrid = showGrid,
			ClearColor = new Vector4(
				ClearColor.X,
				ClearColor.Y,
				ClearColor.Z,
				1f)
		};

		request.Instances.AddRange(instances);

		return request;
	}

	private static UiColor ClearPixel()
	{
		return new UiColor(
			(byte)MathF.Round(ClearColor.X * 255f),
			(byte)MathF.Round(ClearColor.Y * 255f),
			(byte)MathF.Round(ClearColor.Z * 255f));
	}

	private static int CountDifferent(
		UiRasterImage image,
		UiColor reference)
	{
		int count = 0;

		for (int y = 0; y < image.Height; y++)
		{
			for (int x = 0; x < image.Width; x++)
			{
				if (image.GetPixel(x, y) != reference)
				{
					count++;
				}
			}
		}

		return count;
	}

	private static int CountColors(UiRasterImage image)
	{
		HashSet<uint> colors = [];

		for (int y = 0; y < image.Height; y += 4)
		{
			for (int x = 0; x < image.Width; x += 4)
			{
				UiColor pixel = image.GetPixel(x, y);

				colors.Add(
					((uint)pixel.R << 16) |
					((uint)pixel.G << 8) |
					pixel.B);
			}
		}

		return colors.Count;
	}
}
