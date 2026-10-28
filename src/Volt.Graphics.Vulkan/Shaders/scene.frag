#version 450

layout(location = 0) in vec3 fragWorldPosition;
layout(location = 1) in vec3 fragAlbedo;

layout(location = 0) out vec4 outColor;

// One directional light and one ambient term light the scene. The
// light direction and premultiplied color arrive in a uniform buffer
// bound as set 0, so an authored light can change without rebuilding
// the pipeline or growing the push-constant block.
layout(set = 0, binding = 0) uniform SceneLight
{
	vec4 direction;
	vec4 diffuse;
} sceneLight;

const vec3 FallbackDirection = normalize(vec3(0.35, 0.85, 0.40));
const float Ambient = 0.32;
const float Diffuse = 0.68;

void main()
{
	// Built-in meshes carry no normal attribute, so the face normal
	// comes from the screen-space derivatives of the world position.
	// Degenerate triangles would normalize a zero vector, so the
	// fallback keeps the result finite.
	vec3 faceCross =
		cross(dFdx(fragWorldPosition), dFdy(fragWorldPosition));
	float faceLength = length(faceCross);

	vec3 normal = faceLength > 1e-6
		? faceCross / faceLength
		: vec3(0.0, 1.0, 0.0);

	float directionLengthSquared =
		dot(sceneLight.direction.xyz, sceneLight.direction.xyz);

	vec3 toLight = directionLengthSquared > 1e-12
		? sceneLight.direction.xyz * inversesqrt(directionLengthSquared)
		: FallbackDirection;

	float lambert = max(dot(normal, toLight), 0.0);

	vec3 lighting = vec3(Ambient) +
		(sceneLight.diffuse.rgb * (Diffuse * lambert));

	outColor = vec4(fragAlbedo * lighting, 1.0);
}
