# ControlFichajes.API

API REST para gestionar empresas, empleados, huellas biométricas y fichadas.

El backend es la fuente de verdad del contrato que consumirán posteriormente el cliente biométrico y el frontend.

## Requisitos

- .NET 10 SDK
- MySQL 8
- Docker opcional para desplegar en el servidor

## Contrato de la API

Todos los endpoints siguientes, excepto el login y la autenticación del agente, requieren:

```http
Authorization: Bearer <token>
```

### Autenticación

```http
POST /api/auth/login
Content-Type: application/json

{
    "email": "usuario@dominio.local",
    "password": "********"
}
```

La respuesta contiene `token` y `mensaje`.

Las contraseñas almacenadas en `Usuario.PasswordHash` deben ser hashes generados con `PasswordHasher<Usuario>`.

### Registro de usuarios

`POST /api/auth/bootstrap` está apagado. `Auth:PermitirBootstrap` vale `false` en
`appsettings.json`, en el ejemplo de producción y en Docker. Si la clave falta,
también queda apagado: el entorno Development no lo enciende.

Con el flag apagado el endpoint responde `404 Not Found`, aunque `Usuario` esté
vacía. No crea la cuenta ni devuelve un JWT. Un intento queda en
`AuditoriaSeguridad` como `bootstrap_deshabilitado` / `rechazado`, sin correo,
contraseña ni cuerpo del request.

Solo un laboratorio local puede encenderlo, y únicamente en configuración que no
se versiona, por ejemplo `appsettings.Development.local.json` o una variable de
entorno del proceso. En ese caso sigue exigiendo que `Usuario` esté vacía. Si ya
hay alguna fila, responde `409 Conflict`. El rol del cuerpo se ignora: la cuenta
creada es `ADMIN`. El cuerpo no puede crear un `SuperAdmin`.

Después, un administrador puede registrar a sus compañeros mediante:

```http
POST /api/usuarios
Authorization: Bearer <token-del-admin>
Content-Type: application/json

{
    "empresaId": 1,
    "nombreUsuario": "Juan Pérez",
    "email": "juan@empresa.local",
    "password": "OtraClaveSegura123",
    "rol": "RRHH"
}
```

Solo se permiten los roles `ADMIN` y `RRHH`.

El administrador no puede crear usuarios para otra empresa. Las contraseñas se almacenan como hash y nunca se devuelven en la respuesta.

Si ya existe algún usuario y el bootstrap fue habilitado de forma explícita,
`POST /api/auth/bootstrap` responde `409 Conflict`.

En el funcionamiento normal se utiliza el token de un administrador para crear
nuevos usuarios. `POST /api/usuarios` sigue aceptando contraseña mientras
`Auth:PermitirAltaConPassword` sea `true`.

### Invitación

`POST /api/usuarios/invitar` usa las mismas reglas de empresa y rol que el alta
con contraseña. No recibe ni devuelve contraseña, JWT ni token de invitación.
El usuario queda inactivo, con `InvitadoEn` cargado y una contraseña que no se
conserva en claro. El cuerpo no puede crear un `SuperAdmin`.

El token sale de `RandomNumberGenerator` con 32 bytes, se codifica en Base64
url-safe y en la base solo se guarda su SHA-256. Vence a las
`Auth:InvitacionHoras`. Una invitación nueva deja vencidas las anteriores
vigentes del mismo usuario. Esa invalidación y el alta del hash ocurren en una
transacción: MySQL toma `SELECT ... FOR UPDATE` sobre la fila del usuario, así
otras instancias de la API esperan su turno. `POST /api/usuarios/{id}/reenviar-invitacion`
emite otro token; no reutiliza el anterior. El envío SMTP queda fuera de la transacción.

El enlace usa `App:PublicBaseUrl` y el correo sale por `ICorreoSaliente`.
MailKit lee `Smtp:Host`, `Smtp:Port`, `Smtp:User`, `Smtp:Password`, `Smtp:From`
y `Smtp:EnableSsl`. El laboratorio local apunta el SMTP a `127.0.0.1:1025`.
Esa dirección no va en la configuración de producción. El mensaje tiene texto
y HTML. Los logs no guardan el cuerpo, el enlace ni el token.

La cuenta y el hash se confirman antes de abrir SMTP. Si el envío falla, la
cuenta sigue inactiva, el hash permanece y la respuesta es `503` con un mensaje
genérico. No hay rollback: así un reenvío posterior puede invalidar esa
invitación y emitir otra.

`POST /api/Auth/invitacion/aceptar` es anónimo. El cuerpo solo trae el token,
la contraseña y su confirmación. Un `usuarioId`, `empresaId`, `rol` o `activo`
del cuerpo no elige la cuenta: la cuenta sale del SHA-256 del token. No había
una política de contraseña compartida. La aceptación reutiliza las reglas que
ya exigía `POST /api/Auth/cambiar-password`: entre 8 y 20 caracteres, con letra,
dígito y un signo, sin espacios. El hash sigue siendo `PasswordHasher<Usuario>`.
El alta tradicional conserva su límite de 8 a 255 porque ese contrato no cambia.

La aceptación toma la misma fila de `Usuario` con `SELECT ... FOR UPDATE`,
relee la invitación y la cuenta, y recién ahí guarda el hash, marca
`CorreoVerificadoEn` e `InvitacionAceptadaEn`, activa al usuario, marca la
invitación usada e invalida las otras vigentes. `RequiereCambioPassword` queda
en falso porque la persona acaba de elegir la clave. Empresa y rol no se tocan.
Si algo falla antes del commit, la transacción vuelve atrás. La respuesta de
éxito es solo un mensaje: no inicia sesión. El ingreso posterior es `Login`.
Token inexistente, vencido, invalidado o usado devuelve el mismo `400`, sin
datos de la cuenta.

Si la aceptación obtiene el bloqueo antes que un reenvío, la cuenta queda
activa y el reenvío encuentra que ya no hay invitación pendiente. Si el reenvío
lo obtiene antes, vence el token anterior, emite otro y la aceptación de ese
token se rechaza; la cuenta sigue inactiva. Dos aceptaciones del mismo token
dejan una sola aceptación exitosa.

No había un limitador de solicitudes en la API. Este endpoint usa el limitador
de ASP.NET Core, sin un paquete nuevo: 5 solicitudes por 60 segundos y por
dirección IP. Al superarlo responde `429`. Los logs y la auditoría no guardan
el token, la contraseña, la confirmación, el enlace ni el cuerpo.

### Recuperación de contraseña

La tabla `RecuperacionPassword` ya estaba en el esquema. No se agrega migración.
`Auth:RecuperacionHoras` era un valor de reserva, de una hora, que ningún flujo
leía. La expiración efectiva es `Auth:RecuperacionMinutos`, con valor inicial
de 30 minutos.

`POST /api/Auth/recuperacion/solicitar` es anónimo y recibe el correo. Se recorta,
se pasa a minúsculas y se valida el formato. Un formato inválido responde `400`
sin repetir el valor. Un formato válido responde siempre `200` con el mismo
mensaje, exista o no la cuenta, esté inactiva, no tenga el correo verificado o
falle el SMTP. Solo una cuenta activa con `CorreoVerificadoEn` genera un token.
La recuperación no activa cuentas ni reemplaza la aceptación de una invitación.
El correo guardado no se reescribe: la búsqueda no distingue mayúsculas.

El token sale de `TokenOpaco`: 32 bytes de `RandomNumberGenerator`, Base64
url-safe. En la base queda solo el SHA-256. Una solicitud nueva vence las
recuperaciones vigentes anteriores del mismo usuario. Eso ocurre en una
transacción con `SELECT ... FOR UPDATE` sobre la fila de `Usuario`, el mismo
bloqueo que usa la invitación, así varias instancias quedan en serie. El hash
se confirma vigente antes de abrir SMTP. El envío queda fuera de la transacción.
Si el envío falla, el hash permanece, se audita el fallo y la respuesta pública
sigue siendo la genérica. El enlace usa `App:PublicBaseUrl` y
`/restablecer-contrasena`. El mensaje tiene texto y HTML. `ICorreoSaliente`
hace el envío.

`POST /api/Auth/recuperacion/restablecer` es anónimo. El cuerpo solo trae el
token, la contraseña y su confirmación. La cuenta sale del hash del token.
`PasswordPolitica` y `PasswordHasher<Usuario>` son los mismos de la aceptación.
Dentro de la transacción se bloquea la fila, se relee el estado y solo entonces
se guarda el hash, se marca la recuperación usada, se vencen las otras vigentes
y `RequiereCambioPassword` queda en falso. Empresa y rol no cambian. El éxito
es solo un mensaje: no inicia sesión.

Ese guardado incrementa `TokenVersion`. El middleware web compara el claim
`token_version` y rechaza el JWT anterior. El token de `Auth/agente` no lleva
`token_version` y ese middleware no lo evalúa contra `Usuario`, así que su
contrato no cambia y no se agrega otro registro de sesiones.

