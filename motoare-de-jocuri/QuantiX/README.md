# QuantiX

Un motor de jocuri în C#, cu o interfață de randare independentă de implementare, un modul OpenGL, ferestre native și biblioteci pentru sunet, resurse și matematică. Demonstrațiile includ un joc de tip FPS cu valuri de inamici, o lume din voxeli și un experiment cu portaluri.

API-ul de randare separă bufferele, texturile, shaderele, zonele de afișare și operațiile de desenare de implementarea grafică. Quartz implementează acest API folosind OpenGL, iar Glyph explorează randarea într-o fereastră de consolă.

## Compilare

Necesită SDK-ul .NET 8. Din acest director:

```bash
dotnet build src/QuantiX.Engine/QuantiX.Engine.csproj
```

Demonstrațiile grafice necesită și OpenGL, împreună cu bibliotecile native pentru interfața grafică și sunet folosite de platformă. Implementările pentru Linux folosesc X11 și ALSA.

## Încearcă demonstrațiile

```bash
dotnet run --project tests/QuantiX.Engine.Tests.FPS
dotnet run --project tests/QuantiX.Engine.Tests.Rendering
dotnet run --project tests/QuantiX.Engine.Tests.Portal
```

Jocul FPS, **Breach Protocol**, include valuri de inamici, arme, reîncărcare și un magazin disponibil între valuri. Citește comenzile de la tastatură din terminalul din care a fost pornit, deci păstrează acel terminal activ.

| Comandă | Acțiune |
| --- | --- |
| WASD | Deplasare |
| Spațiu | Săritură |
| IJKL sau săgețile | Orientarea privirii |
| F / R | Tragere / reîncărcare |
| 1 / 2 / 3 | Alegerea armei |
| B | Deschiderea magazinului între valuri |
| Enter | Selectarea unei opțiuni din meniu |
| H | Afișarea comenzilor |
| Q sau Escape | Înapoi sau ieșire |

Acestea sunt demonstrații și experimente ale motorului, nu jocuri finalizate.

## Dezvoltare

Am proiectat cadrul de randare, apoi am realizat complet prin vibe coding modulul OpenGL și toate jocurile demonstrative bazate pe acesta.

[Portofoliu](../../README.md)
