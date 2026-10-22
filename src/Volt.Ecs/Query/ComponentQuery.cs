
namespace Volt.Ecs.Component;

/// <summary>
/// Describes the component requirements used to match ECS archetypes.
/// </summary>
public sealed class ComponentQuery
{
	public ComponentSignature Required { get; }
	public ComponentSignature Excluded { get; }
	public ComponentSignature Any { get; }

	public ComponentQuery(
		ComponentSignature? required = null,
		ComponentSignature? excluded = null,
		ComponentSignature? any = null)
	{
		Required = required ?? ComponentSignature.Empty;
		Excluded = excluded ?? ComponentSignature.Empty;
		Any = any ?? ComponentSignature.Empty;

		if (Required.ContainsAny(Excluded))
		{
			throw new ArgumentException(
				"Required and excluded component types cannot overlap.");
		}
	}

	/// <summary>
	/// Determines whether an archetype satisfies this query.
	/// </summary>
	public bool Matches(Archetype archetype)
	{
		ArgumentNullException.ThrowIfNull(archetype);
		ComponentSignature signature = archetype.Signature;

		return signature.ContainsAll(Required)
			&& !signature.ContainsAny(Excluded)
			&& (Any.Count == 0 || signature.ContainsAny(Any));
	}
}
