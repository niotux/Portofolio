# Scripturi de configurare IntelliSense

Aceste scripturi configurează IntelliSense din VS Code pentru diverse API-uri grafice și platforme.

## Utilizare

Rulează scriptul corespunzător platformei tale și API-ului grafic dorit:

### Windows

```bash
# Configurare pentru Vulkan
.\setup-scripts\setup-vulkan-windows.bat

# Configurare pentru OpenGL
.\setup-scripts\setup-opengl-windows.bat

# Configurare pentru DirectX 12
.\setup-scripts\setup-directx12-windows.bat
```

### Linux

```bash
# Configurare pentru Vulkan
./setup-scripts/setup-vulkan-linux.sh

# Configurare pentru OpenGL
./setup-scripts/setup-opengl-linux.sh
```

### macOS

```bash
# Configurare pentru Vulkan
./setup-scripts/setup-vulkan-macos.sh

# Configurare pentru OpenGL
./setup-scripts/setup-opengl-macos.sh

# Configurare pentru Metal
./setup-scripts/setup-metal-macos.sh
```

## După rulare

1. **Reîncarcă VS Code:** apasă `Ctrl+Shift+P` sau `Cmd+Shift+P` pe macOS și execută comanda `Developer: Reload Window`.
2. **Editează `renderer.config.json`** pentru a activa API-ul dorit, setând `"enabled": true`.
3. **Recompilează proiectul:** rulează scriptul de compilare, de exemplu `.\build-scripts\build-windows.bat`.
4. IntelliSense va folosi configurația aleasă, eliminând erorile de configurare marcate în editor.

## Cerințe

- **Vulkan:** variabila de mediu `VULKAN_SDK` trebuie să fie setată.
- **OpenGL:** este de obicei disponibil la nivelul sistemului.
- **DirectX 12:** este inclus în SDK-ul Windows.
- **Metal:** este disponibil doar pe macOS și este inclus în Xcode.

## Observații

- Scripturile configurează **doar IntelliSense**; nu modifică `renderer.config.json`.
- Pentru a schimba API-ul compilat, editează manual `renderer.config.json` și setează câmpul `"enabled"` al API-ului dorit la `true`.
- Poți schimba configurația IntelliSense pentru un alt API rulând scriptul corespunzător.
- Reîncarcă VS Code după rularea unui script de configurare.
