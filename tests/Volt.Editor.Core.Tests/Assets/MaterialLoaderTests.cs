using System.Numerics;
using Volt.Editor.Core.Assets;

namespace Volt.Editor.Core.Tests.Assets;

/// <summary>
/// Covers material parsing: the two accepted albedo forms and the cases
/// a broken file has to report instead of guessing.
/// </summary>
public sealed class MaterialLoaderTests
{
	[Fact]
	public void AHexColourBecomesLinearComponents()
	{
		MaterialAsset material = MaterialLoader.Parse(
			"Assets/Materials/Red.material",
			new StringReader("""{ "albedo": "#FF8000" }"""));

		Assert.Equal("Assets/Materials/Red.material", material.Id);
		Assert.Equal(1f, material.Albedo.X, 4);
		Assert.Equal(128f / 255f, material.Albedo.Y, 4);
		Assert.Equal(0f, material.Albedo.Z, 4);
	}

	[Fact]
	public void AnAlphaPairIsIgnored()
	{
		MaterialAsset material = MaterialLoader.Parse(
			"m.material",
			new StringReader("""{ "albedo": "#336699FF" }"""));

		Assert.Equal(0x33 / 255f, material.Albedo.X, 4);
		Assert.Equal(0x99 / 255f, material.Albedo.Z, 4);
	}

	[Fact]
	public void ThreeNumbersAreAccepted()
	{
		MaterialAsset material = MaterialLoader.Parse(
			"m.material",
			new StringReader("""{ "albedo": [0.25, 0.5, 1.0] }"""));

		Assert.Equal(new Vector3(0.25f, 0.5f, 1f), material.Albedo);
	}

	[Fact]
	public void AMissingAlbedoIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			MaterialLoader.Parse("m.material", new StringReader("{}")));

		Assert.Contains("does not set an albedo", error.Message);
	}

	[Fact]
	public void AnInvalidHexIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			MaterialLoader.Parse(
				"m.material",
				new StringReader("""{ "albedo": "not a colour" }""")));

		Assert.Contains("invalid albedo colour", error.Message);
	}

	[Fact]
	public void MalformedJsonIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			MaterialLoader.Parse("m.material", new StringReader("{ albedo:")));

		Assert.Contains("not valid material JSON", error.Message);
	}

	[Fact]
	public void ATexturePathIsKept()
	{
		MaterialAsset material = MaterialLoader.Parse(
			"m.material",
			new StringReader(
				"""{ "albedo": "#FFFFFF", "texture": "Assets/Textures/wood.png" }"""));

		Assert.Equal("Assets/Textures/wood.png", material.Texture);
	}

	[Fact]
	public void AMaterialWithoutATextureReportsNone()
	{
		MaterialAsset material = MaterialLoader.Parse(
			"m.material",
			new StringReader("""{ "albedo": "#FFFFFF" }"""));

		Assert.Null(material.Texture);
	}

	[Fact]
	public void AnEmptyTexturePathIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			MaterialLoader.Parse(
				"m.material",
				new StringReader(
					"""{ "albedo": "#FFFFFF", "texture": " " }""")));

		Assert.Contains("texture as a non-empty path", error.Message);
	}

	[Fact]
	public void ANonStringTextureIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			MaterialLoader.Parse(
				"m.material",
				new StringReader(
					"""{ "albedo": "#FFFFFF", "texture": 3 }""")));

		Assert.Contains("texture as a non-empty path", error.Message);
	}

	[Fact]
	public void TheDefaultFileReadsBackAsTheDefaultColour()
	{
		MaterialAsset material = MaterialLoader.Parse(
			"m.material",
			new StringReader(MaterialLoader.CreateDefaultJson()));

		Assert.Equal(
			MaterialLoader.DefaultAlbedo,
			Vector3.Clamp(material.Albedo, Vector3.Zero, Vector3.One));
	}
}
