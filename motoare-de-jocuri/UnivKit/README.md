# UnivKit

Un set de biblioteci C# pentru aplicații care au nevoie de ferestre native, sunet, gestionarea resurselor și control explicit asupra duratei de viață a serviciilor. Separă codul aplicației de implementările specifice platformei, cu suport pentru ferestre Win32 și X11.

## Compilare

Necesită SDK-ul .NET 10. Din acest director:

```bash
dotnet build src/UnivKit/UnivKit.csproj
```

## Exemplu de fereastră

Exemplul de dezvoltare pornește o gazdă de aplicație, adaugă serviciul de ferestre și deschide o fereastră nativă:

```bash
dotnet run --project tests/Standard.Tests.DevExp
```

Rulează exemplul într-o sesiune grafică, cu bibliotecile native necesare implementării alese. Închiderea ferestrei oprește gazda aplicației.

## Utilizarea bibliotecilor

Începe cu `UnivKit.Application` pentru găzduirea serviciilor și cu `UnivKit.OS.Windowing` pentru ferestre și evenimente. Celelalte pachete oferă funcționalități pentru sunet, resurse și memorie negestionată. Proiectul agregator `UnivKit` include referințe către componentele principale.

Este un cadru experimental. Unele pachete sunt incomplete; de exemplu, `UnivKit.GUI` conține în prezent doar structura interfețelor, nu un set complet de componente grafice.

[Portofoliu](../../README.md)
