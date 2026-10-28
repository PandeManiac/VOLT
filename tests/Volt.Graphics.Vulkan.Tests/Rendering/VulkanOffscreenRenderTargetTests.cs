using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Rendering;

namespace Volt.Graphics.Vulkan.Tests.Rendering;

/// <summary>
/// Covers the ownership order of the offscreen render target without a
/// Vulkan device.
/// </summary>
/// <remarks>
/// The framebuffer names its render pass, so a target that creates its
/// attachments before publishing the pass handle hands Vulkan a null
/// <c>VK_NULL_HANDLE</c>. These tests pin the ordering and the cleanup
/// contract that prevents it: attachment creation must receive the
/// handle that was just created, and a partial failure must release
/// the attachments before the render pass, each exactly once.
/// </remarks>
public sealed class VulkanOffscreenRenderTargetTests
{
	[Fact]
	public void AttachmentCreation_ReceivesTheCreatedRenderPassHandle()
	{
		RenderPass created = new(7);
		RenderPass observed = default;
		List<string> order = [];

		RenderPass result = VulkanOffscreenRenderTarget
			.CreateRenderPassThenAttachments(
				() =>
				{
					order.Add("create-render-pass");
					return created;
				},
				renderPass =>
				{
					order.Add("create-attachments");
					observed = renderPass;
				},
				_ => order.Add("destroy-render-pass"),
				() => order.Add("destroy-attachments"));

		Assert.Equal(created.Handle, result.Handle);
		Assert.Equal(created.Handle, observed.Handle);
		Assert.NotEqual(0UL, observed.Handle);
		Assert.Equal(["create-render-pass", "create-attachments"], order);
	}

	[Fact]
	public void AttachmentFailure_ReleasesAttachmentsThenRenderPass_AndRethrows()
	{
		RenderPass created = new(11);
		List<string> order = [];
		InvalidOperationException failure = new("framebuffer creation failed");

		InvalidOperationException thrown =
			Assert.Throws<InvalidOperationException>(() =>
				VulkanOffscreenRenderTarget.CreateRenderPassThenAttachments(
					() => created,
					_ =>
					{
						order.Add("create-attachments");
						throw failure;
					},
					renderPass =>
					{
						Assert.Equal(created.Handle, renderPass.Handle);
						order.Add("destroy-render-pass");
					},
					() => order.Add("destroy-attachments")));

		Assert.Same(failure, thrown);
		Assert.Equal(
			["create-attachments", "destroy-attachments", "destroy-render-pass"],
			order);
	}

	[Fact]
	public void RenderPassFailure_SkipsAttachmentWork()
	{
		List<string> order = [];

		Assert.Throws<InvalidOperationException>(() =>
			VulkanOffscreenRenderTarget.CreateRenderPassThenAttachments(
				() =>
				{
					order.Add("create-render-pass");
					throw new InvalidOperationException("pass failed");
				},
				_ => order.Add("create-attachments"),
				_ => order.Add("destroy-render-pass"),
				() => order.Add("destroy-attachments")));

		Assert.Equal(["create-render-pass"], order);
	}

	[Fact]
	public void ResizeFailure_KeepsThePreviousAttachmentsAndDoesNotDestroyThem()
	{
		List<string> destroyed = [];
		string current = "old";

		Assert.Throws<InvalidOperationException>(() =>
			VulkanOffscreenRenderTarget.ReplaceAttachments(
				ref current,
				() => throw new InvalidOperationException("out of memory"),
				previous => destroyed.Add(previous)));

		Assert.Equal("old", current);
		Assert.Empty(destroyed);
	}

	[Fact]
	public void ResizeSuccess_DestroysThePreviousAttachmentsAfterCreatingTheReplacement()
	{
		List<string> order = [];
		string current = "old";

		VulkanOffscreenRenderTarget.ReplaceAttachments(
			ref current,
			() =>
			{
				order.Add("create");
				return "new";
			},
			previous => order.Add($"destroy:{previous}"));

		Assert.Equal("new", current);
		Assert.Equal(["create", "destroy:old"], order);
	}

	[Fact]
	public void NullDelegates_AreRejected()
	{
		Assert.Throws<ArgumentNullException>(() =>
			VulkanOffscreenRenderTarget.CreateRenderPassThenAttachments(
				null!,
				_ => { },
				_ => { },
				() => { }));

		Assert.Throws<ArgumentNullException>(() =>
			VulkanOffscreenRenderTarget.CreateRenderPassThenAttachments(
				() => new RenderPass(1),
				null!,
				_ => { },
				() => { }));

		Assert.Throws<ArgumentNullException>(() =>
			VulkanOffscreenRenderTarget.CreateRenderPassThenAttachments(
				() => new RenderPass(1),
				_ => { },
				null!,
				() => { }));

		Assert.Throws<ArgumentNullException>(() =>
			VulkanOffscreenRenderTarget.CreateRenderPassThenAttachments(
				() => new RenderPass(1),
				_ => { },
				_ => { },
				null!));

		string current = "value";

		Assert.Throws<ArgumentNullException>(() =>
			VulkanOffscreenRenderTarget.ReplaceAttachments(
				ref current,
				null!,
				_ => { }));

		Assert.Throws<ArgumentNullException>(() =>
			VulkanOffscreenRenderTarget.ReplaceAttachments(
				ref current,
				() => "next",
				null!));
	}
}
