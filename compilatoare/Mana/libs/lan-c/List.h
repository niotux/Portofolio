#pragma once
#include <stddef.h>

// A growable list of borrowed pointers.
//
// The list owns its internal storage but NOT the items it points to — freeing
// the list frees the array, never the elements. The handle is stable: only the
// internal storage is reallocated on growth, so a LANList* may be stored and
// passed around freely without being invalidated when the list grows.

typedef struct LANList LANList;

LANList* LANMakeList();
void LANFreeList(LANList** instRef);

void LANList_Push(LANList* inst, void* item);
void* LANList_At(LANList* inst, size_t index);
size_t LANList_Count(LANList* inst);

// Reset to empty while keeping the allocated storage for reuse.
void LANList_Clear(LANList* inst);
void LANList_FreeElements(LANList* inst);
