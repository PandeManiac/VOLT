namespace Volt.Gameplay.Cues;

/// <summary>Identifies one listener subscription in one cue system.</summary>
public readonly struct GameplayCueSubscription : IEquatable<GameplayCueSubscription>
{
	internal GameplayCueSubscription(uint systemId, int slot, uint generation)
	{
		SystemId = systemId;
		Slot = slot;
		Generation = generation;
	}

	/// <summary>An empty subscription handle.</summary>
	public static GameplayCueSubscription None => default;
	/// <summary>Whether this is the empty handle.</summary>
	public bool IsNone => Generation == 0;
	/// <summary>The creating system identity.</summary>
	public uint SystemId { get; }
	/// <summary>The subscription slot.</summary>
	public int Slot { get; }
	/// <summary>The slot generation.</summary>
	public uint Generation { get; }
	/// <inheritdoc />
	public bool Equals(GameplayCueSubscription other) => SystemId == other.SystemId && Slot == other.Slot && Generation == other.Generation;
	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is GameplayCueSubscription other && Equals(other);
	/// <inheritdoc />
	public override int GetHashCode() => HashCode.Combine(SystemId, Slot, Generation);
	/// <summary>Equality operator.</summary>
	public static bool operator ==(GameplayCueSubscription left, GameplayCueSubscription right) => left.Equals(right);
	/// <summary>Inequality operator.</summary>
	public static bool operator !=(GameplayCueSubscription left, GameplayCueSubscription right) => !left.Equals(right);
}
