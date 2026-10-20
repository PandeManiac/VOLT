using System.Numerics;
using Volt.Ecs;
using Volt.Ecs.Components.Rendering;
using Volt.Ecs.Components.Spatial;
using Volt.Rendering.Cameras;
using Volt.Rendering.Transforms;
using Xunit;

namespace Volt.Rendering.Tests.Transforms;

public sealed class TransformMatricesTests
{
	private static readonly Vector3 StartPosition = new(10.0f, 20.0f, 30.0f);

	[Fact]
	public void CreateModelMatrix_IdentityComponentsProduceIdentity()
	{
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(Vector3.Zero),
			RotationComponent.Identity,
			ScaleComponent.Identity);

		Assert.Equal(Matrix4x4.Identity, model);
	}

	[Fact]
	public void CreateModelMatrix_TranslationPlacesLocalOrigin()
	{
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			RotationComponent.Identity,
			ScaleComponent.Identity);

		Vector3 world = Vector3.Transform(Vector3.Zero, model);

		Assert.Equal(StartPosition, world);
	}

	[Fact]
	public void CreateModelMatrix_MovesLocalOffsetByTranslation()
	{
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(new Vector3(4.0f, -2.0f, 7.0f)),
			RotationComponent.Identity,
			ScaleComponent.Identity);

		Vector3 world = Vector3.Transform(
			new Vector3(1.0f, 2.0f, 3.0f),
			model);

		Assert.Equal(new Vector3(5.0f, 0.0f, 10.0f), world);
	}

	[Fact]
	public void CreateModelMatrix_AppliesQuaternionRotation()
	{
		// A quarter turn about +Y in VOLT's right-handed,
		// row-vector convention maps local +X onto world -Z.
		RotationComponent rotation = new(
			Quaternion.CreateFromAxisAngle(
				Vector3.UnitY,
				MathF.PI / 2.0f));

		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			rotation,
			ScaleComponent.Identity);

		Vector3 world = Vector3.Transform(Vector3.UnitX, model);

		AssertVectorNear(
			StartPosition + new Vector3(0.0f, 0.0f, -1.0f),
			world);
	}

	[Fact]
	public void CreateModelMatrix_AppliesUniformScale()
	{
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(2.0f)));

		Vector3 world = Vector3.Transform(
			new Vector3(1.0f, -3.0f, 0.5f),
			model);

		AssertVectorNear(
			StartPosition + new Vector3(2.0f, -6.0f, 1.0f),
			world);
	}

	[Fact]
	public void CreateModelMatrix_AppliesNonuniformScale()
	{
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(2.0f, 3.0f, 0.5f)));

		Vector3 world = Vector3.Transform(
			new Vector3(1.0f, 1.0f, 1.0f),
			model);

		AssertVectorNear(
			StartPosition + new Vector3(2.0f, 3.0f, 0.5f),
			world);
	}

	[Fact]
	public void CreateModelMatrix_AppliesNegativeScale()
	{
		// Negative scale is a valid mirror, documented by
		// ScaleComponent, so it must pass through unchanged.
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(Vector3.Zero),
			RotationComponent.Identity,
			new ScaleComponent(new Vector3(-1.0f, 1.0f, 1.0f)));

		Vector3 world = Vector3.Transform(Vector3.UnitX, model);

		AssertVectorNear(-Vector3.UnitX, world);
	}

	[Fact]
	public void CreateModelMatrix_AppliesZeroScale()
	{
		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			RotationComponent.Identity,
			new ScaleComponent(Vector3.Zero));

		Vector3 world = Vector3.Transform(
			new Vector3(5.0f, -5.0f, 5.0f),
			model);

		Assert.Equal(StartPosition, world);
	}

	[Fact]
	public void CreateModelMatrix_ComposesScaleRotationTranslationInOrder()
	{
		Vector3 position = new(10.0f, 20.0f, 30.0f);
		Vector3 scale = new(2.0f, 3.0f, 4.0f);
		Quaternion rotation = Quaternion.CreateFromAxisAngle(
			Vector3.UnitY,
			MathF.PI / 2.0f);

		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(position),
			new RotationComponent(rotation),
			new ScaleComponent(scale));

		// Scale (2, 3, 4), then rotate +90 degrees about Y onto
		// (4, 3, -2), then translate.
		Vector3 world = Vector3.Transform(
			new Vector3(1.0f, 1.0f, 1.0f),
			model);

		AssertVectorNear(new Vector3(14.0f, 23.0f, 28.0f), world);
	}

	[Fact]
	public void CreateModelMatrix_UsesRowVectorMultiplicationOrder()
	{
		Vector3 position = new(0.5f, -1.5f, 2.0f);
		Vector3 scale = new(2.0f, 0.5f, 3.0f);
		Quaternion rotation = Quaternion.CreateFromYawPitchRoll(
			0.4f,
			-0.2f,
			0.1f);

		Matrix4x4 expected =
			Matrix4x4.CreateScale(scale) *
			Matrix4x4.CreateFromQuaternion(rotation) *
			Matrix4x4.CreateTranslation(position);

		Matrix4x4 actual = TransformMatrices.CreateModelMatrix(
			new PositionComponent(position),
			new RotationComponent(rotation),
			new ScaleComponent(scale));

		Assert.Equal(expected, actual);
	}

	[Fact]
	public void CreateModelMatrix_IsOrderDependent()
	{
		Vector3 position = new(3.0f, 0.0f, -1.0f);
		Vector3 scale = new(2.0f, 0.5f, 1.0f);
		Quaternion rotation = Quaternion.CreateFromAxisAngle(
			Vector3.UnitY,
			MathF.PI / 3.0f);

		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(position),
			new RotationComponent(rotation),
			new ScaleComponent(scale));

		Matrix4x4 reversed =
			Matrix4x4.CreateTranslation(position) *
			Matrix4x4.CreateFromQuaternion(rotation) *
			Matrix4x4.CreateScale(scale);

		// Translating first would move the vertices along with the
		// translation instead of placing the transformed origin.
		Vector3 local = new(1.0f, 0.0f, 1.0f);

		Assert.NotEqual(model, reversed);
		Assert.True(
			Vector3.Distance(
				Vector3.Transform(local, model),
				Vector3.Transform(local, reversed)) > 0.5f);
	}

	[Fact]
	public void CreateModelMatrix_DoesNotNormalizeTheRotation()
	{
		// RotationComponent documents an expectation of a
		// normalized quaternion. Silently normalizing a caller's
		// value here would change both the orientation and the
		// scale it asked for, so the supplied quaternion is used
		// verbatim.
		//
		// The quaternion is deliberately non-unit and has a nonzero
		// vector part: (0, 0, 0.5, 2) rotates about +Z while also
		// stretching the basis. A quaternion of (0, 0, 0, 2) would
		// produce an identity rotation either way and could not
		// detect accidental normalization.
		Quaternion unnormalized = new(0.0f, 0.0f, 0.5f, 2.0f);

		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			new RotationComponent(unnormalized),
			ScaleComponent.Identity);

		// Reference values for the raw quaternion: with z = 0.5 and
		// w = 2 the upper-left block is
		// [0.5, 2; -2, 0.5], which is the unit rotation scaled by
		// four (the quaternion's squared length).
		Assert.Equal(0.5f, model.M11);
		Assert.Equal(2.0f, model.M12);
		Assert.Equal(-2.0f, model.M21);
		Assert.Equal(0.5f, model.M22);
		Assert.Equal(1.0f, model.M33);

		// Local +X therefore lands at (0.5, 2, 0) and is translated
		// to (10.5, 22, 30).
		Vector3 unnormalizedWorld = Vector3.Transform(
			Vector3.UnitX,
			model);

		AssertVectorNear(
			new Vector3(10.5f, 22.0f, 30.0f),
			unnormalizedWorld);

		// Normalizing the same quaternion turns the same vertex into
		// (0.882353, 0.470589, 0) instead, so a normalizing
		// implementation could not satisfy the reference above.
		Matrix4x4 normalizedModel = TransformMatrices.CreateModelMatrix(
			new PositionComponent(StartPosition),
			new RotationComponent(Quaternion.Normalize(unnormalized)),
			ScaleComponent.Identity);

		Vector3 normalizedWorld = Vector3.Transform(
			Vector3.UnitX,
			normalizedModel);

		AssertVectorNear(
			new Vector3(10.882353f, 20.470589f, 30.0f),
			normalizedWorld);

		Assert.True(
			Vector3.Distance(unnormalizedWorld, normalizedWorld) > 1.0f,
			"Normalizing must materially change the transformed vertex.");
	}

	[Fact]
	public void CreateModelMatrix_ConsumesStoredComponentValues()
	{
		PositionComponent position = new(new Vector3(1.0f, 2.0f, 3.0f));
		RotationComponent rotation = RotationComponent.Identity;
		ScaleComponent scale = new(new Vector3(2.0f));

		Matrix4x4 before = TransformMatrices.CreateModelMatrix(
			in position,
			in rotation,
			in scale);

		position.Value = new Vector3(4.0f, 5.0f, 6.0f);
		rotation.Value = Quaternion.CreateFromAxisAngle(
			Vector3.UnitX,
			MathF.PI / 2.0f);
		scale.Value = new Vector3(0.5f);

		Matrix4x4 after = TransformMatrices.CreateModelMatrix(
			in position,
			in rotation,
			in scale);

		Assert.NotEqual(before, after);

		// The mutated components are the ones the helper used:
		// +Y scaled by 0.5, rotated +90 degrees about X onto +Z, then
		// translated.
		AssertVectorNear(
			position.Value + new Vector3(0.0f, 0.0f, 0.5f),
			Vector3.Transform(Vector3.UnitY, after));
	}

	[Fact]
	public void CreateModelMatrix_ConsumesComponentsFromAWorld()
	{
		using World world = new();
		Entity entity = world.CreateEntity();

		world.AddComponent(entity, new PositionComponent(
			new Vector3(-2.0f, 1.0f, 0.5f)));

		world.AddComponent(entity, new RotationComponent(
			Quaternion.CreateFromAxisAngle(
				Vector3.UnitZ,
				MathF.PI / 2.0f)));

		world.AddComponent(entity, new ScaleComponent(
			new Vector3(3.0f, 3.0f, 3.0f)));

		PositionComponent position =
			world.GetComponent<PositionComponent>(entity);

		RotationComponent rotation =
			world.GetComponent<RotationComponent>(entity);

		ScaleComponent scale =
			world.GetComponent<ScaleComponent>(entity);

		Matrix4x4 model = TransformMatrices.CreateModelMatrix(
			in position,
			in rotation,
			in scale);

		// +X scaled by 3, rotated +90 degrees about Z onto +Y, then
		// translated.
		AssertVectorNear(
			new Vector3(-2.0f, 4.0f, 0.5f),
			Vector3.Transform(Vector3.UnitX, model));
	}

	[Fact]
	public void CreateModelViewProjection_ComposesModelViewProjection()
	{
		Matrix4x4 model = Matrix4x4.CreateTranslation(1.0f, 2.0f, 3.0f);
		Matrix4x4 view = Matrix4x4.CreateTranslation(-1.0f, 0.0f, 0.0f);
		Matrix4x4 projection = Matrix4x4.CreateScale(0.5f);

		Matrix4x4 mvp = TransformMatrices.CreateModelViewProjection(
			model,
			view,
			projection);

		Assert.Equal(model * view * projection, mvp);
	}

	[Fact]
	public void CreateModelViewProjection_ProjectsThroughAViewport()
	{
		CameraComponent camera = new(
			MathF.PI / 3.0f,
			0.1f,
			100.0f);

		Matrix4x4 view = CameraComponent.CreateViewMatrix(
			new PositionComponent(new Vector3(0.0f, 0.0f, 3.0f)),
			RotationComponent.Identity);

		Matrix4x4 projection = CameraProjection.CreatePerspective(
			camera,
			1.0f);

		Matrix4x4 mvp = TransformMatrices.CreateModelViewProjection(
			Matrix4x4.Identity,
			view,
			projection);

		Vector4 origin = Vector4.Transform(
			new Vector4(Vector3.Zero, 1.0f),
			mvp);

		Assert.Equal(3.0f, origin.W, 4);
		Assert.Equal(0.0f, origin.X / origin.W, 4);
		Assert.Equal(0.0f, origin.Y / origin.W, 4);

		// The near and far planes map onto Vulkan's 0..1 depth
		// interval.
		Vector4 nearPlane = Vector4.Transform(
			new Vector4(0.0f, 0.0f, 3.0f - camera.NearPlane, 1.0f),
			mvp);

		Vector4 farPlane = Vector4.Transform(
			new Vector4(0.0f, 0.0f, 3.0f - camera.FarPlane, 1.0f),
			mvp);

		Assert.Equal(0.0f, nearPlane.Z / nearPlane.W, 4);
		Assert.Equal(1.0f, farPlane.Z / farPlane.W, 4);
	}

	[Fact]
	public void CreateModelMatrix_DoesNotAllocate()
	{
		PositionComponent position = new(StartPosition);
		RotationComponent rotation = new(
			Quaternion.CreateFromYawPitchRoll(0.2f, 0.1f, -0.3f));

		ScaleComponent scale = new(new Vector3(1.5f, 0.5f, 2.0f));

		AssertNoAllocations(
			() => TransformMatrices.CreateModelMatrix(
				in position,
				in rotation,
				in scale));
	}

	[Fact]
	public void CreateModelViewProjection_DoesNotAllocate()
	{
		Matrix4x4 model = Matrix4x4.CreateTranslation(1.0f, 2.0f, 3.0f);
		Matrix4x4 view = Matrix4x4.CreateLookAt(
			new Vector3(0.0f, 0.0f, 3.0f),
			Vector3.Zero,
			Vector3.UnitY);

		Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(
			MathF.PI / 3.0f,
			16.0f / 9.0f,
			0.1f,
			100.0f);

		AssertNoAllocations(
			() => TransformMatrices.CreateModelViewProjection(
				model,
				view,
				projection));
	}

	/// <summary>
	/// Warms the call site, then proves the measured loop performs no
	/// heap allocation on the calling thread.
	/// </summary>
	private static void AssertNoAllocations(
		Func<Matrix4x4> operation)
	{
		for (int iteration = 0; iteration < 64; iteration++)
		{
			operation();
		}

		long before = GC.GetAllocatedBytesForCurrentThread();

		for (int iteration = 0; iteration < 1024; iteration++)
		{
			operation();
		}

		long after = GC.GetAllocatedBytesForCurrentThread();

		Assert.Equal(before, after);
	}

	private static void AssertVectorNear(
		Vector3 expected,
		Vector3 actual,
		float tolerance = 0.00001f)
	{
		Assert.True(
			Vector3.Distance(expected, actual) <= tolerance,
			$"Expected {expected}, actual {actual}.");
	}
}
