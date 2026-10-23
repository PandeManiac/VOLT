using Volt.Ecs;

namespace Volt.Gameplay.Effects;

/// <summary>
/// Describes the pre-application state of an effect instance that an
/// application stacked into. It lets a failed activation undo only its
/// own stack instead of removing the whole instance.
/// </summary>
/// <remarks>
/// The snapshot stores absolute due times and the pending order. The
/// scheduler leaves the previous heap entry in place, so restoring the
/// order makes that entry valid again without scheduling new work.
/// </remarks>
internal readonly struct GameplayEffectApplicationRollback
{
	internal GameplayEffectApplicationRollback(
		GameplayEffectHandle handle,
		int stackCount,
		Entity source,
		double expireTime,
		double nextPeriodTime,
		long pendingOrder)
	{
		Handle = handle;
		StackCount = stackCount;
		Source = source;
		ExpireTime = expireTime;
		NextPeriodTime = nextPeriodTime;
		PendingOrder = pendingOrder;
		WasStacked = true;
	}

	public GameplayEffectHandle Handle { get; }

	public int StackCount { get; }

	public Entity Source { get; }

	public double ExpireTime { get; }

	public double NextPeriodTime { get; }

	public long PendingOrder { get; }

	public bool WasStacked { get; }
}
