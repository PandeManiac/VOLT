
#version 450

layout(set = 0, binding = 0, std430) readonly buffer CubeFaces
{
    uint faces[];
} faceBuffer;

layout(set = 0, binding = 1, std430) readonly buffer CubeColors
{
    uint colors[];
} colorBuffer;

layout(push_constant) uniform CubePushConstants
{
    float angle;
    float aspect;
} pushConstants;

layout(location = 0) out vec3 fragmentColor;

const vec2 quadCorners[6] = vec2[6](
    vec2(-1.0, -1.0),
    vec2( 1.0, -1.0),
    vec2( 1.0,  1.0),
    vec2(-1.0, -1.0),
    vec2( 1.0,  1.0),
    vec2(-1.0,  1.0)
);

void main()
{
    uint descriptor = faceBuffer.faces[gl_InstanceIndex];

    uint axis = descriptor & 3u;
    uint positive = (descriptor >> 2u) & 1u;
    uint colorIndex = (descriptor >> 3u) & 7u;

    float plane = positive != 0u ? 1.0 : -1.0;

    vec2 corner = quadCorners[gl_VertexIndex];

    vec3 position;

    if (axis == 0u)
    {
        position = vec3(plane, corner.x, corner.y);
    }
    else if (axis == 1u)
    {
        position = vec3(corner.x, plane, corner.y);
    }
    else
    {
        position = vec3(corner.x, corner.y, plane);
    }

    position *= 0.75;

    float yaw = pushConstants.angle;
    float pitch = pushConstants.angle * 0.61;

    float cy = cos(yaw);
    float sy = sin(yaw);

    position = vec3(
         cy * position.x + sy * position.z,
         position.y,
        -sy * position.x + cy * position.z
    );

    float cx = cos(pitch);
    float sx = sin(pitch);

    position = vec3(
        position.x,
        cx * position.y - sx * position.z,
        sx * position.y + cx * position.z
    );

    position.z -= 4.0;

    float nearPlane = 0.1;
    float farPlane = 100.0;
    float focal = 1.0 / tan(radians(55.0) * 0.5);

    float clipZ =
        (farPlane / (nearPlane - farPlane)) * position.z +
        (farPlane * nearPlane / (nearPlane - farPlane));

    gl_Position = vec4(
        position.x * focal / pushConstants.aspect,
        -position.y * focal,
        clipZ,
        -position.z
    );

    uint packedColor = colorBuffer.colors[colorIndex];

    fragmentColor = vec3(
        float((packedColor >> 16u) & 255u),
        float((packedColor >> 8u) & 255u),
        float(packedColor & 255u)
    ) / 255.0;
}
