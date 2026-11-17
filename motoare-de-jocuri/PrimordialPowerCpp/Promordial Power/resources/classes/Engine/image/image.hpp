// IMAGE class //
#pragma once

// IMAGE
class Image
{
public:
	Image(const char* filePath, bool flip);
	~Image();

	// Get size:
	int getWidth();
	int getHeight();

	// Get chanels used:
	int getChanels();

	unsigned char* imageData;	// The image datas

private:
	int p_width;
	int p_height;
	int p_chanels;
};