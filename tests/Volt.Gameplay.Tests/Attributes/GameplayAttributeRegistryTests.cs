using Volt.Gameplay.Attributes;

namespace Volt.Gameplay.Tests.Attributes;

public sealed class GameplayAttributeRegistryTests
{
	private static GameplayAttributeRegistry Registry =>
		GameplayAttributeRegistry.Shared;

	[Fact]
	public void Register_ReturnsDefinition()
	{
		GameplayAttribute attribute = Registry.Register(
			"VoltGameplayTests.Attributes.Registry.Temperature",
			36.6f,
			-100f,
			100f);

		GameplayAttributeDefinition definition = attribute.Definition;

		Assert.True(attribute.IsValid);
		Assert.Equal(
			"VoltGameplayTests.Attributes.Registry.Temperature",
			attribute.Name);
		Assert.Equal(36.6f, definition.DefaultBaseValue);
		Assert.Equal(-100f, definition.Minimum);
		Assert.Equal(100f, definition.Maximum);
		Assert.True(definition.HasMinimum);
		Assert.True(definition.HasMaximum);
	}

	[Fact]
	public void Register_SameDefinition_IsIdempotent()
	{
		GameplayAttribute first = Registry.Register(
			"VoltGameplayTests.Attributes.Registry.Weight",
			10f,
			0f,
			50f);

		GameplayAttribute second = Registry.Register(
			"VoltGameplayTests.Attributes.Registry.Weight",
			10f,
			0f,
			50f);

		Assert.Equal(first, second);
		Assert.Equal(first.Id, second.Id);
	}

	[Fact]
	public void Register_DifferentDefinition_Throws()
	{
		Registry.Register(
			"VoltGameplayTests.Attributes.Registry.Duplicated",
			10f,
			0f,
			50f);

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(
				() => Registry.Register(
					"VoltGameplayTests.Attributes.Registry.Duplicated",
					20f,
					0f,
					50f));

		Assert.Contains("already registered", exception.Message);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("Health Value")]
	[InlineData(".Health")]
	[InlineData("Health.")]
	[InlineData("Vitals..Health")]
	[InlineData("Health\t")]
	[InlineData("Health\"")]
	public void Register_RejectsInvalidNames(string name)
	{
		Assert.False(GameplayAttributeRegistry.IsValidAttributeName(name, out _));

		Assert.Throws<ArgumentException>(() => Registry.Register(name));
	}

	[Theory]
	[InlineData("Health")]
	[InlineData("Vitals.Health")]
	[InlineData("Attack_Power-2")]
	[InlineData("Vie")]
	public void IsValidAttributeName_AcceptsWellFormedNames(string name)
	{
		Assert.True(GameplayAttributeRegistry.IsValidAttributeName(name, out string? error));
		Assert.Null(error);
	}

	[Fact]
	public void Register_RejectsNonFiniteDefault()
	{
		Assert.Throws<ArgumentException>(
			() => Registry.Register(
				"VoltGameplayTests.Attributes.Registry.Infinite",
				float.PositiveInfinity));
	}

	[Fact]
	public void Register_RejectsInvertedRange()
	{
		Assert.Throws<ArgumentException>(
			() => Registry.Register(
				"VoltGameplayTests.Attributes.Registry.Inverted",
				5f,
				10f,
				1f));
	}

	[Fact]
	public void Register_RejectsDefaultOutsideRange()
	{
		Assert.Throws<ArgumentException>(
			() => Registry.Register(
				"VoltGameplayTests.Attributes.Registry.Outside",
				5f,
				10f,
				20f));
	}

	[Fact]
	public void Register_RejectsNaNBounds()
	{
		Assert.Throws<ArgumentException>(
			() => Registry.Register(
				"VoltGameplayTests.Attributes.Registry.NaN",
				0f,
				float.NaN,
				1f));
	}

	[Fact]
	public void Register_IsCaseInsensitive()
	{
		GameplayAttribute canonical = Registry.Register(
			"VoltGameplayTests.Attributes.Registry.CaseSensitive",
			1f);

		GameplayAttribute lower = Registry.Register(
			"voltgameplaytests.attributes.registry.casesensitive",
			1f);

		Assert.Equal(canonical, lower);
		Assert.Equal(
			"VoltGameplayTests.Attributes.Registry.CaseSensitive",
			lower.Name);
	}

	[Fact]
	public void Get_UnknownName_Throws_AndTryGetDoesNotRegister()
	{
		Assert.False(Registry.TryGet(
			"VoltGameplayTests.Attributes.Registry.Missing",
			out GameplayAttribute attribute));

		Assert.Equal(GameplayAttribute.None, attribute);

		Assert.Throws<ArgumentException>(
			() => Registry.Get(
				"VoltGameplayTests.Attributes.Registry.Missing"));
	}

	[Fact]
	public void TryGetDefinition_InvalidAttribute_IsFalse()
	{
		Assert.False(Registry.TryGetDefinition(
			GameplayAttribute.None,
			out GameplayAttributeDefinition definition));

		Assert.Equal(default, definition);
	}

	[Fact]
	public void Definition_ClampsIntoRange()
	{
		GameplayAttributeDefinition definition = AttributeSchema.Health.Definition;

		Assert.Equal(0f, definition.Clamp(-5f));
		Assert.Equal(50f, definition.Clamp(50f));
		Assert.Equal(100f, definition.Clamp(500f));
		Assert.Equal(0f, definition.Clamp(float.NaN));
	}

	[Fact]
	public void EnumerateAttributes_YieldsRegisteredAttributes()
	{
		int seen = 0;

		foreach (GameplayAttribute attribute in Registry.EnumerateAttributes())
		{
			Assert.True(attribute.IsValid);
			Assert.True(Registry.TryGetDefinition(attribute, out _));

			seen++;
		}

		Assert.Equal(Registry.Count, seen);
	}
}
