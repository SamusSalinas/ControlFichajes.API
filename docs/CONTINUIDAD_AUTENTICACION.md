# Continuidad de autenticación

Cierre local del 4 de noviembre de 2026. El repositorio Git es `api/api-nueva-20261023`. El directorio padre del workspace no es el repositorio.

No había un documento de continuidad de autenticación. `docs/ESTADO_ACTUAL.md` y `docs/PENDIENTES.md` describían el cierre de turnos del 2 de noviembre. Este archivo concentra la autenticación. Las copias de esas guías en el repositorio del frontend no se modificaron.

## Git

Comprobado antes de escribir esta documentación:

- Rama: `feat/autenticacion-segura`.
- Commit funcional de la etapa 5B: `0aa14677401352349bcc06deed76c8fd3e72950e`.
- La etapa 6A es `e5df9ba2e5f55326faf07e52046c2ea933442377`. Su padre es `9e8fc5df290c894180cfb50708ef57033e33b671`.
- La etapa 6A.1 queda en el commit que actualiza este documento. Su padre es `e5df9ba2e5f55326faf07e52046c2ea933442377`.
- Árbol limpio.
- `git rev-parse '@{upstream}'` respondió `fatal: no upstream configured for branch 'feat/autenticacion-segura'`.
- No hay remoto configurado para esta rama. No se hizo push, fetch, pull, merge ni rebase en este cierre.

La rama sale de `e20a883c7d3f71f690497adefaf80a7c3f082e4d` (`Permite borrar un turno sin asignaciones abiertas.`).

## Etapas

Todas quedaron en commits locales. El usuario las declaró aprobadas y respaldadas al pedir la etapa siguiente. La etapa 5B quedó implementada y commiteada; su revisión de cierre todavía no fue declarada en el mensaje que pidió esta documentación.

| Etapa | Commit | Mensaje |
| --- | --- | --- |
| 1. Esquema y configuración | `a797ddd13f718f7a5aa9dc152f3e18872f823389` | `feat: prepara esquema de autenticación segura` |
| 2. Auditoría | `53c4794d743c9553bea382c49de5e1583360cdf6` | `feat: audita accesos y cambios de credenciales` |
| 2. Aislamiento de la auditoría | `93fdb8ace9241c70c59adabb58fcf58bfe553d46` | `fix: aísla la auditoría de seguridad del guardado principal` |
| 3. Bootstrap apagado | `91ad7ba4bbddc46fa0a8652196b3b80d1116f0c3` | `feat: apaga el bootstrap público por configuración` |
| 4. Invitación por correo | `35653c7f3cbf52b4c8cb2e9a0c4201fba53772d1` | `feat: invita usuarios por correo sin entregar el token` |
| 5A. Aceptación y contraseña | `c3dad96096fc205e008c86a493f2cd092508b09a` | `feat: acepta la invitación y deja la contraseña elegida sin iniciar sesión` |
| 5B. Recuperación | `0aa14677401352349bcc06deed76c8fd3e72950e` | `feat: recupera la contraseña por correo sin iniciar sesión` |
| 6A. Refresh rotativo | `e5df9ba2e5f55326faf07e52046c2ea933442377` | `feat: rota la sesión web y la revoca al cierre` |
| 6A.1. Cookie por generación | `79d395dc8e16052db5b8fd8e6a9214c643df9046` | `feat: aísla la cookie de cada generación de sesión` |
| 6C. Access corto | `234c75f7b2f10d95600e2270056e7c334867b6fe` | `feat: acorta el access de la sesión web renovable` |
| 7A. Persistencia MFA | `5dd60b94d221ea8ee88ba69be1e8cecb8af53bcc` | `feat: persiste el secreto MFA cifrado sin exigirlo` |
| 7B. Desafío y TOTP | este commit | ver el mensaje del commit |

La etapa 4 se cerró con un amend. El commit previo de esa etapa, `018f1e2b622662c36a4ebaffc9253eadd1834dfe`, ya no es el HEAD de la etapa: el hash vigente es `35653c7`. El padre sigue siendo `91ad7ba`.

## Contratos que se conservan

`POST /api/Auth/Login` sigue respondiendo `token`, `mensaje` y `requiereCambioPassword`. El login tradicional, sin `sid`, dura `Jwt:ExpireMinutes`, hoy `1440`. El login renovable y cada refresh, con `sid`, duran `Jwt:AccessExpireMinutes`, hoy `15`.

`POST /api/Auth/agente` sigue respondiendo el mismo cuerpo. El JWT lleva `token_use=agent` y el rol `AGENTE_SUCURSAL`. No lleva `token_version`.

`Auth:PermitirAltaConPassword` permanece `true`. `POST /api/usuarios` sigue aceptando contraseña mientras ese valor sea verdadero. El DTO de esa alta conserva el límite de 8 a 255 caracteres. No se le aplicó `PasswordPolitica`.

