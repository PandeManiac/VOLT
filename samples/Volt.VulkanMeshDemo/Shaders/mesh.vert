#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inColor;

layout(push_constant) uniform MeshPushConstants
{
    mat4 mvp;
} pushConstants;

layout(location = 0) out vec3 fragmentColor;

void main()
{
    // The CPU composes model * view * projection with row-vector
    // System.Numerics matrices and pushes the sixteen floats in their
    // natural memory order. GLSL reads those bytes as a column-major
    // mat4, which transposes the matrix implicitly, so the vector is
    // multiplied on the right of the shader matrix.
    gl_Position = pushConstants.mvp * vec4(inPosition, 1.0);

    fragmentColor = inColor;
}
