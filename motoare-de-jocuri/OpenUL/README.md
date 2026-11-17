# OpenUL

O colecție de biblioteci C# pentru experimente cu aplicații și grafică: ferestre native, încărcarea și salvarea imaginilor, sunet, matematică, gestionarea resurselor și date cu numărarea referințelor.

## Rulează exemplul

Necesită SDK-ul .NET 10 și un mediu grafic în care pot fi deschise ferestre GLFW. Din acest director:

```bash
dotnet run --project Game
```

Exemplul creează o imagine numită `Mandelbrot.png`, deschide două ferestre și pornește o consolă interactivă care oferă acces la obiectele ferestrelor. Codul actual de generare a imaginii desenează o diagonală cu gradient; un generator Mandelbrot separat este disponibil în `Game/Mandelbrot.cs`.

## Pachete

- `OpenUL.Windowing` definește API-ul ferestrelor; `OpenUL.Windowing.GLFW` oferă implementarea bazată pe GLFW.
- `OpenUL.Assets.Media` și pachetele asociate gestionează imaginile și datele multimedia.
- `OpenUL.Mathematics` oferă tipuri vectoriale.
- `OpenUL.Utility.Collections` și `OpenUL.Utility.RefCounted` oferă structuri de date auxiliare.
- `OpenUL.Manshell` oferă consola interactivă folosită de exemplu.

Pachetele sunt experimentale. `OpenUL.Graphics.Vulkan` este doar o structură preliminară, fără un modul de randare Vulkan implementat.

[Portofoliu](../../README.md)
