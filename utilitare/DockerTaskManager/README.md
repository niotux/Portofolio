# DockerTaskManager

DockerTaskManager este un panou de control în terminal pentru containere și proiecte Compose din contextele tale Docker. Vezi ce rulează, urmărești consumul de resurse, citești jurnalele containerelor și pornești, oprești sau repornești serviciile din aceeași interfață.

Containerele sunt grupate după proiectul Compose. Comutatorul unui proiect controlează întregul grup, iar containerele individuale pot fi gestionate separat. Panoul folosește Docker CLI și descoperă contextele prin `docker context ls`.

## Rulare

Necesită .NET 10 SDK, comanda `docker` disponibilă în PATH și acces la contextele Docker pe care vrei să le gestionezi. Din acest director:

```bash
dotnet run --project src/Dtm
```

## Instalare

```bash
./install.sh
dtm
```

Programul de instalare plasează aplicația în `~/.local/share/dtm` și adaugă comanda în `~/.local/bin/dtm`. Asigură-te că `~/.local/bin` este inclus în PATH. Rulează din nou programul de instalare pentru actualizare sau `./uninstall.sh` pentru dezinstalare. Setează `DTM_HOME` și `DTM_BIN` pentru a schimba directoarele de instalare.

## Utilizare

Apasă un comutator pentru a porni sau opri un container ori un proiect. Extinde rândul unui proiect Compose pentru a vedea containerele sale. Apasă titlul unei coloane pentru sortare sau fă dublu clic pe un container pentru a-i deschide jurnalul.

| Acțiune | Tastă |
| --- | --- |
| Navigare | Săgeți, Page Up, Page Down |
| Extinde sau restrânge un proiect | Right sau Left |
| Comută între pornire și oprire | Space |
| Extinde un proiect sau deschide jurnalul | Enter |
| Imagini | F2 |
| Jurnale | F3 |
| Filtrare | F4 sau `/` |
| Pornire, oprire, repornire | F5, F6, F7 |
| Ștergere | F8 sau Delete |
| Afișează doar containerele active sau toate containerele | F9 |
| Ajutor | F1 |
| Ieșire | F10 sau Q |

Ștergerea solicită confirmare. Ștergerea unui proiect Compose execută `docker compose down` pentru acel proiect.

## Dezvoltare

Realizat integral prin vibe coding. Am creat DockerTaskManager pentru a gestiona serviciile Docker pe care le folosesc personal.

Rulează testele cu `dotnet test`.

[Înapoi la portofoliu](../../README.md)
