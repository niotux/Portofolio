# GameOfLife

Jocul vieții al lui Conway, implementat în Lua și LÖVE. Desenează celule vii pe o grilă, apoi avansează simularea pentru a urmări cum tiparele cresc, se deplasează sau dispar în funcție de celulele vecine.

## Rulare

Necesită LÖVE. Din acest director:

```bash
love .
```

Grila pornește goală.

| Comandă | Acțiune |
| --- | --- |
| Menține apăsat butonul stâng al mouse-ului | Desenează celule vii |
| Menține apăsată tasta W | Avansează generațiile |

Eliberează W pentru a întrerupe simularea și a modifica tiparul. Grila are implicit 100 × 100 de celule; modifică `xSize` și `ySize` în [main.lua](main.lua) pentru a-i ajusta dimensiunile.

[Înapoi la portofoliu](../../README.md)