Si la solicitud obtiene el bloqueo antes que el restablecimiento, el token
anterior vence y ese restablecimiento se rechaza: la contraseña y
`TokenVersion` quedan como estaban. Si el restablecimiento lo obtiene antes,
la contraseña cambia, el token queda usado y `TokenVersion` aumenta; una
solicitud que entre después emite otro token para la cuenta, que sigue activa.
Dos restablecimientos del mismo token dejan un solo éxito. Un fallo antes del
commit no cambia contraseña, token ni `TokenVersion`.

Un token inexistente, vencido, usado, invalidado o de una cuenta que ya no está
activa y verificada devuelve el mismo `400`. Un token de invitación no
restablece la contraseña y uno de recuperación no acepta una invitación.

La solicitud y el restablecimiento tienen límites propios, por dirección IP:
5 por 60 segundos cada uno (`Auth:RecuperacionSolicitudLimite`,
`Auth:RecuperacionSolicitudVentanaSegundos`, `Auth:RecuperacionRestablecerLimite`
y `Auth:RecuperacionRestablecerVentanaSegundos`). Al superarlos responden `429`.
La auditoría registra la solicitud, la invalidación, el envío aceptado o fallido
y el restablecimiento. No guarda token, contraseña, confirmación, enlace ni cuerpo.

### Sesión web con refresh rotativo

La tabla `RefreshToken` ya estaba en el esquema. No se agrega migración. `FamiliaId` sigue siendo `char(36)`. En el modelo es `Guid` porque el conector lee esa columna como identificador, no como texto. El plazo
absoluto es `Jwt:RefreshExpireDays`, 14 días desde `CreadaEn` de la primera fila
de la familia. La inactividad es `Jwt:RefreshInactividadHoras`, 12 horas. Cada
rotación deja `ExpiraEn` en el menor de esos dos plazos. Una rotación no mueve
el inicio de la familia ni alarga el plazo absoluto. El access con `sid`, tanto
en el login renovable como en cada refresh, dura `Jwt:AccessExpireMinutes`. El
valor versionado es 15. Si falta, está vacío o no es un entero positivo, también
queda en 15 y no hereda `Jwt:ExpireMinutes`. El login tradicional, sin `sid`, y
`POST /api/Auth/agente` siguen en `Jwt:ExpireMinutes`. `Jwt:AgentExpireMinutes`
sigue sin leerse. `Jwt:RefreshExpireDays` y `Jwt:RefreshInactividadHoras` no
cambian.

`POST /api/Auth/Login` sin `X-CF-Solicitud` responde igual que antes y no crea
una familia ni escribe la cookie. Si el encabezado está presente y no vale `1`,
la respuesta es `403` y no se intenta el login. Con `X-CF-Solicitud: 1` hace
falta un `Origin` de `Cors:AllowedOrigins`. `Origin` ausente, vacío, `null` o
ajeno a la lista también responde `403`, antes de tocar filas o cookies. El
login renovable escribe la cookie `cf_refresh` solo después de autenticar y
confirmar la fila. Es `HttpOnly`, de host, con `Path=/api/Auth`. `Auth:CookieSameSite`
vale `Lax` por defecto. `Auth:CookieSecure` es opcional: sin valor, `Secure`
queda apagado solo en Development sobre HTTP. `SameSite=None` fuerza `Secure`.
El borrado usa el mismo nombre, `Path` y omite `Domain`, igual que la creación.
El vencimiento que recibe el navegador es el `ExpiraEn` del refresh, con tope de
`Jwt:RefreshExpireDays`, y también se envía `Max-Age`.

### Cookie por generación

Si no llega `X-CF-Generacion`, sigue el modo anterior y la cookie es `cf_refresh`.
Si el encabezado llega, tiene que ser un UUID canónico de 36 caracteres, con
guiones. Un valor presente y mal formado responde `403` y no vuelve al modo
anterior ni escribe cookies. Este modo también exige `X-CF-Solicitud: 1` y un
origen permitido. Cada login renovable usa una generación nueva. Si esa
generación ya está asociada a una familia, responde `403` y no emite otra cookie.

El nombre de la cookie sale de la generación: `cf_g_` más los 32 hexadecimales
del UUID. El identificador no autentica y no reemplaza al refresh token. Se
guarda en `RefreshToken.Generacion`, que queda nulo en las familias del modo
anterior. No hace falta otra tabla. Una base que ya aplicó
`20261103_autenticacion_seguridad.sql` agrega la columna con
`database/20261104_sesion_generacion.sql`. Ese script no borra filas ni cookies.

Refresh y cierre leen solo la cookie de la generación pedida y comprueban que
el token pertenezca a una familia con esa misma generación. Si esa cookie no
está, no se busca otra. Una respuesta solo crea, reemplaza o borra la cookie de
su generación. Por eso un `Set-Cookie` tardío de una generación anterior no
pisa ni borra la cookie de un login posterior. No se usa una comprobación de
“generación vigente” como sustituto: una vez enviada la respuesta, el navegador
igual aplicaría ese `Set-Cookie`.

El cierre de una generación no borra cookies de las demás. El login nuevo
tampoco las borra ni revoca esas familias. La familia que queda sin uso sigue
hasta su inactividad, su plazo absoluto, el cierre de su propia generación, la
reutilización de su token o un cambio de `TokenVersion`. No hay una limpieza
global: borraría o pisaría la sesión posterior. La cookie abandonada vence sola
en el navegador, al `Max-Age` con el que se escribió.

Si ese login no trae `X-CF-Generacion` y presenta una cookie de una familia del mismo usuario, esa familia
se revoca en la misma operación y las demás sesiones siguen. Un login fallido
no revoca la anterior. El alta tradicional, el bootstrap y `Auth/agente` no
crean familias ni cookies. El access de la sesión nueva lleva `sid` y
`token_version`.

`POST /api/Auth/refresh` lee el refresh solo desde la cookie y responde las tres
claves del login. En MySQL bloquea la fila del usuario con `SELECT ... FOR UPDATE`,
relee usuario y token, consume el anterior e inserta un sucesor. La cookie sale
después del commit. No hay ventana de gracia. Si el token ya fue consumido, se
confirma la revocación de toda la familia y recién ahí se responde `401`. Un
fallo antes de ese commit no deja la revocación hecha. Dos refresh simultáneos
del mismo token pueden terminar en `200` y `401`, con la familia revocada: el
`200` no deja una sesión usable. Si la respuesta se pierde después del commit
y el navegador conserva el token consumido, el reintento revoca la familia y
hay que volver a iniciar sesión. El frontend tiene que coordinar las pestañas
antes de activar este flujo.

`POST /api/Auth/cerrar-sesion` exige la misma protección, revoca solo esa
familia, borra la cookie y responde `204`. Cookie ausente, desconocida o
vencida también responde `204` una vez pasado el origen. El middleware, para
un JWT web con `sid`, exige una fila de esa familia del mismo usuario, sin
revocar, sin reemplazar, con el mismo `TokenVersion` y dentro de ambos plazos.
Un `sid` desconocido o de otra cuenta responde `401`. Un JWT web anterior, sin
`sid`, conserva la validación de usuario activo y `token_version`. El cierre
no lo puede invalidar. `Auth/agente` no entra en esa consulta. Una solicitud
que ya pasó el middleware puede completar; la siguiente, no.

Cambiar la contraseña, el restablecimiento administrativo, la recuperación, el
estado, el rol o la identidad siguen incrementando `TokenVersion` y, en esa
misma transacción, revocan todas las familias del usuario. El desbloqueo y
`X-Empresa-Id` no. Cerrar una sesión no incrementa `TokenVersion`, así que las
otras sesiones del usuario siguen vigentes.

`OPTIONS` lo responde CORS y no ejecuta estas operaciones. La política permite
credenciales solo para los orígenes explícitos. Refresh y cierre tienen límite
propio por IP: 30 por 60 segundos (`Auth:RefreshLimite`,
`Auth:RefreshVentanaSegundos`, `Auth:CierreLimite`,
`Auth:CierreVentanaSegundos`). Al superarlo responden `429`. Esas respuestas,
igual que el login, van con `Cache-Control: no-store`. Los logs y la auditoría
no guardan la cookie, el token, el encabezado `Authorization` ni el cuerpo.

### Persistencia de MFA

Esta etapa no cambia `POST /api/Auth/Login`, no abre el enrolamiento y no exige MFA. `Auth:ExigirMfa` y `Auth:MfaGraciaDias` siguen sin efecto. `MfaObligatorio` nace en falso. Las cuentas existentes, incluido el SuperAdmin, conservan contraseña, rol, empresa, estado, `TokenVersion` y acceso.

