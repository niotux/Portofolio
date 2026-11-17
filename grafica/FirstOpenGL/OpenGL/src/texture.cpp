// TEXTURE class //

#include <iostream>
#include <string>

#include "glad/glad.h"
#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#include "texture.hpp"

Texture::Texture(const std::string& texturePath, unsigned int GLColorChannels, unsigned int GLBorderS, unsigned int GLBorderT, unsigned int GLFiltering)
{

    glGenTextures(1, &ID);
    glBindTexture(GL_TEXTURE_2D, ID);
    //parameters
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GLBorderS);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GLBorderT);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GLFiltering);
    if (GLFiltering == GL_NEAREST)
    {
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST_MIPMAP_NEAREST);
    }
    else
    {
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST_MIPMAP_LINEAR);
    }
    //load image data
    int texHeight, texWidth, nrChanels;
    stbi_set_flip_vertically_on_load(true);
    unsigned char* imageData = stbi_load(texturePath.c_str(), &texWidth, &texHeight, &nrChanels, 0);
    //generate texture
    if (imageData)
    {
        glTexImage2D(GL_TEXTURE_2D, 0, GLColorChannels, texWidth, texHeight, 0, GLColorChannels, GL_UNSIGNED_BYTE, imageData);
        glGenerateMipmap(GL_TEXTURE_2D);
    }
    else
    {
        std::cout << "class Texture -> ERROR::Failed to load texture:" << std::endl << texturePath << std::endl;
    }
    //free image data
    stbi_image_free(imageData);
}