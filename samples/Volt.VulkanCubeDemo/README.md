
# VOLT Rotating Vulkan Cube

A continuously rotating, depth-tested Vulkan cube demonstrating
VOLT's graphics and platform infrastructure.

## Features

- Continuous Vulkan rendering until the window closes.
- Perspective projection.
- Rotation around two axes.
- Six individually colored cube faces.
- Hardware depth testing using a device-local depth image.
- SDL3 event polling and responsive window behavior.
- Vulkan swapchain image acquisition and presentation.
- Fence and semaphore synchronization.
- Explicit native resource cleanup.
- Optional Vulkan validation layers.

The cube's 36 vertices are generated in the vertex shader using
`gl_VertexIndex`. Vertex and index buffers are not yet used.

## Requirements

- .NET 10 SDK
- Vulkan-capable GPU and driver
- SDL3 runtime dependencies
- `glslangValidator`
- Vulkan validation layers when using `--validation`

On Arch Linux:

```bash
sudo pacman -S --needed glslang vulkan-validation-layers
```

## Build

From the repository root:

```bash
dotnet build Volt.slnx -c Release
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

Omit `--validation` to run without Vulkan validation layers.

Close the window to exit.

## Limitations

This is a focused rendering sample, not the engine's final renderer.

- Only one graphics submission is allowed in flight.
- The depth attachment is shared between framebuffers.
- Resizing requires swapchain recreation, which is not yet implemented.
- The window is intentionally non-resizable.
- No vertex or index buffers, textures, materials, or ECS scene integration.
- No support for multiple independently rendered 3D objects.

The frame loop currently exits when the swapchain becomes invalid
rather than attempting to recreate it.

The sample is intended to establish the first continuously rendered
3D geometry before adding general GPU resource management.
