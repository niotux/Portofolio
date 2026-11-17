# FirstOpenGL

Un experiment timpuriu în C++ și OpenGL 3.3 pentru construirea componentelor unei aplicații 3D: fereastră, deplasarea camerei, programe shader, texturi, materiale și încărcarea modelelor.

Codul de control include deplasarea liberă a camerei și rotația în jurul axei sale longitudinale. Configurarea scenei este încă neterminată, așa că proiectul este un experiment de randare, nu un demo care poate fi jucat.

## Compilare

Necesită Windows, Visual Studio cu setul de instrumente C++ v143 și GLFW.

1. Deschide `OpenGL.sln`.
2. Adaugă o versiune compatibilă a bibliotecii `glfw3.lib` în `OpenGL/Dependencies/lib` sau actualizează calea bibliotecii în setările proiectului.
3. Compilează proiectul OpenGL.

Sunt incluse sursele GLAD și fișierele antet GLFW/GLM. Aplicația folosește căi relative pentru resurse, deci ruleaz-o cu directorul `OpenGL` drept director de lucru.

## Comenzi

Codul de control al camerei folosește WASD pentru deplasare, mouse-ul pentru orientare, Q/E pentru rotația în jurul axei longitudinale și Shift/Ctrl pentru ajustarea vitezei. Inițializarea camerei și scena mai necesită lucru înainte ca aceste comenzi să poată fi folosite într-un demo complet.

[Înapoi la portofoliu](../../README.md)
