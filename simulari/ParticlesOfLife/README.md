# ParticlesOfLife

O simulare de particule scrisă în Lua cu LÖVE. Trei sute de particule aparțin uneia dintre șase categorii, fiecare cu propriile reguli de atracție și respingere. Interacțiunile lor, gravitația și coliziunile cu pereții produc o mișcare în continuă schimbare.

## Rulare

Necesită LÖVE. Din acest director:

```bash
love .
```

Menține apăsată tasta **W** pentru a atrage particulele spre colțul din stânga sus, originea simulării.

## Experimentează cu regulile

Modifică [main.lua](main.lua) pentru a schimba numărul de particule (`n`) sau matricea de interacțiune de șase pe șase (`rules`). Fiecare rând stabilește modul în care un tip de particulă reacționează la celelalte. Păstrează numărul de particule divizibil cu șase: inițializarea le împarte egal între cele șase tipuri.

Simularea se actualizează în timpul desenării, deci viteza ei depinde de rata de cadre.

[Înapoi la portofoliu](../../README.md)
