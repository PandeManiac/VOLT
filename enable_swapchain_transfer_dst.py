#!/usr/bin/env python3
"""Enable VK_IMAGE_USAGE_TRANSFER_DST_BIT when the surface supports it.

Run this from the VOLT repository root. Existing source is modified only
when all expected code fragments match exactly.
"""

from pathlib import Path

path = Path('src/Volt.Graphics.Vulkan/Swapchain/VulkanSwapchain.cs')
original = path.read_text(encoding='utf-8')

replacements = [
    (
        '    public Extent2D Extent { get; }\n',
        '    public Extent2D Extent { get; }\n\n'
        '    public ImageUsageFlags ImageUsage { get; }\n',
    ),
    (
        '        SurfaceCapabilitiesKHR capabilities =\n'
        '            support.Capabilities;\n',
        '        SurfaceCapabilitiesKHR capabilities =\n'
        '            support.Capabilities;\n\n'
        '        ImageUsageFlags imageUsage =\n'
        '            ImageUsageFlags.ColorAttachmentBit;\n\n'
        '        if ((capabilities.SupportedUsageFlags &\n'
        '             ImageUsageFlags.TransferDstBit) != 0)\n'
        '        {\n'
        '            imageUsage |= ImageUsageFlags.TransferDstBit;\n'
        '        }\n',
    ),
    (
        '                ImageUsage = ImageUsageFlags.ColorAttachmentBit,\n',
        '                ImageUsage = imageUsage,\n',
    ),
    (
        '            Extent = extent;\n',
        '            Extent = extent;\n'
        '            ImageUsage = imageUsage;\n',
    ),
]

updated = original
for old, new in replacements:
    if old not in updated:
        raise SystemExit(
            'Could not safely update VulkanSwapchain.cs; unexpected '
            f'source fragment:\n{old!r}\nNo changes were made.'
        )
    if updated.count(old) != 1:
        raise SystemExit(
            f'Expected exactly one occurrence of {old!r}. '
            'No changes were made.'
        )
    updated = updated.replace(old, new, 1)

path.write_text(updated, encoding='utf-8')
print(f'Updated {path} to select and expose transfer-destination image usage.')
