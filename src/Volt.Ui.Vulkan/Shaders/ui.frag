#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in vec4 fragColor;
layout(location = 2) flat in float fragLayer;
layout(location = 3) flat in float fragImageMode;
layout(location = 4) flat in float fragSrgbTarget;

layout(set = 0, binding = 0) uniform sampler2DArray atlasTexture;

layout(location = 0) out vec4 outColor;

// Palette bytes and image texels are authored in sRGB. An sRGB
// framebuffer expects linear values from the fragment shader and
// encodes them again on write, so handing the encoded value straight
// through applied the transfer function twice and rendered every
// surface noticeably brighter than the palette it came from. Decoding
// here also makes blending happen in linear light, which is where a
// half-transparent overlay composites correctly.
float srgbToLinear(float value)
{
	return value <= 0.04045
		? value / 12.92
		: pow((value + 0.055) / 1.055, 2.4);
}

vec3 srgbToLinear(vec3 value)
{
	return vec3(
		srgbToLinear(value.r),
		srgbToLinear(value.g),
		srgbToLinear(value.b));
}

void main()
{
	vec4 texel = texture(
		atlasTexture,
		vec3(fragUv, int(fragLayer)));

	vec3 vertexColor = fragColor.rgb;
	vec3 imageColor = texel.rgb;

	if (fragSrgbTarget > 0.5)
	{
		vertexColor = srgbToLinear(vertexColor);
		imageColor = srgbToLinear(imageColor);
	}

	// Coverage layers (glyph and mask atlases, plus the opaque white
	// layer that solid geometry samples) store coverage in the red
	// channel and take their colour from the vertex. Registered image
	// textures carry their own colour and alpha, so image geometry
	// multiplies them by the vertex tint instead. A batch only pushes
	// image mode for a texture the renderer has actually bound.

	if (fragImageMode > 0.5)
	{
		outColor = vec4(
			vertexColor * imageColor,
			fragColor.a * texel.a);
		return;
	}

	outColor = vec4(vertexColor, fragColor.a * texel.r);
}
