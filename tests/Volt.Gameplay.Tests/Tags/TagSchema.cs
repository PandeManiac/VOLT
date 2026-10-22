using Volt.Gameplay.Tags;

namespace Volt.Gameplay.Tests.Tags;

/// <summary>
/// The tag tree shared by the tag tests.
/// </summary>
/// <remarks>
/// The registry is process-wide, so every test uses this one schema
/// rather than registering colliding names of its own. Field order
/// registers parents before children, which also exercises implicit
/// ancestor registration for the deeper names.
/// </remarks>
internal static class TagSchema
{
	private const string Root = "VoltGameplayTests";

	internal static readonly GameplayTag Effect =
		Register($"{Root}.Effect");

	internal static readonly GameplayTag EffectDamage =
		Register($"{Root}.Effect.Damage");

	internal static readonly GameplayTag EffectDamageType1 =
		Register($"{Root}.Effect.Damage.Type1");

	internal static readonly GameplayTag EffectDamageType2 =
		Register($"{Root}.Effect.Damage.Type2");

	internal static readonly GameplayTag EffectBuff =
		Register($"{Root}.Effect.Buff");

	internal static readonly GameplayTag EffectShield =
		Register($"{Root}.Effect.Shield");

	internal static readonly GameplayTag Status =
		Register($"{Root}.Status");

	internal static readonly GameplayTag StatusStunned =
		Register($"{Root}.Status.Stunned");

	internal static readonly GameplayTag StatusStunnedDeep =
		Register($"{Root}.Status.Stunned.Deep");

	internal static readonly GameplayTag StatusBurning =
		Register($"{Root}.Status.Burning");

	internal static readonly GameplayTag Cue =
		Register($"{Root}.Cue");

	internal static readonly GameplayTag CueBurning =
		Register($"{Root}.Cue.Burning");

	internal static readonly GameplayTag Ability =
		Register($"{Root}.Ability");

	internal static readonly GameplayTag AbilityDash =
		Register($"{Root}.Ability.Dash");

	internal static readonly GameplayTag Other =
		Register($"{Root}.Other");

	internal static readonly GameplayTag OtherLeaf =
		Register($"{Root}.Other.Leaf");

	/// <summary>
	/// A bulk tag used to build containers with many tags.
	/// </summary>
	internal static readonly GameplayTag[] Bulk = CreateBulk(40);

	internal static string NameOf(GameplayTag tag)
	{
		return GameplayTagRegistry.Shared.GetName(tag);
	}

	internal static GameplayTag[] Two(params GameplayTag[] tags)
	{
		return tags;
	}

	private static GameplayTag Register(string name)
	{
		return GameplayTagRegistry.Shared.Register(name);
	}

	private static GameplayTag[] CreateBulk(int count)
	{
		GameplayTag[] tags = new GameplayTag[count];

		for (int index = 0; index < count; index++)
		{
			tags[index] = Register($"{Root}.Bulk.Tag{index}");
		}

		return tags;
	}
}
