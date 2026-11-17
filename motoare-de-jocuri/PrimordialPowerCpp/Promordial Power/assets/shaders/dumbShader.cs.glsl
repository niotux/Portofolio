// DUMB - FRAGMENT SHADER
#version 460 core

in vec3 normal;
in vec3 fragPos;
in vec2 texCoord;

uniform sampler2D Texture;
uniform vec3 lightDirection;
uniform vec3 cameraPos;

out vec4 fragColor;

void main()
{
    vec4 textureColor = texture(Texture, texCoord);
    
    // Add simple diffuse lighting
    vec3 lightDir = normalize(lightDirection);
    float diff = max(dot(normal, lightDir), 0.2); // 0.2 for ambient
    
    fragColor = vec4(textureColor.rgb * diff, textureColor.a);
}
