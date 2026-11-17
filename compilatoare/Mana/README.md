# Mana

Un limbaj de programare experimental și un ansamblu de instrumente de compilare care explorează controlul explicit asupra memoriei, tipurilor și operațiilor de nivel jos.

Proiectul are două niveluri de limbaj:

- **Mana** este limbajul de nivel superior, proiectat cu module, clase și control explicit asupra resurselor.
- **Lan**, sau Linear Assembly Notation, este limbajul de nivel inferior folosit pentru a explora instrucțiuni, simboluri, operații de memorie și bytecode.

Implementarea include un analizor lexical și sintactic în C#, instrumente pentru bytecode, un frontend anterior în C și o implementare în C pentru traducerea în LLVM IR. Acestea sunt experimente de compilare; ansamblul nu este încă un compilator complet pentru aplicații.

## Descoperă limbajul

[HelloWorld.mana](languages/Mana/examples/HelloWorld.mana) prezintă sintaxa de nivel superior:

```text
use Base;
use Base.Aliases;

unit HelloWorld;

module App
{
  entry void Main()
  {
    IO.WriteLn("Hello, World!");
  }
}
```

Acesta este un exemplu de limbaj; instrumentele actuale nu îl compilează de la un capăt la altul. [Specificația limbajului Lan](languages/Lan/specs/language-spec.md) și [referința instrucțiunilor](languages/Lan/specs/instructions.md) descriu nivelul inferior.

## Compilează frontendul C#

Ai nevoie de SDK-ul .NET 10. Din acest director:

```bash
dotnet build tools/lanasm/lanasm.csproj
```

Frontendul are momentan erori de compilare în punctul de intrare și în analizorul sintactic, deci trebuie reparat înainte de a putea procesa fișiere. Interfața sa în linie de comandă este concepută să primească un fișier `.lan` și să afișeze instrucțiunile analizate și mesajele de diagnostic.

Frontendul C se află în `libs/lan-c`; `libs/lan2llvm-c` și `tools/lan2llvm` conțin implementarea traducerii în LLVM IR. Configurația lor de compilare necesită și actualizarea căilor către dependențe înainte de utilizare.

[Înapoi la portofoliu](../../README.md)
