# Instrucciones para agentes

Repositorio: `ControlFichajes.API` (API ASP.NET Core). El workspace local también puede contener `control-accesos-frontend`. No hay un repositorio padre.

Antes de cambiar comportamiento, leer en este orden:

1. `docs/ARQUITECTURA.md`
2. `docs/ESTADO_ACTUAL.md`
3. `docs/PENDIENTES.md`
4. `docs/DECISIONES_TECNICAS.md`
5. `README.md` para el contrato HTTP ya documentado

Esos cuatro documentos de `docs/` están copiados en el frontend. Un hecho compartido se actualiza en las dos copias en la misma sesión de trabajo. No reemplazar `README.md` ni los documentos históricos del frontend.

## Límites

- No modificar producción, no desplegar y no hacer merge a `main` salvo pedido explícito.
- No hacer push ni operaciones destructivas de Git salvo pedido explícito.
- No inventar pantallas, endpoints ni un repositorio del lector biométrico: ese código no está en este workspace.
- No copiar secretos, connection strings, JWT, `clientSecret`, plantillas biométricas ni variables `CFB_*` con valores reales.
- La configuración local vive en `appsettings.*.local.json`, ignorado por Git. Usar la plantilla `appsettings.Development.local.json.example`.

## Verificación habitual

```text
dotnet build ControlFichajes.sln --configuration Release
dotnet test ControlFichajes.sln --configuration Release --filter "Category!=LocalTunnelAcceptance&Category!=ProductionAcceptance"
```

Esas pruebas de aceptación escriben datos de un entorno real y quedan fuera de la corrida habitual.
