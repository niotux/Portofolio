#include "String.h"
#include "NullChecks.h"

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

struct LANString
{
  size_t Count;
};

struct LANStringArray
{
  size_t Count;
};

static inline LANString** LANStringArray_Data(LANStringArray* inst)
{
  return (LANString**)((char*)inst + sizeof(LANStringArray));
}

LANString* LANMakeStringOfLength(size_t count)
{
  LANString* inst = calloc(1, sizeof(LANString) + sizeof(char) * (count + 1));
  EXIT_ON_NULL(inst);
  inst->Count = count;
  return inst;
}

LANString* LANMakeString(const char* cstring)
{
  EXIT_ON_NULL(cstring);
  size_t count = strlen(cstring);
  LANString* inst = malloc(sizeof(LANString) + sizeof(char) * count + 1);
  EXIT_ON_NULL(inst);
  inst->Count = count;
  memcpy(LANString_Chars(inst), cstring, count);
  return inst;
}

void LANFreeString(LANString** instRef)
{
  EXIT_ON_NULL_VOID(instRef);
  EXIT_ON_NULL_VOID(*instRef);
  free(*instRef);
  *instRef = NULL;
}

size_t LANString_Count(const LANString* inst)
{
  return inst->Count;
}

int LANString_Equals(const LANString* inst, const LANString* other)
{
  if (inst == other)
  {
    return 1;
  }
  if (inst == NULL || other == NULL)
  {
    return 0;
  }
  if (inst->Count != other->Count)
  {
    return 0;
  }
  return memcmp(LANString_Chars(inst), LANString_Chars(other), inst->Count) == 0;
}

int LANString_EqualsSpan(const LANString* inst, LANStringSpan other)
{
  EXIT_ON_NULL_INT(inst);
  EXIT_ON_NULL_INT(other.Chars);
  if (inst->Count != other.Count)
  {
    return 0;
  }
  return memcmp(LANString_Chars(inst), other.Chars, other.Count) == 0;
}

int LANStringSpan_Equals(LANStringSpan inst, LANStringSpan other)
{
  EXIT_ON_NULL_INT(other.Chars);
  if (inst.Count != other.Count)
  {
    return 0;
  }
  return memcmp(inst.Chars, other.Chars, inst.Count) == 0;
}

size_t LANString_HashCode(const LANString* inst)
{
  uint64_t hash = 1469598103934665603ULL;
  const char* bytes = LANString_Chars(inst);
  for (size_t i = 0; i < inst->Count; i++)
  {
    hash ^= (unsigned char)bytes[i];
    hash *= 1099511628211ULL;
  }
  return (size_t)hash;
}

char* LANString_Chars(const LANString* inst)
{
  return (char*)inst + sizeof(LANString);
}

char* LANString_At(const LANString* inst, size_t index)
{
  return &LANString_Chars(inst)[index];
}

size_t LANString_WriteChars(const LANString* inst, size_t destOffset, const char* chars)
{
  if (chars == NULL)
  {
    return 0;
  }
  size_t len = strlen(chars);
  memcpy(LANString_Chars(inst) + destOffset, chars, len);
  return len;
}

size_t LANString_Write(LANString* inst, size_t destOffset, const LANString* src, size_t srcOffset)
{
  if (srcOffset >= src->Count || destOffset >= inst->Count)
  {
    return 0;
  }
  size_t destLen = inst->Count - destOffset;
  size_t srcLen = src->Count - srcOffset;
  size_t len = destLen > srcLen ? srcLen : destLen;
  memcpy(LANString_Chars(inst) + destOffset, LANString_Chars(src) + srcOffset, len);
  return len;
}

LANStringArray* LANMakeStringArray(size_t count)
{
  LANStringArray* inst = calloc(1, sizeof(LANStringArray) + sizeof(LANString*) * count);
  if (inst == NULL)
  {
    return NULL;
  }
  inst->Count = count;
  return inst;
}

void LANFreeStringArray(LANStringArray** instRef)
{
  if (instRef == NULL || *instRef == NULL)
  {
    return;
  }
  LANStringArray* inst = *instRef;
  LANString** slots = LANStringArray_Data(inst);
  for (size_t i = 0; i < inst->Count; i++)
  {
    if (slots[i] != NULL)
    {
      LANFreeString(&slots[i]);
    }
  }
  free(inst);
  *instRef = NULL;
}

size_t LANStringArray_Count(LANStringArray* inst)
{
  return inst->Count;
}

LANString** LANStringArray_At(LANStringArray* inst, size_t index)
{
  return &LANStringArray_Data(inst)[index];
}
