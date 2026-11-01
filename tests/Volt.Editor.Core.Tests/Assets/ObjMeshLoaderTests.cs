using System.Numerics;
using Volt.Editor.Core.Assets;

namespace Volt.Editor.Core.Tests.Assets;

/// <summary>
/// Covers OBJ parsing: what a developer's exported file actually contains,
/// including the parts the renderer ignores.
/// </summary>
public sealed class ObjMeshLoaderTests
{
	[Fact]
	public void AQuadBecomesTwoTrianglesWithMatchingBounds()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"Assets/Meshes/quad.obj",
			new StringReader(
				"""
				v -1 -1 0
				v 1 -1 0
				v 1 1 0
				v -1 1 0
				f 1 2 3 4
				"""));

		Assert.Equal(2, mesh.TriangleCount);
		Assert.Equal(6, mesh.Indices.Count);
		Assert.Equal(Vector3.Zero, mesh.Center);

		// A flat mesh still needs a non-zero picking depth.
		Assert.Equal(new Vector3(1f, 1f, 0.001f), mesh.HalfExtent);
		Assert.Equal("Assets/Meshes/quad.obj", mesh.Id);
	}

	[Fact]
	public void NegativeIndicesCountFromTheVerticesSeenSoFar()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"tri.obj",
			new StringReader(
				"""
				v 0 0 0
				v 2 0 0
				v 0 4 0
				f -3 -2 -1
				"""));

		Assert.Equal(1, mesh.TriangleCount);
		Assert.Equal([0, 1, 2], mesh.Indices);
		Assert.Equal(new Vector3(1f, 2f, 0f), mesh.Center);
		Assert.Equal(new Vector3(1f, 2f, 0.001f), mesh.HalfExtent);
	}

	[Fact]
	public void NormalComponentsAreAcceptedAndDiscarded()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"tri.obj",
			new StringReader(
				"""
				# exported by a modelling tool
				mtllib scene.mtl
				v 0 0 0
				v 1 0 0
				v 0 1 0
				vt 0 0
				vn 0 0 1
				usemtl red
				s 1
				f 1/1/1 2/1/1 3/1/1
				"""));

		Assert.Equal(1, mesh.TriangleCount);
		Assert.Equal(3, mesh.Positions.Count);
		Assert.Equal([Vector2.Zero, Vector2.Zero, Vector2.Zero], mesh.Uvs);
	}

	[Fact]
	public void TextureCoordinatesAreKeptPerCorner()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"quad.obj",
			new StringReader(
				"""
				v 0 0 0
				v 1 0 0
				v 1 1 0
				v 0 1 0
				vt 0 0
				vt 1 0
				vt 1 1
				vt 0 1
				f 1/1 2/2 3/3 4/4
				"""));

		Assert.True(mesh.HasTextureCoordinates);
		Assert.Equal(6, mesh.Uvs.Count);
		Assert.Equal(
			[
				new Vector2(0f, 0f),
				new Vector2(1f, 0f),
				new Vector2(1f, 1f),
				new Vector2(0f, 0f),
				new Vector2(1f, 1f),
				new Vector2(0f, 1f)
			],
			mesh.Uvs);
	}

	[Fact]
	public void OnePositionMayCarryDifferentTextureCoordinates()
	{
		// Two triangles that share every position but use a different
		// corner of the image on the shared seam: a per-position layout
		// could not represent this, a per-corner one can.
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"seam.obj",
			new StringReader(
				"""
				v 0 0 0
				v 1 0 0
				v 0 1 0
				vt 0 0
				vt 1 0
				vt 0 1
				vt 0.5 0.5
				f 1/1 2/2 3/3
				f 1/4 3/4 2/4
				"""));

		Assert.Equal(2, mesh.TriangleCount);
		Assert.Equal(6, mesh.Uvs.Count);
		Assert.Equal(new Vector2(0f, 0f), mesh.Uvs[0]);
		Assert.Equal(new Vector2(0.5f, 0.5f), mesh.Uvs[3]);
	}

	[Fact]
	public void NegativeTextureIndicesCountFromTheCoordinatesSeenSoFar()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"tri.obj",
			new StringReader(
				"""
				v 0 0 0
				v 1 0 0
				v 0 1 0
				vt 0 0
				vt 1 1
				f 1/-2 2/-1 3/-2
				"""));

		Assert.Equal(
			[
				new Vector2(0f, 0f),
				new Vector2(1f, 1f),
				new Vector2(0f, 0f)
			],
			mesh.Uvs);
	}

	[Fact]
	public void AMeshWithoutTextureCoordinatesReportsNone()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"plain.obj",
			new StringReader(
				"""
				v 0 0 0
				v 1 0 0
				v 0 1 0
				f 1 2 3
				"""));

		Assert.False(mesh.HasTextureCoordinates);
		Assert.Empty(mesh.Uvs);
	}

	[Fact]
	public void AFaceMayOmitTextureCoordinatesThatOtherFacesUse()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"mixed.obj",
			new StringReader(
				"""
				v 0 0 0
				v 1 0 0
				v 0 1 0
				v 1 1 0
				vt 0 0
				vt 1 1
				f 1/1 2/2 3/1
				f 2 3 4
				"""));

		Assert.Equal(2, mesh.TriangleCount);
		Assert.Equal(6, mesh.Uvs.Count);
		Assert.Equal(Vector2.Zero, mesh.Uvs[3]);
		Assert.Equal(Vector2.Zero, mesh.Uvs[5]);
	}

	[Fact]
	public void ATextureIndexPastTheEndIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			ObjMeshLoader.Parse(
				"bad.obj",
				new StringReader(
					"""
					v 0 0 0
					v 1 0 0
					v 0 1 0
					vt 0 0
					f 1/1 2/2 3/1
					""")));

		Assert.Contains("texture coordinate 2", error.Message);
	}

	[Fact]
	public void AFaceWithoutVerticesIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			ObjMeshLoader.Parse(
				"empty.obj",
				new StringReader(
					"""
					v 0 0 0
					v 1 0 0
					v 0 1 0
					""")));

		Assert.Contains("no triangles", error.Message);
	}

	[Fact]
	public void AFaceWithTooFewCornersIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			ObjMeshLoader.Parse(
				"line.obj",
				new StringReader(
					"""
					v 0 0 0
					v 1 0 0
					f 1 2
					""")));

		Assert.Contains("fewer than three", error.Message);
	}

	[Fact]
	public void AnIndexPastTheEndOfTheVertexListIsReported()
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			ObjMeshLoader.Parse(
				"bad.obj",
				new StringReader(
					"""
					v 0 0 0
					v 1 0 0
					v 0 1 0
					f 1 2 9
					""")));

		Assert.Contains("does not exist", error.Message);
	}

	[Fact]
	public void UnreferencedVerticesDoNotInflateTheBounds()
	{
		ImportedMesh mesh = ObjMeshLoader.Parse(
			"loose.obj",
			new StringReader(
				"""
				v 0 0 0
				v 1 0 0
				v 0 1 0
				v 100 100 100
				f 1 2 3
				"""));

		Assert.Equal(new Vector3(0.5f, 0.5f, 0f), mesh.Center);
		Assert.Equal(new Vector3(0.5f, 0.5f, 0.001f), mesh.HalfExtent);
	}
}
