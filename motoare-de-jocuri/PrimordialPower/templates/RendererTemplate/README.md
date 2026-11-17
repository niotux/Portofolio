# Randare Vulkan

Un modul de randare Vulkan simplu și eficient, care afișează un triunghi colorat.

## Compilare

### Compilarea shaderelor

Intră în directorul `src` și compilează shaderele GLSL în SPIR-V:

```bash
cd src
glslc triangle.vert.glsl -o triangle_vert.spv
glslc triangle.frag.glsl -o triangle_frag.spv
```

**Notă:** Asigură-te că SDK-ul Vulkan este instalat și că `glslc` se află în PATH.

### Compilarea proiectului

Din directorul VulkanRenderer:

```bash
# Creează directorul de compilare
mkdir build-win64
cd build-win64

# Generează fișierele de compilare
cmake ..

# Compilează
cmake --build . --config Release
```

Biblioteca DLL compilată va fi în `build-win64/bin/VulkanRenderer.dll`.

## Utilizare

Modulul de randare expune trei funcții prin API-ul C:

```cpp
// Inițializează Vulkan folosind datele ferestrei
bool InitVulkan(ViewportData* viewportData);

// Randează un cadru
void Render();

// Eliberează resursele
void Cleanup();
```

### Exemplu în C#

```csharp
[DllImport("VulkanRenderer.dll")]
private static extern bool InitVulkan(ref ViewportData viewportData);

[DllImport("VulkanRenderer.dll")]
private static extern void Render();

[DllImport("VulkanRenderer.dll")]
private static extern void Cleanup();
```

## Funcționalități

- Inițializează Vulkan cu un cost suplimentar minim.
- Creează un lanț de imagini pentru prezentare, numit swapchain.
- Randează un triunghi cu vârfuri roșu, verde și albastru.
- Folosește poziții și culori ale vârfurilor definite direct în shaderul de vârfuri.
- Randează câte un cadru, cu sincronizarea necesară.

## Fișierele shaderelor

- `triangle.vert.glsl`: shader de vârfuri cu pozițiile și culorile triunghiului definite direct în cod.
- `triangle.frag.glsl`: shader de fragmente care produce culorile interpolate ale vârfurilor.
- `triangle_vert.spv`: shaderul de vârfuri compilat, generat la compilare.
- `triangle_frag.spv`: shaderul de fragmente compilat, generat la compilare.

Pune fișierele `.spv` compilate în același director cu executabilul sau specifică o cale completă.
