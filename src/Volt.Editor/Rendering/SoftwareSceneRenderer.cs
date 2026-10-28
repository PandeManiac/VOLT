using System.Numerics;
using Volt.Editor.Viewport;
using Volt.Graphics.Vulkan.Rendering;
using Volt.Ui;
using Volt.Ui.Rendering;

namespace Volt.Editor.Rendering;

/// <summary>
/// Renders viewport requests into CPU images for the headless editor.
/// </summary>
/// <remarks>
/// <para>
/// This is the software counterpart of <c>EditorSceneRenderer</c>. It
/// implements the same <see cref="IViewportRenderer"/> contract, so the
/// viewport widgets publish their requests and draw an image exactly as
/// they do in a window; the difference is that the pixels here come
/// from <see cref="SoftwareSceneRasterizer"/> instead of Vulkan.
/// </para>
/// <para>
/// Requests are copied when published. A viewport reuses and mutates
/// one request instance per frame, so copying is what lets a capture
/// render the most recently painted frame without depending on when the
/// next frame starts.
/// </para>
/// </remarks>
internal sealed class SoftwareSceneRenderer : IViewportRenderer
{
	private readonly Dictionary<ViewportKind, SoftwareViewportJob> _jobs = [];
	private readonly Dictionary<UiTextureId, UiRasterImage> _textures = [];

	/// <summary>
	/// Gets the frame slot requests are recorded for.
	/// </summary>
	/// <remarks>
	/// A software renderer owns no in-flight frames, so one slot is
	/// enough and every viewport can draw immediately.
	/// </remarks>
	public int FrameSlot => 0;

	/// <summary>
	/// Gets the images produced by the last <see cref="RenderFrame"/>,
	/// keyed by the texture identifiers the viewports draw.
	/// </summary>
	public IReadOnlyDictionary<UiTextureId, UiRasterImage> Textures => _textures;

	/// <summary>
	/// Gets the texture identifier a viewport kind is registered under.
	/// </summary>
	/// <remarks>
	/// Identifiers are stable for the life of the renderer, so a
	/// draw list recorded before <see cref="RenderFrame"/> still names
	/// the right image.
	/// </remarks>
	public static UiTextureId TextureIdFor(ViewportKind kind)
	{
		return UiTextureId.FromIndex(
			kind == ViewportKind.Scene ? 1 : 2);
	}

	/// <inheritdoc />
	public UiTextureId? PrepareTexture(ViewportKind kind, int slot)
	{
		if (!_jobs.TryGetValue(kind, out SoftwareViewportJob? job) ||
			job.Width <= 0 ||
			job.Height <= 0)
		{
			return null;
		}

		return TextureIdFor(kind);
	}

	/// <inheritdoc />
	public void PublishRequest(ViewportRenderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		_jobs[request.Kind] = new SoftwareViewportJob(
			request.PixelWidth,
			request.PixelHeight,
			request.View,
			request.Projection,
			[.. request.Instances],
			request.ShowGrid,
			request.ClearColor,
			request.Light);
	}

	/// <inheritdoc />
	public void ClearRequest(ViewportKind kind)
	{
		_jobs.Remove(kind);
	}

	/// <summary>
	/// Rasterizes every published request.
	/// </summary>
	/// <remarks>
	/// The viewports publish while they paint, so this is called after
	/// a frame has been drawn and before its draw list is rasterized.
	/// </remarks>
	public void RenderFrame()
	{
		_textures.Clear();

		foreach (KeyValuePair<ViewportKind, SoftwareViewportJob> pair in
			_jobs)
		{
			_textures[TextureIdFor(pair.Key)] =
				SoftwareSceneRasterizer.Render(pair.Value);
		}
	}

	/// <summary>
	/// Gets the image a viewport produced in the last render.
	/// </summary>
	public bool TryGetImage(ViewportKind kind, out UiRasterImage? image)
	{
		return _textures.TryGetValue(TextureIdFor(kind), out image);
	}
}

/// <summary>
/// One viewport's complete description of a frame to rasterize.
/// </summary>
/// <param name="Width">The target width in pixels.</param>
/// <param name="Height">The target height in pixels.</param>
/// <param name="View">The world-to-view matrix.</param>
/// <param name="Projection">
/// The view-to-clip matrix, oriented for a Vulkan target (Y down,
/// depth 0 to 1).
/// </param>
/// <param name="Instances">The meshes to draw, in draw order.</param>
/// <param name="ShowGrid">Whether the ground grid is drawn.</param>
/// <param name="ClearColor">The background colour components.</param>
/// <param name="Light">The authored directional light.</param>
internal sealed record SoftwareViewportJob(
	int Width,
	int Height,
	Matrix4x4 View,
	Matrix4x4 Projection,
	ViewportMeshInstance[] Instances,
	bool ShowGrid,
	Vector4 ClearColor,
	ViewportLight Light);

