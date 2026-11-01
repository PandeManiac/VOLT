using System.Reflection;
using Volt.Ecs;
using Volt.Ecs.Component;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Gameplay;

/// <summary>
/// Maps one authored component onto an unmanaged ECS component.
/// </summary>
/// <remarks>
/// <para>
/// Authored data is plain managed JSON-backed data so a scene can be
/// edited and saved without a world. A system, however, needs the
/// data-oriented layout the ECS provides. A binding is the declared
/// translation between the two: when Play Mode builds an entity that has
/// the authored component, the binding adds the ECS component with the
/// value its converter returns.
/// </para>
/// <para>
/// The conversion runs once per entity at world construction, not per
/// tick, so it may be as expensive as cloning a value needs to be.
/// </para>
/// </remarks>
public abstract class GameplayComponentBinding
{
	private protected GameplayComponentBinding(string authoredTypeId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(authoredTypeId);
		AuthoredTypeId = authoredTypeId;
	}

	/// <summary>
	/// Gets the stable authored component identifier this binding reads.
	/// </summary>
	public string AuthoredTypeId { get; }

	/// <summary>
	/// Gets the ECS component type name, for diagnostics.
	/// </summary>
	public abstract string ComponentTypeName { get; }

	/// <summary>
	/// Adds this binding's ECS component to one runtime entity.
	/// </summary>
	/// <returns>
	/// <see langword="false"/> when the authored component is not the
	/// bound type, so a caller can keep looking.
	/// </returns>
	/// <remarks>
	/// The caller reports a thrown exception as a diagnostic; a conversion
	/// that rejects its input therefore fails one entity rather than the
	/// whole world.
	/// </remarks>
	public abstract bool Attach(
		World world,
		Entity entity,
		AuthoredComponent authored);
}

/// <summary>
/// Binds an authored component type to an unmanaged ECS component type.
/// </summary>
/// <typeparam name="TAuthored">The authored component type.</typeparam>
/// <typeparam name="TEcs">The unmanaged ECS component type.</typeparam>
public sealed class GameplayComponentBinding<TAuthored, TEcs>
	: GameplayComponentBinding
	where TAuthored : AuthoredComponent
	where TEcs : unmanaged
{
	private readonly Func<TAuthored, TEcs> _convert;

	/// <summary>
	/// Creates a binding whose converter projects authored data into ECS
	/// storage.
	/// </summary>
	public GameplayComponentBinding(Func<TAuthored, TEcs> convert)
		: base(GetAuthoredTypeId())
	{
		ArgumentNullException.ThrowIfNull(convert);
		_convert = convert;
	}

	public override string ComponentTypeName => typeof(TEcs).Name;

	public override bool Attach(
		World world,
		Entity entity,
		AuthoredComponent authored)
	{
		ArgumentNullException.ThrowIfNull(world);
		ArgumentNullException.ThrowIfNull(authored);

		if (authored is not TAuthored typed)
		{
			return false;
		}

		world.AddComponent(entity, _convert(typed));
		return true;
	}

	private static string GetAuthoredTypeId()
	{
		Type type = typeof(TAuthored);

		EditorComponentAttribute attribute =
			type.GetCustomAttribute<EditorComponentAttribute>()
			?? throw new InvalidOperationException(
				$"Component type '{type.Name}' has no " +
				$"{nameof(EditorComponentAttribute)}, so it has no stable " +
				"identity to bind.");

		return attribute.ComponentTypeId;
	}
}
