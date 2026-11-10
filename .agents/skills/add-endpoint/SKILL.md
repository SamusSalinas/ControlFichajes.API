---
name: add-endpoint
description: Agrega un endpoint completo a un controlador existente o crea uno nuevo en la API.
---

# Add Endpoint

1. Verifica si necesitas crear clases DTO para Request/Response en `ControlFichajes.API/DTOs/`.
2. Actualiza la interfaz del servicio correspondiente en `ControlFichajes.API/Services/` e implementa la lógica.
3. Expón el endpoint en el controlador pertinente dentro de `ControlFichajes.API/Controllers/`.
4. Asegúrate de devolver los códigos HTTP correctos (ej. 200 OK, 201 Created, 404 Not Found) y de proteger el endpoint usando las constantes de roles en `Constants/AppRoles.cs`.
