# Configurarea modulelor Empire

## Variabile de mediu

Disponibile în toate expandările `$VAR` din câmpurile `Flags` și `Command`:

| Variabilă | Valoare |
| --- | --- |
| `$EMPIRE_OS` | `linux` / `macos` / `windows` |
| `$EMPIRE_ARCH` | `x86_64` / `x86` / `arm64` |
| `$EMPIRE_RID` | `$EMPIRE_OS-$EMPIRE_ARCH` |
| `$EMPIRE_MODULE_DIR` | Calea absolută către directorul modulului |
| `$EMPIRE_CONFIG` | Numele configurației, de exemplu `debug` / `release` |
| `$EMPIRE_TARGET_NAME` | Valoarea câmpului `"Name"` din target.json |
| `$EMPIRE_TARGET_SRC` | Calea absolută către directorul surselor țintei |
| `$EMPIRE_TARGET_OUTBASE` | Calea absolută către `OutputBase` |
| `$EMPIRE_TARGET_OUT` | Calea absolută către `OutputBase/Output` |
| `$EMPIRE_TARGET_ART` | Calea absolută către `OutputBase/Artefacts` |
| `$EMPIRE_FLAGS_CONFIG` | Valoarea câmpului `"Flags"` din configurația activă |
| `$EMPIRE_FLAGS_LINKING` | Opțiunile de legare acumulate din toate dependențele |

## Substituenți pentru legare

Aceștia sunt înlocuiți doar în câmpul `"Linking"` al unei opțiuni de dependență, când dependența este rezolvată pentru un proiect care o folosește:

| Substituent | Valoare |
| --- | --- |
| `{NAME}` | Câmpul `"Name"` al țintei dependenței |
| `{ROOT}` | Directorul rădăcină al proiectului dependenței, unde se află target.json |
| `{SRC}` | Directorul surselor dependenței după rezolvare (`ROOT/Source`) |
| `{OUT}` | Directorul rezultatelor dependenței după rezolvare (`OutputBase/Output`) |

## Câmpurile din target.json

Câmpurile citite de Empire:

| Câmp | Rol |
| --- | --- |
| `Name` | Numele țintei, disponibil în `$EMPIRE_TARGET_NAME` |
| `Source` | Subdirectorul surselor, relativ la rădăcina proiectului |
| `OutputBase` | Directorul de bază pentru rezultate, de exemplu `bin` |
| `Output` | Subdirectorul configurației și al identificatorului de platformă, relativ la `OutputBase` |
| `Artefacts` | Directorul fișierelor intermediare, relativ la `OutputBase` |
| `Configs` | Lista de obiecte `{ "Name", "Flags" }` |
| `Dependencies` | Lista de obiecte `{ "Path", "Module", "On": [{ "Tool", "Option" }] }` |

## Câmpurile opțiunilor și instrumentelor din module.json

Câmpurile citite de Empire:

| Câmp | Unde se folosește | Rol |
| --- | --- | --- |
| `Command` | Instrument | Comanda de bază care va fi executată |
| `Flags` | Opțiune | Se adaugă la `Command` |
| `Linking` | Opțiune | Se introduce în `$EMPIRE_FLAGS_LINKING` al proiectelor care folosesc dependența |
| `Propagate` | Opțiune sau instrument | Compilează mai întâi această dependență, înaintea țintei |
| `CopyOutput` | Opțiune | Copiază rezultatele dependenței în directorul de rezultate al proiectului care o folosește |
| `RequireConfig` | Opțiune sau instrument | Impune transmiterea unui argument de configurație |
| `CreateDirs` | Opțiune sau instrument | Creează directoarele de rezultate și fișiere intermediare înainte de execuție |
