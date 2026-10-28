namespace Volt.Ui.Layout;

/// <summary>
/// Describes how a dimension is derived from available space.
/// </summary>
public enum UiLengthKind
{
	/// <summary>
	/// The dimension is determined by the widget's content.
	/// </summary>
	Auto = 0,

	/// <summary>
	/// The dimension is a fixed number of logical units.
	/// </summary>
	Points = 1,

	/// <summary>
	/// The dimension is a fraction of the available space.
	/// </summary>
	Percent = 2
}

/// <summary>
/// A resolved-or-relative dimension used by the layout engine.
/// </summary>
public readonly record struct UiLength(UiLengthKind Kind, float Value)
{
	public static UiLength Auto => new(UiLengthKind.Auto, 0f);

	public static UiLength Points(float value)
	{
		if (!float.IsFinite(value))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				"UI dimensions must be finite.");
		}

		return new UiLength(UiLengthKind.Points, value);
	}

	/// <summary>
	/// Creates a percentage length. Values are clamped to 0..1 when
	/// resolved.
	/// </summary>
	public static UiLength Percent(float fraction)
	{
		if (!float.IsFinite(fraction))
		{
			throw new ArgumentOutOfRangeException(
				nameof(fraction),
				"UI percentages must be finite.");
		}

		return new UiLength(UiLengthKind.Percent, fraction);
	}

	public bool IsAuto => Kind == UiLengthKind.Auto;

	/// <summary>
	/// Resolves the length against available space.
	/// </summary>
	public float Resolve(float available)
	{
		return Kind switch
		{
			UiLengthKind.Points => Value,
			UiLengthKind.Percent => MathF.Max(0f, available) * Value,
			_ => 0f
		};
	}
}
