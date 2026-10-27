namespace Volt.Ui;

/// <summary>
/// Owns the image slots a <see cref="UiContext"/> can draw.
/// </summary>
/// <remarks>
/// <para>
/// Registration assigns an identifier from one to
/// <see cref="MaximumTextures"/>. Slot numbers are stable: releasing
/// an image makes its slot available to the next registration without
/// renumbering the images that stayed. A renderer can therefore keep
/// one GPU resource per slot and free exactly the slots that became
/// empty.
/// </para>
/// <para>
/// The cap exists because a renderer binds the images as one
/// descriptor array of that length. It is deliberately small: the
/// registry is for the handful of images a user interface draws, not
/// for a general texture atlas.
/// </para>
/// </remarks>
public sealed class UiTextureRegistry
{
	/// <summary>
	/// The number of images one context can hold at a time.
	/// </summary>
	/// <remarks>
	/// Renderers bind exactly this many image descriptors, and the UI
	/// shaders declare an array of this length. All three must agree.
	/// </remarks>
	public const int MaximumTextures = 16;

	private readonly UiImageTexture?[] _slots =
		new UiImageTexture?[MaximumTextures];

	/// <summary>
	/// Gets the live images per slot. A free slot is null.
	/// </summary>
	public ReadOnlySpan<UiImageTexture?> Slots => _slots;

	/// <summary>
	/// Gets the number of live images.
	/// </summary>
	public int Count { get; private set; }

	/// <summary>
	/// Creates an image and immediately registers it.
	/// </summary>
	public UiImageTexture Create(
		int width,
		int height,
		string? name = null)
	{
		UiImageTexture texture = new(width, height, name);
		Register(texture);

		return texture;
	}

	/// <summary>
	/// Registers an image and returns its identifier.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The image is already registered, or every slot is taken.
	/// </exception>
	public UiTextureId Register(UiImageTexture texture)
	{
		ArgumentNullException.ThrowIfNull(texture);

		if (texture.IsRegistered)
		{
			throw new InvalidOperationException(
				$"Image '{texture.Name}' is already registered as " +
				$"texture {texture.Id.Value}.");
		}

		for (int index = 0; index < _slots.Length; index++)
		{
			if (_slots[index] is not null)
			{
				continue;
			}

			_slots[index] = texture;
			texture.Id = UiTextureId.FromIndex(index + 1);
			Count++;

			return texture.Id;
		}

		throw new InvalidOperationException(
			$"A UI context draws at most {MaximumTextures} images. " +
			"Release one before registering another.");
	}

	/// <summary>
	/// Releases an image and frees its slot.
	/// </summary>
	/// <returns>
	/// True when the image held a slot in this registry.
	/// </returns>
	public bool Release(UiImageTexture texture)
	{
		ArgumentNullException.ThrowIfNull(texture);

		int index = texture.Id.Value - 1;

		if (index < 0 ||
			index >= _slots.Length ||
			!ReferenceEquals(_slots[index], texture))
		{
			return false;
		}

		_slots[index] = null;
		texture.Id = UiTextureId.White;
		Count--;

		return true;
	}

	/// <summary>
	/// Releases every image. Used when a context is disposed.
	/// </summary>
	public void Clear()
	{
		for (int index = 0; index < _slots.Length; index++)
		{
			UiImageTexture? texture = _slots[index];

			if (texture is null)
			{
				continue;
			}

			_slots[index] = null;
			texture.Id = UiTextureId.White;
		}

		Count = 0;
	}

	/// <summary>
	/// Gets whether an image currently holds a slot here.
	/// </summary>
	public bool IsRegistered(UiImageTexture texture)
	{
		if (texture is null)
		{
			return false;
		}

		int index = texture.Id.Value - 1;

		return index >= 0 &&
			index < _slots.Length &&
			ReferenceEquals(_slots[index], texture);
	}

	/// <summary>
	/// Gets the image a slot identifier refers to, or null when the
	/// identifier is out of range or its slot is free.
	/// </summary>
	public UiImageTexture? Resolve(UiTextureId id)
	{
		int index = id.Value - 1;

		return index >= 0 && index < _slots.Length
			? _slots[index]
			: null;
	}
}
