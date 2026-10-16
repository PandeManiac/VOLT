
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Instance;

namespace Volt.Graphics.Vulkan.Commands;

/// <summary>
/// Provides operations for recording primary Vulkan command buffers.
/// </summary>
/// <remarks>
/// Command buffers must be allocated from a valid command pool.
///
/// Recording commands requires externally synchronized host access.
/// Do not record the same command buffer concurrently.
///
/// Begin and End must be called in the correct Vulkan command
/// buffer state. This class does not track native command buffer
/// states or GPU submissions.
/// </remarks>
public static class VulkanCommandRecording
{
    private const CommandBufferUsageFlags AllowedFlags =
        CommandBufferUsageFlags.OneTimeSubmitBit |
        CommandBufferUsageFlags.SimultaneousUseBit;

    /// <summary>
    /// Begins recording a primary Vulkan command buffer.
    /// </summary>
    public static void Begin(
        VulkanInstance instance,
        CommandBuffer buffer,
        CommandBufferUsageFlags flags =
            CommandBufferUsageFlags.OneTimeSubmitBit)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ValidateBuffer(buffer);

        CommandBufferBeginInfo beginInfo =
            CreateBeginDescription(flags);

        Result result = instance.Api.BeginCommandBuffer(
            buffer,
            in beginInfo);

        EnsureSuccess(
            result,
            "Begin Vulkan command buffer recording");
    }

    /// <summary>
    /// Ends recording a primary Vulkan command buffer.
    /// </summary>
    public static void End(
        VulkanInstance instance,
        CommandBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(instance);

        ValidateBuffer(buffer);
        Result result = instance.Api.EndCommandBuffer(buffer);

        EnsureSuccess(
            result,
            "End Vulkan command buffer recording");
    }

    /// <summary>
    /// Creates a command buffer begin description without
    /// accessing the Vulkan driver.
    /// </summary>
    /// <remarks>
    /// RenderPassContinueBit is intentionally unsupported.
    /// This helper is designed for primary command buffers.
    /// </remarks>
    internal static CommandBufferBeginInfo CreateBeginDescription(
        CommandBufferUsageFlags flags =
            CommandBufferUsageFlags.OneTimeSubmitBit)
    {
        if ((flags & ~AllowedFlags) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flags),
                flags,
                "Unsupported primary command buffer usage flags.");
        }

        return new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = flags
        };
    }

    private static void ValidateBuffer(CommandBuffer buffer)
    {
        if (buffer.Handle == 0)
        {
            throw new ArgumentException(
                "A valid Vulkan command buffer handle is required.",
                nameof(buffer));
        }
    }

    private static void EnsureSuccess(
        Result result,
        string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result}.");
        }
    }
}
