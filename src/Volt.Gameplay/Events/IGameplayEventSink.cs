namespace Volt.Gameplay.Events;

/// <summary>
/// Receives events for an engine-owned subscription without a delegate
/// per subscription.
/// </summary>
/// <remarks>
/// Ability tasks are the motivating client: ten thousand waiting tasks
/// must not allocate ten thousand captured delegates. The sink is one
/// object per subsystem, and the subscription handle identifies which
/// internal record the delivery belongs to.
/// </remarks>
internal interface IGameplayEventSink
{
	/// <summary>
	/// Delivers one event to the subscription that matched it.
	/// </summary>
	/// <param name="gameplayEvent">The published event.</param>
	/// <param name="subscription">The subscription being delivered.</param>
	/// <param name="token">The sink's opaque token.</param>
	void OnGameplayEvent(
		in GameplayEvent gameplayEvent,
		GameplayEventSubscription subscription,
		int token);
}
