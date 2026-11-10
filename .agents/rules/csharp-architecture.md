---

trigger: always*on[cite: 2]
trigger: glob[cite: 2]
globs: "ControlFichajes.API/\**/\_.cs"[cite: 2]
description: "Estándares de arquitectura para la API de Control de Fichajes."[cite: 2]

# Arquitectura de la API[cite: 2]

- Los modelos de entidad (base de datos) DEBEN ir en la carpeta `Models/` (ej. `Empleado.cs`, `Fichada.cs`)[cite: 2].
- Para la transferencia de datos en los endpoints, usa EXCLUSIVAMENTE objetos de la carpeta `DTOs/` (ej. `EmpleadoDto`, `SucursalDto`)[cite: 2].
- Toda lógica de negocio debe residir en la carpeta `Services/`, utilizando interfaces (ej. `IAuthService`, `IEmpleadoService`)[cite: 2].
- El acceso a datos se hace a través del `AppDbContext` ubicado en la carpeta `Data/`[cite: 2].