El secreto activo sigue en `Usuario.TotpSecretoCifrado`. El pendiente va en `TotpPendienteCifrado` y vence en `TotpPendienteExpiraEn`. Esos valores, cuando una etapa posterior los escriba, son un sobre AES-GCM: versión, identificador de clave, nonce, ciphertext y etiqueta. El cifrado autentica también el id del usuario y el propósito (`totp-activo` o `totp-pendiente`), así que un sobre no se puede mover de cuenta ni de uso. `IProtectorSecretosMfa` usa la clave activa para cifrar y cualquier clave de `Auth:TotpClaves:Lectura` para abrir. No lee `Jwt:Key` ni `Auth:TotpProtectionKey`. Si la clave activa falta, no está entre las de lectura, o el material no es Base64 de 32 bytes, proteger y abrir fallan con `proteccion_secreto_no_disponible`. El arranque no genera una clave y el login de quien no tiene MFA sigue igual.

La misma `Activa` y el mismo conjunto de lectura tienen que estar en cada instancia. Se provisionan fuera del repositorio, se respaldan junto con `Jwt:Key` antes del primer secreto y no se copian a archivos versionados. Para rotar se publica un id nuevo como activo y se conserva el anterior en lectura hasta reescribir los sobres.

`MfaIntentosFallidos` y `MfaBloqueadoHasta` quedan en el usuario. Crear otro `DesafioMfa` no los reinicia. El desafío guarda, desde que exista, `TokenVersion`, `Generacion`, `ModoSesion`, propósito, vencimiento e `Intentos`. `CodigoRecuperacionMfa.CodigoHash` tiene índice único. La migración lo crea solo si no hay hashes duplicados; si los hay, no borra filas.

Una base que ya aplicó `20261104_sesion_generacion.sql` continúa con `database/20261105_mfa_persistencia.sql`. El esquema limpio ya incluye esas columnas. El rollback de esta etapa no se usa en la instalación y no toca los campos TOTP anteriores.

Lo que queda para después, sin implementarlo aquí: con MFA obligatorio se puede reemplazar el autenticador mediante verificación segura y no se puede desactivar. Cada código de recuperación tendrá 128 bits de entropía, se mostrará una sola vez y solo se guardará su hash. El token restringido de vinculación tendrá propósito y vencimiento propios, separados del JWT tradicional.

### Desafío de login y TOTP

Solo una cuenta con `TotpHabilitado` pasa por el segundo factor. El enrolamiento, la desactivación, el reemplazo y la administración de MFA no están expuestos. El frontend todavía no presenta el desafío. Las cuentas sin MFA, incluido el SuperAdmin compartido, conservan el login anterior y no se les activa MFA ni `MfaObligatorio`.

La validación usa Otp.NET 1.4.1, compatible con `net10.0` y sin dependencias propias. `ValidadorTotp` fija HMAC-SHA1, seis dígitos y pasos de 30 segundos. La tolerancia inicial es el paso actual y uno anterior, sin paso futuro: `VerificationWindow(previous: 1, future: 0)`. No se usa el valor de la biblioteca, que acepta solo el paso actual, ni `RfcSpecifiedNetworkDelay`, que también acepta un paso futuro. Un paso atrás cubre un autenticador o una red que llegan tarde. Un paso adelante aceptaría un código que todavía no es válido y ensancha la ventana hacia el futuro.

Una contraseña correcta con MFA configurado no entrega JWT ni refresh, no crea familia y no modifica cookies. Responde `401` con `codigo=mfa_requerido`, un desafío opaco y su vencimiento. Esa respuesta no lleva el mensaje de autenticación exitosa. La contraseña incorrecta sigue con el error genérico. El desafío sale de `RandomNumberGenerator` con 256 bits; en base queda solo su SHA-256. Vence a los `Auth:MfaDesafioMinutos` (5) y admite `Auth:MfaIntentosDesafio` (5) intentos. Desde su creación queda atado al usuario, a `TokenVersion`, al propósito `login`, al modo y a la generación. En modo tradicional la generación se guarda nula. No es un JWT y no autoriza endpoints de negocio. Origin y encabezados se validan antes de crear ese estado.

`POST /api/Auth/mfa/verificar` es anónimo y recibe el desafío y el código. El cliente no elige usuario, empresa, rol, modo ni generación: los encabezados tienen que coincidir con el contexto guardado. Dentro de una transacción se bloquea la fila del usuario y se releen desafío y cuenta. El secreto se abre con el propósito `totp-activo`. El paso coincidente tiene que ser mayor que `UltimoPasoTotp`. En éxito se confirman en la misma transacción el paso usado, el desafío consumido, el reinicio de los contadores y, si el modo guardado es renovable, la familia de refresh. El JWT y la cookie se emiten después del commit, con los servicios que ya existían. La respuesta exitosa conserva `token`, `mensaje` y `requiereCambioPassword`. El modo renovable conserva `sid`, la cookie de su generación y el access de 15 minutos. El tradicional conserva su duración. Si hay que cambiar la contraseña, MFA se verifica primero y el middleware actual deja la sesión restringida.

`MfaIntentosFallidos` y `MfaBloqueadoHasta` siguen en el usuario. El límite es `Auth:MfaIntentosUsuario` (5) y el bloqueo dura `Auth:MfaBloqueoMinutos` (5). El contador vuelve a cero solo cuando una verificación observa que el bloqueo ya venció, antes de contar el intento nuevo, o cuando MFA se completa. Crear otro desafío no borra el bloqueo ni reinicia el presupuesto. Los fallos se confirman antes de responder. La emisión admite `Auth:MfaEmisionLimite` (10) cada `Auth:MfaEmisionVentanaSegundos` (60) por IP. La verificación admite `Auth:MfaVerificacionLimite` (10) cada `Auth:MfaVerificacionVentanaSegundos` (60). Agotar ese límite responde `429` y no cuenta como código incorrecto. Una clave ausente, desconocida o un cifrado inválido responden `503` con `codigo=mfa_no_disponible`, no omiten MFA, no registran el secreto y no incrementan los fallos del usuario.

Dos verificaciones del mismo desafío dejan un solo éxito. El mismo paso no se puede consumir en otro desafío ni desde otra instancia: la fila queda bloqueada y `UltimoPasoTotp` se confirma en la misma transacción. Si la respuesta se pierde después del commit, el desafío sigue consumido. Hay que iniciar otro login, con otra generación, y usar un paso que todavía no se haya consumido. La cookie sigue aislada por generación y `TokenVersion` sigue invalidando el access.

La clave de laboratorio vive solo en `ControlFichajes.API/appsettings.Development.local.json`, bajo `Auth:TotpClaves`. Ese archivo está ignorado por Git. No reutiliza `Jwt:Key` ni el valor anterior `Auth:TotpProtectionKey`. Hay que respaldarla junto con `Jwt:Key` antes de guardar un secreto real. Las pruebas de unidad usan claves efímeras en memoria.

### Enrolamiento voluntario

`Auth:PermitirEnrolamientoMfa` está en falso. Mientras siga así, iniciar, confirmar y regenerar responden 404. El frontend todavía no los presenta. No se exige MFA a las cuentas existentes.

`POST /api/Auth/mfa/enrolamiento/iniciar` es autenticado. Sirve para una cuenta activa, sin MFA y sin cambio de contraseña pendiente, y pide la contraseña actual. Guarda un secreto nuevo cifrado con `totp-pendiente` y una operación ligada al usuario, a `TokenVersion` y a la sesión que la pidió. Vence a los `Auth:MfaEnrolamientoMinutos` (10). Responde la operación, la URI `otpauth` y la clave manual, con `Cache-Control: no-store`. No llama a un servicio externo para dibujar el código. Otro inicio invalida la operación anterior. Si MFA ya está configurado, responde `409` con `mfa_ya_configurado`.

`POST /api/Auth/mfa/enrolamiento/confirmar` exige esa sesión y esa operación. Abre el secreto pendiente y lo vuelve a cifrar con `totp-activo`; no copia el sobre de un propósito al otro. Activa MFA, recuerda el paso, borra el pendiente, emite diez códigos, incrementa `TokenVersion`, invalida los desafíos abiertos y revoca las familias. La respuesta, una sola vez, trae los códigos y pide volver a iniciar sesión. Los intentos fallidos siguen en `MfaIntentosFallidos`: empezar de nuevo no los pone en cero.

Cada código tiene 128 bits. Se muestra en Base32 separado por guiones. La normalización quita espacios y guiones y pasa a mayúsculas. En base se guarda el SHA-256, el conjunto, la creación y el uso. `POST /api/Auth/mfa/verificar` acepta un TOTP o un código de recuperación, nunca los dos juntos. El código se consume junto con el desafío y la sesión. No apaga MFA ni su obligación, y cuenta para el mismo límite de intentos.

`POST /api/Auth/mfa/recuperacion/regenerar` pide la contraseña y un segundo factor vigente. Marca como usado el conjunto anterior, emite otro, sube `TokenVersion` y revoca las sesiones. El autenticador queda igual. Si la respuesta de confirmación o de regeneración se pierde, esos códigos en claro no se pueden volver a pedir: hay que entrar con el autenticador y regenerar. Esta etapa no reemplaza un autenticador ya configurado.

