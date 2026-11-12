# Seguridad web, sesiones y MFA

Volver al [[Inicio|indice]] o consultar [[API/01-Contrato-y-autenticacion|contrato y autenticacion]] y [[Administracion/02-Usuarios|administracion de usuarios]].

## Login y segundo factor

`POST /api/Auth/Login` autentica con correo y contrasena. Si la cuenta no
requiere MFA, inicia sesion directamente. Si tiene TOTP habilitado, una
contrasena correcta responde `401` con `codigo=mfa_requerido`, un desafio y su
vencimiento; no emite JWT ni cookie. `POST /api/Auth/mfa/verificar` recibe el
desafio y el TOTP o un codigo de recuperacion. El desafio no es un JWT ni
autoriza operaciones de negocio.

Si una cuenta tiene MFA obligatorio pero aun no configuro TOTP, el login
responde `401` con `codigo=vinculacion_mfa_pendiente`. El enrolamiento de
vinculacion no inicia una sesion: despues de confirmarlo, el usuario debe
volver a autenticarse con su segundo factor.

## Sesiones renovables

El login renovable emite un JWT de acceso y una cookie `HttpOnly` con refresh
token; el refresh no se envia en el cuerpo. `POST /api/Auth/refresh` rota el
token de la cookie. Si se reutiliza uno ya consumido, se revoca su familia y
se requiere volver a iniciar sesion. `POST /api/Auth/cerrar-sesion` revoca esa
familia y responde `204`; no cierra las demas sesiones del usuario.

El modo de cookie por generacion requiere `X-CF-Generacion`,
`X-CF-Solicitud: 1` y un origen permitido. Cada respuesta modifica solamente
la cookie de esa generacion. Las operaciones de refresh y cierre requieren
origen permitido y tienen limitacion de solicitudes. Las cookies y los tokens
no se registran en logs.

Cambiar contrasena, restablecerla, cambiar estado, rol o identidad revoca las
sesiones del usuario. Desbloquear la cuenta no las revoca.

## Invitaciones y recuperacion

Las rutas de invitacion se describen en
[[Administracion/02-Usuarios|administracion de usuarios]]. La aceptacion
anonima es `POST /api/Auth/invitacion/aceptar`: usa un token de un solo uso,
define la contrasena y activa la cuenta, pero no inicia sesion.

`POST /api/Auth/recuperacion/solicitar` es anonimo y responde con el mismo
mensaje para correos validos, exista o no una cuenta recuperable. La
restauracion se completa con `POST /api/Auth/recuperacion/restablecer`. Los
tokens son opacos, de un solo uso y se persiste su hash; las solicitudes de
recuperacion no activan cuentas.

El cambio autenticado de contrasena es `POST /api/Auth/cambiar-password`. Una
cuenta con cambio obligatorio solo puede usar esa ruta hasta completarlo.

## MFA

Rutas disponibles:

```text
GET  /api/Auth/mfa/estado
POST /api/Auth/mfa/verificar
POST /api/Auth/mfa/enrolamiento/iniciar
POST /api/Auth/mfa/enrolamiento/confirmar
POST /api/Auth/mfa/vinculacion/iniciar
POST /api/Auth/mfa/vinculacion/confirmar
POST /api/Auth/mfa/recuperacion/regenerar
POST /api/Auth/mfa/desactivar
POST /api/usuarios/{id}/exigir-mfa
```

El enrolamiento voluntario esta controlado por
`Auth:PermitirEnrolamientoMfa`; cuando esta apagado, iniciar, confirmar y
regenerar responden `404`. El secreto TOTP se cifra en reposo y las claves de
cifrado se provisionan fuera del repositorio. La confirmacion entrega los
codigos de recuperacion una sola vez; solo se conserva su hash.

Una invitacion puede marcar MFA como obligatorio para la cuenta nueva. Las
cuentas existentes no se vuelven obligatorias por defecto. La desactivacion
solo se permite para MFA voluntario; no permite retirar un requisito
obligatorio.

## Correo saliente

La administracion requiere una sesion de `SuperAdmin`:

```text
GET  /api/correo-saliente/estado
PUT  /api/correo-saliente/configuracion
POST /api/correo-saliente/prueba
```

El estado no devuelve la contrasena SMTP. No guardar en esta boveda
credenciales, enlaces con token, JWT ni datos de configuracion reales. Para
detalles de despliegue y provisionamiento, consultar
[[Operaciones/03-Despliegue|despliegue]].

## Referencias

- [[Operaciones/02-Base-de-datos|Esquema y scripts de seguridad]]
- [[Administracion/02-Usuarios|Roles y operaciones de usuario]]
- [[Referencia/Compatibilidad|Historial tecnico]]