`POST /api/Auth/bootstrap` responde `404` cuando `Auth:PermitirBootstrap` no está en `true`. En el `appsettings.json` versionado está en `false`.

No se modificó el frontend. Sigue en `feat/autenticacion-sesiones-web`, commit `36dbe061c86035c60eaa79c56d1ebb02ea3e6e26`. Todavía no presenta el desafío. La etapa 7B verifica TOTP solo en cuentas que ya tienen `TotpHabilitado`. El enrolamiento, la desactivación, el reemplazo y la administración siguen pendientes. Ninguna cuenta existente, incluido el SuperAdmin, quedó con MFA activado o obligatorio.

## Endpoints de esta rama

- `POST /api/usuarios/invitar`. Autenticado. Mismas reglas de empresa y rol que el alta con contraseña. No recibe ni devuelve contraseña, JWT ni token. El usuario queda inactivo.
- `POST /api/usuarios/{id}/reenviar-invitacion`. Emite otro token. No reutiliza el anterior.
- `POST /api/Auth/invitacion/aceptar`. Anónimo. Cuerpo: `token`, `password`, `confirmarPassword`. Éxito `200` con un mensaje, sin sesión. Token inexistente, vencido, invalidado o usado: el mismo `400`.
- `POST /api/Auth/recuperacion/solicitar`. Anónimo. Cuerpo: `correo`. Formato válido: siempre el mismo `200`, exista o no la cuenta, esté inactiva, no verificada o falle SMTP. Formato inválido: `400` sin repetir el valor.
- `POST /api/Auth/recuperacion/restablecer`. Anónimo. Cuerpo: `token`, `password`, `confirmarPassword`. Éxito `200` con un mensaje, sin sesión. Token inexistente, vencido, usado, invalidado o de una cuenta que ya no está activa y verificada: el mismo `400`.

Un token de invitación no restablece la contraseña. Un token de recuperación no acepta una invitación. La recuperación no activa una cuenta ni completa una invitación.

El detalle operativo está en `README.md`, sección de invitación y de recuperación.

## Decisiones

Los tokens de invitación y de recuperación salen de `TokenOpaco`: 32 bytes de `RandomNumberGenerator`, Base64 url-safe. En MySQL se guarda solo el SHA-256 en hexadecimal, en `TokenHash`. La invitación vence a las `Auth:InvitacionHoras` (72). La recuperación vence a los `Auth:RecuperacionMinutos` (30). `Auth:RecuperacionHoras` era un valor de reserva de una hora que ningún flujo leía; se reemplazó por los minutos. No hubo migración: `RecuperacionPassword` ya existía.

La contraseña elegida en aceptación, recuperación y cambio usa `PasswordPolitica`: 8 a 20 caracteres, letra, dígito y un signo, sin espacios. El hash es `PasswordHasher<Usuario>`. `RequiereCambioPassword` queda en falso cuando la persona acaba de elegir la clave.

La auditoría escribe con un `DbContext` independiente, después del guardado principal. Si falla, se registra el tipo de excepción y no se revierte la operación. No se guardan token, contraseña, confirmación, enlace ni cuerpo. Los eventos de invitación y recuperación están en `AuditoriaSeguridadService`.

`TokenVersion` sube al restablecer la contraseña y en los cambios de credenciales o de estado que ya lo hacían. `WebSessionSecurityMiddleware` compara el claim `token_version` solo en JWT con `token_use=web`. Un JWT web anterior queda rechazado. El token de agente no entra en esa comparación.

La invitación confirma usuario inactivo y hash, y después abre SMTP fuera de la transacción. Si el envío falla, la cuenta sigue inactiva, el hash permanece y la respuesta es `503`. La recuperación confirma el hash vigente y también envía fuera de la transacción. Si el envío falla, el hash permanece y la respuesta pública sigue siendo el `200` genérico.

El correo de recuperación se recorta y se compara sin distinguir mayúsculas. El correo guardado no se reescribe. `Login` solo recorta el correo; esa diferencia no se cambió.

El restablecimiento no limpia `IntentosFallidos` ni `BloqueadoHasta`. No se pidió hacerlo.

## Concurrencia

MySQL toma `SELECT Id FROM Usuario WHERE Id = @id FOR UPDATE` dentro de una transacción `ReadCommitted`. Lo usan el reenvío, la aceptación, la solicitud de recuperación y el restablecimiento. El bloqueo es por fila de usuario y vale entre instancias de la API. El proveedor en memoria de las pruebas no tiene ese bloqueo; cada servicio usa un `SemaphoreSlim` estático solo en ese caso.

Resultados que deben mantenerse:

