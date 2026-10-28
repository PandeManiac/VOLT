using System.Numerics;
using Volt.Editor.Core.Scenes;
using Volt.Editor.Rendering;
using Volt.Editor.Viewport;

namespace Volt.Editor.Tests;

/// <summary>
/// Covers how the viewports turn authored light data into the value
/// both renderers consume.
/// </summary>
public sealed class ViewportLightingTests
{
	[Fact]
	public void Resolve_WithoutALight_UsesTheDefaultKeyLight()
	{
		ViewportLight light = ViewportMath.ResolveLight(
			new SceneDocument("Empty"));

		Assert.Equal(ViewportLight.Default.UnitDirection, light.UnitDirection);
		Assert.Equal(Vector3.One, light.DiffuseColor);
	}

	[Fact]
	public void DefaultEulerAngles_ReproduceTheDefaultKeyLight()
	{
		SceneDocument scene = new("Lighting");
		SceneEntity entity = scene.CreateEntity("Sun");

		entity.Transform.EulerAngles = LightData.DefaultEulerAngles;
		entity.AddComponent(new LightData());

		ViewportLight resolved = ViewportMath.ResolveLight(scene);

		Assert.Equal(
			ViewportLight.Default.UnitDirection.X,
			resolved.UnitDirection.X,
			2);
		Assert.Equal(
			ViewportLight.Default.UnitDirection.Y,
			resolved.UnitDirection.Y,
			2);
		Assert.Equal(
			ViewportLight.Default.UnitDirection.Z,
			resolved.UnitDirection.Z,
			2);
	}

	[Fact]
	public void Catalog_ExposesLightColorAndIntensity()
	{
		EditorComponentDescriptor descriptor = ComponentCatalog.Default.Get(
			LightData.ComponentTypeId);

		Assert.Contains(
			descriptor.Properties,
			property => property.Name == nameof(LightData.Color) &&
				property.Kind == EditorPropertyKind.Color);

		Assert.Contains(
			descriptor.Properties,
			property => property.Name == nameof(LightData.Intensity) &&
				property.Kind == EditorPropertyKind.Float);
	}

	[Fact]
	public void Resolve_UsesTheFirstEnabledLightAndItsWorldRotation()
	{
		SceneDocument scene = new("Lighting");
		SceneEntity disabled = scene.CreateEntity("Disabled");
		disabled.AddComponent(new LightData
		{
			Enabled = false,
			Color = new Vector4(1f, 0f, 0f, 1f)
		});

		SceneEntity light = scene.CreateEntity("Sun");
		light.Transform.EulerAngles = new Vector3(0f, 90f, 0f);
		light.AddComponent(new LightData
		{
			Color = new Vector4(0.2f, 0.4f, 0.8f, 1f),
			Intensity = 2f
		});

		ViewportLight resolved = ViewportMath.ResolveLight(scene);

		Vector3 expected = Vector3.Normalize(
			-Vector3.Transform(Vector3.UnitZ, light.Transform.Rotation));

		Assert.Equal(expected.X, resolved.UnitDirection.X, 4);
		Assert.Equal(expected.Y, resolved.UnitDirection.Y, 4);
		Assert.Equal(expected.Z, resolved.UnitDirection.Z, 4);
		Assert.Equal(0.4f, resolved.DiffuseColor.X, 4);
		Assert.Equal(0.8f, resolved.DiffuseColor.Y, 4);
		Assert.Equal(1.6f, resolved.DiffuseColor.Z, 4);
	}

	[Fact]
	public void DiffuseColor_ClampsCorruptAuthoringValues()
	{
		SceneDocument scene = new("Lighting");
		SceneEntity light = scene.CreateEntity("Sun");

		light.AddComponent(new LightData
		{
			Color = new Vector4(-1f, 2f, 0.5f, 1f),
			Intensity = 20f
		});

		Vector3 diffuse = ViewportMath.ResolveLight(scene).DiffuseColor;

		Assert.Equal(0f, diffuse.X);
		Assert.Equal(8f, diffuse.Y);
		Assert.Equal(4f, diffuse.Z);
	}
}
