#version 450

layout(location = 0) in vec2 inPosition;
layout(location = 1) in vec2 inUv;
layout(location = 2) in vec4 inColor;

layout(location = 0) out vec2 fragUv;
layout(location = 1) out vec4 fragColor;
layout(location = 2) flat out float fragLayer;
layout(location = 3) flat out float fragImageMode;
layout(location = 4) flat out float fragSrgbTarget;

layout(push_constant) uniform PushConstants
{
	float layer;
	float imageMode;
	float srgbTarget;
} push;

void main()
{
	gl_Position = vec4(inPosition, 0.0, 1.0);
	fragUv = inUv;
	fragColor = inColor;
	fragLayer = push.layer;
	fragImageMode = push.imageMode;
	fragSrgbTarget = push.srgbTarget;
}
