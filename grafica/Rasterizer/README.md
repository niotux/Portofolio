# Rasterizer

Un rasterizator software de triunghiuri scris în Lua. Desenează triunghiuri colorate pixel cu pixel și folosește un buffer de adâncime pentru a stabili ce triunghi este vizibil în zonele de suprapunere.

## Rulare

Necesită LÖVE. Din acest director:

```bash
love Version3
```

Demo-ul randează o scenă fixă și nu are comenzi interactive. Modifică pozițiile triunghiurilor, adâncimile și culorile vârfurilor în `Version3/main.lua` pentru a experimenta cu randarea.

## Versiuni

| Versiune | Funcționalitate |
| --- | --- |
| [Version1](Version1) | Prima implementare a desenării triunghiurilor pe linii de scanare |
| [Version2](Version2) | Rasterizarea triunghiurilor, revizuită |
| [Version3](Version3) | Interpolarea culorilor și buffer de adâncime |

Fiecare versiune poate fi lansată cu `love Version1`, `love Version2` sau `love Version3`. Version3 este cel mai bun punct de pornire. Este un experiment de randare cu limite fixe ale suprafeței de desenare, nu un motor 3D de uz general.

[Înapoi la portofoliu](../../README.md)