/// <summary>
/// Rasterizes scene meshes and the ground grid on the CPU.
/// </summary>
/// <remarks>
/// <para>
/// The renderer follows the same contract as the GPU scene pipeline:
/// right-handed view space, a Vulkan-style projection with Y down and
/// depth in 0..1, no back-face culling, the authored directional light
/// with an ambient term, and a depth buffer. Meshes come from
/// <see cref="EditorMeshLibrary"/>, so a headless capture and a window
/// draw the same geometry rather than two approximations of it.
/// </para>
/// <para>
/// Geometry is clipped against the six clip planes before projection,
/// which keeps a camera standing inside an object from producing
/// enormous or inverted triangles. Frames are supersampled where the
/// target is small enough and box-filtered down, so thin grid lines and
/// silhouette edges stay readable in a screenshot.
/// </para>
/// <para>
/// This is a review renderer, not a replacement for the GPU path. It
/// shares the lighting constants, not the rasterization rules, so a
/// pixel-exact comparison with Vulkan is not expected.
/// </para>
/// </remarks>
internal static class SoftwareSceneRasterizer
{
	private const float Ambient = 0.32f;
	private const float Diffuse = 0.68f;

	/// <summary>
	/// The largest supersampled target, in pixels, before falling back
	/// to one sample per pixel.
	/// </summary>
	private const int SupersampleLimit = 2_500_000;

	private static readonly Lazy<Dictionary<string, VulkanSceneVertex[]>>
		s_meshes = new(CreateMeshes);

	/// <summary>
	/// Renders one viewport request.
	/// </summary>
	/// <param name="job">The frame to draw.</param>
	/// <returns>The opaque target image.</returns>
	public static UiRasterImage Render(SoftwareViewportJob job)
	{
		ArgumentNullException.ThrowIfNull(job);

		int width = Math.Max(1, job.Width);
		int height = Math.Max(1, job.Height);

		int samples = width * height <= SupersampleLimit ? 2 : 1;
		int surfaceWidth = width * samples;
		int surfaceHeight = height * samples;
		int pixelCount = surfaceWidth * surfaceHeight;

		byte[] color = new byte[pixelCount * 3];
		float[] depth = new float[pixelCount];

		Array.Fill(depth, float.PositiveInfinity);

		byte clearR = ToByte(job.ClearColor.X);
		byte clearG = ToByte(job.ClearColor.Y);
		byte clearB = ToByte(job.ClearColor.Z);

		for (int index = 0; index < pixelCount; index++)
		{
			int offset = index * 3;

			color[offset] = clearR;
			color[offset + 1] = clearG;
			color[offset + 2] = clearB;
		}

		Matrix4x4 viewProjection = job.View * job.Projection;

		Vector3 lightDirection = job.Light.UnitDirection;
		Vector3 lightDiffuse = job.Light.DiffuseColor;

		if (job.ShowGrid &&
			s_meshes.Value.TryGetValue(
				EditorMeshLibrary.Grid,
				out VulkanSceneVertex[]? grid))
		{
			DrawMesh(
				grid,
				Matrix4x4.Identity,
				viewProjection,
				color,
				depth,
				surfaceWidth,
				surfaceHeight,
				lit: true,
				lightDirection,
				lightDiffuse);
		}

		foreach (ViewportMeshInstance instance in job.Instances)
		{
			if (!s_meshes.Value.TryGetValue(
					instance.MeshId,
					out VulkanSceneVertex[]? mesh))
			{
				continue;
			}

			DrawMesh(
				mesh,
				instance.World,
				viewProjection,
				color,
				depth,
				surfaceWidth,
				surfaceHeight,
				lit: true,
				lightDirection,
				lightDiffuse);
		}

		return Downsample(
			color,
			surfaceWidth,
			surfaceHeight,
			width,
			height,
			samples);
	}

