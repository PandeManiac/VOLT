
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
- Two independently synchronized frames in flight
- Per-swapchain-image depth attachments
- Per-image graphics submission tracking

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

VOLT also provides `VulkanStagingBufferCache`, which can
retain staging allocations across multiple completed batches.

### Batched uploads

`VulkanBufferUploadBatch` collects multiple payloads into
one staging buffer.

Each payload receives its own device-local destination buffer.

The batch records all buffer copies into one command buffer,
inserts a transfer memory barrier, submits the commands once,
and waits for one completion fence.

The default submission path destroys its temporary staging
allocation after completion.

An optional staging cache can retain the staging buffer for
reuse by subsequent batches.

The caller owns all returned destination buffers.

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

### Reusable staging storage

Applications performing repeated synchronous uploads can
create a staging cache:

```csharp
using VulkanStagingBufferCache stagingCache = new(
    instance,
    device,
    physicalDevice);
```

Pass the cache to successive upload batches:

```csharp
VulkanBuffer[] buffers = batch.Submit(
    instance,
    device,
    physicalDevice,
    stagingCache);
```

The cache retains its allocation between completed batches.
It grows when a larger staging capacity is required.

Only one upload can use the cache at a time.

The cache does not own destination buffers or perform
asynchronous transfers.

The cube sample currently uses the default submission path.

### Data layout and alignment

Vulkan buffer-copy operations support arbitrary positive
byte counts and do not require four-byte-aligned source
offsets, destination offsets, or copy sizes.

The batch packs payloads contiguously without automatically
inserting alignment padding.

Applications must still respect alignment and data-layout
requirements imposed by their actual buffer consumers,
such as storage-buffer descriptors or shader structures.

VOLT does not impose a mandatory vertex format or mesh
representation.

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

This is a focused graphics sample intended to demonstrate VOLT's Vulkan infrastructure, not a general-purpose renderer.

### Windowing and Presentation

- The window is intentionally non-resizable.
- Swapchain recreation is not yet supported.

### GPU Uploads and Transfers

- The sample uses synchronous, one-shot upload batches.
- Upload payloads originate in managed memory and are copied into Vulkan staging memory before submission.
- Reusable staging storage is optional and supports synchronous uploads only.
- The sample does not use a persistent asynchronous staging ring.
- The sample does not schedule asynchronous transfers or use a dedicated transfer queue.

### Rendering and Scene Management

- Buffer device addresses are not required.
- No engine-defined mesh or material system is used.
- The sample does not render ECS-managed scene objects.

For frequently changing geometry, future improvements should
include device-memory suballocation, asynchronous completion
tracking, and more advanced staging allocation management.

## Resource Ownership

All destination buffers returned by an upload batch belong
to the caller and must be disposed explicitly.

The batch waits for the transfer to complete before releasing
its temporary Vulkan resources.

A reusable staging cache owns its retained staging buffer.
It must be disposed before the Vulkan device is destroyed.

The demo waits for the device to become idle before releasing
persistent rendering resources.

GPU buffer accesses must be synchronized with unrelated
pending operations by the application.
```
