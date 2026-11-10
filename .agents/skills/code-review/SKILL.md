---
name: code-review
description: Revisa el diff de código en Pull Requests de ControlFichajes.API aplicando estándares de arquitectura, seguridad y severidad.
---

# Code Review para ControlFichajes.API

Evalúa los cambios introducidos en el diff considerando el contexto arquitectónico del repositorio.

## 1. Alcance
- Ignorar archivos autogenerados (`obj/`, `bin/`, migraciones EF automáticas triviales, dependencias de paquetes NuGet).
- Centrarse en el código de aplicación en `ControlFichajes.API/` y pruebas en `ControlFichajes.Tests/`.

## 2. Clasificación de Severidad
- **Crítico**: Vulnerabilidades de seguridad, exposición de secretos o tokens JWT, bypass de autorización/roles, SQL Injection o corrupción de base de datos.
- **Alto**: Rompimiento de contratos REST existentes, falta de transacciones en operaciones de escritura críticas, inyección incorrecta de dependencias o manejo inadecuado de excepciones no controladas.
- **Medio**: Código duplicado en lógica de negocio, ausencia de validación en DTOs entrantes, códigos de estado HTTP inconsistentes (ej. devolver 200 con cuerpo de error).
- **Bajo**: Mejoras de estilo, nombres de variables poco claros, comentarios redundantes o formatos menores.

## 3. Estándares Arquitectónicos del Proyecto
- **Capas y DI**: Los controladores delegan la lógica a interfaces de servicio (`IServicio`), inyectadas por constructor; no acoplar lógica de negocio directamente dentro de los controllers ni instanciar servicios con `new`.
- **Seguridad**: Proteger endpoints mediante autorización basada en roles usando las constantes de `Constants/AppRoles.cs`.
- **Datos y DTOs**: Las entradas y salidas de la API deben usar DTOs dedicados (`DTOs/`); no exponer entidades de EF Core directamente en las respuestas HTTP.
- **Secretos**: No incluir connection strings, JWT secrets ni credenciales sensibles en el código fuente ni en commits.

