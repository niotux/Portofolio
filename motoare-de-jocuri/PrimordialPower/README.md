# PrimordialPower

Un motor în C# care explorează integrarea serviciilor aplicației, a ferestrelor native, a sunetului și a randării. Include un mediu de execuție, servicii specifice platformei, un modul de randare Vulkan numit Obsidian, instrumente de inspecție și o consolă REPL.

## Primii pași

Necesită SDK-ul .NET 8. Din acest director, compilează biblioteca mediului de execuție:

```bash
dotnet build Packages/PrimordialPower/PrimordialPower.Runtime/PrimordialPower.Runtime.csproj
```

Mediul de execuție gestionează serviciile aplicației și durata lor de viață. [DemoPrime](Projects/DemoPrime/DemoPrime) arată cum o aplicație adaugă servicii de afișare și sunet înainte de a porni serverul principal. Folosește căi relative pentru fișierele multimedia și implementări native specifice platformei, deci rularea demonstrației necesită și bibliotecile corespunzătoare pentru interfața grafică și sunet.

Obsidian compilează o bibliotecă nativă prin CMake; compilarea necesită și un set de instrumente C++, împreună cu generatorul CMake configurat.

## Ce poți explora

- `Packages/PrimordialPower`: implementările mediului de execuție, serviciilor de platformă, randării și consolei REPL.
- `Packages/PrimordialInspector`: instrumente de inspecție și scripting.
- `Programs/PrimordialPower.Lab`: experimente cu motorul.
- `templates`: puncte de pornire pentru jocuri, pachete și module de randare.

O arhitectură mai veche se găsește în `Packages/PrimordialPower-old`. Motorul este experimental, iar pachetele se află în etape diferite de dezvoltare.

[Portofoliu](../../README.md)
