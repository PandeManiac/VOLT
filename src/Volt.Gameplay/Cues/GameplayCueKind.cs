namespace Volt.Gameplay.Cues;

/// <summary>Classifies a gameplay cue notification.</summary>
public enum GameplayCueKind : byte
{
	/// <summary>A one-shot notification with no persistent state.</summary>
	Execute = 0,
	/// <summary>A persistent cue became active.</summary>
	Added = 1,
	/// <summary>A persistent cue's value changed.</summary>
	Updated = 2,
	/// <summary>A persistent cue ended; its handle is an identity only.</summary>
	Removed = 3
}