- Dos reenvíos del mismo usuario dejan una invitación vigente.
- Dos aceptaciones del mismo token dejan un solo éxito.
- Si la aceptación toma el bloqueo antes que el reenvío, la cuenta queda activa y el reenvío ya no encuentra una invitación pendiente. Si el reenvío lo toma antes, vence el token anterior y la aceptación de ese token se rechaza; la cuenta sigue inactiva.
- Dos restablecimientos del mismo token dejan un solo éxito.
- Si la solicitud de recuperación toma el bloqueo antes que el restablecimiento, el token anterior vence y ese restablecimiento se rechaza. Contraseña y `TokenVersion` no cambian. Si el restablecimiento lo toma antes, la contraseña cambia, el token queda usado y `TokenVersion` aumenta.
- Un fallo antes del commit no deja una activación parcial ni un token consumido sin la contraseña.

En la prueba local de recuperación, la solicitud ganó la carrera. El restablecimiento respondió `400` y la contraseña no cambió. Una consulta inmediata contó dos filas con vencimiento futuro; una lectura posterior mostró una fila ya vencida y una con unos 30 minutos. El reloj de la API y `UTC_TIMESTAMP()` de MySQL pueden diferir unos segundos cuando la invalidación graba el `UtcNow` de .NET. La revalidación de la API usa su propio reloj y rechazó el token.

## Configuración

Nombres versionados. Los secretos no van en este documento.

`ControlFichajes.API/appsettings.json`:

- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpireMinutes` (`1440`), `Jwt:AccessExpireMinutes` (`15`), `Jwt:RefreshExpireDays` (`14`). `Jwt:Key` no está en ese archivo.
- `Auth:PermitirBootstrap` (`false`), `Auth:PermitirAltaConPassword` (`true`), `Auth:ExigirMfa` (`false`), `Auth:MfaGraciaDias` (`14`).
- `Auth:InvitacionHoras` (`72`), `Auth:AceptacionLimite` (`5`), `Auth:AceptacionVentanaSegundos` (`60`).
- `Auth:RecuperacionMinutos` (`30`), `Auth:RecuperacionSolicitudLimite` (`5`), `Auth:RecuperacionSolicitudVentanaSegundos` (`60`), `Auth:RecuperacionRestablecerLimite` (`5`), `Auth:RecuperacionRestablecerVentanaSegundos` (`60`).
- `Smtp:Host`, `Smtp:Port` (`587`), `Smtp:User`, `Smtp:Password`, `Smtp:From`, `Smtp:EnableSsl` (`true`). En el archivo versionado el host, el usuario, la contraseña y el remitente están vacíos.
- `App:PublicBaseUrl`, vacío en el archivo versionado.
- `ForwardedHeaders:Habilitado` (`false`).

Los mismos nombres de entorno están en `ControlFichajes.API/.env.production.example`, con el prefijo de ASP.NET Core (`Auth__RecuperacionMinutos`, `Smtp__Host`, `App__PublicBaseUrl`).

La configuración local no versionada es `ControlFichajes.API/appsettings.Development.local.json`. En el laboratorio, el SMTP efectivo apunta a `127.0.0.1`, puerto `1025`, sin SSL, y `App:PublicBaseUrl` es `http://127.0.0.1:5173`. No copiar ese archivo al repositorio.

Los límites de aceptación y de recuperación son ventanas fijas del limitador de ASP.NET Core, separadas por endpoint y por IP. Al superarlas la respuesta es `429` con el mensaje genérico. No se agregó un paquete NuGet para eso.

## Servicios locales

Comprobado al cerrar: el contenedor `cf-auth-mysql` (`mysql:8.0`) seguía en marcha, con `127.0.0.1:3308->3306/tcp`. La API escuchaba en `127.0.0.1:5088` y el capturador en `127.0.0.1:1025`.

MySQL se creó con esta invocación. La contraseña salió de un archivo temporal y no se repite aquí:

```text
docker run -d --name cf-auth-mysql --restart no -e MYSQL_ROOT_PASSWORD=<archivo> -e MYSQL_DATABASE=cf_auth_stage -p 127.0.0.1:3308:3306 mysql:8.0 --character-set-server=utf8mb4 --collation-server=utf8mb4_0900_ai_ci
```

La base que usa la API es `cf_auth_stage`. También se creó `cf_auth_rollback` durante la etapa 1. Comprobación usada: `docker ps --filter name=cf-auth-mysql`. La consulta usada fue `docker exec -i -e MYSQL_PWD=<archivo> cf-auth-mysql mysql --batch --raw -uroot cf_auth_stage`. La versión informada en la etapa 1 fue MySQL 8.0.46.

El directorio de la API y de los tests es:

```text
C:\Users\Tinchota\Documents\PROYECTOS\control-accesos-departamentos-20260921-105708\api\api-nueva-20261023
```

Para iniciar la API después de un build se usó, en PowerShell y en ese directorio:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5088'
dotnet run --project 'C:\Users\Tinchota\Documents\PROYECTOS\control-accesos-departamentos-20260921-105708\api\api-nueva-20261023\ControlFichajes.API\ControlFichajes.API.csproj' --no-launch-profile --no-build
```

`--no-build` exige que el proyecto ya esté compilado. Si el proceso de `5088` sigue abierto, el build no puede reemplazar el ejecutable. Hay que detener ese listener antes de compilar. La señal de arranque vista fue `Now listening` en `http://127.0.0.1:5088`. También se comprobó con `netstat` buscando `LISTENING` en `:5088`.

