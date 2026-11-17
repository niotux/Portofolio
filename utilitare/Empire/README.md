# Empire

Empire este un instrument de compilare din linia de comandă pentru proiecte cu mai multe ținte și dependențe. Păstrează configurațiile de compilare în JSON și oferă un format comun de comenzi pentru proiecte C, C# și proiecte care reunesc mai multe ținte.

Poate compila executabile și biblioteci, poate compila dependențele înaintea proiectelor care le folosesc, poate gestiona configurații debug/release și poate modifica setările țintelor din terminal. Instrumentele și șabloanele sunt definite prin module, fără a fi fixate în codul interfeței din linia de comandă.

## Primii pași

Necesită Python 3. Modulele de compilare incluse au nevoie și de instrumentele corespunzătoare: GCC și `ar` pentru C, respectiv .NET SDK pentru C#. Scripturile incluse folosesc Bash și utilitare Unix.

Din acest director, poți consulta comenzile disponibile:

```bash
python3 empire.py --help
python3 empire.py c --help
python3 empire.py csharp --help
```

Pentru a face comanda `empire` disponibilă în PATH:

```bash
bash installer.sh install
```

Instalarea creează o legătură în `/usr/local/bin` și solicită sudo pentru acest pas. Păstrează directorul Empire în aceeași locație după instalare. Folosește `bash installer.sh uninstall` pentru a elimina legătura.

## Compilarea unui proiect

Rulează Empire din directorul proiectului pe care vrei să îl compilezi. Pentru un proiect C existent, cu fișierele `.c` în acel director:

```bash
empire c init
empire c build -exe debug
empire c build -exe release
```

`init` creează un fișier `target.c.json` cu setările pentru surse, rezultate și configurații. Pentru proiectele cu sursele în `src`, folosește `empire c init -t classic`. Compilarea citește fișierul țintei din directorul curent.

Alte exemple:

```bash
empire c build -dylib release
empire csharp build -lib Debug
empire target info
empire target ls-deps
```

Fișierele țintelor pot declara dependențe și comenzile necesare pentru compilarea lor. Consultă [documentația modulelor](modules/readme.md) pentru câmpurile țintelor, variabilele de mediu și setările dependențelor.

## Dezvoltare

Realizat integral prin vibe coding. Am creat Empire pentru propriile proiecte și îl folosesc ca instrument de compilare.

[Înapoi la portofoliu](../../README.md)
