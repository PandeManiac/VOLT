namespace Volt.Ui;

/// <summary>
/// A caller-owned RGBA8 image that Volt.Ui can draw.
/// </summary>
/// <remarks>
/// <para>
/// An image is a plain CPU buffer of straight-alpha, eight-bit-per-
/// channel pixels stored top-down, row by row, exactly as a renderer
/// samples it: the first pixel is the one drawn at the top-left
/// corner. It carries no graphics-API object, so it can be created,
/// filled, and tested without a device.
/// </para>
/// <para>
/// An image is handed to a <see cref="UiTextureRegistry"/> to obtain
/// its <see cref="UiTextureId"/>. The identity, and therefore the
/// identifier, stays with the instance for as long as it is
/// registered; changing pixels only advances <see cref="Revision"/>,
/// which is what lets a renderer re-upload an image without copying
/// one that did not change. The size is fixed for the lifetime of the
/// instance; create another image instead of resizing one.
/// </para>
/// </remarks>
public sealed class UiImageTexture
{
	private readonly byte[] _pixels;
	private int _revision;

	/// <summary>
	/// Creates a fully transparent image in caller-owned memory.
	/// </summary>
	public UiImageTexture(
		int width,
		int height,
		string? name = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
		ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

		Width = width;
		Height = height;
		Name = string.IsNullOrWhiteSpace(name) ? "image" : name;
		_pixels = new byte[checked(Stride * height)];
	}

	/// <summary>
	/// Gets the diagnostic name. It has no effect on identity.
	/// </summary>
	public string Name { get; }

	/// <summary>
	/// Gets the image edge in texels.
	/// </summary>
	public int Width { get; }

	/// <summary>
	/// Gets the image edge in texels.
	/// </summary>
	public int Height { get; }

	/// <summary>
	/// Gets the byte distance between two rows.
	/// </summary>
	public int Stride => Width * 4;

	/// <summary>
	/// Gets the identifier assigned when the image was registered,
	/// or <see cref="UiTextureId.White"/> while it is not registered.
	/// </summary>
	public UiTextureId Id { get; internal set; }

	/// <summary>
	/// Gets whether the image is currently held by a registry.
	/// </summary>
	public bool IsRegistered => !Id.IsWhite;

	/// <summary>
	/// Gets a value that changes whenever pixel data changed.
	/// </summary>
	public int Revision => _revision;

	/// <summary>
	/// Gets the pixels as straight-alpha RGBA, top-down.
	/// </summary>
	public ReadOnlySpan<byte> Pixels => _pixels;

	/// <summary>
	/// Replaces every pixel from a matching-length RGBA buffer.
	/// </summary>
	public void SetPixels(ReadOnlySpan<byte> pixels)
	{
		if (pixels.Length != _pixels.Length)
		{
			throw new ArgumentException(
				$"An image of {Width}x{Height} needs " +
				$"{_pixels.Length} bytes, but {pixels.Length} were " +
				"supplied.",
				nameof(pixels));
		}

		pixels.CopyTo(_pixels);
		_revision++;
	}

	/// <summary>
	/// Writes one pixel. Coordinates outside the image are ignored.
	/// </summary>
	public void SetPixel(int x, int y, UiColor color)
	{
		if (!Contains(x, y))
		{
			return;
		}

		int offset = (y * Stride) + (x * 4);

		_pixels[offset] = color.R;
		_pixels[offset + 1] = color.G;
		_pixels[offset + 2] = color.B;
		_pixels[offset + 3] = color.A;

		_revision++;
	}

	/// <summary>
	/// Reads one pixel. Coordinates outside the image yield a
	/// transparent color.
	/// </summary>
	public UiColor GetPixel(int x, int y)
	{
		if (!Contains(x, y))
		{
			return UiColor.Transparent;
		}

		int offset = (y * Stride) + (x * 4);

		return new UiColor(
			_pixels[offset],
			_pixels[offset + 1],
			_pixels[offset + 2],
			_pixels[offset + 3]);
	}

	/// <summary>
	/// Fills a texel rectangle with one color, clamped to the image.
	/// </summary>
	public void Fill(UiPixelRect rect, UiColor color)
	{
		int left = Math.Clamp(rect.X, 0, Width);
		int top = Math.Clamp(rect.Y, 0, Height);
		int right = Math.Clamp(rect.Right, left, Width);
		int bottom = Math.Clamp(rect.Bottom, top, Height);

		for (int y = top; y < bottom; y++)
		{
			int row = y * Stride;

			for (int x = left; x < right; x++)
			{
				int offset = row + (x * 4);

				_pixels[offset] = color.R;
				_pixels[offset + 1] = color.G;
				_pixels[offset + 2] = color.B;
				_pixels[offset + 3] = color.A;
			}
		}

		if (bottom > top && right > left)
		{
			_revision++;
		}
	}

	private bool Contains(int x, int y)
	{
		return x >= 0 && x < Width && y >= 0 && y < Height;
	}
}
