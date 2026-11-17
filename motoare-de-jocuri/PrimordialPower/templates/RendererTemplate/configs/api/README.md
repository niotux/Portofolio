# Fișierele de configurare a API-urilor grafice

Acest director conține configurația centralizată pentru fiecare API grafic acceptat.

## Formatul fișierelor

Fiecare fișier JSON definește:

- **api:** numele API-ului.
- **defines:** definițiile de preprocesor pentru API.
- **include_paths:** directoarele de includere specifice platformei.
- **libraries:** bibliotecile care trebuie legate pe fiecare platformă.
- **frameworks:** bibliotecile de sistem care trebuie legate doar pe macOS.

## API-uri acceptate

- `vulkan.json`: Vulkan.
- `opengl.json`: OpenGL.
- `directx12.json`: DirectX 12, doar pe Windows.
- `metal.json`: Metal, doar pe macOS.

## Utilizare

Fișierele de configurare sunt folosite de:

1. **Sistemul de compilare CMake**, pentru configurarea căilor de includere și legarea bibliotecilor.
2. **Scripturile de configurare**, pentru configurarea IntelliSense din VS Code, în `setup-scripts/`.

## Adăugarea unui API nou

1. Creează un fișier JSON nou: `your-api.json`.
2. Definește căile de includere și bibliotecile pentru fiecare platformă.
3. Actualizează `renderer.config.json` pentru a include API-ul nou.
4. Creează scripturile corespunzătoare în `setup-scripts/`.
5. Actualizează modulele CMake din `cmake/`.

## Exemplu de structură

```json
{
  "api": "example",
  "defines": [
    "USE_EXAMPLE",
    "RENDERER_API_EXAMPLE"
  ],
  "include_paths": {
    "windows": ["C:/Path/To/SDK/include"],
    "linux": ["/usr/include/example"],
    "macos": ["/usr/local/include/example"]
  },
  "libraries": {
    "windows": ["example.lib"],
    "linux": ["example"],
    "macos": ["example"]
  }
}
```
