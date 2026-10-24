namespace Volt.Gameplay.Tasks;

/// <summary>
/// Selects whether a tag-condition task waits for a tag to be present
/// or absent.
/// </summary>
public enum GameplayTagWaitMode : byte
{
	/// <summary>
	/// Completes once the tag is present.
	/// </summary>
	Present = 0,

	/// <summary>
	/// Completes once the tag is absent.
	/// </summary>
	Absent = 1
}
