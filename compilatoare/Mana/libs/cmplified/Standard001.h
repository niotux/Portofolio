#pragma once;
#include <stdint.h>
#include <stdlib.h>

typedef uint8_t Bool;
typedef uint8_t Byte;
typedef int8_t SByte;
typedef uint16_t UShort;
typedef int16_t Short;
typedef uint32_t UInt;
typedef int32_t Int;
typedef uint64_t ULong;
typedef int64_t Long;
typedef intptr_t Size;
typedef uintptr_t USize;
typedef char Char;
typedef void Void;

typedef void (*FAction)();

void* New(USize size);
void Dispose(void** instRef);

extern void* _;

#define new(Type, ...)   \
  _ = New(sizeof(Type)); \
  New##Type((Type*)_, ##__VA_ARGS__)

#define alloc(inst, Type, ...) \
  Type* inst = New(sizeof(Type));

#define dispose(inst) \
  (Dispose(&inst), Dispose##Type(inst))

#define free(inst) \
  Dispose(&inst)

#define class(Name) \
  typedef struct Name Name

#define callback(Name, Ret, ...) \
  typedef Ret (*Name)(##__VA_ARGS__)

#define ctor(Type, ...) \
  void New##Type(Type* inst, ##__VA_ARGS__)

#define dtor(Type) \
  void Dispose##Type(Type* inst)

#define method(Ret, Type, Name, ...) \
  Ret Type##_##Name(Type* inst, ##__VA_ARGS__)

#define call(Type, inst, Name, ...) \
  Type##_##Name(inst, ##__VA_ARGS__)

#define true 1
#define false 0
#define null 0
#define inst inst