Una base que ya aplicó `20261105_mfa_persistencia.sql` continúa con `database/20261106_mfa_enrolamiento.sql`. Si ya hay códigos, ese script aborta y no les inventa un conjunto. El esquema limpio ya trae la tabla y las columnas.

### Alta inicial de SuperAdmin

Esta cuenta no se crea por un endpoint público. La inserta un administrador de
la base, con la API detenida o sin usar `POST /api/auth/bootstrap`.

No guardar la contraseña ni el hash resultante en el repositorio, en argumentos
del proceso ni en logs. El hash se genera en el momento con
`PasswordHasher<Usuario>` de ASP.NET Core Identity, el mismo que usa la API.
Cada ejecución produce un hash distinto. No reutilizar un hash de ejemplo.

En una consola local, leer la contraseña desde el teclado y escribir solo el
hash. Descartar el programa al terminar:

```csharp
using ControlFichajes.API.Models;
using Microsoft.AspNetCore.Identity;

var hasher = new PasswordHasher<Usuario>();
Console.Write("Contraseña: ");
var password = LeerOculta();
Console.WriteLine(hasher.HashPassword(new Usuario(), password));

static string LeerOculta()
{
    var buffer = new System.Text.StringBuilder();
    while (true)
    {
        var tecla = Console.ReadKey(intercept: true);
        if (tecla.Key == ConsoleKey.Enter)
            break;
        if (tecla.Key == ConsoleKey.Backspace && buffer.Length > 0)
        {
            buffer.Length--;
            continue;
        }

        if (!char.IsControl(tecla.KeyChar))
            buffer.Append(tecla.KeyChar);
    }

    Console.WriteLine();
    return buffer.ToString();
}
```

La empresa ya tiene que existir. El correo se guarda como lo normaliza el login:
sin espacios al inicio ni al final, y con las mayúsculas tal como se van a
escribir después. El rol exacto es `SuperAdmin`. `Activo` queda en `1`.

```sql
INSERT INTO `Usuario` (
  `EmpresaId`, `NombreUsuario`, `Correo`, `PasswordHash`, `Rol`, `Activo`,
  `RequiereCambioPassword`, `IntentosFallidos`, `PasswordTemporalUsada`,
  `TokenVersion`, `TotpHabilitado`
) VALUES (
  /* REEMPLAZAR_EMPRESA_ID */ 0,
  'REEMPLAZAR_NOMBRE',
  'REEMPLAZAR_CORREO',
  'REEMPLAZAR_HASH_PASSWORDHASHER',
  'SuperAdmin',
  1,
  0,
  0,
  0,
  0,
  0
);
```

Comprobar empresa, correo, rol y estado antes de iniciar sesión:

```sql
SELECT e.Id AS EmpresaId, e.CUIT, u.Correo, u.Rol, u.Activo, u.TokenVersion,
       CHAR_LENGTH(u.PasswordHash) AS LargoHash
FROM `Usuario` u
JOIN `Empresa` e ON e.Id = u.EmpresaId
WHERE u.Correo = 'REEMPLAZAR_CORREO';
```

El resultado esperado es la empresa elegida, el correo sin espacios, `Rol`
igual a `SuperAdmin`, `Activo` en `1` y un hash de Identity V3. Ese hash
empieza con `AQAAAA` y no es la contraseña.

Si la cuenta quedó mal, revocarla sin borrar historia de otras tablas:

```sql
UPDATE `Usuario`
SET `Activo` = 0, `TokenVersion` = `TokenVersion` + 1
WHERE `Correo` = 'REEMPLAZAR_CORREO' AND `Rol` = 'SuperAdmin';
```

Eso impide el login y deja inválidos los JWT web ya emitidos. Si la fila no
llegó a usarse y hay que eliminarla:

```sql
DELETE FROM `Usuario`
WHERE `Correo` = 'REEMPLAZAR_CORREO' AND `Rol` = 'SuperAdmin';
```

Las observaciones y reposiciones que apunten a ese usuario quedan con el autor
en NULL. Las tablas de invitación, recuperación, refresh y MFA se borran en
cascada. `AuditoriaSeguridad` no se borra con la cuenta.

### Flujo del SuperAdmin

La cuenta tiene que existir en la base con `Rol = "SuperAdmin"` y
`Activo = true`. No sale de `POST /api/auth/bootstrap`.

1. Iniciar sesión con `POST /api/auth/login` usando las credenciales del SuperAdmin.
2. Guardar el JWT recibido. El token de SuperAdmin no contiene `empresa_id`.
3. Consultar todas las empresas sin seleccionar un tenant:

```http
GET /api/empresas
Authorization: Bearer <token-superadmin>
```

4. Seleccionar la empresa operativa enviando `X-Empresa-Id` en cada endpoint que
trabaja sobre una empresa concreta:

```http
GET /api/sucursales
Authorization: Bearer <token-superadmin>
X-Empresa-Id: 2
```

El contexto de empresa se aplica únicamente a SuperAdmin. Los usuarios `ADMIN`
y `RRHH` utilizan el `empresa_id` incluido en su propio JWT y no pueden cambiar
de empresa mediante este header.

5. Administrar agentes biométricos. Estas operaciones son globales y requieren
únicamente el JWT del SuperAdmin:

```http
POST /api/agentes
GET /api/agentes
GET /api/agentes/{id}
POST /api/agentes/{id}/rotar-secret
PATCH /api/agentes/{id}/desactivar
```

Para crear un agente se debe indicar la sucursal a la que quedará vinculado:

```json
{
   "sucursalId": 2,
   "clientId": "lector-sucursal-2",
   "nombre": "Lector principal"
}
```

La respuesta de alta o rotación contiene el `clientSecret` en claro por única
vez. Debe entregarse al instalador biométrico y no registrarse en logs ni
guardarse en el frontend.

### Agentes biométricos

Los agentes son instalaciones de servicio vinculadas a una sucursal. No utilizan el
login humano ni se almacenan como usuarios en la tabla `Usuario`. La API persiste
el hash del secreto en `AgenteInstalaciones.ClientSecretHash` y solo devuelve el
secreto en claro al crear o rotar una instalación.

```text
POST  /api/auth/agente
POST  /api/agentes
GET   /api/agentes
GET   /api/agentes/{id}
POST  /api/agentes/{id}/rotar-secret
PATCH /api/agentes/{id}/desactivar
GET   /api/agentes/sucursal
POST  /api/agentes/{id}/heartbeat
```

El login de agente recibe `clientId` y `clientSecret` y devuelve un JWT con:

- `token_use = agent`
- `agente_id`
- `empresa_id`
- `sucursal_id`
- rol `AGENTE_SUCURSAL`

Además de los endpoints de gestión, el agente autenticado puede consultar su
sucursal asociada mediante:

```http
GET /api/agentes/sucursal
Authorization: Bearer <token-agent>
```

La respuesta reutiliza el DTO de sucursal y devuelve `serialLector` para que
la aplicación local pueda preguntar cuál es el lector de la sucursal a la que
está ligada, sin exigir un JWT web administrativo ni subvertir el tenant de la
empresa.

```http
POST /api/auth/agente
Content-Type: application/json

{
  "clientId": "lector-central",
  "clientSecret": "********"
}
```

Solo `SuperAdmin` puede crear, listar, consultar, rotar o desactivar agentes.
El heartbeat solo acepta el token del agente cuyo `agente_id` coincide con la
ruta. La tabla actual solo dispone de `UltimoAcceso`, por lo que ese es el dato
de estado persistido.

Los tokens de agente pueden consumir el catálogo de empleados, huellas,
enrolamiento y `POST /api/fichadas/biometricas/bulk`. También se conserva
`POST /api/fichadas/bulk` para integraciones existentes. Los agentes no pueden
acceder a las operaciones web de gestión, empresas, sucursales, usuarios ni
fichadas históricas.

## Empresas, usuarios, sucursales y departamentos

### Empresas

```text
GET  /api/empresas
POST /api/empresas
```

- `GET /api/empresas`: devuelve todas las empresas para `SuperAdmin`; para `ADMIN` y `RRHH`, devuelve únicamente la empresa indicada por `empresa_id` en el JWT.
- `POST /api/empresas`: crea una nueva empresa y solo lo puede hacer un usuario con rol `SuperAdmin`.

### Usuarios

```text
GET    /api/usuarios
POST   /api/usuarios
PATCH  /api/usuarios/{id}/estado
PATCH  /api/usuarios/{id}/rol
POST   /api/usuarios/{id}/restablecer-password
POST   /api/usuarios/{id}/desbloquear
POST   /api/auth/cambiar-password
```

No existe `DELETE /api/usuarios/{id}` ni baja física. La baja se representa con
`Activo = false` y la reactivación con `Activo = true`. La cuenta inactiva
conserva identidad, empresa, rol, fichadas, relaciones e historial.

