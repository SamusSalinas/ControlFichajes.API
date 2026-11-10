---

trigger: always_on[cite: 2]
trigger: glob[cite: 2]
globs: "ControlFichajes.API/Controllers/\*.cs"[cite: 2]
description: "Reglas de seguridad para nuevos controladores y endpoints."[cite: 2]

# Políticas de Seguridad[cite: 2]

- Todo endpoint debe considerar el contexto de la empresa. Revisa y respeta la implementación de `EmpresaContextMiddleware` en la carpeta `Security/`[cite: 2].
- Utiliza las reglas de identidad definidas en `UsuarioIdentidadRules.cs` y controla los accesos utilizando `UsuarioAdministracionAccess.cs`[cite: 2].
- Los roles de aplicación están en `Constants/AppRoles.cs`. No inventes roles nuevos ni quemes strings en el código[cite: 2].
