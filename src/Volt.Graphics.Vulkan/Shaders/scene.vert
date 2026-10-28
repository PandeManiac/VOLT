#version 450

// Vertex shader for VOLT's reusable scene mesh pipeline. Vertices
// carry a position and an albedo colour; placement and projection
// arrive as two 4x4 push constants.

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inColor;

layout(location = 0) out vec3 fragWorldPosition;
layout(location = 1) out vec3 fragAlbedo;

layout(push_constant) uniform PushConstants
{
	mat4 mvp;
	mat4 model;
} push;

void main()
{
	// The matrices are uploaded in System.Numerics memory order and
	// read here as column-major mat4 values, which transposes them
	// implicitly, so a row-vector multiply on the CPU and this
	// expression agree without an explicit transpose.
	vec4 world = push.model * vec4(inPosition, 1.0);

	gl_Position = push.mvp * vec4(inPosition, 1.0);
	fragWorldPosition = world.xyz;
	fragAlbedo = inColor;
}