- Registra un usuario de la empresa autenticada.
- Puede ejecutarlo un usuario con rol `ADMIN` o `SuperAdmin`.
- `ADMIN` solo puede crear usuarios `RRHH` de su propia empresa; el `empresaId`
  del body se ignora y se fuerza el `empresa_id` del JWT. Un `"rol": "ADMIN"`
  enviado por `ADMIN` se rechaza con `400`.
- `SuperAdmin` debe seleccionar la empresa mediante `X-Empresa-Id` coincidente
  con `body.empresaId` y puede crear `ADMIN` o `RRHH`.
- Los roles permitidos en alta y en cambio de rol son `ADMIN` y `RRHH`. Nadie
  puede crear ni asignar `SuperAdmin` desde estos endpoints.
- El `GET /api/usuarios` expone un listado seguro con un DTO mínimo que no
  devuelve `PasswordHash`, secretos, `Jwt`, ni contraseñas temporales.
- El listado devuelve exclusivamente:

```json
{
  "id": 1,
  "empresaId": 1,
  "nombreUsuario": "Juan Pérez",
  "correo": "juan@empresa.local",
  "rol": "RRHH",
  "activo": true,
  "requiereCambioPassword": false,
  "bloqueado": false,
  "bloqueadoHasta": null
}
```

Matriz de alcance de administración de usuarios:

- `SuperAdmin`: puede listar, crear, cambiar estado, cambiar rol (`ADMIN` ↔
  `RRHH`), restablecer y desbloquear usuarios `ADMIN` y `RRHH` de cualquier
  empresa. No puede administrar otro `SuperAdmin`, no puede operarse a sí
  mismo ni crear o asignar `SuperAdmin`.
- `ADMIN`: solo lista usuarios de su propia empresa. Puede crear, desactivar,
  reactivar, restablecer y desbloquear usuarios `RRHH` de esa empresa. No
  puede cambiar roles, crear `ADMIN`, ni operar sobre un `ADMIN`, `SuperAdmin`
  u otra empresa.
- `RRHH`: no tiene acceso administrativo a usuarios.

Para cualquier operación por `{id}`, la API carga el usuario objetivo desde
la base, valida el operador por claims (`TryParse`) y el alcance del objetivo
antes de permitir el cambio. Fuera de alcance o inexistente responde `404`.
`ADMIN` que intenta `PATCH /rol` sobre un `RRHH` de su empresa recibe `403`.
Los DTO de estado y rol no admiten cambio de empresa.

`SuperAdmin` no necesita `X-Empresa-Id` para `PATCH /estado`, `PATCH /rol`,
`POST /restablecer-password` ni `POST /desbloquear`. Ese header sí es
obligatorio y debe coincidir con el body en `POST /api/usuarios`.

#### Cambio de estado

```http
PATCH /api/usuarios/{id}/estado
Authorization: Bearer <token>
Content-Type: application/json

{ "activo": false }
```

El mismo endpoint reactiva con `"activo": true`. El campo es obligatorio; un
body vacío o `activo: null` responde `400`.

#### Cambio de rol

```http
PATCH /api/usuarios/{id}/rol
Authorization: Bearer <token>
Content-Type: application/json

{ "rol": "RRHH" }
```

Valores admitidos: `ADMIN` y `RRHH` (se normalizan recortando espacios y
ignorando mayúsculas). `SuperAdmin` y valores desconocidos responden `400`.
Solo `SuperAdmin` puede ejecutar este endpoint.

Ambos PATCH responden `200` con el DTO seguro de listado:

```json
{
  "mensaje": "Usuario actualizado correctamente.",
  "usuario": {
    "id": 10,
    "empresaId": 2,
    "nombreUsuario": "martin.eloy",
    "correo": "usuario@example.test",
    "rol": "RRHH",
    "activo": false,
    "requiereCambioPassword": false,
    "bloqueado": false,
    "bloqueadoHasta": null
  }
}
```

No se devuelven `PasswordHash`, JWT, temporales ni secretos. La serialización
es camelCase.

Un cambio efectivo de estado o rol incrementa `TokenVersion` una sola vez en
la misma persistencia e invalida los JWT web anteriores. El objetivo debe
iniciar sesión de nuevo. Un valor idéntico al actual es idempotente: no
escribe ni incrementa `TokenVersion`. Desactivar o reactivar no modifica
contraseña, `RequiereCambioPassword`, bloqueo ni intentos. Cambiar el rol no
modifica empresa, estado activo, contraseña ni bloqueo. El usuario inactivo
sigue apareciendo en `GET /api/usuarios`.

- Permisos de lectura:
  - `SuperAdmin`: puede consultar usuarios de todas las empresas o aplicar un
    `empresaId` por query y, si quiere fijar contexto global para inspección,
    usar `X-Empresa-Id`.
  - `ADMIN`: solo usuarios de su propia empresa detectada desde `empresa_id`
    del JWT.
  - `RRHH`: sin acceso al listado administrativo.

- Para `SuperAdmin`, el contexto es el que llega por `X-Empresa-Id`; si el
  header no lleva empresa válida, se devuelve contexto global y el contrato
  queda vacío de sesgo de tenant. Para `ADMIN` y `RRHH`, el `empresa_id` del
  JWT es el único contexto aceptado.

- El `POST /api/usuarios/{id}/restablecer-password` crea una contraseña
  temporal aleatoria con un generador criptográficamente seguro, genera el
  hash de forma segura y responde una sola vez con el valor temporal claro.
  La contraseña temporal:

  - tiene 16 caracteres, con al menos una letra, un número y un carácter
    especial, sin espacios
  - se marca con `RequiereCambioPassword = true`
  - se marca `PasswordTemporalUsada = false`
  - vence a las 24 horas mediante `PasswordTemporalVenceEn` en UTC
  - se limpia `IntentosFallidos`, `BloqueadoHasta`, `UltimoIntentoFallido`
  - incrementa `TokenVersion` e invalida los JWT web anteriores
  - no se registra en logs, auditoría ni telemetría

- El `POST /api/auth/cambiar-password` es la ruta de primer ingreso y cambio
  obligatorio. La contraseña nueva debe cumplir el contrato mínimo:

  - 8 a 20 caracteres
  - al menos una letra
  - al menos un número
  - al menos un carácter especial
  - sin espacios
  - la `nuevaPassword` y `confirmarPassword` deben coincidir
  - la nueva no debe ser igual a la contraseña actual

  La operación debe verificar `PasswordActual` contra `PasswordHash` antes
  de aceptar el cambio. Cuando `RequiereCambioPassword = true`, también exige
  que la temporal no esté usada, tenga vencimiento y siga vigente. Al completar:

  - actualiza el hash
  - pone `RequiereCambioPassword = false`
  - pone `PasswordTemporalUsada = true`
  - limpia `PasswordTemporalVenceEn`
  - limpia `IntentosFallidos`, `BloqueadoHasta`, `UltimoIntentoFallido`
  - incrementa `TokenVersion`

  El cambio exitoso invalida el JWT utilizado. El usuario debe iniciar sesión
  nuevamente con la contraseña definitiva. Una contraseña actual incorrecta,
  una temporal no válida o el incumplimiento de la política responden `400`
  con un mensaje genérico.

- No existe una ruta administrativa para cambiar directamente la contraseña
  definitiva de otro usuario. El flujo administrativo admitido es
  `restablecer-password`, seguido del cambio obligatorio realizado por el
  propio usuario.

- El `POST /api/usuarios/{id}/desbloquear` limpia `IntentosFallidos`,
  `BloqueadoHasta` y `UltimoIntentoFallido` sin tocar la `PasswordHash` ni
  el estado `Activo`, `RequiereCambioPassword` o `PasswordTemporalUsada`.

- El bloqueo de cuentas se asegura con:

```text
IntentosFallidos
BloqueadoHasta
UltimoIntentoFallido
```

- En login, 5 verificaciones fallidas establecen un bloqueo de 15 minutos.
  Actualmente este comportamiento también alcanza a `SuperAdmin`. El contador
  se actualiza con la entidad de EF y no mediante un incremento SQL atómico.
  Login devuelve `401` tanto para credenciales inválidas como para una cuenta
  bloqueada o una contraseña temporal vencida/no válida.

- El flujo de cambio obligatorio emite un JWT con el claim
  `requiere_cambio_password=true`. Un middleware central consulta el estado
  actual del usuario y permite a ese JWT únicamente
  `POST /api/auth/cambiar-password`; los demás endpoints web protegidos
  responden `403`. Los endpoints anónimos y los tokens `token_use=agent` no
  quedan sujetos a esta restricción.

- Cada endpoint protegido valida los JWT `token_use=web` contra la base:
  `NameIdentifier` debe identificar un usuario existente y activo, y el claim
  `token_version` debe ser entero y coincidir con `Usuario.TokenVersion`. Un
  claim ausente, inválido o diferente responde `401`. Esta validación no se
  aplica a tokens `token_use=agent`.

