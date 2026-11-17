// DESTROY functions //
#pragma once

//----------------------------------------------
// DESTROY_OBJECT
template <typename type>
void destroyObject(type*& object)
{
	if (object)
	{
		delete object;
		object = nullptr;
	}
}

//----------------------------------------------
// DESTROY_ARRAY
template <typename type>
void destroyArray(type*& array)
{
	if (array)
	{
		delete[] array;
		array = nullptr;
	}
}

//----------------------------------------------
// DESTROY_PTR
template <typename type>
void destroyPtr(type*& ptr)
{
	if (ptr)
	{
		ptr = nullptr;
	}
}