# Mandelbrot

Un explorator interactiv al mulțimii Mandelbrot, scris în Lua cu LÖVE. Deplasează imaginea pe planul complex, mărește fractalul și crește limita de iterații pentru a descoperi mai multe detalii. Imaginea este calculată pe CPU și desenată pe o suprafață de tip canvas.

## Rulare

Necesită LÖVE. Din acest director:

```bash
love .
```

**Apasă Up după lansare**: limita de iterații pornește de la zero, așa că imaginea inițială nu are detalii.

## Comenzi

| Tastă | Acțiune |
| --- | --- |
| W / A / S / D | Deplasează imaginea |
| E / Q | Mărește / micșorează |
| Up / Down | Crește / scade limita de iterații |

Fiecare modificare recalculează imaginea. Limitele mai mari de iterații necesită mai mult timp pentru randare. Menține limita de iterații peste zero în timpul explorării.

[Înapoi la portofoliu](../../README.md)
