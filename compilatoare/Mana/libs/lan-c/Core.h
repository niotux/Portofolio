#pragma once
#include <stdint.h>

typedef enum
{
  Equal = 0,
  Greater = 1,
  Less = -1,

} LANComparaison;

typedef struct
{
  const uint8_t Minor;
  const uint8_t Major;
  const uint16_t Patch;

} LANVersion;

LANComparaison LANVersion_Compare(LANVersion v1, LANVersion v2);

extern const LANVersion LANCurrentLanguageVersion;
extern const LANVersion LANCurrentBytecodeVersion;

typedef enum
{
  LANArch_32Bit,
  LANArch_64Bit,

} LANArch;

extern const LANArch LANCurrentArch;