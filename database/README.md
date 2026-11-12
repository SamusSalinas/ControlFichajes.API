# Esquema de ControlFichajes.API

Los scripts no eligen el nombre de la base. Hay que crearla vacía, con `utf8mb4`, y seleccionarla antes de ejecutarlos. No contienen usuarios, contraseñas ni datos de negocio.

`20260929_nueva_funcionalidad.sql` está reservado. No tiene DDL y no se ejecuta en ninguno de los dos caminos.

## A. Instalación limpia

Una base que no tiene tablas:

1. `20261023_esquema.sql`

Ese archivo ya es el esquema actual, incluida la clave biométrica, las tablas de autenticación, la persistencia de MFA de la etapa 7A y el enrolamiento de la etapa 7C.1. No ejecutar después `20260910_usuario_seguridad.sql`, `20260914_fichada_observacion.sql`, `20261001_turnos_jornadas.sql`, `20261023_fichada_clave_idempotencia.sql`, `20261103_autenticacion_seguridad.sql`, `20261104_sesion_generacion.sql`, `20261105_mfa_persistencia.sql` ni `20261106_mfa_enrolamiento.sql`: volverían a crear columnas o índices que el snapshot ya trae. Esos incrementales son idempotentes y no duplican la columna si ya existe.

## B. Actualización

Una base que ya tiene el esquema operativo anterior. Esa estructura, sin datos, está descrita en `20261022_esquema_previo.sql`. Puede tener o no la columna heredada `Empleado.TurnoId`.

1. `20261001_turnos_jornadas.sql`
2. `20261023_fichada_clave_idempotencia.sql`
3. `20261103_autenticacion_seguridad.sql`
4. `20261104_sesion_generacion.sql`
5. `20261105_mfa_persistencia.sql`
6. `20261106_mfa_enrolamiento.sql`
7. `20261110_usuario_eliminado.sql`

Esos scripts son idempotentes. Una segunda ejecución no borra fichadas, no duplica asignaciones y no reescribe `MinutosAlmuerzo`. Las fichadas ya guardadas quedan con `ClaveIdempotencia` en NULL. Si `Empleado.TurnoId` existe, la primera ejecución copia la asignación abierta que todavía no tenga fila vigente; `EmpleadoTurno` pasa a ser la fuente canónica y la API no lee `TurnoId`.

Si la base es todavía anterior a las columnas de seguridad de `Usuario` o a `FichadaObservacion`, antes de esos dos archivos corresponden, en este orden, `20260910_usuario_seguridad.sql` y `20260914_fichada_observacion.sql`.

`20261103_autenticacion_seguridad.sql` agrega columnas nullable en `Usuario`, deja `TotpHabilitado` en 0 y marca `CorreoVerificadoEn` solo en filas que ya tienen `PasswordHash` y no fueron invitadas. No toca el hash ni el rol. La segunda ejecución no vuelve a escribir una fecha ya cargada.

`20261103_autenticacion_seguridad_rollback.sql` no se ejecuta en la instalación. Quita las tablas nuevas y esas columnas. No restaura una marca de verificación previa porque la columna no existía. No correrlo en producción sin respaldo.

`20261104_sesion_generacion.sql` agrega `RefreshToken.Generacion` solo si falta. No borra filas.

`20261105_mfa_persistencia.sql` agrega `Usuario.MfaObligatorio` en 0, el secreto pendiente y su vencimiento, los contadores de usuario y los campos del desafío. No ejecuta `UPDATE` y no toca `TotpHabilitado`, `TotpSecretoCifrado`, `MfaOmitidoHasta`, `UltimoPasoTotp`, el hash, el rol, la empresa, el estado ni `TokenVersion`. Antes del índice único cuenta hashes duplicados en `CodigoRecuperacionMfa`. Si hay alguno, aborta sin borrar filas y sin crear el índice. `20261105_mfa_persistencia_rollback.sql` no se ejecuta en la instalación. Quita solo lo que agrega esa etapa.

`20261106_mfa_enrolamiento.sql` crea `OperacionEnrolamientoMfa` y, solo si `CodigoRecuperacionMfa` está vacía, agrega `ConjuntoId` y `CreadoEn`. Si ya hay códigos, aborta sin reescribirlos y sin borrar filas. `20261106_mfa_enrolamiento_rollback.sql` no se ejecuta en la instalación.

`20261110_usuario_eliminado.sql` agrega `Usuario.EliminadoEn` en NULL solo si falta. No ejecuta `UPDATE` y no borra usuarios, fichadas ni auditoría.
