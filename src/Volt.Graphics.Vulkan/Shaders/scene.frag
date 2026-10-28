#version 450

layout(location = 0) in vec3 fragWorldPosition;
layout(location = 1) in vec3 fragAlbedo;

layout(location = 0) out vec4 outColor;

// One directional light and one ambient term are baked into the
// pipeline. Per-entity materials and lights are a later milestone;
// what this shader owes the editor is a lit, depth-tested image that
// makes an authored transform visible.

const vec3 LightDirection = normalize(vec3(0.35, 0.85, 0.40));
const float Ambient = 0.32;
const float Diffuse = 0.68;

void main()
{
	// Built-in meshes carry no normal attribute, so the face normal
	// comes from the screen-space derivatives of the world position.
	// Degenerate triangles would normalize a zero vector, so the
	// fallback keeps the result finite.
	vec3 cross = cross(dFdx(fragWorldPosition), dFdy(fragWorldPosition));
	float length = length(cross);

	vec3 normal = length > 1e-6
		? cross / length
		: vec3(0.0, 1.0, 0.0);

	float lambert = max(dot(normal, LightDirection), 0.0);
	float lighting = Ambient + (Diffuse * lambert);

	outColor = vec4(fragAlbedo * lighting, 1.0);
}
