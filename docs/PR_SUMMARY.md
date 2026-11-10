# Resumen del PR

Rama: `feat/autenticacion-segura`. No hay PR abierto. La publicación de esta rama sigue sujeta a la autorización de Samu.

El resumen del frontend está en la rama `fix/smtp-cifrado-selector` del repositorio `control-accesos-frontend`, archivo `docs/PR_SUMMARY.md`. El arreglo del selector es `ac4e07ce010e6c30c445dea5cf453ea3943c8a67` y su paquete todavía no está desplegado. El panel publicado sigue siendo `a2e60f09f3d94de743dc2c4949d8ea524a36bd92`.

## Objetivo

Publicar la autenticación segura sobre la API que ya estaba en producción: bloqueo por contraseña, invitaciones, MFA y baja lógica de cuentas, sin reabrir una cuenta eliminada al revertir.

## Alcance

Incluye el ingreso, la sesión web, las invitaciones, el enrolamiento y la verificación MFA, la baja lógica y los scripts de base que el ensayo aplicó. No incluye el panel. No incluye contraseñas, tokens, cadenas de conexión, volcados ni imágenes Docker.

## Cambios principales

- El tercer intento consecutivo de contraseña incorrecta bloquea una cuenta Admin o RRHH durante 15 minutos. Mientras dura el bloqueo, la contraseña correcta también se rechaza con el mensaje genérico de credenciales. Una contraseña válida antes del bloqueo reinicia el contador.
- Un SuperAdmin no se bloquea por contraseña. Un bloqueo ya existente no impide su ingreso si la contraseña es válida. La excepción usa el rol que la API lee de la cuenta. El desbloqueo administrativo y la auditoría `usuario_desbloqueo` se mantienen. El MFA sigue en 5 intentos y 5 minutos.
- Las invitaciones se envían por correo, se aceptan con una contraseña elegida y pueden exigir MFA antes de abrir sesión.
- El MFA voluntario se enrola, se verifica en el ingreso y se puede desactivar cuando no es obligatorio. El secreto no se devuelve al consultar el estado.
- La baja lógica marca `Usuario.EliminadoEn`, deja la cuenta inactiva y fuera del listado, e impide el ingreso y la reactivación.
- `POST /api/Auth/Login` responde 403 `{"mensaje":"Solicitud rechazada."}` cuando el navegador envía `X-CF-Solicitud: 1` y el `Origin` no está, con comparación exacta, en `Cors:AllowedOrigins`. Esa comprobación ocurre en [AuthController.cs](../ControlFichajes.API/Controllers/AuthController.cs) antes de validar la contraseña. No la emite un middleware.

## Dependencias entre frontend y API

El panel publicado en `a2e60f09f3d94de743dc2c4949d8ea524a36bd92` llama a `/api` en el mismo origen y envía `X-CF-Solicitud` y `X-CF-Generacion` en el login renovable. Depende de las invitaciones, el desbloqueo, el estado MFA y la baja lógica de esta rama. El selector de correo de ese panel puede mostrar «TLS desde la conexión» y enviar `starttls`; el arreglo está en el frontend y no forma parte de este SHA. Esta API depende de ese panel para el ingreso desde el navegador y de `Cors:AllowedOrigins` con el origen exacto `https://controldeaccesos.online`.

## Pruebas del entorno aislado

Se ejecutaron contra la copia `tesis_ensayo`, no contra producción.

- `dotnet test` sobre `f56a851119b9d140975ef4d80c80148e103da865`, excluyendo la categoría de aceptación de producción, dejó 497 pruebas correctas, 1 omitida y 1 fallida: `LocalApiAcceptsPunchesAndCleansOnlyTheDedicatedEmployeesNewBiometricRows`, porque falta `CFB_LOCAL_TEST_API_BASE_URL`.
- Las pruebas de bloqueo cubren el tercer intento, el reinicio del contador con una contraseña válida y la excepción de SuperAdmin.
- En cuentas ficticias se comprobó el bloqueo de Admin y RRHH, el desbloqueo, que RRHH no desbloquea, que un administrador de otra empresa no desbloquea, y que SuperAdmin no queda bloqueado. También se comprobó que un SuperAdmin con MFA recibe el segundo factor y no una sesión.
- La copia aislada recibió estos scripts, en este orden. Las cantidades e identificadores de empleados, asignaciones y fichadas históricas no cambiaron:

  1. [20261110_usuario_eliminado.sql](../database/20261110_usuario_eliminado.sql)
  2. [20261103_autenticacion_seguridad.sql](../database/20261103_autenticacion_seguridad.sql)
  3. [20261104_sesion_generacion.sql](../database/20261104_sesion_generacion.sql)
  4. [20261105_mfa_persistencia.sql](../database/20261105_mfa_persistencia.sql)
  5. [20261106_mfa_enrolamiento.sql](../database/20261106_mfa_enrolamiento.sql)

