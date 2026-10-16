
# VOLT first Vulkan triangle

A minimal color-interpolated Vulkan triangle, building on the previously
validated single-frame swapchain presentation demo. This deliberately does
not add vertex buffers, descriptors, dynamic rendering, or resize handling.

## Installation

Copy the package's `src/` and `samples/` paths into the root of your VOLT
repository, preserving the paths. Do not copy `build_triangle_demo.py`, which
was only used to generate the replacement Program.cs.

Install a GLSL -> SPIR-V compiler on Arch Linux:

    sudo pacman -S --needed glslang

The updated sample project automatically runs `glslangValidator` when shader
sources change and copies the resulting `.spv` files to its output directory.

Build and run:

    dotnet build Volt.slnx -c Release
    dotnet test Volt.slnx -c Release --no-build
    SDL_VIDEODRIVER=wayland dotnet run --project samples/Volt.VulkanSurfaceDemo -c Release -- --validation
    SDL_VIDEODRIVER=x11 dotnet run --project samples/Volt.VulkanSurfaceDemo -c Release -- --validation

Expected: a red/green/blue interpolated triangle on dark blue background for
about five seconds; no Vulkan validation errors.

## Ownership and limitations

- Uses Vulkan 1.x core render-pass and graphics-pipeline APIs; no optional
  device features required.
- Creates a framebuffer for each swapchain image view.
- Uses `gl_VertexIndex`, so no vertex or index buffers.
- LoadOp.Clear discards the prior contents and initializes the background.
- The render pass transitions the attachment to `PresentSrcKhr`.
- Uses the existing per-frame acquisition semaphore, per-image render-finished
  semaphore, and frame fence.
- Runs **one frame only**, with a five-second hold for inspection.
- The outer demo's previous empty command-buffer smoke checks are retained.
- Swapchain recreation and continuous rendering are out of scope.
- This is code prepared for integration, not hardware-tested in this container.