- Este parche no incorpora rate limiting, respuestas `423`/`429`, incremento
  atómico de intentos ni un rediseño general del lockout. Esos puntos quedan
  como deuda técnica posterior.

#### Actualización de esquema de seguridad

El script versionado
`database/20260910_usuario_seguridad.sql` agrega de forma idempotente:

```text
RequiereCambioPassword
IntentosFallidos
BloqueadoHasta
UltimoIntentoFallido
PasswordTemporalVenceEn
PasswordTemporalUsada
TokenVersion
```

Es compatible con MySQL 8.0.42 y consulta `information_schema.COLUMNS` antes
de cada `ALTER TABLE`; no depende de `ADD COLUMN IF NOT EXISTS`. Puede
ejecutarse sobre la tabla original, sobre el servidor que ya tiene las
primeras seis columnas y más de una vez, sin eliminar usuarios.

Antes de ejecutarlo se debe realizar y verificar un respaldo. El orden de
despliegue es **backup → SQL → API → frontend**. El esquema (`TokenVersion` y
el resto de columnas de este script) debe existir antes del binario de API;
el frontend se publica después. Después del SQL se debe ejecutar la consulta
de verificación incluida en el mismo archivo. Los JWT web existentes sin
`token_version` serán rechazados por el binario nuevo, por lo que los
usuarios deberán iniciar sesión nuevamente.

El rollback manual está documentado al final del script. El rollback seguro del
binario conserva este esquema aditivo: `dbbabd8` ya mapea `TokenVersion` y no
puede funcionar si se elimina esa columna. Solo debe quitarse una columna
después de desplegar una versión que ya no la mapee; no deben eliminarse las
primeras seis columnas que ya existían en el servidor actual.

#### Observaciones de fichadas

Este repositorio no versiona el esquema con migraciones EF. Los cambios
incrementales de MySQL viven en `database/YYYYMMDD_descripcion.sql`, igual
que `database/20260910_usuario_seguridad.sql`.

La tabla `FichadaObservacion` (relación 1:1 con `Fichada`) se crea con:

```text
database/20260914_fichada_observacion.sql
database/20260914_fichada_observacion.rollback.sql
```

El script forward es idempotente, no toca `Fichada`, `Empleado` ni `Usuario`,
y no incluye connection strings. Orden de despliegue: **backup → SQL → API**.
No ejecutar estos scripts desde el entorno de desarrollo de esta entrega.

### Sucursales

```text
GET    /api/sucursales
GET    /api/sucursales/{id}
POST   /api/sucursales
PUT    /api/sucursales/{id}
DELETE /api/sucursales/{id}
```

- Las sucursales están vinculadas a la empresa del usuario autenticado.
- Solo `SuperAdmin` puede crear sucursales; debe enviar `X-Empresa-Id` para seleccionar la empresa operativa.
- Los `GET` devuelven únicamente los datos de la sucursal (`id`, `nombre`, `empresaId` y `serialLector`).
- Los departamentos se consultan por separado mediante `/api/departamentos`, evitando ciclos de serialización entre sucursales y departamentos.

### Departamentos

```text
GET    /api/departamentos
GET    /api/departamentos/{id}
POST   /api/departamentos
PUT    /api/departamentos/{id}
DELETE /api/departamentos/{id}
```

- Los departamentos pertenecen a una sucursal.
- **Permisos por rol:** Los endpoints de consulta (`GET`) están permitidos para todos los roles autenticados (`SuperAdmin`, `ADMIN` y `RRHH`), lo que permite a RRHH listar y asignar departamentos a empleados. Las operaciones de modificación (`POST`, `PUT`, `DELETE`) requieren la política `PuedeAdministrarDepartamentos` (`SuperAdmin` y `ADMIN`); el rol `RRHH` recibe `403 Forbidden` si intenta crear, editar o eliminar.
- La asociación con la sucursal se valida contra la empresa del usuario autenticado (devuelve `403 Forbidden` si pertenece a otra empresa).
- Todos los endpoints utilizan DTOs planos (`DepartamentoDto` y `DepartamentoCrearDto`), eliminando referencias circulares en la serialización JSON.
- `POST` y `PUT` validan la unicidad del nombre dentro de la misma sucursal (`Nombre` + `SucursalId`). Si el nombre ya existe en la sucursal, responde `409 Conflict`. El mismo nombre en sucursales distintas está permitido.
- La asociación con la sucursal se valida contra la empresa del usuario autenticado antes de guardar o editar (403 Forbidden si pertenece a otra empresa).
- Los endpoints de consulta y creación devuelven DTOs planos (`id`, `nombre`, `sucursalId`) evitando ciclos de serialización.
- POST `/api/departamentos` valida unicidad de nombre por sucursal: si ya existe un departamento con el mismo nombre en la misma sucursal, devuelve 409 Conflict. El mismo nombre en una sucursal distinta sí está permitido.

### Turnos

```text
GET    /api/turnos
GET    /api/turnos/{id}
POST   /api/turnos
PUT    /api/turnos/{id}
DELETE /api/turnos/{id}
```

- Permite definir y gestionar los horarios de trabajo asignables a los empleados.
- Todos los endpoints operan exclusivamente con DTOs planos (`TurnoDto`, `TurnoCrearDto`, `TurnoDiaDto`), evitando dependencias directas con los modelos de datos en el contrato público.
- Las consultas `GET` permiten visualizar horarios semanales por día (`DiaSemana`: domingo `0` a sábado `6`, `HoraEntrada`, `HoraSalida`). La comida es de 60 minutos, común para todos; el tiempo trabajado se calcula con fichadas reales de entrada/salida, incluyendo salida y reentrada para comer.
- `ToleranciaMinutos` se define desde el frontend por turno; `0` significa que no hay tolerancia.
- Las operaciones de creación, edición y eliminación (`POST`, `PUT`, `DELETE`) están restringidas a los roles `SuperAdmin` y `ADMIN`.
- Todas las operaciones están estrictamente aisladas por el contexto de empresa del usuario autenticado (`empresa_id`).

Ejemplo de turno recurrente:

```http
POST /api/turnos
Authorization: Bearer <token-admin>
Content-Type: application/json
```

```json
{
  "nombre": "Administración",
  "toleranciaMinutos": 5,
  "dias": [
    { "diaSemana": 1, "horaEntrada": "09:00", "horaSalida": "18:00" },
    { "diaSemana": 2, "horaEntrada": "09:00", "horaSalida": "18:00" },
    { "diaSemana": 3, "horaEntrada": "10:00", "horaSalida": "18:00" },
    { "diaSemana": 4, "horaEntrada": "09:00", "horaSalida": "18:00" },
    { "diaSemana": 5, "horaEntrada": "09:00", "horaSalida": "17:00" }
  ]
}
```

Asignar el turno a un empleado con vigencia e historial:

```http
POST /api/jornadas/empleados/12/asignaciones
Authorization: Bearer <token-admin>
Content-Type: application/json
```

```json
{
  "turnoId": 3,
  "fechaInicio": "2026-10-01",
  "fechaFin": null
}
```

Una asignación nueva desde una fecha cierra la asignación anterior el día
previo; no reescribe el historial. El turno puede reutilizarse para varios
empleados.

Consultar el calendario efectivo, incluidos días libres y excepciones:

```http
GET /api/jornadas/empleados/12/calendario?desde=2026-10-01&hasta=2026-10-31
Authorization: Bearer <token>
```

El resultado indica `tieneTurnoAsignado`, `tieneHorarioConfigurado` y
`esDiaLibre`. Un turno asignado sin días semanales definidos no se interpreta
como descanso: debe configurarse antes de calcular sus horas. La instancia
actual tiene filas heredadas `Empleado.TurnoId`, pero `TurnoDia` está vacío;
esas asignaciones se migran desde la fecha de corte y sus días deben cargarse
según el horario real del cliente.

Sobrescribir únicamente una fecha (una jornada o día libre):

```http
PUT /api/jornadas/empleados/12/dias/2026-10-15
Authorization: Bearer <token-admin>
Content-Type: application/json
```

```json
{
  "esDiaLibre": false,
  "horaEntrada": "10:00",
  "horaSalida": "16:00",
  "toleranciaMinutos": 0
}
```

`DELETE /api/jornadas/empleados/12/dias/2026-10-15` elimina la excepción y
restaura el horario recurrente para esa fecha. Cambiar el turno con una nueva
fecha de inicio es distinto: crea/actualiza la vigencia del turno hacia el
futuro y conserva la asignación anterior.

### Horas, reposiciones y alertas

```text
GET  /api/jornadas/empleados/{id}/resumen?desde=2026-10-01&hasta=2026-10-31
GET  /api/jornadas/empleados/{id}/reposiciones?desde=2026-10-01&hasta=2026-10-31
POST /api/jornadas/empleados/{id}/reposiciones
GET  /api/fichadas/alertas/incompletas?desde=2026-10-01&hasta=2026-10-31
```