- Con la API nueva se consultaron empleados y fichadas históricas y se registró la fichada manual 1974.
- Una cuenta ficticia entró y luego fue dada de baja. La imagen anterior, sin la variante, volvió a listarla inactiva y no le permitió entrar.
- La variante `rollback/50843f8-eliminado`, commit `a012fc24517b4c83d31cd254e679e9f721b5752f`, se probó sobre la misma base migrada, sin restaurar el volcado. La cuenta de control entró, la fichada 1974 siguió visible y la cuenta eliminada quedó fuera del listado, sin acceso y sin reactivación.
- La imagen de `f56a851119b9d140975ef4d80c80148e103da865` arrancó contra esa misma base y la cuenta de control inició sesión.

## Verificaciones en producción

Separadas del ensayo aislado.

- El navegador, en `https://controldeaccesos.online/api/Auth/Login`, con `Origin: https://controldeaccesos.online`, `X-CF-Solicitud: 1` y una generación válida, recibió HTTP 403 `{"mensaje":"Solicitud rechazada."}`.
- La instancia desplegada permite exactamente `https://controldeaccesos.online` en `Cors:AllowedOrigins`. `App:PublicBaseUrl` usa la misma dirección y no decide ese rechazo.
- Después de permitir ese origen, el login y la visualización de empleados y fichadas quedaron confirmados en producción.
- El SMTP del panel persiste en `/app/smtp.panel.json`. El guardado crea `smtp.panel.json.tmp` en el mismo directorio y después lo mueve encima del archivo. La ruta no es configurable. El montaje de solo ese archivo no alcanza: el usuario 1654 puede escribir el archivo, pero no crear el temporal si `/app` no es escribible. El directorio de la aplicación queda montado sobre `/app` y pertenece a 1654. Al recrear el contenedor hay que conservar ese directorio y la misma `Smtp:ProtectionKey`, sin volver a copiar la imagen encima, porque eso borraría el archivo. El archivo guarda el sobre cifrado y no la clave.
- El enrolamiento MFA está habilitado con `Auth:PermitirEnrolamientoMfa=true`. La clave es la sección `Auth:TotpClaves`: `Activa` nombra un identificador de 1 a 32 caracteres, formado por letras, dígitos, `_` o `-`, y `Lectura` tiene una entrada con ese mismo identificador. El valor es Base64, también con `-` y `_`, y decodifica a 32 bytes. Vincular usa esa clave; validar un autenticador ya guardado también, y no consulta la bandera. Esos valores no están en el repositorio. `appsettings.json` versionado sigue con el enrolamiento en falso y `TotpClaves` vacío.

## Resultados

`f56a851119b9d140975ef4d80c80148e103da865` es el SHA desplegado. El SHA final de la rama es el commit posterior que agrega este archivo.

La reversión usable no es el commit `50843f8` solo: hace falta `rollback/50843f8-eliminado` en `a012fc24517b4c83d31cd254e679e9f721b5752f` para respetar `Usuario.EliminadoEn`.

## Pendientes

- No hay PR abierto. Publicar la rama sigue sujeto a la autorización de Samu.
- La fichada nueva desde el lector en producción queda pendiente.
- Falta repetir en producción el guardado SMTP con `tls-conexion` cuando esté publicado el panel `frontend-5cce792.tar.gz`.
- Falta vincular y validar un autenticador en producción con el enrolamiento habilitado.
- La prueba del túnel biométrico local sigue sin las variables de entorno que exige.

## Despliegue y reversión

El despliegue de hoy usa el SHA `f56a851119b9d140975ef4d80c80148e103da865`, los cinco scripts en el orden de arriba, el origen `https://controldeaccesos.online` en `Cors:AllowedOrigins`, el directorio de la aplicación montado en `/app` y `Auth:PermitirEnrolamientoMfa` habilitado. Las claves quedan fuera de Git.

Para revertir la API sin restaurar la base, usar la rama `rollback/50843f8-eliminado`, commit `a012fc24517b4c83d31cd254e679e9f721b5752f`. Esa variante respeta la baja al listar, autenticar y reactivar. La columna `Usuario.EliminadoEn` permanece.