	private static void DrawMesh(
		VulkanSceneVertex[] vertices,
		Matrix4x4 world,
		Matrix4x4 viewProjection,
		byte[] color,
		float[] depth,
		int width,
		int height,
		bool lit,
		Vector3 lightDirection,
		Vector3 lightDiffuse)
	{
		Vector4[] clip = new Vector4[vertices.Length];
		Vector3[] positions = new Vector3[vertices.Length];

		for (int index = 0; index < vertices.Length; index++)
		{
			Vector3 position = Vector3.Transform(
				vertices[index].Position,
				world);

			positions[index] = position;

			clip[index] = Vector4.Transform(
				new Vector4(
					position.X,
					position.Y,
					position.Z,
					1f),
				viewProjection);
		}

		for (int index = 0; index + 2 < vertices.Length; index += 3)
		{
			Vector3 first = positions[index];
			Vector3 second = positions[index + 1];
			Vector3 third = positions[index + 2];

			Vector3 lighting = Vector3.One;

			if (lit)
			{
				Vector3 normal = Vector3.Cross(
					second - first,
					third - first);

				float lengthSquared = normal.LengthSquared();

				if (lengthSquared > 1e-12f)
				{
					normal /= MathF.Sqrt(lengthSquared);

					// The GPU derives its normal from screen-space
					// derivatives, which flips it toward the camera.
					// The absolute dot reproduces that two-sided
					// lighting without needing a camera position.
					float lambert = MathF.Abs(
						Vector3.Dot(normal, lightDirection));

					lighting = new Vector3(Ambient) +
						(lightDiffuse * (Diffuse * lambert));
				}
			}

			Vector3 albedo = vertices[index].Color;
			byte red = ToByte(albedo.X * lighting.X);
			byte green = ToByte(albedo.Y * lighting.Y);
			byte blue = ToByte(albedo.Z * lighting.Z);

			ClipAndDraw(
				clip[index],
				clip[index + 1],
				clip[index + 2],
				red,
				green,
				blue,
				color,
				depth,
				width,
				height);
		}
	}

	/// <summary>
	/// Clips one triangle against the clip volume and rasterizes what
	/// remains.
	/// </summary>
	private static void ClipAndDraw(
		Vector4 first,
		Vector4 second,
		Vector4 third,
		byte red,
		byte green,
		byte blue,
		byte[] color,
		float[] depth,
		int width,
		int height)
	{
		Span<Vector4> source = stackalloc Vector4[12];
		Span<Vector4> target = stackalloc Vector4[12];

		source[0] = first;
		source[1] = second;
		source[2] = third;

		int count = 3;

		// Near, far, left, right, top, and bottom in homogeneous clip
		// space. Every plane keeps the non-negative side.
		for (int plane = 0; plane < 6; plane++)
		{
			int output = 0;

			for (int index = 0; index < count; index++)
			{
				Vector4 current = source[index];
				Vector4 next = source[(index + 1) % count];

				float currentDistance = Distance(current, plane);
				float nextDistance = Distance(next, plane);

				bool currentInside = currentDistance >= 0f;
				bool nextInside = nextDistance >= 0f;

				if (currentInside)
				{
					target[output++] = current;
				}

				if (currentInside != nextInside)
				{
					float amount = currentDistance /
						(currentDistance - nextDistance);

					target[output++] = current +
						((next - current) * amount);
				}
			}

			if (output < 3)
			{
				return;
			}

			target[..output].CopyTo(source);
			count = output;
		}

		Span<Vector3> screen = stackalloc Vector3[12];

		for (int index = 0; index < count; index++)
		{
			Vector4 point = source[index];

			if (point.W <= 1e-6f)
			{
				return;
			}

			float inverse = 1f / point.W;

			screen[index] = new Vector3(
				((point.X * inverse) * 0.5f + 0.5f) * width,
				((point.Y * inverse) * 0.5f + 0.5f) * height,
				point.Z * inverse);
		}

		for (int index = 1; index + 1 < count; index++)
		{
			Rasterize(
				screen[0],
				screen[index],
				screen[index + 1],
				red,
				green,
				blue,
				color,
				depth,
				width,
				height);
		}
	}

	private static float Distance(Vector4 point, int plane)
	{
		return plane switch
		{
			0 => point.Z,
			1 => point.W - point.Z,
			2 => point.X + point.W,
			3 => point.W - point.X,
			4 => point.Y + point.W,
			_ => point.W - point.Y
		};
	}

