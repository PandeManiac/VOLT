using System.Reflection;
using System.Runtime.Loader;
using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Gameplay;

/// <summary>
/// The outcome of loading one gameplay assembly.
/// </summary>
/// <remarks>
/// A failed load is an ordinary result, not an exception: a project with
/// a broken or stale build must leave the editor usable. Callers report
/// <see cref="Error"/> where the developer can read it.
/// </remarks>
public sealed class GameplayAssemblyLoadResult
{
	private GameplayAssemblyLoadResult(
		GameplayAssembly? assembly,
		string? error,
		IReadOnlyList<string> diagnostics)
	{
		Assembly = assembly;
		Error = error;
		Diagnostics = diagnostics;
	}

	/// <summary>
	/// Gets the loaded assembly, or <see langword="null"/> on failure.
	/// </summary>
	public GameplayAssembly? Assembly { get; }

	public bool Succeeded => Assembly is not null;

	/// <summary>
	/// Gets the reason the load failed, or <see langword="null"/>.
	/// </summary>
	public string? Error { get; }

	/// <summary>
	/// Gets the registration and load diagnostics collected either way.
	/// </summary>
	public IReadOnlyList<string> Diagnostics { get; }

	internal static GameplayAssemblyLoadResult Success(
		GameplayAssembly assembly)
	{
		return new GameplayAssemblyLoadResult(
			assembly,
			null,
			assembly.Diagnostics);
	}

	internal static GameplayAssemblyLoadResult Failure(
		string error,
		IReadOnlyList<string> diagnostics)
	{
		return new GameplayAssemblyLoadResult(null, error, diagnostics);
	}
}

/// <summary>
/// A project-owned gameplay assembly loaded beside the engine.
/// </summary>
/// <remarks>
/// <para>
/// The assembly is loaded into its own collectible
/// <see cref="AssemblyLoadContext"/>, but every assembly the host also
/// provides resolves to the host's copy. That is what keeps
/// <c>AuthoredComponent</c>, <c>World</c>, and the rest one identity
/// across the boundary: a game that built against a copy of the engine
/// would otherwise hand the editor objects from unrelated types.
/// </para>
/// <para>
/// Loading is explicit. Opening a folder, refreshing the asset browser,
/// or adding a component never loads code; only a build or Play workflow
/// does.
/// </para>
/// <para>
/// <see cref="Unload"/> deactivates the context and detaches everything the
/// assembly registered with the rest of the process, so a later load is a
/// clean load rather than a second copy of the same contribution. It does
/// not promise that the managed memory is reclaimed: the ECS component
/// registry records each component's CLR type for the lifetime of the
/// process, and a game necessarily registers component types. That is why
/// there is no hot reload here — a rebuild is a new load, not a swap.
/// </para>
/// </remarks>
public sealed class GameplayAssembly : IDisposable
{
	private readonly string _assemblyPath;
	private GameplayLoadContext? _context;
	private bool _disposed;

	private GameplayAssembly(
		string assemblyPath,
		string assemblyName,
		GameplayLoadContext context,
		GameplayRegistrar registration,
		IReadOnlyList<string> diagnostics)
	{
		_assemblyPath = assemblyPath;
		AssemblyName = assemblyName;
		_context = context;
		Registration = registration;
		Diagnostics = diagnostics;
	}

	/// <summary>
	/// Gets the absolute path the assembly was loaded from.
	/// </summary>
	public string AssemblyPath => _assemblyPath;

	/// <summary>
	/// Gets the simple assembly name.
	/// </summary>
	public string AssemblyName { get; }

	/// <summary>
	/// Gets the components, bindings, and systems the assembly declared.
	/// </summary>
	public GameplayRegistrar Registration { get; }

	/// <summary>
	/// Gets the messages produced by loading and registering.
	/// </summary>
	public IReadOnlyList<string> Diagnostics { get; }

