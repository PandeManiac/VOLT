using System.Numerics;
using Volt.Ui.Layout;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Ui.Widgets;

/// <summary>
/// Draws a registered image texture inside its content area.
/// </summary>
/// <remarks>
/// <para>
/// The image is laid out in <see cref="Widget.GlobalContentBounds"/>,
/// so padding insets it and the background and border follow the
/// widget's outer bounds. <see cref="Scale"/> decides how the image
/// relates to that area; <see cref="Fill"/> is the mode a viewport
/// wants, because the image then covers every pixel of the panel and
/// is clipped to it.
/// </para>
/// <para>
/// <see cref="ImageBounds"/> reports the rectangle the image was
/// actually drawn into, and <see cref="TryMapToTexture"/> converts a
/// root-space point back into a texel coordinate. A viewport uses both
/// to turn pointer positions into render-target coordinates without
/// duplicating the scale arithmetic.
/// </para>
/// </remarks>
public class ImageView : Widget
{
	private UiImageTexture? _texture;
	private UiImageScale _scale = UiImageScale.Fit;
	private UiRect _uv = new(0f, 0f, 1f, 1f);

	public ImageView()
	{
		ClipToBounds = true;
	}

	public ImageView(UiImageTexture? texture)
		: this()
	{
		Texture = texture;
	}

	/// <summary>
	/// Gets or sets the image to draw. An image that is not registered
	/// paints nothing.
	/// </summary>
	public UiImageTexture? Texture
	{
		get => _texture;
		set
		{
			if (ReferenceEquals(_texture, value))
			{
				return;
			}

			_texture = value;
			InvalidateMeasure();
		}
	}

	/// <summary>
	/// Gets or sets how the image fills its content area.
	/// </summary>
	public UiImageScale Scale
	{
		get => _scale;
		set
		{
			if (_scale == value)
			{
				return;
			}

			_scale = value;
			InvalidateVisual();
		}
	}

	/// <summary>
	/// Gets or sets the sampled region, measured from the image's
	/// top-left corner in normalized coordinates.
	/// </summary>
	public UiRect Uv
	{
		get => _uv;
		set
		{
			if (_uv == value)
			{
				return;
			}

			_uv = value;
			InvalidateVisual();
		}
	}

	/// <summary>
	/// Gets or sets the color the image is multiplied by.
	/// </summary>
	public UiColor Tint { get; set; } = UiColor.White;

	/// <summary>
	/// Gets or sets an explicit background color. When it is unset,
	/// the theme's window background is painted instead.
	/// </summary>
	public UiColor? Background { get; set; }

	/// <summary>
	/// Gets or sets whether a themed one-pixel border is painted.
	/// </summary>
	public bool DrawBorder { get; set; }

	public UiColor? BorderColor { get; set; }

	/// <summary>
	/// Gets the rectangle the image occupies in root coordinates, or
	/// an empty rectangle when there is nothing to draw.
	/// </summary>
	public UiRect ImageBounds => GetImageBounds(GlobalContentBounds);

	/// <summary>
	/// Gets the natural size of the image in logical units.
	/// </summary>
	public UiSize TextureSize => _texture is null
		? default
		: new UiSize(_texture.Width, _texture.Height);

	/// <summary>
	/// Converts a root-space point into a texel coordinate.
	/// </summary>
	/// <param name="position">A point in root coordinates.</param>
	/// <param name="texel">
	/// The horizontal and vertical texel coordinate, measured from the
	/// image's top-left corner. It is not clamped, so a point outside
	/// the image yields a coordinate outside it.
	/// </param>
	/// <returns>
	/// True when an image is drawn and the point maps into it.
	/// </returns>
	public bool TryMapToTexture(UiPoint position, out Vector2 texel)
	{
		texel = default;

		UiImageTexture? texture = _texture;

		if (texture is null || !IsTextureDrawable(texture))
		{
			return false;
		}

		UiRect bounds = GetImageBounds(GlobalContentBounds);

		if (bounds.IsEmpty)
		{
			return false;
		}

		UiRect uv = _uv;

		if (uv.Width <= 0f || uv.Height <= 0f)
		{
			return false;
		}

		float u = (position.X - bounds.X) / bounds.Width;
		float v = (position.Y - bounds.Y) / bounds.Height;

		u = uv.X + (u * uv.Width);
		v = uv.Y + (v * uv.Height);

		texel = new Vector2(u * texture.Width, v * texture.Height);

		return true;
	}

	protected override UiSize MeasureContent(UiSize contentAvailable)
	{
		// The texture's pixel size is its natural size in logical
		// units. An image with no texture takes no room of its own, so
		// a parent's minimum and explicit sizes still govern it.
		return TextureSize;
	}

	protected override void RenderSelf(UiDrawList drawList)
	{
		UiTheme theme = Context?.Theme ?? UiTheme.CreateDark();
		UiRect visual = GlobalVisualBounds;

		drawList.AddRect(
			visual,
			Background ?? theme.Palette.WindowBackground);

		if (DrawBorder)
		{
			drawList.AddBorder(
				visual,
				BorderColor ?? theme.Palette.Border,
				theme.Metrics.BorderWidth);
		}

		UiImageTexture? texture = _texture;

		if (texture is null || !IsTextureDrawable(texture))
		{
			return;
		}

		UiRect bounds = GetImageBounds(GlobalContentBounds);

		if (bounds.IsEmpty)
		{
			return;
		}

		drawList.AddImage(bounds, texture.Id, _uv, Tint);
	}

	/// <summary>
	/// Gets whether an image is registered in the drawing context.
	/// </summary>
	/// <remarks>
	/// A texture identifier only means something inside the registry
	/// that issued it, so an image that was never registered, or was
	/// released, would draw the slot of an unrelated image.
	/// </remarks>
	private bool IsTextureDrawable(UiImageTexture texture)
	{
		return Context?.Textures.IsRegistered(texture) ?? false;
	}

	private UiRect GetImageBounds(UiRect content)
	{
		UiImageTexture? texture = _texture;

		if (texture is null ||
			content.Width <= 0f ||
			content.Height <= 0f)
		{
			return default;
		}

		if (_scale == UiImageScale.Stretch)
		{
			return content;
		}

		float aspect = texture.Width / (float)texture.Height;
		float width = content.Width;
		float height = content.Height;

		// Fit keeps the whole image inside the area, so the tighter
		// axis wins and the other one shrinks. Fill covers the area,
		// so the looser axis wins and the other one grows.
		bool tallerThanImage =
			(content.Width / content.Height) > aspect;

		bool scaleByHeight = _scale == UiImageScale.Fit
			? tallerThanImage
			: !tallerThanImage;

		if (scaleByHeight)
		{
			width = height * aspect;
		}

		else
		{
			height = width / aspect;
		}

		return new UiRect(
			content.X + ((content.Width - width) * 0.5f),
			content.Y + ((content.Height - height) * 0.5f),
			width,
			height);
	}
}