Hace falta Docker para MySQL, el SDK usado por el proyecto (`net10.0`) y el archivo local de configuración. No hace falta el frontend para estos endpoints.

## Capturador SMTP

No está en el repositorio. No depende de un script guardado. Los scripts temporales de prueba se borraron de `%TEMP%\cf-auth-lab`. Quedaron ahí archivos de secreto del laboratorio; no se versionan y no se copian a este documento.

El proceso que quedó escuchando es un `TcpListener` de PowerShell en `127.0.0.1:1025`. Responde `220` al conectar, `250` a los comandos, `354` después de `DATA`, `250` después de la línea que solo contiene un punto, y `221` ante `QUIT`. No guarda el cuerpo. Para recrearlo, ejecutar en PowerShell un listener con esas respuestas y dejar el proceso vivo. Comprobar con `netstat` buscando `LISTENING` en `:1025`.

Para leer un token de prueba hay que reemplazar ese proceso por otro que guarde el cuerpo, decodificar quoted-printable (`=` al final de línea y `=HH`) y tomar `token=` de 43 caracteres. El SHA-256 se calcula en memoria y se compara con `TokenHash`. No guardar ni mostrar el token, el enlace ni el cuerpo. Al terminar, volver al capturador que descarta el cuerpo.

## Build y tests

Directorio: el del repositorio de arriba. Comando usado para la suite:

```text
dotnet test ControlFichajes.API.Tests/ControlFichajes.API.Tests.csproj --filter "FullyQualifiedName!~LocalTunnelBiometricAcceptanceTests"
```

Esa corrida compila la API y los tests. No se habilitó `CFB_PRODUCTION_TEST_OPT_IN` ni `CFB_LOCAL_TEST_API_BASE_URL`. La prueba de producción se omite sola.

Resultado después de la etapa 5B: 393 correctas, 0 con error, 1 omitida, 394 en total. La omitida fue `ProductionBiometricAcceptanceTests.AgentCanRegisterAndCleanUpBiometricPunchesForDedicatedDevsEmployee`. Advertencia conocida, sin corregir: `NU1902` por `MySqlConnector` 2.4.0, aviso `GHSA-473q-m89c-ghf8`.

Al cerrar la etapa 5A, la misma exclusión dejó 383 correctas, 0 con error y 1 omitida. No se repitió una corrida intermedia entre ese resultado y el de 5B.

## Bundles

Los archivos están fuera del árbol del repositorio, en `C:\Users\Tinchota\Desktop\backups-control-fichajes`. Esta sesión no ejecutó `git bundle` ni comprobó esos archivos. Los datos siguientes los aportó el usuario, a partir de salidas de PowerShell que verificó en ChatGPT:

- `ControlFichajes-API-autenticacion-etapa-4.bundle`: historial completo y commit `35653c7f3cbf52b4c8cb2e9a0c4201fba53772d1`.
- `ControlFichajes-API-autenticacion-etapa-5a-aceptacion.bundle`: historial completo y commit `c3dad96096fc205e008c86a493f2cd092508b09a`.
- `ControlFichajes-API-autenticacion-etapa-5b-recuperacion.bundle`: historial completo y commit `0aa14677401352349bcc06deed76c8fd3e72950e`.

El usuario mostró una captura de Google Drive, en TECNICATURA → PROYECTO, con los bundles hasta la etapa 4. La subida de 5A y 5B no fue confirmada.

## Restricciones vigentes

Trabajar solo en local y con datos ficticios. MySQL en `127.0.0.1:3308`, API en `127.0.0.1:5088` y SMTP en `127.0.0.1:1025`.

No hacer push, fetch, pull, merge ni rebase. No configurar upstream ni recrear la rama remota. No interactuar con GitHub ni con producción. No usar credenciales ni correos reales. No modificar el frontend. No commitear `appsettings.Development.local.json` ni secretos. No guardar en documentación ni en logs tokens, contraseñas, enlaces ni cuerpos de correo.

## Etapa 6A

Implementada en la API y pendiente de revisión. No se tocó el frontend ni se acortó el access token. No hubo migración: se reutiliza `RefreshToken`. `FamiliaId` sigue en `char(36)` y el modelo lo lee como `Guid`, porque el conector devuelve un identificador para esa columna.

`POST /api/Auth/Login` sin `X-CF-Solicitud` mantiene `token`, `mensaje` y `requiereCambioPassword`, sin familia ni cookie. Si el encabezado está y no vale `1`, responde `403` y no entra al login. Con el valor `1` exige un `Origin` de `Cors:AllowedOrigins`. `Origin` ausente, vacío, `null` o rechazado responde `403` antes de modificar filas o cookies. La cookie `cf_refresh` sale solo después de autenticar y confirmar. Es `HttpOnly`, de host y `Path=/api/Auth`. El borrado usa el mismo nombre y `Path`, y tampoco envía `Domain`.

