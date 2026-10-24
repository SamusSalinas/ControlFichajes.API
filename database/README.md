# Esquema de ControlFichajes.API

Los scripts no eligen el nombre de la base. Hay que crearla vacía, con `utf8mb4`, y seleccionarla antes de ejecutarlos. No contienen usuarios, contraseñas ni datos de negocio.

`20260929_nueva_funcionalidad.sql` está reservado. No tiene DDL y no se ejecuta en ninguno de los dos caminos.

## A. Instalación limpia

Una base que no tiene tablas:

1. `20261023_esquema.sql`

Ese archivo ya es el esquema actual, incluida la clave biométrica. No ejecutar después `20260910_usuario_seguridad.sql`, `20260914_fichada_observacion.sql`, `20261001_turnos_jornadas.sql` ni `20261023_fichada_clave_idempotencia.sql`: volverían a crear columnas o índices que el snapshot ya trae.

## B. Actualización

Una base que ya tiene el esquema operativo anterior. Esa estructura, sin datos, está descrita en `20261022_esquema_previo.sql`. Puede tener o no la columna heredada `Empleado.TurnoId`.

1. `20261001_turnos_jornadas.sql`
2. `20261023_fichada_clave_idempotencia.sql`

Los dos son idempotentes. Una segunda ejecución no borra fichadas, no duplica asignaciones y no reescribe `MinutosAlmuerzo`. Las fichadas ya guardadas quedan con `ClaveIdempotencia` en NULL. Si `Empleado.TurnoId` existe, la primera ejecución copia la asignación abierta que todavía no tenga fila vigente; `EmpleadoTurno` pasa a ser la fuente canónica y la API no lee `TurnoId`.

Si la base es todavía anterior a las columnas de seguridad de `Usuario` o a `FichadaObservacion`, antes de esos dos archivos corresponden, en este orden, `20260910_usuario_seguridad.sql` y `20260914_fichada_observacion.sql`.
