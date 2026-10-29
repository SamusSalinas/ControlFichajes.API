---
name: add-service
description: Crea un nuevo servicio en la API siguiendo la inyección de dependencias del proyecto.
---

# Add Service

1. Crea la interfaz `I<Nombre>Service.cs` en `ControlFichajes.API/Services/`.
2. Crea la implementación `<Nombre>Service.cs` en la misma carpeta.
3. Asegúrate de inyectar dependencias (como el `AppDbContext` de la carpeta `Data/`) a través del constructor.
4. Abre `ControlFichajes.API/Program.cs` y registra la nueva interfaz e implementación en el contenedor de inyección de dependencias.
