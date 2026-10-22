
using Volt.Graphics.Vulkan.Surface;

namespace Volt.Graphics.Vulkan.Tests.Surface;

public sealed class VulkanSurfaceTests
{
	[Fact]
	public void NullInstance_ThrowsBeforeNativeAccess()
	{
		Assert.Throws<ArgumentNullException>(
			() => new VulkanSurface(
				null!,
				null!));
	}
}
