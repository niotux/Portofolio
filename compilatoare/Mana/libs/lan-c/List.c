#include "List.h"
#include "NullChecks.h"

#include <stdlib.h>

#define ListInitialCapacity 4
#define ListGrowthFactor 2

struct LANList
{
  size_t Count;
  size_t Capacity;
  void** Items;
};

LANList* LANMakeList()
{
  LANList* inst = malloc(sizeof(LANList));
  EXIT_ON_NULL(inst);

  inst->Items = malloc(sizeof(void*) * ListInitialCapacity);
  if (inst->Items == NULL)
  {
    free(inst);
    return NULL;
  }

  inst->Count = 0;
  inst->Capacity = ListInitialCapacity;
  return inst;
}

void LANFreeList(LANList** instRef)
{
  EXIT_ON_NULL_VOID(instRef);
  EXIT_ON_NULL_VOID(*instRef);

  free((*instRef)->Items);
  free(*instRef);
  *instRef = NULL;
}

void LANList_Push(LANList* inst, void* item)
{
  if (inst->Count >= inst->Capacity)
  {
    inst->Capacity *= ListGrowthFactor;
    inst->Items = realloc(inst->Items, sizeof(void*) * inst->Capacity);
  }

  inst->Items[inst->Count] = item;
  inst->Count++;
}

void* LANList_At(LANList* inst, size_t index)
{
  return inst->Items[index];
}

size_t LANList_Count(LANList* inst)
{
  return inst->Count;
}

void LANList_Clear(LANList* inst)
{
  inst->Count = 0;
}

void LANList_FreeElements(LANList* inst)
{
  for (size_t i = 0; i < inst->Count; i++)
  {
    free(LANList_At(inst, i));
  }
}