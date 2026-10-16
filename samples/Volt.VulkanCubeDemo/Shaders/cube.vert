
#version 450

layout(push_constant) uniform CubePushConstants
{
    float angle;
    float aspect;
} pushConstants;

layout(location = 0) out vec3 fragmentColor;

const vec3 corners[8] = vec3[8](
    vec3(-1.0, -1.0, -1.0),
    vec3( 1.0, -1.0, -1.0),
    vec3( 1.0,  1.0, -1.0),
    vec3(-1.0,  1.0, -1.0),

    vec3(-1.0, -1.0,  1.0),
    vec3( 1.0, -1.0,  1.0),
    vec3( 1.0,  1.0,  1.0),
    vec3(-1.0,  1.0,  1.0)
);

const int triangles[36] = int[36](
    4, 5, 6, 4, 6, 7,
    1, 0, 3, 1, 3, 2,
    5, 1, 2, 5, 2, 6,
    0, 4, 7, 0, 7, 3,
    7, 6, 2, 7, 2, 3,
    0, 1, 5, 0, 5, 4
);

const vec3 faceColors[6] = vec3[6](
    vec3(0.22, 0.74, 1.00),
    vec3(0.96, 0.35, 0.47),
    vec3(0.38, 0.95, 0.62),
    vec3(0.99, 0.76, 0.30),
    vec3(0.70, 0.47, 0.98),
    vec3(0.98, 0.53, 0.24)
);

void main()
{
    int cornerIndex = triangles[gl_VertexIndex];
    int faceIndex = gl_VertexIndex / 6;

    vec3 position = corners[cornerIndex] * 0.75;

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

    // The camera is at the origin, looking toward negative Z.
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

    fragmentColor = faceColors[faceIndex];
}
