
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;
using Volt.Graphics.Vulkan.Devices;
using Volt.Graphics.Vulkan.Instance;
using Volt.Graphics.Vulkan.Swapchain;

namespace Volt.VulkanCubeDemo;

/// <summary>
/// Owns the graphics resources for a rotating, depth-tested cube.
/// </summary>
/// <remarks>
/// This is a demonstration renderer, not a general-purpose rendering
/// abstraction.
///
/// A single shared depth image is safe because the demo waits for its
/// only in-flight graphics submission before recording the next frame.
///
/// Callers must finish all GPU and presentation operations before
/// disposing the renderer.
/// </remarks>
internal sealed unsafe class CubeRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Angle;
        public float Aspect;
    }

    private readonly VulkanLogicalDevice _device;
    private readonly Vk _api;
    private readonly Framebuffer[] _framebuffers;
    private readonly CubeFaceStorage _faces;

    private RenderPass _renderPass;
    private PipelineLayout _layout;
    private Pipeline _pipeline;

    private Image _depthImage;
    private DeviceMemory _depthMemory;
    private ImageView _depthView;

    private bool _disposed;

    public Extent2D Extent { get; }

    public Format DepthFormat { get; }

    public CubeRenderer(
        VulkanInstance instance,
        VulkanLogicalDevice device,
        PhysicalDevice physicalDevice,
        VulkanSwapchain swapchain,
        VulkanSwapchainImageViews imageViews,
        string vertexShaderPath,
        string fragmentShaderPath)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(swapchain);
        ArgumentNullException.ThrowIfNull(imageViews);

        if (physicalDevice.Handle == 0)
        {
            throw new ArgumentException(
                "A valid physical device is required.",
                nameof(physicalDevice));
        }

        if (imageViews.Count != checked((int)swapchain.ImageCount))
        {
            throw new ArgumentException(
                "Every swapchain image requires an image view.",
                nameof(imageViews));
        }

        if ((swapchain.ImageUsage & ImageUsageFlags.ColorAttachmentBit) == 0)
        {
            throw new NotSupportedException(
                "The swapchain must support color attachments.");
        }

        Vk api = instance.Api;
        Device deviceHandle = device.Handle;

        Format depthFormat = SelectDepthFormat(
            api,
            physicalDevice);

        Image depthImage = default;
        DeviceMemory depthMemory = default;
        ImageView depthView = default;

        RenderPass renderPass = default;
        PipelineLayout layout = default;
        Pipeline pipeline = default;
        CubeFaceStorage? faces = null;

        Framebuffer[] framebuffers =
            new Framebuffer[imageViews.Count];

        try
        {
            depthImage = CreateDepthImage(
                api,
                deviceHandle,
                swapchain.Extent,
                depthFormat);

            depthMemory = AllocateDepthMemory(
                api,
                physicalDevice,
                deviceHandle,
                depthImage);

            EnsureSuccess(
                api.BindImageMemory(
                    deviceHandle,
                    depthImage,
                    depthMemory,
                    0),
                "Bind depth image memory");

            depthView = CreateDepthView(
                api,
                deviceHandle,
                depthImage,
                depthFormat);

            renderPass = CreateRenderPass(
                api,
                deviceHandle,
                swapchain.ImageFormat,
                depthFormat);

            faces = new CubeFaceStorage(
                instance,
                device,
                physicalDevice);

            layout = CreatePipelineLayout(
                api,
                deviceHandle,
                faces.Layout);

            pipeline = CreateGraphicsPipeline(
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
                    depthView,
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
                renderPass,
                depthView,
                depthImage,
                depthMemory);

            faces?.Dispose();
            throw;
        }

        _device = device;
        _api = api;
        _framebuffers = framebuffers;

        _faces = faces ?? throw new InvalidOperationException(
            "Cube face storage was not initialized.");

        _renderPass = renderPass;
        _layout = layout;
        _pipeline = pipeline;

        _depthImage = depthImage;
        _depthMemory = depthMemory;
        _depthView = depthView;

        Extent = swapchain.Extent;
        DepthFormat = depthFormat;
    }

    /// <summary>
    /// Records the cube rendering pass for the acquired image.
    /// </summary>
    public void Record(
        CommandBuffer commandBuffer,
        int imageIndex,
        float angle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (commandBuffer.Handle == 0)
        {
            throw new ArgumentException(
                "A valid command buffer is required.",
                nameof(commandBuffer));
        }

        if ((uint)imageIndex >= (uint)_framebuffers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(imageIndex));
        }

        if (!float.IsFinite(angle))
        {
            throw new ArgumentOutOfRangeException(nameof(angle));
        }

        ClearValue* clearValues = stackalloc ClearValue[2];

        clearValues[0] = new ClearValue
        {
            Color = new ClearColorValue(
                float32_0: 0.045f,
                float32_1: 0.075f,
                float32_2: 0.13f,
                float32_3: 1.0f)
        };

        clearValues[1] = new ClearValue
        {
            DepthStencil = new ClearDepthStencilValue
            {
                Depth = 1.0f,
                Stencil = 0
            }
        };

        RenderPassBeginInfo begin = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffers[imageIndex],

            RenderArea = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = Extent
            },

            ClearValueCount = 2,
            PClearValues = clearValues
        };

        PushConstants constants = new()
        {
            Angle = angle,
            Aspect = (float)Extent.Width / Extent.Height
        };

        _api.CmdBeginRenderPass(
            commandBuffer,
            in begin,
            SubpassContents.Inline);

        _api.CmdBindPipeline(
            commandBuffer,
            PipelineBindPoint.Graphics,
            _pipeline);

        _faces.Bind(commandBuffer, _layout);

        _api.CmdPushConstants(
            commandBuffer,
            _layout,
            ShaderStageFlags.VertexBit,
            0,
            (uint)sizeof(PushConstants),
            &constants);

        _api.CmdDraw(
            commandBuffer,
            vertexCount: 6,
            instanceCount: 6,
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

        DestroyResources(
            _api,
            _device.Handle,
            _framebuffers,
            _pipeline,
            _layout,
            _renderPass,
            _depthView,
            _depthImage,
            _depthMemory);

        _faces.Dispose();

        _pipeline = default;
        _layout = default;
        _renderPass = default;

        _depthImage = default;
        _depthMemory = default;
        _depthView = default;

        _disposed = true;
    }

    private static Format SelectDepthFormat(
        Vk api,
        PhysicalDevice physicalDevice)
    {
        Format[] formats =
        [
            Format.D32Sfloat,
            Format.D16Unorm
        ];

        foreach (Format format in formats)
        {
            FormatProperties properties =
                api.GetPhysicalDeviceFormatProperties(
                    physicalDevice,
                    format);

            if ((properties.OptimalTilingFeatures &
                FormatFeatureFlags.DepthStencilAttachmentBit) != 0)
            {
                return format;
            }
        }

        throw new NotSupportedException(
            "No supported depth attachment format was found.");
    }

    private static Image CreateDepthImage(
        Vk api,
        Device device,
        Extent2D extent,
        Format format)
    {
        ImageCreateInfo description = new()
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,

            Extent = new Extent3D
            {
                Width = extent.Width,
                Height = extent.Height,
                Depth = 1
            },

            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined
        };

        EnsureSuccess(
            api.CreateImage(
                device,
                in description,
                null,
                out Image image),
            "Create depth image");

        if (image.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null depth image.");
        }

        return image;
    }

    private static DeviceMemory AllocateDepthMemory(
        Vk api,
        PhysicalDevice physicalDevice,
        Device device,
        Image image)
    {
        MemoryRequirements requirements =
            api.GetImageMemoryRequirements(
                device,
                image);

        PhysicalDeviceMemoryProperties properties =
            api.GetPhysicalDeviceMemoryProperties(
                physicalDevice);

        uint? memoryTypeIndex = null;

        for (uint index = 0; index < properties.MemoryTypeCount; index++)
        {
            uint bit = 1u << checked((int)index);

            bool compatible =
                (requirements.MemoryTypeBits & bit) != 0;

            MemoryPropertyFlags flags =
                properties.MemoryTypes[checked((int)index)]
                    .PropertyFlags;

            bool deviceLocal =
                (flags & MemoryPropertyFlags.DeviceLocalBit) != 0;

            if (compatible && deviceLocal)
            {
                memoryTypeIndex = index;
                break;
            }
        }

        if (!memoryTypeIndex.HasValue)
        {
            throw new NotSupportedException(
                "No device-local memory type supports the depth image.");
        }

        MemoryAllocateInfo description = new()
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = requirements.Size,
            MemoryTypeIndex = memoryTypeIndex.Value
        };

        EnsureSuccess(
            api.AllocateMemory(
                device,
                in description,
                null,
                out DeviceMemory memory),
            "Allocate depth image memory");

        if (memory.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null depth memory allocation.");
        }

        return memory;
    }

    private static ImageView CreateDepthView(
        Vk api,
        Device device,
        Image image,
        Format format)
    {
        ImageViewCreateInfo description = new()
        {
            SType = StructureType.ImageViewCreateInfo,
            Image = image,
            ViewType = ImageViewType.Type2D,
            Format = format,
            SubresourceRange = new ImageSubresourceRange
            {
                AspectMask = ImageAspectFlags.DepthBit,
                BaseMipLevel = 0,
                LevelCount = 1,
                BaseArrayLayer = 0,
                LayerCount = 1
            }
        };

        EnsureSuccess(
            api.CreateImageView(
                device,
                in description,
                null,
                out ImageView view),
            "Create depth image view");

        if (view.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null depth image view.");
        }

        return view;
    }

    private static RenderPass CreateRenderPass(
        Vk api,
        Device device,
        Format colorFormat,
        Format depthFormat)
    {
        AttachmentDescription* attachments =
            stackalloc AttachmentDescription[2];

        attachments[0] = new AttachmentDescription
        {
            Format = colorFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        attachments[1] = new AttachmentDescription
        {
            Format = depthFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.DontCare,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.DepthStencilAttachmentOptimal,
        };

        AttachmentReference colorReference = new()
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        AttachmentReference depthReference = new()
        {
            Attachment = 1,
            Layout = ImageLayout.DepthStencilAttachmentOptimal
        };

        SubpassDescription subpass = new()
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
            PDepthStencilAttachment = &depthReference
        };

        SubpassDependency* dependencies =
            stackalloc SubpassDependency[2];

        dependencies[0] = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.TopOfPipeBit,

            DstStageMask =
                PipelineStageFlags.ColorAttachmentOutputBit |
                PipelineStageFlags.EarlyFragmentTestsBit,

            SrcAccessMask = AccessFlags.None,

            DstAccessMask =
                AccessFlags.ColorAttachmentWriteBit |
                AccessFlags.DepthStencilAttachmentWriteBit
        };

        dependencies[1] = new SubpassDependency
        {
            SrcSubpass = 0,
            DstSubpass = Vk.SubpassExternal,

            SrcStageMask =
                PipelineStageFlags.ColorAttachmentOutputBit |
                PipelineStageFlags.EarlyFragmentTestsBit |
                PipelineStageFlags.LateFragmentTestsBit,

            DstStageMask = PipelineStageFlags.BottomOfPipeBit,

            SrcAccessMask =
                AccessFlags.ColorAttachmentWriteBit |
                AccessFlags.DepthStencilAttachmentWriteBit,

            DstAccessMask = AccessFlags.None
        };

        RenderPassCreateInfo description = new()
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 2,
            PAttachments = attachments,
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
            "Create cube render pass");

        return renderPass;
    }

    private static PipelineLayout CreatePipelineLayout(
        Vk api,
        Device device,
        DescriptorSetLayout faceLayout)
    {
        PushConstantRange range = new()
        {
            StageFlags = ShaderStageFlags.VertexBit,
            Offset = 0,
            Size = (uint)sizeof(PushConstants)
        };

        PipelineLayoutCreateInfo description = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &faceLayout,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &range
        };

        EnsureSuccess(
            api.CreatePipelineLayout(
                device,
                in description,
                null,
                out PipelineLayout layout),
            "Create cube pipeline layout");

        if (layout.Handle == 0)
        {
            throw new InvalidOperationException(
                "Vulkan returned a null pipeline layout.");
        }

        return layout;
    }

    private static Pipeline CreateGraphicsPipeline(
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

            PipelineInputAssemblyStateCreateInfo assembly = new()
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

            PipelineDepthStencilStateCreateInfo depthState = new()
            {
                SType = StructureType.PipelineDepthStencilStateCreateInfo,
                DepthTestEnable = Vk.True,
                DepthWriteEnable = Vk.True,
                DepthCompareOp = CompareOp.Less,
                DepthBoundsTestEnable = Vk.False,
                StencilTestEnable = Vk.False
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

            PipelineColorBlendStateCreateInfo blending = new()
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
                PInputAssemblyState = &assembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterization,
                PMultisampleState = &multisampling,
                PDepthStencilState = &depthState,
                PColorBlendState = &blending,
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
                    $"Create cube graphics pipeline failed: {result}.");
            }

            return pipeline;
        }

        finally
        {
            if (fragmentShader.Handle != 0)
            {
                api.DestroyShaderModule(
                    device,
                    fragmentShader,
                    null);
            }

            if (vertexShader.Handle != 0)
            {
                api.DestroyShaderModule(
                    device,
                    vertexShader,
                    null);
            }
        }
    }

    private static ShaderModule CreateShaderModule(
        Vk api,
        Device device,
        string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        if (bytes.Length < 20 ||
            bytes.Length % sizeof(uint) != 0 ||
            BitConverter.ToUInt32(bytes, 0) != 0x07230203u)
        {
            throw new InvalidDataException(
                $"Shader file is not valid SPIR-V: {path}");
        }

        fixed (byte* bytecode = bytes)
        {
            ShaderModuleCreateInfo description = new()
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = checked((nuint)bytes.Length),
                PCode = (uint*)bytecode
            };

            EnsureSuccess(
                api.CreateShaderModule(
                    device,
                    in description,
                    null,
                    out ShaderModule shader),
                "Create cube shader module");

            return shader;
        }
    }

    private static Framebuffer CreateFramebuffer(
        Vk api,
        Device device,
        RenderPass renderPass,
        ImageView colorView,
        ImageView depthView,
        Extent2D extent)
    {
        ImageView* attachments = stackalloc ImageView[2];

        attachments[0] = colorView;
        attachments[1] = depthView;

        FramebufferCreateInfo description = new()
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = renderPass,
            AttachmentCount = 2,
            PAttachments = attachments,
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
            "Create cube framebuffer");

        return framebuffer;
    }

    private static void DestroyResources(
        Vk api,
        Device device,
        Framebuffer[] framebuffers,
        Pipeline pipeline,
        PipelineLayout layout,
        RenderPass renderPass,
        ImageView depthView,
        Image depthImage,
        DeviceMemory depthMemory)
    {
        for (int index = framebuffers.Length - 1; index >= 0; index--)
        {
            if (framebuffers[index].Handle != 0)
            {
                api.DestroyFramebuffer(
                    device,
                    framebuffers[index],
                    null);
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

        if (depthView.Handle != 0)
        {
            api.DestroyImageView(device, depthView, null);
        }

        if (depthImage.Handle != 0)
        {
            api.DestroyImage(device, depthImage, null);
        }

        if (depthMemory.Handle != 0)
        {
            api.FreeMemory(device, depthMemory, null);
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
