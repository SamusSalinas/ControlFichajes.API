# Decisiones técnicas

Decisiones ya visibles en el código o pedidas de forma explícita. No son propuestas nuevas.

## Dos repositorios, sin monorepo

El frontend y la API tienen remotos distintos. El directorio que los contiene no es un repositorio. La documentación compartida queda duplicada en los dos clones para que un agente la encuentre al abrir cualquiera de los dos. Un repositorio tercer no se creó.

## Descanso del turno

- `0` significa «Sin descanso» y es un valor persistible.
- No se usa `valor > 0 ? valor : 60` ni `MinutosAlmuerzo = 60` al mapear un día recibido.
- `?? 60` solo corresponde cuando el valor está ausente. El inicializador del DTO cubre un JSON que no trae la propiedad.
- El centinela de EF es `-1`, no `0`, con `ValueGeneratedNever`, para que el default de la columna no reemplace un cero.
- El descanso puede ser cualquier entero mayor o igual que cero y menor que la duración del tramo. No hay una lista cerrada en la validación.
- El campo del formulario es numérico. Los accesos «Sin descanso», «30 min», «45 min» y «60 min» solo completan el campo.
- Un día nuevo puede iniciar en 60. Borrar el campo mientras se edita no lo vuelve a poner en 60.

## Jornadas

- La clasificación original de fichadas permanece en `JornadaClasificador.Clasificar`. El resumen no inventa una entrada o una salida que la clasificación no haya marcado.
- El reloj laboral del resumen es `America/Argentina/Buenos_Aires`.
- Un día futuro no suma minutos programados ni saldo. Un día pasado laborable sin fichadas sí genera la deuda del turno.
- El saldo del resumen es trabajado menos programado más reposiciones. La deuda y el tiempo a favor se muestran por separado.
- Una salida anterior a la entrada es un turno que cruza la medianoche. La duración usa el tramo completo y el descanso se resta una sola vez.

## Pruebas y entorno

- La prueba de persistencia del descanso usa SQLite en memoria porque el proveedor en memoria no reproduce el default de la columna. `SQLitePCLRaw.lib.e_sqlite3` quedó en 2.1.14 por el aviso de la versión transitiva anterior.
- La corrida habitual excluye las categorías `LocalTunnelAcceptance` y `ProductionAcceptance`.
- Los secretos de desarrollo no se versionan: `appsettings.*.local.json` en la API y `.env.local` en el frontend.

## Lo que no se decidió aquí

No se eligió un repositorio único para la documentación, no se creó la skill de traspaso y no se cambió el descanso fijo de 60 minutos de una excepción de jornada con horario propio.