`Jwt:RefreshExpireDays` sigue en 14 y es el tope absoluto, medido desde `CreadaEn` de la primera fila de la familia. `Jwt:RefreshInactividadHoras` queda en 12. La rotación no extiende el absoluto. `Auth:CookieSameSite` queda en `Lax`. `Auth:CookieSecure` no se fija: `Secure` se apaga solo en Development sobre HTTP, y `SameSite=None` lo fuerza. `Auth:RefreshLimite` y `Auth:CierreLimite` quedan en 30, con ventanas de 60 segundos (`Auth:RefreshVentanaSegundos`, `Auth:CierreVentanaSegundos`).

`POST /api/Auth/refresh` y `POST /api/Auth/cerrar-sesion` exigen el mismo encabezado y origen. El refresh rota una sola vez. La reutilización confirma en MySQL la revocación de toda la familia y después responde `401`. No hay ventana de gracia. El cierre revoca solo esa familia, borra la cookie y responde `204`; sin cookie, con cookie desconocida o vencida también responde `204` después del origen. El JWT web nuevo lleva `sid`. El middleware lo acepta solo si la familia es del usuario, tiene un token vigente y el `TokenVersion` coincide, dentro de ambos plazos. Un JWT web sin `sid` conserva la validación anterior. El agente no cambia.

Dos refresh simultáneos del mismo token pueden responder `200` y `401`, y la familia queda revocada. Si la respuesta se pierde después del commit y el navegador conserva el token consumido, el reintento revoca la familia y hay que iniciar sesión de nuevo. El frontend tiene que coordinar las pestañas antes de activar el flujo. Una solicitud que ya pasó el middleware puede completar.

El cambio de contraseña, el restablecimiento administrativo, la recuperación, el estado, el rol y la identidad revocan todas las familias en la misma transacción que incrementa `TokenVersion`. El desbloqueo no. Cerrar sesión no incrementa `TokenVersion`.

El detalle operativo está en `README.md`, sección de sesión web.

La suite con la exclusión ya documentada, después de esta etapa, dejó 415 correctas, 0 con error y 1 omitida. La omitida sigue siendo la prueba productiva de fichadas biométricas. No se habilitó `CFB_PRODUCTION_TEST_OPT_IN` ni `CFB_LOCAL_TEST_API_BASE_URL`. La advertencia `NU1902` de `MySqlConnector` 2.4.0 sigue igual.

La comprobación manual usó MySQL local en `127.0.0.1:3308` y la API en `127.0.0.1:5088`, con un usuario ficticio. El login sin encabezado respondió `200` sin cookie y sin `sid`. El encabezado distinto de `1`, el origen ajeno y el origen `null` respondieron `403` sin filas nuevas ni cookie. El login renovable respondió `200` con `sid`, `Cache-Control: no-store` y cookie `HttpOnly`, `Path=/api/Auth`, `SameSite=Lax`, sin `Domain` ni `Secure`. El cierre respondió `204`, dejó en `401` el JWT de esa sesión y en pie la otra. El refresh siguiente respondió `200`; repetir la cookie consumida respondió `401` y esa familia quedó revocada. Dos procesos simultáneos respondieron `200` y `401`, y la familia nueva quedó revocada. El preflight permitido respondió `204` con credenciales; el origen rechazado no recibió `Access-Control-Allow-Origin`. El límite de refresh respondió `429` al agotar 30 solicitudes en la ventana. Un JWT sin `sid` siguió llegando al cambio de contraseña. Los logs de la API no contenían la contraseña, la cookie ni `Bearer`. La auditoría guardó los eventos de sesión con detalle `cierre` o `reutilizacion`, sin el token.

## Etapa 6A.1

Aísla la cookie por generación. El padre es `e5df9ba2e5f55326faf07e52046c2ea933442377`. No se tocó el frontend ni se acortó el access token.

`X-CF-Generacion` es opcional. Si no llega, el modo de la etapa 6A sigue igual y la cookie es `cf_refresh`. Si llega, tiene que ser un solo UUID canónico de 36 caracteres, con guiones. Junto con `X-CF-Solicitud: 1` y un `Origin` permitido, el login renovable asocia esa generación a la familia nueva y escribe solo `cf_g_` más los 32 hexadecimales. El identificador no autentica y no reemplaza al refresh token. Un valor presente y mal formado responde `403` y no cae al modo anterior. Repetir una generación ya asociada también responde `403`. Refresh y cierre leen únicamente esa cookie y exigen que el token pertenezca a la familia de esa generación. Si falta, no buscan otra. Cada respuesta crea, reemplaza o borra solo su propio nombre.