El resumen expresa horas en minutos e incluye saldo diario/total, tardanza por
encima de la tolerancia y reposiciones aprobadas. Registrar reposición:

```json
{
  "fecha": "2026-10-15",
  "minutos": 60,
  "detalle": "Reposición autorizada"
}
```

Las reposiciones y cambios de calendario requieren `ADMIN` o `SuperAdmin`.
Las alertas web se consultan con polling mediante el `GET` anterior, solo por
`ADMIN`/`SuperAdmin`.

### Fichadas manuales

```http
POST /api/fichadasmanuales
Authorization: Bearer <token-admin>
Content-Type: application/json
```

```json
{
  "empleadoId": 12,
  "fechaHora": "2026-10-01T09:00:00",
  "tipo": "Entrada",
  "motivo": "OlvidoDeFichaje",
  "detalle": "El lector no registró la entrada"
}
```

Solo `ADMIN` y `SuperAdmin` pueden crear una fichada manual; `RRHH` no. El
motivo debe pertenecer al catálogo de observaciones. La fichada queda marcada
con `esManual: true`, `metodo: "Manual"` y conserva usuario/fecha de auditoría.
`GET /api/fichadas` incluye esos campos para distinguirla de una lectura
biométrica.

## Empleados y huellas

```text
GET    /api/empleados
GET    /api/empleados/empresa/{empresaId}
GET    /api/empleados/{id}
POST   /api/empleados
PATCH  /api/empleados/{id}
POST   /api/empleados/enrolar
DELETE /api/empleados/{id}
GET    /api/huellas/empresa/{empresaId}
```

El `empresa_id` incluido en el JWT limita las operaciones a la empresa correspondiente al usuario autenticado.

El endpoint `PATCH /api/empleados/{id}` permite editar campos de un empleado activo, por ejemplo: legajo, DNI, CUIL, nombre, apellido, departamento, categoría, sucursal y horario. Solo se actualiza el empleado de la empresa autorizada por el token.

El `DELETE /api/empleados/{id}` realiza un soft delete: marca al empleado como inactivo (`Activo = false`) y evita que siga apareciendo en listados activos. No existe borrado físico de la fila.

El enrolamiento recibe:

- `empleadoId`
- `templateHuellaBase64`
- `indiceDedo`

La plantilla debe ser FMD ANSI binaria serializada como Base64. No se acepta mezclarla con XML.

## Compatibilidad mínima

### Empleados

| Problema | Antes | Cambio mínimo | Resultado | Requiere BD |
| --- | --- | --- | --- | --- |
| Modelo `Empleado` desalineado con producción | EF esperaba `Departamento` y `Sucursal` como columnas de texto | Se mapean `DepartamentoId` y `SucursalId`; los nombres y `TieneHuella` se proyectan en `EmpleadoDto` | Los listados usan las relaciones reales sin exponer templates biométricos | No |

### SuperAdmin / empresas

| Problema | Antes | Cambio mínimo | Resultado | Requiere BD |
| --- | --- | --- | --- | --- |
| SuperAdmin quedaba ligado a `empresa_id` y no podía listar ni seleccionar empresas globalmente | El JWT siempre incluía `empresa_id`; `GET /api/empresas` filtraba por ese claim y `X-Empresa-Id` se ignoraba | Se normaliza el rol a `SuperAdmin`, su JWT web omite `empresa_id`, el listado de empresas es global y el contexto tenant se toma únicamente de `X-Empresa-Id` | SuperAdmin selecciona contexto sin permitir que `ADMIN`/`RRHH` cambien de tenant por header | No |

## Fichadas

```text
GET  /api/fichadas?empleadoId=1&desde=2026-08-01&hasta=2026-09-01&tipo=Entrada&metodo=Biometrico&limite=100
POST /api/fichadas/bulk
POST /api/fichadas/biometricas/bulk
```

El endpoint biométrico recibe una lista de eventos con `empleadoId` y
`fechaHora`; no se debe enviar `tipoRegistro`. La API los procesa en orden
cronológico, calcula `Entrada` o `Salida` a partir de las fichadas del mismo
empleado y fecha calendario y no empareja una entrada con una salida del día
siguiente. Un ingreso antiguo sin salida se marca incompleto hasta el corte de
medianoche. El endpoint requiere un token de agente, acepta hasta 500 eventos
y responde `{ "recibidas": n }` tras aceptar el lote. Reenviar un evento
biométrico del mismo empleado con la misma fecha y hora no crea otra fichada;
esto permite reintentar una solicitud si se pierde la respuesta HTTP. La
cantidad confirmada incluye eventos ya registrados que se reconocen como
reintentos.

```json
[
  {
    "empleadoId": 1,
    "fechaHora": "2026-10-12T08:59:00"
  }
]
```

Los valores válidos para `TipoRegistro` son:

- `Entrada`
- `Salida`

Los valores aceptados para `Metodo` son:

- `Biometrico`
- `Biométrico`
- `Manual`

El lote admite como máximo 500 elementos y únicamente empleados activos pertenecientes a la empresa indicada por el token.

Las fechas deben enviarse en formato ISO 8601.

El endpoint `GET` devuelve:

- `id`
- `empleadoId`
- `nombre`
- `apellido`
- `legajo`
- `fechaHora`
- `tipo`
- `metodo`
- `esManual`
- `estado`
- `minutosHastaCorte` (solo para jornadas incompletas)
- `observacion` (motivo, detalle y auditoría, cuando existe)

### Procesamiento automático (CorteMedianocheWorker)

El sistema incluye un servicio en segundo plano (`CorteMedianocheWorker` registrado mediante `AddHostedService`) que se ejecuta a la medianoche:

- Detecta automáticamente las fichadas de entrada del día anterior que no registraron su correspondiente marca de salida.
- Actualiza el estado de la fichada a `Incompleto`.
- Persiste los minutos desde la entrada hasta las 00:00 en `MinutosHastaCorte`; una entrada del día siguiente no cierra la jornada anterior.
- Expone las jornadas que requieren revisión en `GET /api/fichadas/alertas/incompletas`.
- Al iniciar la API, espera a la próxima medianoche y no procesa retroactivamente el día anterior.

## Desarrollo local

Para desarrollo se puede utilizar el archivo:

```text
ControlFichajes.API/appsettings.Development.local.json
```

Existe una plantilla:

```text
ControlFichajes.API/appsettings.Development.local.json.example
```

Copiar la plantilla y reemplazar los valores correspondientes a la conexión MySQL, JWT y CORS.

Ejemplo de estructura:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=REEMPLAZAR;Pwd=REEMPLAZAR;"
  },
  "Jwt": {
    "Key": "REEMPLAZAR_POR_UNA_CLAVE_LARGA_Y_SEGURA",
    "Issuer": "ControlFichajes.API.Local",
    "Audience": "ControlFichajes.Frontend.Local",
    "ExpireMinutes": "60",
    "AccessExpireMinutes": "15"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173"
    ]
  }
}
```

Los archivos de configuración locales que contienen credenciales o secretos reales no deben subirse al repositorio.

Para restaurar dependencias y compilar:

```powershell
dotnet restore
dotnet build ControlFichajes.sln
```

## Pruebas

El repositorio incluye un proyecto de pruebas en:

```text
ControlFichajes.API.Tests
```

Estas pruebas validan la lógica principal de autenticación y empleados usando EF Core InMemory para evitar depender de MySQL durante la ejecución local de pruebas.

Ejecutar toda la suite:

```powershell
dotnet test ControlFichajes.API.Tests/ControlFichajes.API.Tests.csproj -nologo
```

También puede ejecutarse desde la solución:

```powershell
dotnet test ControlFichajes.sln -nologo
```

La validación actual cubre:

- registro de usuarios
- login con credenciales inválidas
- rechazo de login para usuarios inactivos
- claims JWT de SuperAdmin sin `empresa_id`
- selección de empresa por `X-Empresa-Id`
- listado de sucursales con contexto SuperAdmin
- alta, autenticación y desactivación de agentes
- claims tenant del JWT de agente
- límites de lote, validación de empleados y autorización del endpoint biométrico
- orden cronológico, separación por empleado/día, fichadas iguales y reintentos

### Aceptación explícita contra `.devs` de producción

La prueba `ProductionBiometricAcceptanceTests` está omitida por defecto. Usa
el servicio HTTP de producción y una conexión de lectura/escritura a MySQL, por
lo que solo debe habilitarse después de provisionar en `.devs` un agente y un
empleado dedicado a pruebas cuyo legajo comience con `TEST-AUTOMATION-`.
La prueba valida el host HTTPS, el `empresa_id` del token, el nombre de la
empresa y la empresa/estado/legajo del empleado; selecciona un día futuro vacío,
envía dos fichadas y elimina solamente filas biométricas del empleado de prueba
con las marcas de tiempo generadas por esa ejecución. No crea ni modifica
empleados, agentes o empresas.

Configura estas variables solo en el entorno local del proceso o en un almacén
de secretos de CI; no las guardes en archivos del repositorio:

- `CFB_PRODUCTION_TEST_OPT_IN` con el valor literal
  `WRITE_TEST_PUNCHES_IN_DEVS_PRODUCTION`
- `CFB_PRODUCTION_TEST_API_BASE_URL` y `CFB_PRODUCTION_TEST_API_HOST`
- `CFB_PRODUCTION_TEST_DB_CONNECTION`
- `CFB_PRODUCTION_TEST_AGENT_CLIENT_ID` y `CFB_PRODUCTION_TEST_AGENT_CLIENT_SECRET`
- `CFB_PRODUCTION_TEST_COMPANY_ID` y `CFB_PRODUCTION_TEST_EMPLOYEE_ID`

Para ejecutarla explícitamente:

```powershell
dotnet test ControlFichajes.API.Tests/ControlFichajes.API.Tests.csproj -nologo --filter "FullyQualifiedName~ProductionBiometricAcceptanceTests"
```

Si falla la verificación o limpieza, inspecciona el empleado de automatización
y el día futuro de esa ejecución antes de repetirla. Nunca uses un empleado
real. Las pruebas unitarias/HTTP normales no requieren estas variables y no
acceden a producción.

### Aceptación local con túnel SSH y lector físico

`LocalTunnelBiometricAcceptanceTests` está omitida por defecto. La prueba solo
se habilita con el opt-in explícito y verifica el agente, el tenant `.devs`, el
empleado dedicado cuyo legajo comienza por `TEST-AUTOMATION-`, la huella del
dedo seleccionado creada durante la ejecución y un día futuro sin fichadas.
Envía dos fichadas biométricas a la API local y limpia únicamente esa huella y
las fichadas biométricas del empleado dedicado con fecha desde el inicio
configurado, incluso si una aserción falla. Por eso el inicio debe ser anterior
al enrolamiento y no se deben usar otras operaciones con ese empleado durante
la prueba. La API local sigue conectada a la base remota mediante túnel; no es
una base aislada.

Arranca la API local en `http://localhost:5052` con su configuración local
ignorada por Git y abre el túnel SSH hacia MySQL en `localhost:3307`. Después,
configura las siguientes variables solo en el entorno del proceso de pruebas;
no guardes secretos ni cadenas de conexión en el repositorio:

- `CFB_LOCAL_TUNNEL_TEST_OPT_IN` con el valor literal
  `WRITE_TEST_DATA_FOR_DEDICATED_DEVS_EMPLOYEE`
- `CFB_LOCAL_TEST_API_BASE_URL` (debe ser `http://localhost:5052/`)
- `CFB_LOCAL_TEST_DB_CONNECTION` y `CFB_LOCAL_TEST_DB_NAME`
- `CFB_LOCAL_TEST_AGENT_CLIENT_ID` y `CFB_LOCAL_TEST_AGENT_CLIENT_SECRET`
- `CFB_LOCAL_TEST_COMPANY_ID`, `CFB_LOCAL_TEST_EMPLOYEE_ID` y
  `CFB_LOCAL_TEST_EMPLOYEE_LEGAJO`
- `CFB_LOCAL_TEST_FINGER_INDEX` (1–10) y `CFB_LOCAL_TEST_STARTED_AT_UTC`

Registra el valor UTC de inicio **antes** de enrolar el dedo de prueba y
ejecuta la prueba después del enrolamiento desde la aplicación. La aceptación
no crea el template: comprueba el que la aplicación acaba de guardar. No la
ejecutes con otro empleado ni habilites el opt-in en CI.

```powershell
dotnet test ControlFichajes.API.Tests/ControlFichajes.API.Tests.csproj --filter "FullyQualifiedName~LocalTunnelBiometricAcceptanceTests"
```

- prevención de duplicados en DNI/CUIL
- actualización de empleados activos vía PATCH
- baja lógica (soft delete) de empleados
- enrolamiento de huellas para empleados activos
- creación de departamentos con DTOs planos y prevención de ciclos JSON
- validación de departamentos duplicados por sucursal y restricción de creación/modificación para rol RRHH
- corte de medianoche de fichadas abiertas (marcado `Incompleto` y cálculo de horas) vía `CorteMedianocheWorker`
- operaciones CRUD de turnos y sus horarios diarios aisladas por tenant de empresa y con mocks de `Moq` y `xUnit` (`TurnosControllerTests` y `TurnoServiceTests`)

El documento OpenAPI se publica en desarrollo mediante `MapOpenApi`.

## Despliegue en el servidor

En el servidor, comprobar que Docker Compose esté disponible:

```bash
docker compose version
```

Si el plugin no está instalado:

```bash
sudo apt-get update
sudo apt-get install -y docker-compose-plugin
docker compose version
```

Clonar el repositorio y acceder al proyecto:

```bash
git clone https://github.com/SamusSalinas/ControlFichajes.API.git
cd ControlFichajes.API
```

Crear el archivo de configuración de producción:

```bash
cp .env.production.example .env.production
nano .env.production
```

La configuración debe incluir la conexión a MySQL y las variables necesarias para JWT y CORS.

Ejemplo:

```env
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=REEMPLAZAR;Pwd=REEMPLAZAR;

Jwt__Key=REEMPLAZAR_POR_UNA_CLAVE_LARGA_Y_SEGURA
Jwt__Issuer=ControlFichajes.API
Jwt__Audience=ControlFichajes.Frontend
Jwt__ExpireMinutes=60
Jwt__AccessExpireMinutes=15

Cors__AllowedOrigins__0=http://localhost:5173
```

> **Importante:** `.env.production` puede contener credenciales y secretos reales, por lo que no debe subirse al repositorio.

Construir e iniciar la API:

```bash
docker compose up -d --build
```

Consultar los logs:

```bash
docker compose logs -f api
```

El contenedor utiliza la red del host para alcanzar MySQL mediante:

```text
127.0.0.1:3306
```

La API queda disponible actualmente en:

```text
http://<host-de-produccion>:8080
```

Para actualizar el despliegue después de nuevos cambios:

```bash
git pull
docker compose up -d --build
```

Si la distribución utiliza el comando legado de Docker Compose, reemplazar `docker compose` por `docker-compose`.

---

## Variables de entorno de producción

Se sugiere mantener el archivo `.env.production` localmente en el entorno real y no versionarlo en Git.

La plantilla `.env.production.example` incluye las variables esenciales para:

- cadena de conexión a MySQL
- JWT
- CORS
- configuración del entorno de despliegue


```text
Jwt__Key
Jwt__Issuer
Jwt__Audience
Jwt__ExpireMinutes
Jwt__AccessExpireMinutes
Cors__AllowedOrigins__0
```

`.env.production.example` funciona únicamente como plantilla y no debe contener secretos reales.

Para utilizarla se debe copiar como `.env.production` y reemplazar los valores de ejemplo por la configuración correspondiente al entorno.

## Validación

La rama corregida fue validada localmente mediante:

```powershell
dotnet build
```

utilizando .NET 10.

La compilación finalizó correctamente.

## Comparación antes y después

### `Huella.cs`

**Antes (`pruebas/api-estable`)**

```csharp
[Required]
public int IndiceDedo { get; set; } = string.Empty;
```

**Después (`pruebas/api-estable-corregida`)**

```csharp
[Required]
public int IndiceDedo { get; set; }
```

### `HuellasController.cs`

**Antes (`pruebas/api-estable`)**

```csharp
.Select(h => new
{
    h.Id,
    h.EmpleadoId,
    h.NombreDedo,
    h.TemplateBiometrico
})
```

**Después (`pruebas/api-estable-corregida`)**

```csharp
.Select(h => new
{
    h.Id,
    h.EmpleadoId,
    h.IndiceDedo,
    h.TemplateBiometrico
})
```

### `.env.production.example`

**Antes (`pruebas/api-estable`)**

```env
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=REEMPLAZAR;Pwd=REEMPLAZAR;
```

**Después (`pruebas/api-estable-corregida`)**

```env
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=REEMPLAZAR;Pwd=REEMPLAZAR;

Jwt__Key=REEMPLAZAR_POR_UNA_CLAVE_LARGA_Y_SEGURA
Jwt__Issuer=ControlFichajes.API
Jwt__Audience=ControlFichajes.Frontend
Jwt__ExpireMinutes=60
Jwt__AccessExpireMinutes=15

Cors__AllowedOrigins__0=http://localhost:5173
```

---

## Seguridad

- No almacenar contraseñas en texto plano.
- No subir `.env.production` al repositorio.
- No subir `appsettings.Development.local.json` si contiene credenciales.
- Utilizar una clave JWT larga y aleatoria en producción.
- No cifrar el secreto TOTP con `Jwt:Key`. Provisionar `Auth:TotpClaves` aparte y respaldarla. No generarla al arrancar ni versionarla.
- Limitar los orígenes CORS a los frontends autorizados.
- Utilizar HTTPS cuando el sistema pase de la etapa de pruebas a un entorno definitivo.
