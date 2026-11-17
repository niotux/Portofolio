#include "Standard001.h"

extern void* _ = null;

void* New(USize size)
{
  return malloc(size);
}

void Dispose(void** instRef)
{
  if (instRef == null)
  {
    return;
  }
  if (*instRef == null)
  {
    return;
  }
  free(*instRef);
  *instRef = null;
}