# Suport pentru limbajul LAN

Evidențiere minimală a sintaxei limbajului bytecode LAN în VS Code.

## Funcționalități

- Evidențierea codurilor de operație: controlul execuției, operații și declarații globale
- Literali de șir, inclusiv sufixele de codificare (`ascii`, `utf8`, `utf16`, `utf32`, `latin`)
- Literali numerici: întregi, hexazecimali, octali, binari și numere în virgulă mobilă, inclusiv `inf`, `neginf` și `nan`
- Literali booleeni (`true` / `false`)
- Expresii de tip (`.Type`, `.Array[Int]`, `.Int + String`, identificatori separați prin punct)

Extensia oferă doar evidențierea sintaxei, printr-o gramatică TextMate. Nu include un server de limbaj, diagnostic sau IntelliSense.

## Extensia fișierelor

`.lan`

## Exemplu

```
fun .4 Program.Add;
  ldarg .4 left;
  ldarg .4 right;

  add .4 res left right;
  ret res;
```

## Limitări cunoscute

- Doar evidențiere, fără validare semantică
- Gramatica folosește expresii regulate; structurile profund imbricate sau formatarea neobișnuită pot fi analizate incorect

## Contribuții

Aceasta este o extensie mică, pentru uz personal. Poți crea propria variantă și extinde gramatica din `syntaxes/lan.tmLanguage.json`.
