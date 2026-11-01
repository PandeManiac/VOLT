using System.Numerics;
using Volt.Editor.Core.Assets;
using Volt.Editor.Core.Projects;

namespace Volt.Editor.Core.Tests.Projects;

/// <summary>
/// Covers loading a mesh a scene references: what a project can import,
/// how failures are reported, and when the cached geometry is replaced.
/// </summary>
public sealed class ProjectMeshTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		$"volt-project-meshes-{Guid.NewGuid():N}");

	public ProjectMeshTests()
	{
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}

		catch (IOException)
		{
			// A leftover file must not fail a test that already ran.
		}
	}

	[Fact]
	public void AnObjAssetLoadsAsTriangles()
	{
		EditorProject project = EditorProject.Create(_root, "Meshes");

		Write(
			project,
			"Assets/Meshes/triangle.obj",
			"""
			v 0 0 0
			v 2 0 0
			v 0 2 0
			f 1 2 3
			""");

		project.AssetCatalog.Refresh();

		Assert.True(project.TryLoadMesh(
			"Assets/Meshes/triangle.obj",
			out ImportedMesh mesh,
			out string? diagnostic));

		Assert.Null(diagnostic);
		Assert.Equal(1, mesh.TriangleCount);
		Assert.Equal(new Vector3(1f, 1f, 0f), mesh.Center);
		Assert.Equal(new Vector3(1f, 1f, 0.001f), mesh.HalfExtent);
	}

	[Fact]
	public void AnEditedFileReplacesTheCachedGeometry()
	{
		EditorProject project = EditorProject.Create(_root, "Meshes");
		string path = Write(
			project,
			"Assets/Meshes/shape.obj",
			"""
			v 0 0 0
			v 1 0 0
			v 0 1 0
			f 1 2 3
			""");

		project.AssetCatalog.Refresh();
		Assert.True(project.TryLoadMesh(
			"Assets/Meshes/shape.obj",
			out ImportedMesh first,
			out _));
		Assert.Equal(1, first.TriangleCount);

		File.WriteAllText(
			path,
			"""
			v 0 0 0
			v 1 0 0
			v 0 1 0
			v 1 1 0
			f 1 2 3
			f 2 4 3
			""");

		// The cache is keyed by size and write time, so make the edit
		// unambiguous even on a coarse filesystem clock.
		File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(2));
		project.AssetCatalog.Refresh();

		Assert.True(project.TryLoadMesh(
			"Assets/Meshes/shape.obj",
			out ImportedMesh second,
			out _));

		Assert.Equal(2, second.TriangleCount);
		Assert.NotSame(first, second);
	}

	[Fact]
	public void AnUnsupportedFormatIsReportedRatherThanThrown()
	{
		EditorProject project = EditorProject.Create(_root, "Meshes");

		Write(project, "Assets/Meshes/chair.gltf", "{}");
		project.AssetCatalog.Refresh();

		Assert.False(project.TryLoadMesh(
			"Assets/Meshes/chair.gltf",
			out _,
			out string? diagnostic));

		Assert.Contains("not a mesh format", diagnostic);
	}

	[Fact]
	public void ABrokenObjIsReportedRatherThanThrown()
	{
		EditorProject project = EditorProject.Create(_root, "Meshes");

		Write(project, "Assets/Meshes/broken.obj", "v 0 0 0\nf 1 2 3\n");
		project.AssetCatalog.Refresh();

		Assert.False(project.TryLoadMesh(
			"Assets/Meshes/broken.obj",
			out _,
			out string? diagnostic));

		Assert.Contains("could not be read", diagnostic);
	}

	[Fact]
	public void AnAssetOutsideTheProjectIsReported()
	{
		EditorProject project = EditorProject.Create(_root, "Meshes");

		Assert.False(project.TryLoadMesh(
			"Assets/Meshes/missing.obj",
			out _,
			out string? diagnostic));

		Assert.Contains("not part of this project", diagnostic);
	}

	private static string Write(
		EditorProject project,
		string relativePath,
		string content)
	{
		string path = Path.Combine(
			project.RootPath,
			relativePath.Replace('/', Path.DirectorySeparatorChar));

		Directory.CreateDirectory(Path.GetDirectoryName(path) ?? project.RootPath);
		File.WriteAllText(path, content);

		return path;
	}
}
