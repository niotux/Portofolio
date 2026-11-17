// IMAGE class //

#include "c-mplified/destroy.hpp"
#include "c-mplified/logs.hpp"
#define STB_IMAGE_IMPLEMENTATION
#include "stbi/stb_image.h"

#include "image.hpp"

// IMAGE
Image::Image(const char* filePath, bool flip)
{
    // Flip image
    stbi_set_flip_vertically_on_load(flip);

    // Load image
    imageData = stbi_load(filePath, &p_width, &p_height, &p_chanels, 0);
}

//----------------------------------------------
// getWidth()
int Image::getWidth()
{
    return p_width;
}

//----------------------------------------------
// getHeight()
int Image::getHeight()
{
    return p_height;
}

//----------------------------------------------
// getChanels()
int Image::getChanels()
{
    return p_chanels;
}

//----------------------------------------------
//----------------------------------------------
// ~IMAGE
Image::~Image()
{
    //std::cout << "~Image()\n";
    if (imageData)
    {
        //std::cout << "image deleted\n";
        stbi_image_free(imageData);
    }
}