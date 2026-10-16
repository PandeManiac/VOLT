using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.Graphics.Vulkan.Rendering;

/// <summary>
/// Owns a minimal Vulkan graphics pipeline, render pass,
/// and one framebuffer per swapchain image view.
/// </summary>
/// <remarks>
/// This is a first-triangle demonstration pipeline, not a
/// general-purpose renderer. It uses no vertex buffers or
/// descriptor sets; the vertex shader uses gl_VertexIndex.
///
/// The swapchain, image views, Vulkan device, and instance must
/// outlive this object. No submitted commands may reference these
/// resources when the object is disposed.
/// </remarks>
public sealed unsafe class VulkanTrianglePipeline : IDisposable
{
    private readonly VulkanInstance _instance;
    private readonly VulkanLogicalDevice _device;
    private readonly Vk _api;
    private readonly Framebuffer[] _framebuffers;

    private RenderPass _renderPass;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;
    private bool _disposed;

    public Extent2D Extent { get; }

    public int FramebufferCount
    {
        get
        {
            ThrowIfDisposed();
            return _framebuffers.Length;
        }
    }

    /// <summary>
    /// Creates a simple graphics pipeline for the current swapchain.
    /// The shader paths must point to compiled SPIR-V files.
    /// </summary>
    public VulkanTrianglePipeline(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        VulkanSwapchain swapchain,
        VulkanSwapchainImageViews imageViews,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapchain);
        ArgumentNullException.ThrowIfNull(imageViews);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexShaderPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentShaderPath);

        if (swapchain.ImageCount == 0 ||
            imageViews.Count != checked((int)swapchain.ImageCount))
        {
            throw new ArgumentException(
                "Each swapchain image requires an image view.",
                nameof(imageViews));
        }

        if ((swapchain.ImageUsage & ImageUsageFlags.ColorAttachmentBit) == 0)
        {
            throw new NotSupportedException(
                "The swapchain must support color attachments.");
        }

        Vk api = instance.Api;
        Device deviceHandle = device.Handle;

        RenderPass renderPass = default;
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        Framebuffer[] framebuffers = new Framebuffer[imageViews.Count];

        try
        {
            renderPass = CreateRenderPass(
                api,
                deviceHandle,
                swapchain.ImageFormat);

            layout = CreatePipelineLayout(api, deviceHandle);

            pipeline = CreatePipeline(
                api,
                deviceHandle,
                renderPass,
                layout,
                swapchain.Extent,
                vertexShaderPath,
                fragmentShaderPath);

            for (int index = 0; index < framebuffers.Length; index++)
            {
                framebuffers[index] = CreateFramebuffer(
                    api,
                    deviceHandle,
                    renderPass,
                    imageViews.GetView(index),
                    swapchain.Extent);
            }
        }

        catch
        {
            DestroyResources(
                api,
                deviceHandle,
                framebuffers,
                pipeline,
                layout,
                renderPass);

            throw;
        }

        _instance = instance;
        _device = device;
        _api = api;
        _renderPass = renderPass;
        _pipelineLayout = layout;
        _pipeline = pipeline;
        _framebuffers = framebuffers;
        Extent = swapchain.Extent;
    }

    /// <summary>
    /// Records a clear and three-vertex draw into the command buffer.
    /// Call only while recording a primary command buffer, outside
    /// an existing render pass.
    /// </summary>
    public void Record(CommandBuffer commandBuffer, int imageIndex)
    {
        ThrowIfDisposed();

        if (commandBuffer.Handle == 0)
        {
            throw new ArgumentException(
                "A valid command buffer is required.",
                nameof(commandBuffer));
        }

        Framebuffer framebuffer = _framebuffers[imageIndex];

        ClearValue clear = new()
        {
            Color = new ClearColorValue(
                float32_0: 0.08f,
                float32_1: 0.15f,
                float32_2: 0.30f,
                float32_3: 1.0f)
        };

        RenderPassBeginInfo begin = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = framebuffer,
            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = Extent
            },
            ClearValueCount = 1,
            PClearValues = &clear
        };

        _api.CmdBeginRenderPass(
            commandBuffer,
            in begin,
            SubpassContents.Inline);

        _api.CmdBindPipeline(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipeline);

        _api.CmdDraw(
            commandBuffer,
            vertexCount: 3,
            instanceCount: 1,
            firstVertex: 0,
            firstInstance: 0);

        _api.CmdEndRenderPass(commandBuffer);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Device deviceHandle = _device.Handle;
        _ = _instance.Handle;

        DestroyResources(
            _api,
            deviceHandle,
            _framebuffers,
            _pipeline,
            _pipelineLayout,
            _renderPass);

        _pipeline = default;
        _pipelineLayout = default;
        _renderPass = default;
        _disposed = true;
    }

    private static RenderPass CreateRenderPass(
        Vk api,
        Device device,
        Format format)
    {
        AttachmentDescription colorAttachment = new()
        {
            Format = format,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        AttachmentReference colorReference = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference
        };

        SubpassDependency* dependencies = stackalloc SubpassDependency[2];
        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = AccessFlags.None,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };
        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.BottomOfPipeBit,
            SrcAccessMask = AccessFlags.ColorAttachmentWriteBit,
            DstAccessMask = AccessFlags.None
        };

        RenderPassCreateInfo description = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 2,
            PDependencies = dependencies
        };

        EnsureSuccess(
            api.CreateRenderPass(
                device,
                in description,
                null,
                out RenderPass renderPass),
            "Create triangle render pass");

        if (renderPass.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null render pass.");
        }

        return renderPass;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk api,
        Device device)
    {
        PipelineLayoutCreateInfo description = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo
        };

        EnsureSuccess(
            api.CreatePipelineLayout(
                device,
                in description,
                null,
                out PipelineLayout layout),
            "Create triangle pipeline layout");

        if (layout.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null pipeline layout.");
        }

        return layout;
    }

    private static Pipeline CreatePipeline(
        Vk api,
        Device device,
        RenderPass renderPass,
        PipelineLayout layout,
        Extent2D extent,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        ShaderModule vertexShader = default;
        ShaderModule fragmentShader = default;

        try
        {
            vertexShader = CreateShaderModule(
                api,
                device,
                vertexShaderPath);

            fragmentShader = CreateShaderModule(
                api,
                device,
                fragmentShaderPath);

            byte* entryPoint = stackalloc byte[]
            {
                (byte)'m',
                (byte)'a',
                (byte)'i',
                (byte)'n',
                0
            };

            PipelineShaderStageCreateInfo* stages =
                stackalloc PipelineShaderStageCreateInfo[2];

            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertexShader,
                PName = entryPoint
            };

            stages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragmentShader,
                PName = entryPoint
            };

            PipelineVertexInputStateCreateInfo vertexInput = new()
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo
            };

            PipelineInputAssemblyStateCreateInfo inputAssembly = new()
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
                PrimitiveRestartEnable = Vk.False
            };

            Viewport viewport = new()
            {
                X = 0.0f,
                Y = 0.0f,
                Width = extent.Width,
                Height = extent.Height,
                MinDepth = 0.0f,
                MaxDepth = 1.0f
            };

            Rect2D scissor = new()
            {
                Offset = new Offset2D(0, 0),
                Extent = extent
            };

            PipelineViewportStateCreateInfo viewportState = new()
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor
            };

            PipelineRasterizationStateCreateInfo rasterization = new()
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                DepthClampEnable = Vk.False,
                RasterizerDiscardEnable = Vk.False,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                DepthBiasEnable = Vk.False,
                LineWidth = 1.0f
            };

            PipelineMultisampleStateCreateInfo multisampling = new()
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
                SampleShadingEnable = Vk.False
            };

            PipelineColorBlendAttachmentState colorAttachment = new()
            {
                BlendEnable = Vk.False,
                ColorWriteMask =
                    ColorComponentFlags.RBit |
                    ColorComponentFlags.GBit |
                    ColorComponentFlags.BBit |
                    ColorComponentFlags.ABit
            };

            PipelineColorBlendStateCreateInfo colorBlending = new()
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                LogicOpEnable = Vk.False,
                AttachmentCount = 1,
                PAttachments = &colorAttachment
            };

            GraphicsPipelineCreateInfo description = new()
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = 2,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &inputAssembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisampling,
                PColorBlendState = &colorBlending,
                Layout = layout,
                RenderPass = renderPass,
                Subpass = 0
            };

            Pipeline pipeline = default;

            Result result = api.CreateGraphicsPipelines(
                device,
                default,
                1,
                in description,
                null,
                out pipeline);

            if (result != Result.Success)
            {
                if (pipeline.Handle != 0)
                {
                    api.DestroyPipeline(device, pipeline, null);
                }

                throw new InvalidOperationException(
                    $"Create triangle graphics pipeline failed: {result}.");
            }

            if (pipeline.Handle == 0)
            {
                throw new InvalidOperationException(
                    "Vulkan returned a null graphics pipeline.");
            }

            return pipeline;
        }

        finally
        {
            if (fragmentShader.Handle != 0)
            {
                api.DestroyShaderModule(device, fragmentShader, null);
            }

            if (vertexShader.Handle != 0)
            {
                api.DestroyShaderModule(device, vertexShader, null);
            }
        }
    }

    private static ShaderModule CreateShaderModule(
        Vk api,
        Device device,
        string shaderPath)
    {
        byte[] spirv = File.ReadAllBytes(shaderPath);

        if (spirv.Length < 20 || (spirv.Length % sizeof(uint)) != 0 ||
            BitConverter.ToUInt32(spirv, 0) != 0x07230203u)
        {
            throw new InvalidDataException(
                $"Shader is not valid SPIR-V: '{shaderPath}'. " +
                "Compile the GLSL shaders before running the demo.");
        }

        fixed (byte* bytecode = spirv)
        {
            ShaderModuleCreateInfo description = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = checked((nuint)spirv.Length),
                PCode = (uint*)bytecode
            };

            EnsureSuccess(
                api.CreateShaderModule(
                    device,
                    in description,
                    null,
                    out ShaderModule shader),
                $"Create shader module from '{shaderPath}'");

            if (shader.Handle == 0)
            {
                throw new InvalidOperationException(
                    $"Vulkan returned a null shader module: '{shaderPath}'.");
            }

            return shader;
        }
    }

    private static Framebuffer CreateFramebuffer(
        Vk api,
        Device device,
        RenderPass renderPass,
        ImageView imageView,
        Extent2D extent)
    {
        if (imageView.Handle == 0)
        {
            throw new ArgumentException(
                "A valid image view is required.",
                nameof(imageView));
        }

        FramebufferCreateInfo description = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 1,
            PAttachments = &imageView,
            Width = extent.Width,
            Height = extent.Height,
            Layers = 1
        };

        EnsureSuccess(
            api.CreateFramebuffer(
                device,
                in description,
                null,
                out Framebuffer framebuffer),
            "Create triangle framebuffer");

        if (framebuffer.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null framebuffer.");
        }

        return framebuffer;
    }

    private static void DestroyResources(
        Vk api,
        Device device,
        Framebuffer[] framebuffers,
        Pipeline pipeline,
        PipelineLayout layout,
        RenderPass renderPass)
    {
        for (int index = framebuffers.Length - 1; index >= 0; index--)
        {
            if (framebuffers[index].Handle != 0)
            {
                api.DestroyFramebuffer(device, framebuffers[index], null);
                framebuffers[index] = default;
            }
        }

        if (pipeline.Handle != 0)
        {
            api.DestroyPipeline(device, pipeline, null);
        }

        if (layout.Handle != 0)
        {
            api.DestroyPipelineLayout(device, layout, null);
        }

        if (renderPass.Handle != 0)
        {
            api.DestroyRenderPass(device, renderPass, null);
        }
    }

    private static void EnsureSuccess(Result result, string operation)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException(
                $"{operation} failed: {result}.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
