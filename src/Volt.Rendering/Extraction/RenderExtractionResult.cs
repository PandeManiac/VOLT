namespace Volt.Rendering.Extraction;

/// <summary>
/// Describes the outcome of one ECS render extraction.
/// </summary>
/// <remarks>
/// When the destination has enough room, <see cref="WrittenCount"/>
/// equals <see cref="RequiredCount"/> and every matching entity has
/// been written. When it does not, no records are written; the
/// destination is left untouched rather than partially filled, and
/// <see cref="RequiredCount"/> reports the storage a caller must
/// provide for a complete extraction.
/// </remarks>
public readonly record struct RenderExtractionResult(
	int WrittenCount,
	int RequiredCount)
{
	/// <summary>
	/// Gets whether every required record was written.
	/// </summary>
	/// <remarks>
	/// An empty world succeeds with zero written and zero required.
	/// </remarks>
	public bool Succeeded => WrittenCount == RequiredCount;

	/// <summary>
	/// Gets whether the destination was too small.
	/// </summary>
	public bool CapacityInsufficient =>
		RequiredCount > WrittenCount;
}
