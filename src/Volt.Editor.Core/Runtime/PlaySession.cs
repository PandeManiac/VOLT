using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Runtime;

public enum PlayState
{
	Stopped = 0,
	Playing = 1,
	Paused = 2
}

/// <summary>
/// Owns the editor's Play Mode lifetime.
/// </summary>
/// <remarks>
/// Starting Play serializes and reloads the authored scene as a
/// snapshot, then builds a separate ECS world from that snapshot. A
/// runtime system therefore cannot reach authored component objects.
/// Stopping disposes the ECS world; no authored data is copied back.
/// </remarks>
public sealed class PlaySession : IDisposable
{
	public const float FixedDeltaSeconds = 1f / 60f;

	private const float MaximumFrameDelta = 0.25f;
	private const int MaximumTicksPerFrame = 8;

	private readonly SceneDocument _authoredScene;
	private readonly IEnumerable<IRuntimeBehaviourFactory>? _factories;
	private readonly Func<string, bool>? _assetExists;
	private readonly RuntimeInputState _input;

	private RuntimeScene? _runtime;
	private double _accumulator;
	private bool _disposed;

	public PlaySession(
		SceneDocument authoredScene,
		RuntimeInputState? input = null,
		IEnumerable<IRuntimeBehaviourFactory>? behaviourFactories = null,
		Func<string, bool>? assetExists = null)
	{
		ArgumentNullException.ThrowIfNull(authoredScene);

		_authoredScene = authoredScene;
		_input = input ?? new RuntimeInputState();
		_factories = behaviourFactories;
		_assetExists = assetExists;
	}

	public event Action<PlaySession>? StateChanged;

	public PlayState State { get; private set; } = PlayState.Stopped;

	public SceneDocument AuthoredScene => _authoredScene;

	public RuntimeScene? Runtime => _runtime;

	public RuntimeInputState Input => _input;

	public float FixedDelta => FixedDeltaSeconds;

	public int TicksLastFrame { get; private set; }

	public RuntimeScene Start()
	{
		ThrowIfDisposed();

		if (_runtime is not null)
		{
			Stop();
		}

		// A snapshot loaded through the same validated serializer used
		// by disk persistence makes runtime isolation explicit.
		SceneDocument snapshot = SceneSerializer.Deserialize(
			SceneSerializer.Serialize(_authoredScene));

		_runtime = new RuntimeScene(
			snapshot,
			_input,
			_factories,
			_assetExists);

		_accumulator = 0.0;
		TicksLastFrame = 0;
		State = PlayState.Playing;
		StateChanged?.Invoke(this);

		return _runtime;
	}

	public void Pause()
	{
		ThrowIfDisposed();

		if (State != PlayState.Playing)
		{
			return;
		}

		State = PlayState.Paused;
		StateChanged?.Invoke(this);
	}

	public void Resume()
	{
		ThrowIfDisposed();

		if (State != PlayState.Paused)
		{
			return;
		}

		State = PlayState.Playing;
		StateChanged?.Invoke(this);
	}

	/// <summary>
	/// Advances exactly one fixed tick while paused.
	/// </summary>
	public bool Step()
	{
		ThrowIfDisposed();

		if (State != PlayState.Paused || _runtime is null)
		{
			return false;
		}

		_runtime.Update(FixedDeltaSeconds);
		TicksLastFrame = 1;
		return true;
	}

	public void Stop()
	{
		ThrowIfDisposed();

		if (_runtime is not null)
		{
			_runtime.Dispose();
			_runtime = null;
		}

		_accumulator = 0.0;
		TicksLastFrame = 0;
		State = PlayState.Stopped;
		StateChanged?.Invoke(this);
	}

	/// <summary>
	/// Advances the runtime from a rendered frame delta. Only fixed
	/// ticks mutate simulation state.
	/// </summary>
	public int Update(float frameDeltaSeconds)
	{
		ThrowIfDisposed();

		TicksLastFrame = 0;

		if (State != PlayState.Playing || _runtime is null)
		{
			return 0;
		}

		if (!float.IsFinite(frameDeltaSeconds) || frameDeltaSeconds <= 0f)
		{
			return 0;
		}

		_accumulator += Math.Min(
			frameDeltaSeconds,
			MaximumFrameDelta);

		int ticks = 0;

		while (_accumulator >= FixedDeltaSeconds &&
			ticks < MaximumTicksPerFrame)
		{
			_runtime.Update(FixedDeltaSeconds);
			_accumulator -= FixedDeltaSeconds;
			ticks++;
		}

		if (ticks == MaximumTicksPerFrame)
		{
			// Do not let a slow frame build an unbounded catch-up debt.
			_accumulator = Math.Min(
				_accumulator,
				FixedDeltaSeconds);
		}

		TicksLastFrame = ticks;
		return ticks;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		if (_runtime is not null)
		{
			_runtime.Dispose();
			_runtime = null;
		}

		_disposed = true;
		State = PlayState.Stopped;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}
}
