#pragma once

#include <string.h>

typedef struct LANString LANString;

typedef struct
{
  const char* Chars;
  const size_t Count;

} LANStringSpan;

LANString* LANMakeStringOfLength(size_t count);
LANString* LANMakeString(const char* cstring);
void LANFreeString(LANString** instRef);

size_t LANString_Count(const LANString* inst);
int LANString_Equals(const LANString* inst, const LANString* other);
int LANString_EqualsSpan(const LANString* inst, LANStringSpan other);
size_t LANString_HashCode(const LANString* inst);
char* LANString_Chars(const LANString* inst);
char* LANString_At(const LANString* inst, size_t index);
size_t LANString_WriteChars(const LANString* inst, size_t destOffset, const char* chars);
size_t LANString_Write(LANString* inst, size_t destOffset, const LANString* chars, size_t srcOffset);

int LANStringSpan_Equals(LANStringSpan inst, LANStringSpan other);

typedef struct LANStringArray LANStringArray;

LANStringArray* LANMakeStringArray(size_t count);
void LANFreeStringArray(LANStringArray** instRef);

size_t LANStringArray_Count(LANStringArray* inst);
LANString** LANStringArray_At(LANStringArray* inst, size_t index);