La columna es `RefreshToken.Generacion`, `char(36)` nula, con índice `IX_RefreshToken_Generacion`. El modelo la lee como `Guid?`. Las familias del modo anterior quedan con el valor nulo. No hay otra tabla. El script versionado es `database/20261104_sesion_generacion.sql`: agrega la columna solo si falta y no borra filas. El esquema limpio y el incremental del 3 de noviembre también la incluyen. En MySQL local, `127.0.0.1:3308`, la columna quedó `char(36)`, nullable e indexada, y los valores nuevos tienen 36 caracteres.

La cookie lleva `HttpOnly`, `Path=/api/Auth`, `SameSite` y `Max-Age` acotado a `ExpiraEn`, con tope de `Jwt:RefreshExpireDays`. El cierre de una generación no borra las otras cookies. Un login nuevo no revoca las otras familias. La familia huérfana sigue hasta su inactividad, su plazo absoluto, su propio cierre, una reutilización o un cambio de `TokenVersion`.

La comprobación usó `System.Net.CookieContainer.SetCookies` contra la API reemplazada en `http://127.0.0.1:5088`, con el usuario ficticio de la etapa 6A. El jar aplicó el `Set-Cookie` de A después del de B y conservó la cookie y la sesión de B. Lo mismo ocurrió con el refresh tardío, el cierre tardío y el rechazo por reutilización de A. El encabezado de B con la cookie de A no renovó ni cerró. La generación malformada respondió `403` sin cookie. La cookie pedida y ausente dejó el refresh en `401` sin buscar otra. El modo `cf_refresh` renovó y su cierre no borró la cookie de generación. El login tradicional respondió `200` sin cookie y sin `sid`. Los logs de esa instancia no contenían la cookie, el token ni `Bearer`.

La suite con la exclusión ya documentada, sobre este código, dejó 421 correctas, 0 con error y 1 omitida. La omitida sigue siendo la prueba productiva de fichadas biométricas. No se habilitó `CFB_PRODUCTION_TEST_OPT_IN` ni `CFB_LOCAL_TEST_API_BASE_URL`. La advertencia `NU1902` de `MySqlConnector` 2.4.0 sigue igual.

## Etapa 6C

Acorta el access de la sesión web renovable. El padre es `79d395dc8e16052db5b8fd8e6a9214c643df9046`. No se modificó el frontend: la etapa 6B ya está integrada en `feat/autenticacion-sesiones-web`, commit `36dbe061c86035c60eaa79c56d1ebb02ea3e6e26`. No se cambió el contrato JSON ni las cookies. No se implementó MFA.

`JwtTokenService` sigue emitiendo todos los JWT. Si el access lleva `sid`, la duración es `Jwt:AccessExpireMinutes`. El valor versionado es 15 y lo usan el login renovable y cada refresh. Si la clave falta, está vacía, no es entera o no es positiva, queda en 15. No hereda `Jwt:ExpireMinutes`. El login tradicional, sin `sid`, y `POST /api/Auth/agente` siguen en `Jwt:ExpireMinutes` (`1440`). `Jwt:AgentExpireMinutes` sigue sin leerse. `Jwt:RefreshExpireDays` sigue en 14 y `Jwt:RefreshInactividadHoras` en 12.

`Program.cs` no fija `ClockSkew`. El validador JWT de ASP.NET Core conserva la tolerancia predeterminada de 5 minutos: un access ya vencido sigue aceptado hasta `exp` más esos 5 minutos. La prueba de expiración real del laboratorio esperó esa tolerancia. No se cambió el reloj ni la clave de firma.

`dotnet build` terminó con la advertencia conocida `NU1902`. La suite con la exclusión ya documentada dejó 428 correctas, 0 con error y 1 omitida. La omitida sigue siendo la prueba productiva de fichadas biométricas. No se habilitó `CFB_PRODUCTION_TEST_OPT_IN` ni `CFB_LOCAL_TEST_API_BASE_URL`.

La comprobación usó el frontend de 6B en `http://127.0.0.1:5173` y la API en `http://127.0.0.1:5088`. Con la configuración efectiva de 15, el access renovable del SuperAdmin ficticio midió 15 minutos entre `iat` y `exp`, con `sid`. El login tradicional midió 1440 y no llevó `sid`. Un agente temporal, desactivado al terminar, también midió 1440, con `token_use=agent` y sin `sid`.

Para la expiración real, `appsettings.Development.local.json` tuvo `Jwt:AccessExpireMinutes` en 1. Ese archivo sigue ignorado por Git. El access midió 1 minuto. Se eligió Empresa Dos. Después de `exp` más la tolerancia de 5 minutos, la lectura del panel recibió 401, hizo un refresh y un solo reintento, y respondió 200. La otra pestaña no envió refresh. Quedaron el mismo usuario, el mismo `sid` y el contexto de empresa 2. La familia siguió con una fila vigente. Una escritura autenticada, ya vencida, se envió una sola vez: el refresh renovó la sesión y el cliente no repitió el POST.