	/// <summary>
	/// Gets whether the load context is still active.
	/// </summary>
	public bool IsLoaded => _context is not null;

	/// <summary>
	/// Gets the load context while it is active, for tests that assert
	/// it is released.
	/// </summary>
	internal AssemblyLoadContext? LoadContext => _context;

	/// <summary>
	/// Loads a built gameplay assembly and runs its modules'
	/// registration.
	/// </summary>
	/// <param name="assemblyPath">The built assembly file.</param>
	/// <param name="catalog">
	/// The catalog the editor reads authored components from. Passed in
	/// rather than assumed so a headless tool or test can use its own.
	/// </param>
	/// <returns>
	/// A result carrying either the assembly or the reason it could not be
	/// loaded. This method does not throw for a missing, corrupt, or
	/// mismatched assembly.
	/// </returns>
	public static GameplayAssemblyLoadResult Load(
		string assemblyPath,
		ComponentCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);

		List<string> diagnostics = [];

		if (string.IsNullOrWhiteSpace(assemblyPath))
		{
			return GameplayAssemblyLoadResult.Failure(
				"No gameplay assembly path was given.",
				diagnostics);
		}

		string fullPath;

		try
		{
			fullPath = Path.GetFullPath(assemblyPath);
		}

		catch (Exception exception)
			when (exception is ArgumentException or
				NotSupportedException or
				PathTooLongException or
				IOException)
		{
			return GameplayAssemblyLoadResult.Failure(
				$"'{assemblyPath}' is not a usable assembly path: " +
				$"{exception.Message}",
				diagnostics);
		}

		if (!File.Exists(fullPath))
		{
			return GameplayAssemblyLoadResult.Failure(
				$"The gameplay assembly '{fullPath}' does not exist. " +
				"Build the project's game code first.",
				diagnostics);
		}

		GameplayLoadContext? context = null;

		try
		{
			context = new GameplayLoadContext(fullPath);

			Assembly assembly = context.LoadFromAssemblyPath(fullPath);
			string assemblyName = assembly.GetName().Name ??
				Path.GetFileNameWithoutExtension(fullPath);

			GameplayRegistrar registrar = new(
				catalog,
				assemblyName,
				diagnostics);

			RegisterModules(assembly, assemblyName, registrar, diagnostics);

			GameplayAssembly loaded = new(
				fullPath,
				assemblyName,
				context,
				registrar,
				diagnostics);

			context = null;
			return GameplayAssemblyLoadResult.Success(loaded);
		}

		catch (Exception exception)
		{
			// A user-provided assembly can fail in ways the runtime does
			// not document, and none of them may take the editor down.
			context?.Unload();

			return GameplayAssemblyLoadResult.Failure(
				$"The gameplay assembly '{fullPath}' could not be " +
				$"loaded: {exception.Message}",
				diagnostics);
		}
	}

	/// <summary>
	/// Unloads the load context.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> when the assembly was already unloaded.
	/// </returns>
	public bool Unload()
	{
		if (_context is null)
		{
			return false;
		}

		// A runtime description closes over the component's CLR type, so
		// leaving it registered would keep this assembly's load context
		// reachable. Component and binding metadata is inert and may stay:
		// it is what lets an already-loaded scene keep saving.
		foreach (IRuntimeComponentFormatter description in
			Registration.Descriptions)
		{
			RuntimeComponentFormatters.Unregister(description.Type);
		}

		_context.Unload();
		_context = null;

		return true;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		Unload();
	}

	private static void RegisterModules(
		Assembly assembly,
		string assemblyName,
		GameplayRegistrar registrar,
		List<string> diagnostics)
	{
		Type[] types;

		try
		{
			types = assembly.GetTypes();
		}

		catch (ReflectionTypeLoadException exception)
		{
			types = [.. exception.Types.OfType<Type>()];

			string reason = exception.LoaderExceptions
				.FirstOrDefault(static loader => loader is not null)
				?.Message ?? "a type could not be loaded";

			diagnostics.Add(
				$"'{assemblyName}' did not load completely: {reason}");
		}

		List<Type> modules = [.. types
			.Where(IsGameplayModule)
			.OrderBy(static type => type.FullName, StringComparer.Ordinal)];

		if (modules.Count == 0)
		{
			diagnostics.Add(
				$"'{assemblyName}' declares no {nameof(IGameplayModule)} " +
				"implementation, so it contributes no gameplay.");
			return;
		}

		foreach (Type type in modules)
		{
			IGameplayModule module;

			try
			{
				module = (IGameplayModule)(
					Activator.CreateInstance(type) ??
					throw new InvalidOperationException(
						$"'{type.Name}' could not be created."));
			}

			catch (Exception exception)
			{
				diagnostics.Add(
					$"Gameplay module '{type.FullName}' could not be " +
					$"created: {exception.Message}");

				continue;
			}

			try
			{
				module.Register(registrar);
			}

			catch (Exception exception)
			{
				// One broken module must not hide the others, and the
				// editor stays in a recoverable state.
				diagnostics.Add(
					$"Gameplay module '{type.FullName}' failed to " +
					$"register: {exception.Message}");
			}
		}
	}

	private static bool IsGameplayModule(Type type)
	{
		return typeof(IGameplayModule).IsAssignableFrom(type) &&
			type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } &&
			type.GetConstructor(Type.EmptyTypes) is not null;
	}
}

