# PrimordialPowerCpp

Un motor experimental în C++ și OpenGL, construit în jurul unei scene 3D, al unei camere mobile și al unor clase reutilizabile pentru geometrii, materiale, texturi și shadere. Scena demonstrativă creează un obiect texturat în jurul căruia te poți deplasa.

## Compilare și rulare

Necesită Windows, Visual Studio 2022 cu instrumentele C++, o placă video compatibilă cu OpenGL și GLFW.

1. Deschide `Promordial Power.sln`.
2. Adaugă biblioteca GLFW la calea așteptată, `Promordial Power/resources/lib/glfw/glfw3.lib`, sau actualizează setările bibliotecilor din proiect.
3. Alege `Promordial Power` ca director de lucru, pentru ca fișierele folosite de aplicație să poată fi găsite prin căile relative.
4. Compilează și pornește aplicația.

Motorul este un prototip neterminat. Scena demonstrativă folosește `assets/shaders/planetShader.fs.glsl`, care lipsește; pentru afișarea corectă a scenei este necesar un shader de fragmente compatibil.

## Controlul camerei

- WASD deplasează camera.
- Poziția mouse-ului față de centrul ferestrei controlează înclinarea și orientarea camerei.
- Q/E rotește camera în jurul propriei axe de privire.

[Portofoliu](../../README.md)
