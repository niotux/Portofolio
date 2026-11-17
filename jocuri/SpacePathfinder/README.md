# SpacePathfinder

Un prototip de navigare 3D în Lua, construit cu LÖVE. Deplasează-te printr-o hartă de voxeli afișată prin proiecție în perspectivă, cu obiectele desenate prin muchii și verificări de coliziune cu lumea. Proiectul include și experimente ulterioare de randare, precum și un editor simplu de lumi.

## Joacă

Necesită LÖVE. Din acest director:

```bash
love 0.1.1
```

Folosește **WASD** pentru deplasare și **tastele săgeți** pentru rotirea camerei. Versiunea 0.1.1 este punctul de pornire recomandat pentru explorarea lumii.

## Versiuni

| Director | Conținut |
| --- | --- |
| [0.1.0](0.1.0) | Experiment anterior cu o hartă desenată prin muchii și coliziuni |
| [0.1.1](0.1.1) | Demo de navigare pe o hartă de voxeli |
| [0.2.0](0.2.0) | Rescriere a randării software de triunghiuri |
| [0.2.1](0.2.1) | Rescriere a randării pereților proiectați |
| [WorldEditor](WorldEditor) | Editor de vârfuri și pereți |

Versiunile 0.2 sunt experimente de randare mai restrânse, nu jocuri mai complete. Rotația este incompletă în 0.2.1, iar sortarea fețelor este încă neterminată în implementarea anterioară.

## Editor de lumi

```bash
love WorldEditor
```

Clicul stânga adaugă sau elimină vârfuri, clicul dreapta selectează vârfuri, iar menținerea tastei **F** apăsate unește vârfurile selectate printr-un perete. Salvarea nu este implementată.

[Înapoi la portofoliu](../../README.md)
