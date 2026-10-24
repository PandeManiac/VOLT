namespace Volt.Gameplay.Cues;

/// <summary>Controls how cue subscriptions match cue tags.</summary>
public enum GameplayCueMatch : byte
{
	/// <summary>Only the exact registered cue tag matches.</summary>
	Exact = 0,
	/// <summary>The subscription tag and every descendant match.</summary>
	Hierarchical = 1
}
