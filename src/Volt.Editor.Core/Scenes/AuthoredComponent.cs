using System.Text.Json;

namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Base type for data authored on a scene entity.
/// </summary>
/// <remarks>
/// Components are plain data objects. They are deliberately not ECS
/// components: an authored scene survives Play Mode, asset reloads,
/// and engine restarts, while a runtime <c>World</c> is disposable.
/// The runtime builder translates these values into real ECS
/// components.
/// </remarks>
public abstract class AuthoredComponent
{
	public abstract string TypeId { get; }

	public virtual string DisplayName => TypeId;

	/// <summary>
	/// Creates a detached copy. Component data may contain nested
	/// mutable values, so overrides must copy those explicitly.
	/// </summary>
	public AuthoredComponent Clone()
	{
		AuthoredComponent clone = (AuthoredComponent)MemberwiseClone();
		clone.OnCloned();
		return clone;
	}

	protected virtual void OnCloned()
	{
	}
}

/// <summary>
/// Preserves a component type that this editor build does not know.
/// </summary>
/// <remarks>
/// Unknown components round-trip through save and load unchanged, so
/// opening a newer scene with an older editor does not silently delete
/// authored data. The inspector shows them read-only.
/// </remarks>
public sealed class UnknownComponentData : AuthoredComponent
{
	public UnknownComponentData(string typeId, JsonElement data)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
		TypeId = typeId;
		Data = data.Clone();
	}

	public override string TypeId { get; }

	public JsonElement Data { get; private set; }

	internal void ReplaceData(JsonElement data)
	{
		Data = data.Clone();
	}
}
