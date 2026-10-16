
# VOLT Vulkan Cube: Batched Device-Local Vertex Pulling

A continuously rotating, depth-tested Vulkan cube demonstrating
VOLT's graphics infrastructure, custom vertex pulling, and
batched device-local GPU uploads.

## Features

- Continuous Vulkan rendering until the window closes
- Perspective projection and rotation around two axes
- Six individually colored cube faces
- Hardware depth testing
- SDL3 event polling
- Explicit GPU synchronization and resource ownership
- Descriptor-backed storage buffers
- Shader-driven vertex pulling
- Compact, application-defined geometry descriptors
- Device-local GPU buffers
- Multiple buffer uploads in one transfer submission

## Vertex Pulling

The cube uses six 32-bit face descriptors stored in a Vulkan
storage buffer.

Each descriptor encodes:

| Bits | Purpose |
|---|---|
| 0-1 | Face axis |
| 2 | Positive or negative direction |
| 3-5 | Face color index |

A second storage buffer contains six packed RGB colors.

The vertex shader reads both buffers and reconstructs each
quad using `gl_VertexIndex` and `gl_InstanceIndex`.

The renderer issues a draw with six vertices and six instances.

There are no conventional vertex attributes or vertex-buffer
bindings.

Both packing formats are specific to this sample, not
mandatory VOLT data formats.

## GPU Buffer Infrastructure

The sample uses:

- `VulkanBuffer` for buffer creation and allocation
- `VulkanBufferTransfer` for individual synchronous transfers
- `VulkanBufferUploadBatch` for batched synchronous uploads

### Batched uploads

`VulkanBufferUploadBatch` collects multiple payloads into
one staging allocation.

Each payload receives its own device-local destination buffer.

The batch records all buffer copies into one command buffer,
inserts a transfer memory barrier, submits the commands once,
and waits for one completion fence.

After completion, the staging allocation is destroyed and
the destination buffers are returned to the caller.

The caller owns the returned buffers.

### Example

```csharp
VulkanBufferUploadBatch batch = new();

int facesIndex = batch.Add<uint>(
    faces,
    BufferUsageFlags.StorageBufferBit);

int colorsIndex = batch.Add<uint>(
    colors,
    BufferUsageFlags.StorageBufferBit);

VulkanBuffer[] buffers = batch.Submit(
    instance,
    device,
    physicalDevice);
```

In this example, `buffers[facesIndex]` and
`buffers[colorsIndex]` are independent device-local buffers.

Both were populated through the same Vulkan submission.

Uploads must currently be positive multiples of four bytes.
Applications are responsible for packing or padding their
data to meet that requirement.

## Requirements

- .NET 10 SDK
- Vulkan-capable GPU and driver
- SDL3 runtime dependencies
- glslangValidator
- Optional Vulkan validation layers

On Arch Linux:

```bash
sudo pacman -S --needed glslang vulkan-validation-layers
```

## Build

From the repository root:

```bash
dotnet build Volt.slnx -c Release
dotnet test Volt.slnx -c Release --no-build
```

## Run

Wayland:

```bash
SDL_VIDEODRIVER=wayland dotnet run \
    --project samples/Volt.VulkanCubeDemo \
    -c Release -- --validation
```

X11:

```bash
SDL_VIDEODRIVER=x11 dotnet run \
    --project samples/Volt.VulkanCubeDemo \
    -c Release -- --validation
```

Close the window to exit.

## Limitations

This is a focused graphics sample, not VOLT's final renderer.

- Only one graphics submission is in flight.
- The depth attachment is shared between framebuffers.
- The window is intentionally non-resizable.
- Swapchain recreation is not yet supported.
- Upload batches are synchronous and one-shot.
- Each destination buffer has a dedicated Vulkan allocation.
- Uploads are staged in managed memory before submission.
- No persistent staging ring or upload allocation pool exists.
- There is no asynchronous transfer scheduling.
- No dedicated transfer queue is used.
- Buffer device addresses are not required.
- No engine-defined mesh or material system is required.
- The sample does not render ECS-managed scene objects.

For frequently changing geometry, future improvements should
include staging allocation reuse, device-memory suballocation,
and asynchronous completion tracking.

## Resource Ownership

All destination buffers returned by an upload batch belong
to the caller and must be disposed explicitly.

The batch waits for the transfer to complete before releasing
its temporary Vulkan resources.

The demo waits for the device to become idle before releasing
persistent rendering resources.

GPU buffer accesses must be synchronized with unrelated
pending operations by the application.