	private static void Rasterize(
		Vector3 first,
		Vector3 second,
		Vector3 third,
		byte red,
		byte green,
		byte blue,
		byte[] color,
		float[] depth,
		int width,
		int height)
	{
		float minimumY = MathF.Min(
			first.Y,
			MathF.Min(second.Y, third.Y));

		float maximumY = MathF.Max(
			first.Y,
			MathF.Max(second.Y, third.Y));

		if (!(maximumY > minimumY))
		{
			return;
		}

		int firstRow = Math.Max(
			0,
			(int)MathF.Ceiling(minimumY - 0.5f));

		int lastRow = Math.Min(
			height - 1,
			(int)MathF.Floor(maximumY - 0.5f));

		for (int y = firstRow; y <= lastRow; y++)
		{
			float row = y + 0.5f;

			float left = float.MaxValue;
			float right = float.MinValue;
			float leftDepth = 0f;
			float rightDepth = 0f;

			Intersect(first, second, row, ref left, ref leftDepth, ref right, ref rightDepth);
			Intersect(second, third, row, ref left, ref leftDepth, ref right, ref rightDepth);
			Intersect(third, first, row, ref left, ref leftDepth, ref right, ref rightDepth);

			if (right < left)
			{
				continue;
			}

			int firstColumn = Math.Max(
				0,
				(int)MathF.Ceiling(left - 0.5f));

			int lastColumn = Math.Min(
				width - 1,
				(int)MathF.Floor(right - 0.5f));

			if (lastColumn < firstColumn)
			{
				continue;
			}

			float span = right - left;
			float inverseSpan = span > 1e-6f
				? 1f / span
				: 0f;

			for (int x = firstColumn; x <= lastColumn; x++)
			{
				float z = span > 1e-6f
					? leftDepth +
						((rightDepth - leftDepth) *
							((x + 0.5f - left) * inverseSpan))
					: leftDepth;

				if (z < 0f || z > 1f)
				{
					continue;
				}

				int index = (y * width) + x;

				if (z >= depth[index])
				{
					continue;
				}

				depth[index] = z;

				int offset = index * 3;

				color[offset] = red;
				color[offset + 1] = green;
				color[offset + 2] = blue;
			}
		}
	}

	private static void Intersect(
		Vector3 start,
		Vector3 end,
		float row,
		ref float left,
		ref float leftDepth,
		ref float right,
		ref float rightDepth)
	{
		if (!((start.Y <= row && end.Y > row) ||
			(end.Y <= row && start.Y > row)))
		{
			return;
		}

		float amount = (row - start.Y) / (end.Y - start.Y);
		float x = start.X + ((end.X - start.X) * amount);
		float z = start.Z + ((end.Z - start.Z) * amount);

		if (x < left)
		{
			left = x;
			leftDepth = z;
		}

		if (x > right)
		{
			right = x;
			rightDepth = z;
		}
	}

	private static UiRasterImage Downsample(
		byte[] color,
		int surfaceWidth,
		int surfaceHeight,
		int width,
		int height,
		int samples)
	{
		UiRasterImage image = UiRasterImage.Create(width, height);

		int sampleCount = samples * samples;

		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				int red = 0;
				int green = 0;
				int blue = 0;

				for (int offsetY = 0; offsetY < samples; offsetY++)
				{
					int row = (y * samples) + offsetY;

					for (int offsetX = 0; offsetX < samples; offsetX++)
					{
						int column = (x * samples) + offsetX;
						int offset =
							((row * surfaceWidth) + column) * 3;

						red += color[offset];
						green += color[offset + 1];
						blue += color[offset + 2];
					}
				}

				image.SetPixel(
					x,
					y,
					new UiColor(
						(byte)(red / sampleCount),
						(byte)(green / sampleCount),
						(byte)(blue / sampleCount)));
			}
		}

		return image;
	}

	private static byte ToByte(float value)
	{
		return (byte)Math.Clamp(
			(int)MathF.Round(value * 255f),
			0,
			255);
	}

	private static Dictionary<string, VulkanSceneVertex[]> CreateMeshes()
	{
		Dictionary<string, VulkanSceneVertex[]> meshes =
			new(StringComparer.OrdinalIgnoreCase);

		foreach (EditorMeshInfo mesh in EditorMeshLibrary.Meshes)
		{
			meshes.Add(
				mesh.Id,
				EditorMeshLibrary.CreateVertices(mesh.Id));
		}

		meshes.Add(
			EditorMeshLibrary.Grid,
			EditorMeshLibrary.CreateVertices(EditorMeshLibrary.Grid));

		return meshes;
	}
}