/// <summary>
/// The collectible context a gameplay assembly is loaded into.
/// </summary>
/// <remarks>
/// Resolution order is deliberate. An assembly the host already has wins,
/// so engine types keep one identity; then an engine assembly the host
/// ships but has not loaded yet is loaded into the default context from
/// the host's own directory; then the assembly's private dependencies
/// resolve next to it.
/// </remarks>
internal sealed class GameplayLoadContext : AssemblyLoadContext
{
	private readonly AssemblyDependencyResolver _resolver;
	private readonly string _hostDirectory;

	public GameplayLoadContext(string mainAssemblyPath)
		: base(
			$"Volt.Gameplay:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}",
			isCollectible: true)
	{
		_resolver = new AssemblyDependencyResolver(mainAssemblyPath);
		_hostDirectory = AppContext.BaseDirectory;
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		string? name = assemblyName.Name;

		if (string.IsNullOrEmpty(name))
		{
			return null;
		}

		Assembly? shared = FindLoadedHostAssembly(name);

		if (shared is not null)
		{
			return shared;
		}

		if (IsEngineAssemblyName(name))
		{
			string hostPath = Path.Combine(_hostDirectory, name + ".dll");

			if (File.Exists(hostPath))
			{
				try
				{
					return Default.LoadFromAssemblyPath(
						Path.GetFullPath(hostPath));
				}

				catch (FileLoadException)
				{
					// Already loaded from another location; fall through
					// to the host copy that exists.
					Assembly? loaded = FindLoadedHostAssembly(name);

					if (loaded is not null)
					{
						return loaded;
					}
				}
			}
		}

		string? path = _resolver.ResolveAssemblyToPath(assemblyName);

		if (!string.IsNullOrEmpty(path) && File.Exists(path))
		{
			return LoadFromAssemblyPath(path);
		}

		return null;
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);

		return !string.IsNullOrEmpty(path) && File.Exists(path)
			? LoadUnmanagedDllFromPath(path)
			: IntPtr.Zero;
	}

	private static Assembly? FindLoadedHostAssembly(string name)
	{
		foreach (Assembly loaded in Default.Assemblies)
		{
			if (string.Equals(
				loaded.GetName().Name,
				name,
				StringComparison.OrdinalIgnoreCase))
			{
				return loaded;
			}
		}

		return null;
	}

	private static bool IsEngineAssemblyName(string name)
	{
		return name.StartsWith("Volt.", StringComparison.Ordinal);
	}
}
