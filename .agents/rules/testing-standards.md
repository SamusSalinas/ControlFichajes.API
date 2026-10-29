---

trigger: always*on[cite: 2]
trigger: glob[cite: 2]
globs: "ControlFichajes.API.Tests/\**/\_.cs"[cite: 2]
description: "Convenciones para el proyecto de pruebas."[cite: 2]

# Convenciones de Testing[cite: 2]

- Todo controlador o servicio modificado debe tener su clase de prueba correspondiente en el proyecto `ControlFichajes.API.Tests/` (ej. siguiendo el patrón de `DepartamentosControllerTests.cs` o `ApiServicesTests.cs`)[cite: 2].
- Se deben mockear las dependencias de la carpeta `Services/` para aislar las pruebas de los controladores[cite: 2].