Al terminar se quitó el valor temporal. Un login renovable nuevo volvió a medir 15 minutos y la API quedó escuchando en `http://127.0.0.1:5088`.

## Etapa 7A

Persiste y cifra el material de MFA. El padre es `234c75f7b2f10d95600e2270056e7c334867b6fe`. No se modificó el login, el frontend ni el agente. No se agregó una biblioteca TOTP. No se expone enrolamiento y ninguna cuenta queda con MFA obligatorio o configurado.

`Usuario` suma `MfaObligatorio` (predeterminado falso), `TotpPendienteCifrado`, `TotpPendienteExpiraEn`, `MfaIntentosFallidos` y `MfaBloqueadoHasta`. Los dos últimos viven en el usuario: crear un `DesafioMfa` no los reinicia. El desafío suma `TokenVersion`, `Generacion`, `ModoSesion` e `Intentos`, junto al propósito y al vencimiento que ya tenía. `CodigoRecuperacionMfa.CodigoHash` queda con el índice único `UX_CodigoRecuperacionMfa_CodigoHash`.

`IProtectorSecretosMfa` cifra con AES-GCM del runtime. El sobre es `v1.{id}.{nonce}.{ciphertext}.{etiqueta}`. La autenticación adicional es el id del usuario y el propósito (`totp-activo` o `totp-pendiente`). Cifra con `Auth:TotpClaves:Activa` y abre con cualquiera de `Auth:TotpClaves:Lectura`. No lee `Jwt:Key` ni el nombre anterior `Auth:TotpProtectionKey`. Si la clave falta, no está identificada o no es Base64 de 32 bytes, la operación falla con `proteccion_secreto_no_disponible` y el proceso igual arranca. Un sobre alterado, de otro usuario, de otro propósito o de una clave que ya no está en lectura falla con `proteccion_secreto_rechazada`. Las pruebas generan las claves en memoria y no las imprimen. Los archivos versionados dejan la clave vacía.

Para provisionar: generar una sola vez, fuera de la API, 32 bytes y un identificador; poner el mismo identificador en `Activa` y en `Lectura`; repetir ese par en cada instancia; respaldarlo junto con `Jwt:Key` antes del primer secreto. Para rotar, publicar otro identificador como activo y conservar el anterior en lectura hasta reescribir los sobres. No se generó ni se mostró una clave real en esta etapa.

La migración incremental es `database/20261105_mfa_persistencia.sql`. No tiene `UPDATE` ni `DELETE`. Si hay hashes duplicados, aborta llamando a un procedimiento inexistente y no borra filas. El esquema limpio ya incluye las columnas. El rollback de esta etapa no toca los campos TOTP anteriores y no se ejecuta en la instalación.

En la copia local `cf_auth_mfa7a`, tomada de `cf_auth_stage` en `127.0.0.1:3308`, no había secretos, desafíos ni códigos. La migración conservó 21 usuarios, 2 empresas, 66 refresh, 30 invitaciones, 10 recuperaciones y 324 auditorías, además de los hashes, roles, empresas, estados y `TokenVersion`. Una segunda ejecución no los cambió. El esquema limpio `cf_auth_mfa7a_limpia` creó las columnas y el índice, sin usuarios. Una base con dos hashes iguales abortó, dejó las dos filas y no creó el índice. No se transformó ningún valor TOTP previo porque no había. Después de esa prueba se aplicó el mismo script a la base local del laboratorio y se eliminaron las tres bases temporales.

El SuperAdmin quedó activo, con `TokenVersion` 1, sin secreto y con `MfaObligatorio` en falso. No se repitió su login HTTP: el archivo temporal de su contraseña ya no existe, y un resto de tres dígitos fue rechazado por el mínimo de ocho caracteres antes de consultar la cuenta. La cuenta existente `existente@local.test` inició sesión con el contrato anterior: `200`, `token`, `mensaje` y `requiereCambioPassword` en falso, 1440 minutos, sin `sid` y con `token_use=web`. La consulta de login ya lee las columnas nuevas y no devolvió un desafío.

`dotnet build` terminó con la advertencia conocida `NU1902`. La suite con la exclusión ya documentada dejó 434 correctas, 0 con error y 1 omitida. La omitida sigue siendo la prueba productiva de fichadas biométricas.

Decisiones que esta etapa registra y no implementa:

- Con MFA obligatorio se puede reemplazar el autenticador mediante una verificación segura, y no se puede desactivar.
- El desafío queda vinculado a la generación y al modo de sesión desde su creación.
- Los límites del usuario persisten entre desafíos.
- La ventana TOTP tiene que ser configuración explícita y justificada, no el valor implícito de una biblioteca.
- Cada código de recuperación tendrá 128 bits de entropía, se entregará una sola vez y solo se guardará su hash.
- El token restringido de vinculación tendrá propósito y vencimiento propios, separados del JWT tradicional.

