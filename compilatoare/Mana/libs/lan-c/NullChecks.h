#pragma once

#define EXIT_ON_NULL_VOID(value) \
  if ((value) == NULL)           \
  {                              \
    return;                      \
  }

#define EXIT_ON_NULL(value) \
  if ((value) == NULL)      \
  {                         \
    return NULL;            \
  }

#define EXIT_ON_NULL_INT(value) \
  if ((value) == NULL)          \
  {                             \
    return 0;                   \
  }

#define EXIT return