namespace Volt.Gameplay.Simulation;

/// <summary>
/// Identifies the clock claim of one fixed-step gameplay integration.
/// </summary>
/// <remarks>
/// <para>
/// A gameplay clock has one owner. Effect, task, and ability systems
/// store the claim they were attached with and compare it by reference
/// when releasing, so a system can only release the claim its own
/// integration acquired. A construction that fails part way through
/// therefore cannot detach a different integration that already owns
/// the clock.
/// </para>
/// <para>
/// The claim is a plain identity token. It holds no state, allocates
/// nothing per fixed step, and is never compared on the entity or task
/// hot path.
/// </para>
/// </remarks>
internal sealed class SimulationClockClaim
{
}
