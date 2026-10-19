#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inColor;

layout(push_constant) uniform MeshPushConstants
{
    float aspect;
} pushConstants;

layout(location = 0) out vec3 fragmentColor;

void main()
{
    // Preserve the geometry's shape at any window aspect ratio while
    // keeping the three meshes inside the horizontal frame.
    float horizontalScale = 1.0 / max(pushConstants.aspect, 1.0);
    float verticalScale = min(pushConstants.aspect, 1.0);

    // Vulkan's viewport Y axis points down, so negate the authored
    // Y coordinate to keep the meshes upright.
    gl_Position = vec4(
        inPosition.x * horizontalScale,
        -inPosition.y * verticalScale,
        0.0,
        1.0);

    fragmentColor = inColor;
}
