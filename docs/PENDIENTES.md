# Pendientes

Autenticación, 4 de noviembre de 2026. El detalle está en `docs/CONTINUIDAD_AUTENTICACION.md`.

- La etapa 7C.1 enrola MFA de forma voluntaria y emite códigos de recuperación. Queda para revisión, con el enrolamiento deshabilitado por configuración. El frontend sigue en `36dbe061c86035c60eaa79c56d1ebb02ea3e6e26` y no presenta el desafío ni el enrolamiento. No se generó un bundle.
- Siguen pendientes la casilla de exigir MFA, el token de vinculación obligatoria, la desactivación, el reemplazo y el restablecimiento administrativo.
- Los bundles locales de 5A y 5B fueron verificados según las salidas de PowerShell aportadas por el usuario. Las rutas y los hashes están en `docs/CONTINUIDAD_AUTENTICACION.md`. La subida de ambos a Drive sigue pendiente de confirmación.

Hechos abiertos al cierre de la noche del 2 de noviembre de 2026.

- `feat/turnos-asignaciones-ux` no está mergeada a `main` y no se desplegó.
- El proceso de la API que ya estaba en marcha no se reinició. Hasta reiniciarlo, el borrado de turnos puede seguir rechazando asignaciones históricas.
- No se repitió la suite completa de `dotnet test` ni `npm run check` del frontend.

## Relevamiento anterior

Hechos abiertos al 28 de octubre de 2026. No se resolvieron en este relevamiento.

## Publicación

- `feat/turnos-asignaciones-ux` está en los dos remotos y no está mergeada a `main`.
- No se desplegó la API ni el frontend. Un proxy apuntando al servidor ya desplegado puede seguir ejecutando el código de `main` hasta que alguien publique estas ramas.
- Esta documentación nueva está solo en el working tree. No se commiteó ni se hizo push.

## Entorno y pruebas

- `scripts/check-deploy-archive.js` ejecuta `bash -n` sobre el script de publicación. En la corrida del 27 de octubre de 2026, Windows respondió `/bin/bash` con exit 127. No se cambió el script.
- `LocalTunnelBiometricAcceptanceTests` tiene la categoría `LocalTunnelAcceptance`. El comentario de `LocalTunnelAcceptanceFactAttribute` dice que la prueba corre igual, mientras el README de la API dice que está omitida por defecto. Sin `CFB_LOCAL_TEST_API_BASE_URL`, la corrida sin filtro del 27 de octubre falló por esa variable ausente. No se ejecutó en este relevamiento.
- `ProductionBiometricAcceptanceTests` usa el servicio y la base de `.devs`. Queda fuera de la corrida habitual. No se habilitó.

## Código conocido, no corregido aquí

- Una excepción de jornada con horario propio sigue asignando `MinutosAlmuerzo = 60` en `JornadaService`. No es el guardado de `TurnoDia`. No se cambió.
- El parámetro por defecto `MinutosAlmuerzo = 60` de `HorarioPrevisto` tampoco es el guardado del turno.
- `docs/ADAPTACION_FRONTEND_MULTIEMPRESA_P0.md` dice que no había pantallas nuevas de usuarios, empresas, sucursales ni agentes. El código actual de `administracion.js` sí administra agentes. El documento histórico se dejó como estaba.
- El código del lector biométrico no está en el workspace. No hay evidencia aquí de su rama, remoto ni estado.

## Documentación

- Las guías compartidas están duplicadas en los dos repositorios. Hay que editar las dos copias hasta que exista un repositorio común.
- La propuesta de skill de traspaso está descrita abajo y no se creó.

## Propuesta de skill, sin implementar

Nombre tentativo: `traspaso-computadora`.

Ubicación sugerida: una skill de proyecto en cada repositorio, `.cursor/skills/traspaso-computadora/SKILL.md`, no en el directorio personal ni en `skills-cursor`. Hace falta confirmación antes de crearla.

Cuándo usarla: al empezar en otra computadora, al cerrar una sesión para continuarla en otro equipo, o si se pide sincronizar el contexto sin desplegar.

Pasos propuestos:

1. En cada clon, leer `AGENTS.md` y las cuatro guías de `docs/`.
2. Ejecutar `git status -sb`, `git branch -vv` y `git log -1` sin hacer fetch forzado, reset ni merge.
3. Comparar las copias duplicadas de la documentación y señalar diferencias, sin elegir una en silencio.
4. Actualizar `ESTADO_ACTUAL.md` y `PENDIENTES.md` solo con comandos o archivos vistos en esa sesión.
5. No copiar `.env.local`, `appsettings.*.local.json`, secretos ni datos biométricos.
6. No hacer push, merge ni deploy salvo pedido explícito.
7. Dejar un resumen de archivos de documentación tocados.

No incluye scripts todavía. El detalle para revisión está en la respuesta de la fase 1, no en un `SKILL.md`.

## Informe de asistencia

Abierto el 1 de noviembre de 2026. El detalle está en `docs/INFORME_ASISTENCIA.md`.

- El frontend ya muestra un informe operativo con los contratos actuales. No es un cierre histórico.
- Falta `GET /api/jornadas/informe-asistencia` y la exportación correspondiente.
- Falta definir la tolerancia de salida, el snapshot del turno por jornada y el cierre mensual.
- No se crearon migraciones ni endpoints en esta nota.
