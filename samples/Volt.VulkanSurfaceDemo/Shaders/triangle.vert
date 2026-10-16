#version 450

layout(location = 0) out vec3 vertexColor;

const vec2 positions[3] = vec2[](
    vec2(-0.76,  0.70),
    vec2( 0.76,  0.70),
    vec2( 0.00, -0.76)
);

const vec3 colors[3] = vec3[](
    vec3(1.00, 0.30, 0.34),
    vec3(0.32, 1.00, 0.56),
    vec3(0.30, 0.66, 1.00)
);

void main()
{
    gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
    vertexColor = colors[gl_VertexIndex];
}
