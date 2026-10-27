#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in vec4 fragColor;
layout(location = 2) flat in float fragLayer;

layout(set = 0, binding = 0) uniform sampler2DArray atlasTexture;

layout(location = 0) out vec4 outColor;

void main()
{
	float coverage = texture(
		atlasTexture,
		vec3(fragUv, int(fragLayer))).r;

	outColor = vec4(fragColor.rgb, fragColor.a * coverage);
}
