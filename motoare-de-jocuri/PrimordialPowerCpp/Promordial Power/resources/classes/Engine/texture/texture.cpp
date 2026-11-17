// TEXTURE class //

#include "c-mplified/destroy.hpp"
#include "c-mplified/logs.hpp"
#include "glad/glad.h"

#include "Engine/texture/texture.hpp"

// TEXTURE
Texture::Texture(const char* type, Image& image, unsigned int GLBorderS, unsigned int GLBorderT, unsigned int GLFiltering)
{
    // Logging level
    p_LOG.level = 0;

    // Generate texture
    glGenTextures(1, &ID);
    glBindTexture(GL_TEXTURE_2D, ID);
    
    // Parameters
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

    //generate texture
    if (image.imageData)
    {
        if (image.getChanels() == 4)
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, image.getWidth(), image.getHeight(), 0, GL_RGBA, GL_UNSIGNED_BYTE, image.imageData);
        else if (image.getChanels() == 3)
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGB, image.getWidth(), image.getHeight(), 0, GL_RGB, GL_UNSIGNED_BYTE, image.imageData);
        
        // Generate mipmap
        glGenerateMipmap(GL_TEXTURE_2D);
    }
    else
    {
        p_LOG.errorLog("Failed to load texture");
    }
}