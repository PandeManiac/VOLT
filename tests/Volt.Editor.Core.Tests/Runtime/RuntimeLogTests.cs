using Volt.Editor.Core.Gameplay;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Runtime;

/// <summary>
/// Covers the log game code writes through the world it runs against:
/// levels, the bounded buffer, and one log per world.
/// </summary>
public sealed class RuntimeLogTests
{
	[Fact]
	public void MessagesKeepTheirLevelAndOrder()
	{
		RuntimeLog log = new();

		log.Info("started");
		log.Warning("careful");

		Assert.Equal(
			[
				new RuntimeLogEntry(RuntimeLogLevel.Info, "started"),
				new RuntimeLogEntry(RuntimeLogLevel.Warning, "careful")
			],
			log.Entries);

		Assert.Equal(0, log.DroppedCount);
	}

	[Fact]
	public void TheBufferIsBoundedAndCountsWhatItDropped()
	{
		RuntimeLog log = new();

		for (int index = 0;
			index < RuntimeLog.MaximumEntries + 5;
			index++)
		{
			log.Info($"tick {index}");
		}

		Assert.Equal(RuntimeLog.MaximumEntries, log.Entries.Count);
		Assert.Equal(5, log.DroppedCount);
		Assert.Equal("tick 0", log.Entries[0].Message);
	}

	[Fact]
	public void ASystemWritesToTheWorldItRunsIn()
	{
		RuntimeScene runtime = CreateRuntime(new LoggingSystem("hello"));

		Assert.Equal("hello", runtime.Log.Entries[0].Message);

		runtime.Update(PlaySession.FixedDeltaSeconds);
		runtime.Update(PlaySession.FixedDeltaSeconds);

		Assert.Equal(3, runtime.Log.Entries.Count);

		runtime.Dispose();
	}

	[Fact]
	public void EachWorldOwnsItsLog()
	{
		RuntimeScene first = CreateRuntime(new LoggingSystem("first"));
		RuntimeScene second = CreateRuntime(new LoggingSystem("second"));

		Assert.Single(first.Log.Entries);
		Assert.Equal("first", first.Log.Entries[0].Message);
		Assert.Equal("second", second.Log.Entries[0].Message);

		first.Dispose();
		second.Dispose();
	}

	[Fact]
	public void AnEmptyMessageIsRejected()
	{
		RuntimeLog log = new();

		Assert.Throws<ArgumentException>(() => log.Info(" "));
	}

	private static RuntimeScene CreateRuntime(IGameplaySystem system)
	{
		return new RuntimeScene(
			new SceneDocument("Log Test"),
			systems: [system]);
	}

	private sealed class LoggingSystem(string message) : IGameplaySystem
	{
		public void OnStart(RuntimeScene scene)
		{
			scene.Log.Info(message);
		}

		public void Update(RuntimeScene scene, float deltaSeconds)
		{
			scene.Log.Info($"{message} {scene.TickCount + 1}");
		}
	}
}
