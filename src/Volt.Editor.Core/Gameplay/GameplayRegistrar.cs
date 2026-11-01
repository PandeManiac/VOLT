using Volt.Editor.Core.Runtime;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Gameplay;

/// <summary>
/// Collects what one gameplay assembly contributes to the editor and the
/// runtime.
/// </summary>
/// <remarks>
/// <para>
/// A module receives this object from its
/// <see cref="IGameplayModule.Register"/> call. Everything registered is
/// either editor metadata (an authored component the inspector can add),
/// a runtime translation (authored data to ECS storage), or a system to
/// execute. Registering twice is a no-op for components and additive for
/// systems, so a module that is loaded again after a rebuild does not
/// duplicate editor entries.
/// </para>
/// <para>
/// Only plain mutable data becomes an authored component. Arbitrary
/// classes, references to engine objects, and non-unmanaged structs
/// cannot be stored in ECS chunks, so a component the inspector can edit
/// and an ECS component a system iterates are two types joined by a
/// binding, not one type pretending to be both.
/// </para>
/// </remarks>
public sealed class GameplayRegistrar
{
	private readonly ComponentCatalog _catalog;
	private readonly List<EditorComponentDescriptor> _components = [];
	private readonly List<GameplayComponentBinding> _bindings = [];
	private readonly List<IGameplaySystem> _systems = [];
	private readonly List<IRuntimeComponentFormatter> _descriptions = [];
	private readonly List<string> _diagnostics;

	internal GameplayRegistrar(
		ComponentCatalog catalog,
		string assemblyName,
		List<string> diagnostics)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(diagnostics);

		_catalog = catalog;
		AssemblyName = assemblyName;
		_diagnostics = diagnostics;
	}

	/// <summary>
	/// Gets the simple name of the assembly being registered.
	/// </summary>
	public string AssemblyName { get; }

	/// <summary>
	/// Gets the authored components this module registered.
	/// </summary>
	public IReadOnlyList<EditorComponentDescriptor> Components => _components;

	/// <summary>
	/// Gets the authored-to-ECS mappings this module registered.
	/// </summary>
	public IReadOnlyList<GameplayComponentBinding> Bindings => _bindings;

	/// <summary>
	/// Gets the systems this module registered, in execution order.
	/// </summary>
	public IReadOnlyList<IGameplaySystem> Systems => _systems;

	/// <summary>
	/// Gets the runtime component descriptions this module registered.
	/// </summary>
	public IReadOnlyList<IRuntimeComponentFormatter> Descriptions =>
		_descriptions;

	/// <summary>
	/// Gets the diagnostics collected while registering.
	/// </summary>
	public IReadOnlyList<string> Diagnostics => _diagnostics;

	/// <summary>
	/// Gets whether this module contributed nothing at all.
	/// </summary>
	public bool IsEmpty =>
		_components.Count == 0 &&
		_bindings.Count == 0 &&
		_systems.Count == 0 &&
		_descriptions.Count == 0;

	/// <summary>
	/// Registers an authored component so the inspector can add and edit
	/// it.
	/// </summary>
	/// <remarks>
	/// The type needs
	/// <see cref="EditorComponentAttribute"/> for its stable type
	/// identifier and properties of a supported editable type. An
	/// unsupported property type is reported here instead of failing when
	/// the inspector later paints.
	/// </remarks>
	/// <returns>The catalog entry, which may already exist.</returns>
	public EditorComponentDescriptor AddComponent<TComponent>(
		Func<TComponent>? factory = null)
		where TComponent : AuthoredComponent
	{
		EditorComponentDescriptor descriptor;

		try
		{
			descriptor = factory is null
				? _catalog.RegisterOrUpdate<TComponent>()
				: _catalog.RegisterOrUpdate(() => factory());
		}

		catch (Exception exception)
			when (exception is InvalidOperationException or
				NotSupportedException or
				MissingMethodException or
				ArgumentException)
		{
			Report(
				$"Component '{typeof(TComponent).Name}' could not be " +
				$"registered: {exception.Message}");

			throw new GameplayRegistrationException(
				$"Component '{typeof(TComponent).Name}' could not be " +
				"registered.",
				exception);
		}

		_components.Add(descriptor);
		return descriptor;
	}

	/// <summary>
	/// Maps an authored component onto an unmanaged ECS component.
	/// </summary>
	/// <remarks>
	/// The authored type must also be registered with
	/// <see cref="AddComponent{TComponent}"/>; a binding for a type the
	/// editor does not know can never see any data, so that case is
	/// reported as a diagnostic.
	/// </remarks>
	public GameplayComponentBinding BindComponent<TAuthored, TEcs>(
		Func<TAuthored, TEcs> convert)
		where TAuthored : AuthoredComponent
		where TEcs : unmanaged
	{
		GameplayComponentBinding<TAuthored, TEcs> binding = new(convert);

		if (!_catalog.TryGet(typeof(TAuthored), out _))
		{
			Report(
				$"Component '{typeof(TAuthored).Name}' is bound to " +
				$"'{typeof(TEcs).Name}' but is not registered with " +
				"AddComponent, so it can never be authored.");
		}

		_bindings.Add(binding);
		return binding;
	}

	/// <summary>
	/// Describes an unmanaged component so Play Mode can show its values.
	/// </summary>
	/// <remarks>
	/// Without a description the runtime inspector can only report that the
	/// component exists and how large it is, because native storage has no
	/// field names. The projection reads the value the world stores, so it
	/// shows live gameplay state rather than authored data.
	/// </remarks>
	public IRuntimeComponentFormatter DescribeComponent<TComponent>(
		string displayName,
		Func<TComponent, IReadOnlyList<RuntimeFieldSnapshot>> describe)
		where TComponent : unmanaged
	{
		IRuntimeComponentFormatter formatter =
			RuntimeComponentFormatters.Register(displayName, describe);

		_descriptions.Add(formatter);
		return formatter;
	}

	/// <summary>
	/// Registers a system instance.
	/// </summary>
	public void AddSystem(IGameplaySystem system)
	{
		ArgumentNullException.ThrowIfNull(system);
		_systems.Add(system);
	}

	/// <summary>
	/// Creates and registers a system that has a parameterless
	/// constructor.
	/// </summary>
	/// <returns>The instance, so a module can hold a reference.</returns>
	public TSystem AddSystem<TSystem>()
		where TSystem : IGameplaySystem, new()
	{
		TSystem system = new();
		_systems.Add(system);
		return system;
	}

	/// <summary>
	/// Records a message for the Console without failing the load.
	/// </summary>
	public void Report(string message)
	{
		if (!string.IsNullOrWhiteSpace(message))
		{
			_diagnostics.Add(message);
		}
	}
}

/// <summary>
/// Reports a component registration the catalog rejected.
/// </summary>
/// <remarks>
/// The load still completes: the message is already in the diagnostics,
/// and the remaining modules are allowed to register.
/// </remarks>
public sealed class GameplayRegistrationException : Exception
{
	public GameplayRegistrationException(string message, Exception inner)
		: base(message, inner)
	{
	}
}
