# Directorul bibliotecilor externe

Pune aici fișierele bibliotecilor externe. CMake le găsește automat și le leagă la proiect.

## Ce face CMake

CMake caută **recursiv** în întregul director `lib/` și:

1. **Leagă toate bibliotecile** găsite:
   - Windows: fișiere `*.lib`.
   - Linux: fișiere `*.a` și `*.so`.
   - macOS: fișiere `*.a` și `*.dylib`.
2. **Copiază bibliotecile DLL**, doar pe Windows:
   - Toate fișierele `*.dll` sunt copiate în directorul de ieșire, lângă modulul de randare compilat.

## Utilizare

Fișierele pot fi puse oriunde în acest director:

```text
lib/
├── mylib.lib
├── mylib.dll
├── some-folder/
│   ├── anotherlib.lib
│   └── headers/
│       └── myheader.h
└── whatever-you-want/
    └── thirdlib.a
```

CMake găsește și leagă automat aceste biblioteci.

## Tipuri de fișiere

| Platformă | Fișiere legate de CMake | Fișiere copiate de CMake |
| --- | --- | --- |
| Windows | `*.lib` | `*.dll` |
| Linux | `*.a`, `*.so` | — |
| macOS | `*.a`, `*.dylib` | — |

## Includerea antetelor

Directorul `lib/` este adăugat automat în căile de includere, deci antetele pot fi incluse direct:

```cpp
// Pentru lib/mylib/myheader.h
#include <mylib/myheader.h>

// Pentru lib/header.h
#include <header.h>
```

Atât CMake, cât și IntelliSense includ `lib/` în căile de căutare a antetelor.

## Observații

- Nu este impusă o structură a directoarelor.
- CMake caută în toate subdirectoarele.
- Legarea este automată, fără modificarea fișierului `CMakeLists.txt`.
- Sunt legate doar tipurile de fișiere potrivite platformei folosite.

## Exemplu

**Windows:**

```text
lib/
├── glfw3.lib
├── glfw3.dll
└── my-libs/
    ├── assimp.lib
    └── assimp.dll
```

CMake leagă `glfw3.lib` și `assimp.lib`, apoi copiază `glfw3.dll` și `assimp.dll` în `build-win-x64/bin/Release/`.

**Linux:**

```text
lib/
├── libglfw3.a
└── dependencies/
    └── libassimp.so
```

CMake leagă `libglfw3.a` și `libassimp.so`.
