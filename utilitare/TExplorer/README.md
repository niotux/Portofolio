# TExplorer

TExplorer este un manager de fișiere pentru Linux care rulează în terminal. Poți naviga prin directoare în file separate, folosi bara laterală Places, copia și muta fișiere, trimite elemente la coș, crea fișiere din șabloane sau deschide un terminal în directorul curent.

Acceptă atât interacțiunea cu mouse-ul, cât și o linie de comandă: introdu o acțiune precum `mkdir photos`, o cale către un director sau un text pentru a filtra conținutul directorului curent.

## Rulare

Necesită Linux și .NET 10 SDK. Din acest director:

```bash
dotnet run --project src/Texp
```

Pentru a deschide un anumit director:

```bash
dotnet run --project src/Texp -- ~/Downloads
```

## Instalare

```bash
./install.sh
texp
texp ~/Downloads
```

Programul de instalare plasează aplicația în `~/.local/share/texp` și adaugă comanda în `~/.local/bin/texp`. Asigură-te că `~/.local/bin` este inclus în PATH. Rulează din nou programul de instalare pentru actualizare sau `./uninstall.sh` pentru dezinstalare. Setează `TEXP_HOME` și `TEXP_BIN` pentru a schimba directoarele de instalare.

## Comenzi și taste

Comenzile se aplică elementelor marcate sau elementului curent, dacă nu este marcat nimic.

| Acțiune | Tastă |
| --- | --- |
| Marchează un element | Space |
| Selectează tot | Ctrl+A |
| Copiază, decupează, lipește | Ctrl+C, Ctrl+X, Ctrl+V |
| Redenumește | Ctrl+R |
| Director nou | Ctrl+D |
| Fișier nou dintr-un șablon | Ctrl+N |
| Mută la coș | Delete |
| Deschide un terminal aici | Ctrl+E |
| Filă nouă, închide fila | Ctrl+T, Ctrl+W |
| Ajutor | F1 |
| Ieșire | Ctrl+Q |

Linia de comandă acceptă `copy`, `mkdir photos`, `rename report.pdf`, `cd ~/Downloads` și `help`. Tab completează comenzile și căile. Șabloanele provin din `~/Templates`; Places folosește aceleași marcaje ca GNOME Files.

## Dezvoltare

Realizat integral prin vibe coding. Am creat TExplorer ca manager de fișiere pe care îl folosesc personal.

Rulează testele cu `dotnet test`. Comenzi suplimentare pot fi adăugate în `src/Texp/Commands`.

[Înapoi la portofoliu](../../README.md)
