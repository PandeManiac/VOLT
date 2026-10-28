using System.Runtime.InteropServices;
using Volt.Ui.Text;

namespace Volt.Ui.Rendering;

/// <summary>
/// Accumulates one frame of UI drawing commands in paint order.
/// </summary>
/// <remarks>
/// The list is a recording buffer, not a widget tree. Widgets append
/// commands while painting; the renderer consumes the completed list
/// after layout and never walks the widget tree itself. Clipping is
/// captured on each command, so a later clip change cannot alter an
/// earlier command.
/// </remarks>
public sealed class UiDrawList
{
	private readonly List<UiDrawCommand> _commands = [];
	private readonly Stack<UiRect> _clips = new();

	private UiRect _clip;
	private UiSize _surface;

	public ReadOnlySpan<UiDrawCommand> Commands =>
		CollectionsMarshal.AsSpan(_commands);

	public int Count => _commands.Count;

	public UiSize Surface => _surface;

	public UiRect Clip => _clip;

	/// <summary>
	/// Starts a frame, clears previous commands, and establishes the
	/// surface-sized root clip.
	/// </summary>
	public void Begin(UiSize surface)
	{
		_commands.Clear();
		_clips.Clear();
		_surface = surface;
		_clip = new UiRect(0f, 0f, surface.Width, surface.Height);
		_clips.Push(_clip);
	}

	/// <summary>
	/// Intersects the current clip with another rectangle.
	/// </summary>
	public void PushClip(UiRect clip)
	{
		_clip = _clip.Intersect(clip);
		_clips.Push(_clip);
	}

	/// <summary>
	/// Restores the previous clip. The root clip cannot be popped.
	/// </summary>
	public void PopClip()
	{
		if (_clips.Count <= 1)
		{
			throw new InvalidOperationException(
				"The root UI clip cannot be popped.");
		}

		_clips.Pop();
		_clip = _clips.Peek();
	}

	public void AddRect(UiRect rect, UiColor color)
	{
		if (rect.IsEmpty || color.A == 0 || _clip.IsEmpty)
		{
			return;
		}

		_commands.Add(
			new UiDrawCommand(
				UiDrawCommandKind.Rectangle,
				rect,
				color,
				_clip,
				0f,
				0f,
				UiPoint.Zero,
				UiPoint.Zero,
				UiTextureId.White,
				default,
				null,
				UiPoint.Zero));
	}

	public void AddRoundedRect(UiRect rect, float radius, UiColor color)
	{
		if (rect.IsEmpty || color.A == 0 || _clip.IsEmpty)
		{
			return;
		}

		_commands.Add(
			new UiDrawCommand(
				UiDrawCommandKind.RoundedRectangle,
				rect,
				color,
				_clip,
				MathF.Max(0f, radius),
				0f,
				UiPoint.Zero,
				UiPoint.Zero,
				UiTextureId.White,
				default,
				null,
				UiPoint.Zero));
	}

	public void AddBorder(
		UiRect rect,
		UiColor color,
		float thickness,
		float radius = 0f)
	{
		if (rect.IsEmpty ||
			color.A == 0 ||
			thickness <= 0f ||
			_clip.IsEmpty)
		{
			return;
		}

		_commands.Add(
			new UiDrawCommand(
				UiDrawCommandKind.Border,
				rect,
				color,
				_clip,
				MathF.Max(0f, radius),
				thickness,
				UiPoint.Zero,
				UiPoint.Zero,
				UiTextureId.White,
				default,
				null,
				UiPoint.Zero));
	}

	public void AddLine(
		UiPoint start,
		UiPoint end,
		float thickness,
		UiColor color)
	{
		if (color.A == 0 || thickness <= 0f || _clip.IsEmpty)
		{
			return;
		}

		_commands.Add(
			new UiDrawCommand(
				UiDrawCommandKind.Line,
				default,
				color,
				_clip,
				0f,
				thickness,
				start,
				end,
				UiTextureId.White,
				default,
				null,
				UiPoint.Zero));
	}

	public void AddText(
		UiTextLayoutResult layout,
		UiPoint origin,
		UiColor color)
	{
		ArgumentNullException.ThrowIfNull(layout);

		if (layout.Glyphs.Length == 0 || color.A == 0 || _clip.IsEmpty)
		{
			return;
		}

		_commands.Add(
			new UiDrawCommand(
				UiDrawCommandKind.Text,
				new UiRect(
					origin.X,
					origin.Y,
					layout.Width,
					layout.Height),
				color,
				_clip,
				0f,
				0f,
				UiPoint.Zero,
				UiPoint.Zero,
				UiTextureId.White,
				default,
				layout,
				origin));
	}

	public void AddImage(
		UiRect rect,
		UiTextureId texture,
		UiRect uv,
		UiColor tint)
	{
		if (rect.IsEmpty || texture.IsWhite || _clip.IsEmpty)
		{
			return;
		}

		_commands.Add(
			new UiDrawCommand(
				UiDrawCommandKind.Image,
				rect,
				tint,
				_clip,
				0f,
				0f,
				UiPoint.Zero,
				UiPoint.Zero,
				texture,
				uv,
				null,
				UiPoint.Zero));
	}

	public UiDrawCommand GetCommand(int index)
	{
		return _commands[index];
	}
}
