#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in vec4 fragColor;
layout(location = 2) flat in float fragLayer;
layout(location = 3) flat in float fragImageMode;

layout(set = 0, binding = 0) uniform sampler2DArray atlasTexture;

layout(location = 0) out vec4 outColor;

void main()
{
	vec4 texel = texture(
		atlasTexture,
		vec3(fragUv, int(fragLayer)));

	// Coverage layers (glyph and mask atlases, plus the opaque white
	// layer that solid geometry samples) store coverage in the red
	// channel and take their colour from the vertex. Registered image
	// textures carry their own colour and alpha, so image geometry
	// multiplies them by the vertex tint instead. A batch only pushes
	// image mode for a texture the renderer has actually bound.

	if (fragImageMode > 0.5)
	{
		outColor = vec4(
			fragColor.rgb * texel.rgb,
			fragColor.a * texel.a);
		return;
	}

	outColor = vec4(fragColor.rgb, fragColor.a * texel.r);
}
