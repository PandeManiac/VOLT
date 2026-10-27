using System.Numerics;
using System.Text.Json;
using Volt.Editor.Core.Scenes;

namespace Volt.Editor.Core.Tests.Scenes;

public sealed class SceneSerializationTests
{
	[Fact]
	public void Serialize_RoundTripsHierarchyTransformsAndComponents()
	{
		SceneDocument scene = new("Round Trip");
		SceneEntity root = scene.CreateEntity("Root");
		root.Transform.Position = new Vector3(1f, 2f, 3f);
		root.Transform.EulerAngles = new Vector3(10f, 20f, 30f);

		SceneEntity child = scene.CreateEntity("Child", root.Id);
		child.Transform.Scale = new Vector3(2f, 2f, 2f);
		child.AddComponent(new MeshRendererData
		{
			Mesh = "Assets/cube.mesh",
			Material = "Assets/red.material"
		});

		string json = SceneSerializer.Serialize(scene);
		SceneDocument restored = SceneSerializer.Deserialize(json);

		Assert.Equal(scene.Id, restored.Id);
		Assert.Equal("Round Trip", restored.Name);
		Assert.Equal(2, restored.EntityCount);

		SceneEntity restoredRoot = restored.Find(root.Id)!;
		SceneEntity restoredChild = restored.Find(child.Id)!;

		Assert.Equal(root.Transform.Position, restoredRoot.Transform.Position);
		Assert.Equal(root.Transform.EulerAngles, restoredRoot.Transform.EulerAngles);
		Assert.Equal(root.Id, restoredChild.ParentId);
		Assert.Equal("Assets/cube.mesh",
			restoredChild.GetComponent<MeshRendererData>()!.Mesh);
		Assert.Equal(2f, restoredChild.Transform.Scale.X);
	}

	[Fact]
	public void Serialize_RoundTripsVectorComponentValues()
	{
		SceneDocument scene = new("Vectors");
		SceneEntity entity = scene.CreateEntity("Spinner");

		entity.AddComponent(new SpinBehaviourData
		{
			Axis = new Vector3(0.25f, -1.5f, 2f),
			DegreesPerSecond = 180f
		});

		SceneDocument restored = SceneSerializer.Deserialize(
			SceneSerializer.Serialize(scene));

		SpinBehaviourData spin = restored.Entities[0]
			.GetComponent<SpinBehaviourData>()!;

		Assert.Equal(new Vector3(0.25f, -1.5f, 2f), spin.Axis);
		Assert.Equal(180f, spin.DegreesPerSecond);
	}

	[Fact]
	public void UnknownComponent_RoundTripsWithoutDataLoss()
	{
		Volt.Editor.Core.Scenes.SceneDocument scene = new("Unknown");
		SceneEntity entity = scene.CreateEntity("Entity");

		using JsonDocument payload = JsonDocument.Parse(
			"""{"value":42,"label":"future"}""");

		entity.AddComponent(new UnknownComponentData(
			"future.component",
			payload.RootElement));

		SceneDocument restored = SceneSerializer.Deserialize(
			SceneSerializer.Serialize(scene));

		UnknownComponentData unknown = Assert.IsType<UnknownComponentData>(
			restored.Entities[0].Components[0]);

		Assert.Equal("future.component", unknown.TypeId);
		Assert.Equal(42, unknown.Data.GetProperty("value").GetInt32());
		Assert.Equal("future", unknown.Data.GetProperty("label").GetString());
	}

	[Fact]
	public void Deserialize_RejectsMalformedDocuments()
	{
		Assert.Throws<InvalidDataException>(
			() => SceneSerializer.Deserialize("{not json}"));

		Assert.Throws<InvalidDataException>(
			() => SceneSerializer.Deserialize(
				"""{"version":999,"id":"00000000-0000-0000-0000-000000000001","name":"bad","entities":[]}"""));

		Assert.Throws<InvalidDataException>(
			() => SceneSerializer.Deserialize(
				"""{"version":1,"id":"00000000-0000-0000-0000-000000000001","name":"bad","entities":[{"id":"00000000-0000-0000-0000-000000000002","name":"a","parentId":"00000000-0000-0000-0000-000000000003","transform":{},"components":[]}]}"""));
	}

	[Fact]
	public void Save_ReplacesAtomicallyAndLeavesNoTemporaryFile()
	{
		string directory = Path.Combine(
			Path.GetTempPath(),
			$"volt-scene-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(
			directory,
			"Main.volt.scene.json");

		try
		{
			SceneDocument first = new("First");
			first.CreateEntity("One");
			SceneSerializer.Save(path, first);

			SceneDocument second = new("Second");
			second.CreateEntity("Two");
			SceneSerializer.Save(path, second);

			SceneDocument loaded = SceneSerializer.Load(path);

			Assert.Equal(second.Id, loaded.Id);
			Assert.Equal("Two", Assert.Single(loaded.Entities).Name);
			Assert.Empty(
				Directory.EnumerateFiles(directory, "*.tmp-*"));
		}

		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}
}
