namespace Volt.Gameplay.Cues;

/// <summary>Identifies a persistent cue owned by one cue system.</summary>
public readonly struct GameplayCueHandle : IEquatable<GameplayCueHandle>
{
	internal GameplayCueHandle(uint systemId, int slot, uint generation)
	{
		SystemId = systemId;
		Slot = slot;
		Generation = generation;
	}

	/// <summary>A handle that refers to no cue.</summary>
	public static GameplayCueHandle None => default;
	/// <summary>Whether this is the empty handle.</summary>
	public bool IsNone => Generation == 0;
	/// <summary>The creating system's identity.</summary>
	public uint SystemId { get; }
	/// <summary>The pool slot.</summary>
	public int Slot { get; }
	/// <summary>The generation occupying the slot.</summary>
	public uint Generation { get; }

	/// <inheritdoc />
	public bool Equals(GameplayCueHandle other) =>
		SystemId == other.SystemId && Slot == other.Slot && Generation == other.Generation;
	/// <inheritdoc />
	public override bool Equals(object? obj) => obj is GameplayCueHandle other && Equals(other);
	/// <inheritdoc />
	public override int GetHashCode() => HashCode.Combine(SystemId, Slot, Generation);
	/// <summary>Equality operator.</summary>
	public static bool operator ==(GameplayCueHandle left, GameplayCueHandle right) => left.Equals(right);
	/// <summary>Inequality operator.</summary>
	public static bool operator !=(GameplayCueHandle left, GameplayCueHandle right) => !left.Equals(right);
	/// <inheritdoc />
	public override string ToString() => IsNone ? "GameplayCueHandle.None" : $"GameplayCueHandle(#{SystemId}:{Slot}:{Generation})";
}
