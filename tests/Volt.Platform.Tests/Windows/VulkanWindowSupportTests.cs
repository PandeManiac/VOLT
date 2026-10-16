
using Volt.Platform.Windows;

namespace Volt.Platform.Tests.Windows;

public sealed class VulkanWindowSupportTests
{
    [Fact]
    public void NullWindow_ThrowsBeforeNativeAccess()
    {
        Assert.Throws<ArgumentNullException>(
            () => VulkanWindowSupport
                .GetRequiredInstanceExtensions(null!));
    }
}
