# Estado actual

La autenticación segura local está en `docs/CONTINUIDAD_AUTENTICACION.md`, rama `feat/autenticacion-segura`. La etapa 7D agrega `exigirMfa` a las invitaciones nuevas y el desafío `vinculacion_mfa_pendiente`. No cambia el alta tradicional ni las cuentas existentes. `Auth:PermitirEnrolamientoMfa` queda en falso. El SuperAdmin compartido no tiene MFA ni obligación. La desactivación voluntaria está en `POST /api/Auth/mfa/desactivar` y no depende de `Auth:PermitirEnrolamientoMfa`. Un factor obligatorio no se desactiva. El reemplazo, el restablecimiento administrativo y exigir MFA a cuentas que ya existen quedan para después.

Cierre de la noche del 2 de noviembre de 2026. No se desplegó ni se mergeó a `main`.

## Verificación de la noche del 2 de noviembre de 2026

No se corrió `dotnet test` completo ni se reinició el proceso de la API.

- `TurnoService.EliminarAsync` permite el borrado lógico si no hay una asignación abierta ni con fin de hoy o posterior. Una asignación ya cerrada no bloquea y la fila de `EmpleadoTurno` se conserva. Sigue rechazado si hay una asignación vigente.
- `dotnet test` con filtro `Turno_SeBorraSiSoloTieneAsignacionHistorica|Turno_NoSeBorraSiTieneAsignacionVigente|EliminarAsync_RealizaBorradoLogico` dejó 3 pruebas correctas.
- En el frontend de la misma sesión, el editor de usuario y el tope de 7 días del turno quedaron cubiertos por checks puntuales. El inicio de sesión se vio sin asterisco rojo en `http://localhost:5173/#/login`. No se corrió `npm run check` completo ni `npm run build`.

Relevado el 28 de octubre de 2026. Solo se lista lo que está en Git o lo que se ejecutó en la sesión anterior. No implica despliegue ni merge a `main`.

## Ramas

| Repositorio | Rama de trabajo | Commit | `main` local, alineado con `origin/main` |
| --- | --- | --- | --- |
| API | `feat/turnos-asignaciones-ux` | `96925be297ccf114eaee170fc8bb9c28b8e1e642` | `5108784` |
| Frontend | `feat/turnos-asignaciones-ux` | `0d558638bfb96d3077bde5f6ddb95cf29d67315f` | `dd682fd` |

Ambas ramas siguen a su remoto del mismo nombre. El working tree de los dos clones estaba limpio en este relevamiento. No se hizo merge a `main`.

## Verificación del 27 de octubre de 2026

Se ejecutó antes de publicar esos commits. No se repitió en este relevamiento.

- API: `dotnet build --configuration Release` terminó sin advertencias. `dotnet test --configuration Release` con filtro `Category!=LocalTunnelAcceptance&Category!=ProductionAcceptance` dejó 331 pruebas correctas.
- Frontend: `npm run build` terminó bien. `node scripts/run-checks.js` falló solo en `check-deploy-archive.js` al invocar `/bin/bash` (exit 127). El resto de los checks de esa corrida pasó.
- `git diff --check` no marcó errores de espacio en blanco. En el frontend, Git avisó la conversión LF/CRLF de `turnos-panel.js`; el archivo se normalizó a CRLF en el working tree antes del commit y el índice guardó LF, como el resto del repositorio.

## Funcionalidad incluida en esas ramas

Turnos y descanso, en la API:

- `TurnoMappers` copia `MinutosAlmuerzo` recibido, incluido `0`. Ya no asigna `60` fijo al persistir.
- `AppDbContext` marca el centinela `-1` y `ValueGeneratedNever` para que EF escriba `0`.
- El PUT reemplaza los `TurnoDia` con `Clear` y `Add`.
- Se rechaza un descanso negativo o mayor o igual que la duración del tramo, también si la salida es del día siguiente.
- Los minutos programados restan el descanso una sola vez.
- Hay prueba de integración SQLite `TurnoDescansoIntegracionTests` para 0, 20, 30 y 60, cambios 60→0 y 0→20, y el turno 22:00–06:00 con 60 minutos.

Turnos, en el frontend:

- El descanso del día es un campo numérico con sufijo `min`, accesos rápidos y texto `Jornada neta`.
- `0` se muestra como «Sin descanso» y se reenvía como `0`.
- Un día que no está en el formulario no exige horario ni descanso.
- La rama también incluye la UI de asignación de empleados, el campo de hora del turno y el resumen laboral del calendario que consume la API.

Jornadas, en la API de esa misma rama:

- El resumen mensual no suma días futuros según el reloj `America/Argentina/Buenos_Aires`.
- Los minutos programados salen del turno vigente o de la excepción, no del horario legado suelto.
- El DTO separa deuda y saldo a favor, llegadas y salidas dentro o fuera de tolerancia, días sin registros y jornadas incompletas.
- `JornadaClasificador.Clasificar` no se modificó para inventar entrada o salida.

## Ya existente en `main` y conservado

El README de cada repositorio y, en el frontend, `docs/FRONTEND-SECURITY.md` describen autenticación, roles, empresas, sucursales, departamentos, usuarios, agentes y fichadas. Esas funciones no se revalidaron en este relevamiento.

## Informe de asistencia, 1 de noviembre de 2026

Solo documentación. No se modificó código productivo, no se crearon migraciones y no se desplegó.

Quedó `docs/INFORME_ASISTENCIA.md` con el problema del informe histórico, el contrato propuesto `GET /api/jornadas/informe-asistencia` y la exportación, las reglas que siguen abiertas y los criterios de aceptación. `PENDIENTES.md` y `DECISIONES_TECNICAS.md` apuntan a ese documento.

El frontend, en su propio repositorio, ya consulta el informe operativo componiendo fichadas, calendario y asignaciones. Ese resultado usa el turno vigente y no reemplaza el cierre mensual que todavía no existe.