## Etapa 7B

Verifica el segundo factor de las cuentas que ya tienen `TotpHabilitado`. El padre es `5dd60b94d221ea8ee88ba69be1e8cecb8af53bcc`. No se modificó el frontend: sigue en `36dbe061c86035c60eaa79c56d1ebb02ea3e6e26` y todavía no presenta el desafío. No se abrió el enrolamiento, la desactivación, el reemplazo ni la administración. No se activó MFA ni `MfaObligatorio` en el SuperAdmin ni en los usuarios existentes.

Se agregó Otp.NET 1.4.1. NuGet lo declara compatible con todos los TFM del proyecto, incluido `net10.0`, y no trae dependencias. La documentación del paquete usa HMAC-SHA1, seis dígitos y 30 segundos como valores propios, pero su ventana predeterminada acepta solo el paso actual y `RfcSpecifiedNetworkDelay` acepta además un paso futuro. `ValidadorTotp` no usa ninguno de esos dos: fija `VerificationWindow(previous: 1, future: 0)`. Un paso anterior absorbe un autenticador o una red atrasados. Un paso futuro aceptaría un código que aún no es válido. Las pruebas cubren el paso actual, el anterior, el siguiente y el de 60 segundos atrás.

Una contraseña correcta con MFA responde `401` y `codigo=mfa_requerido`, con desafío y vencimiento, sin mensaje de éxito, sin JWT, sin refresh y sin cookie. La contraseña incorrecta conserva el error genérico. El desafío tiene 256 bits, se guarda solo su SHA-256, vence a los cinco minutos y admite cinco intentos. Queda atado al usuario, a `TokenVersion`, al propósito `login`, al modo y a la generación. El modo tradicional guarda la generación nula. No es un JWT. Origin y encabezados se evalúan antes de insertarlo.

`POST /api/Auth/mfa/verificar` es anónimo. El cliente no elige otro usuario, empresa, rol, modo ni generación. La transacción bloquea al usuario, relee desafío y cuenta, abre el secreto con `totp-activo` y exige un paso mayor que `UltimoPasoTotp`. El éxito confirma el paso, el consumo, los contadores y, en modo renovable, la familia. JWT y cookie salen después del commit. El tradicional conserva su duración. El renovable conserva `sid`, la cookie de la generación y el access de 15 minutos. Si la contraseña debe cambiarse, MFA se verifica primero y el middleware actual restringe la sesión.

El usuario admite cinco fallos y después queda bloqueado cinco minutos. El contador vuelve a cero cuando una verificación ve que ese bloqueo ya venció, antes de contar el intento, o cuando MFA se completa. Otro desafío no lo reinicia. El fallo se confirma antes de responder. Por IP hay diez emisiones y diez verificaciones por minuto. El `429` no cuenta como código incorrecto. Clave ausente, desconocida o cifrado inválido responden `503` con `mfa_no_disponible`, no omiten MFA y no suman un fallo de código.

Dos verificaciones del mismo desafío dejan un solo éxito. El mismo paso no se consume dos veces, tampoco entre dos sesiones distintas de MySQL: cada una abre su propio `DbContext` y el bloqueo es `FOR UPDATE`. Si la respuesta se pierde después del commit, el desafío queda consumido. La cookie sigue aislada por generación y `TokenVersion` sigue invalidando el access.

`dotnet build` terminó con la advertencia conocida `NU1902`. La suite con la exclusión ya documentada dejó 454 correctas, 0 con error y 1 omitida. La omitida sigue siendo la prueba productiva de fichadas biométricas.

La clave de laboratorio está solo en `ControlFichajes.API/appsettings.Development.local.json`, en `Auth:TotpClaves`, identificador `lab7b`. El archivo sigue ignorado. No se reutilizó `Jwt:Key` ni se copió `Auth:TotpProtectionKey`. Hay que respaldarla con `Jwt:Key` antes de guardar un secreto real. Las pruebas no la imprimen. Una cuenta ficticia se creó y se borró en la base local; el SuperAdmin siguió sin secreto, sin MFA y sin obligación.

## Próximo paso

La etapa 7B queda para revisión. El frontend todavía no presenta el desafío. El enrolamiento, la desactivación, el reemplazo y el restablecimiento administrativo siguen pendientes. No generar un bundle.

## Texto para una conversación nueva

```text
Leé api/api-nueva-20261023/docs/CONTINUIDAD_AUTENTICACION.md.
Después comprobá el estado real: rama, git status, git rev-parse HEAD y git rev-parse '@{upstream}'.
No continúes si la rama no es `feat/autenticacion-segura` o si el árbol no está limpio. No hay upstream.
La etapa 7B verifica TOTP en el login de las cuentas que ya lo tienen configurado. El frontend de 6B está en 36dbe061c86035c60eaa79c56d1ebb02ea3e6e26 y todavía no presenta el desafío.
No generes un bundle y no implementes el enrolamiento.
```
