
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;

namespace Volt.Graphics.Vulkan.Tests.Devices;

public sealed class VulkanLogicalDeviceRetirementTests
{
    [Theory]
    [InlineData(Result.Success)]
    [InlineData(Result.ErrorDeviceLost)]
    public void CanReclaimAfterWait_AcceptsSafeLifetimeResults(
        Result result)
    {
        Assert.True(
            VulkanLogicalDevice.CanReclaimAfterWait(result));
    }

    [Theory]
    [InlineData(Result.Timeout)]
    [InlineData(Result.NotReady)]
    [InlineData(Result.ErrorOutOfHostMemory)]
    [InlineData(Result.ErrorOutOfDeviceMemory)]
    [InlineData(Result.ErrorUnknown)]
    public void CanReclaimAfterWait_RejectsUncertainResults(
        Result result)
    {
        Assert.False(
            VulkanLogicalDevice.CanReclaimAfterWait(result));
    }
}
