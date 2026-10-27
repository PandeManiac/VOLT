#version 450

layout(location = 0) in vec2 fragUv;
layout(location = 1) in vec4 fragColor;

layout(set = 0, binding = 0) uniform sampler2DArray atlasTexture;
layout(set = 0, binding = 1) uniform sampler2D imageTexture;

// Coverage batches carry the atlas layer to sample. Image batches
// ignore it. The push constant is read here, which is why the
// pipeline layout names the fragment stage.
layout(push_constant) uniform PushConstants
{
	float layer;
	float mode;
} push;

layout(location = 0) out vec4 outColor;

void main()
{
	// A coverage batch samples glyph or white-texel coverage and uses
	// the command color for red, green, and blue, so every solid shape
	// and every glyph is tinted by the color it was recorded with.
	if (push.mode < 0.5)
	{
		float coverage = texture(
			atlasTexture,
			vec3(fragUv, int(push.layer))).r;

		outColor = vec4(fragColor.rgb, fragColor.a * coverage);
		return;
	}

	// An image batch samples straight-alpha RGBA and modulates it with
	// the command color, so a white tint draws the image unchanged and
	// a translucent tint fades it.
	outColor = fragColor * texture(imageTexture, fragUv);
}
