using Volt.Rendering.Resources;
using Volt.VulkanMeshDemo;

namespace Volt.VulkanMeshDemo.Tests;

public sealed class EcsMeshResolverTests
{
	[Fact]
	public void CreateDefault_MapsEveryCompleteHandle()
	{
		EcsMeshResolver resolver = CreateResolver();

		Assert.True(
			resolver.TryResolve(
				EcsDemoScene.TriangleMesh,
				out EcsMeshBinding triangle));

		Assert.Equal(0, triangle.BufferIndex);
		Assert.Equal(MeshIndexFormat.None, triangle.Geometry.IndexFormat);

		Assert.True(
			resolver.TryResolve(
				EcsDemoScene.QuadMesh,
				out EcsMeshBinding quad));

		Assert.Equal(1, quad.BufferIndex);
		Assert.Equal(MeshIndexFormat.UInt16, quad.Geometry.IndexFormat);

		Assert.True(
			resolver.TryResolve(
				EcsDemoScene.HexagonMesh,
				out EcsMeshBinding hexagon));

		Assert.Equal(2, hexagon.BufferIndex);
		Assert.Equal(
			MeshIndexFormat.UInt32,
			hexagon.Geometry.IndexFormat);
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(1u)]
	[InlineData(2u)]
	public void TryResolve_ValidHandle_MatchesTheUploadedGeometry(
		uint index)
	{
		MeshGeometry[] uploaded =
			MeshGeometry.CreateCameraTransformMeshes();

		EcsMeshResolver resolver = EcsMeshResolver.CreateDefault(uploaded);

		Assert.True(
			resolver.TryResolve(
				new MeshHandle(index, 1),
				out EcsMeshBinding binding));

		Assert.Equal((int)index, binding.BufferIndex);
		Assert.Same(uploaded[index], binding.Geometry);
	}

	[Fact]
	public void TryResolve_ZeroGeneration_IsRejected()
	{
		EcsMeshResolver resolver = CreateResolver();

		Assert.False(
			resolver.TryResolve(
				new MeshHandle(0, 0),
				out EcsMeshBinding binding));

		Assert.Equal(default, binding);
	}

	[Fact]
	public void TryResolve_StaleGeneration_IsRejected()
	{
		EcsMeshResolver resolver = CreateResolver();

		Assert.False(
			resolver.TryResolve(new MeshHandle(0, 2), out _));

		Assert.False(
			resolver.TryResolve(new MeshHandle(1, 7), out _));
	}

	[Fact]
	public void TryResolve_UnknownIndex_IsRejected()
	{
		EcsMeshResolver resolver = CreateResolver();

		Assert.False(
			resolver.TryResolve(new MeshHandle(3, 1), out _));

		Assert.False(
			resolver.TryResolve(new MeshHandle(uint.MaxValue, 1), out _));
	}

	[Fact]
	public void CreateDefault_RejectsAnUnexpectedMeshOrder()
	{
		MeshGeometry[] geometries =
			MeshGeometry.CreateCameraTransformMeshes();

		MeshGeometry[] reordered =
			[geometries[1], geometries[0], geometries[2]];

		Assert.Throws<ArgumentException>(
			() => EcsMeshResolver.CreateDefault(reordered));
	}

	[Fact]
	public void CreateDefault_RejectsAWrongMeshCount()
	{
		MeshGeometry[] geometries =
			MeshGeometry.CreateCameraTransformMeshes();

		Assert.Throws<ArgumentException>(
			() => EcsMeshResolver.CreateDefault(geometries[..2]));

		Assert.Throws<ArgumentException>(
			() => EcsMeshResolver.CreateDefault([]));
	}

	[Fact]
	public void CreateDefault_RejectsNull()
	{
		Assert.Throws<ArgumentNullException>(
			() => EcsMeshResolver.CreateDefault(null!));
	}

	[Fact]
	public void Materials_AcceptOnlyTheCompleteVertexColorHandle()
	{
		EcsMaterialPalette materials = new();

		Assert.True(materials.Contains(EcsDemoScene.VertexColorMaterial));
		Assert.True(materials.Contains(new MaterialHandle(0, 1)));

		Assert.False(materials.Contains(new MaterialHandle(0, 0)));
		Assert.False(materials.Contains(new MaterialHandle(0, 2)));
		Assert.False(materials.Contains(new MaterialHandle(1, 1)));
	}

	private static EcsMeshResolver CreateResolver()
	{
		return EcsMeshResolver.CreateDefault(
			MeshGeometry.CreateCameraTransformMeshes());
	}
}
