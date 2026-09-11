# ControlFichajes.API

API REST para gestionar empresas, empleados, huellas biométricas y fichadas.

El backend es la fuente de verdad del contrato que consumirán posteriormente el cliente biométrico y el frontend.

## Requisitos

- .NET 10 SDK
- MySQL 8
- Docker opcional para desplegar en el servidor

## Contrato de la API

Todos los endpoints siguientes, excepto el login y el registro inicial, requieren:

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

El primer usuario se crea una sola vez mediante el endpoint de bootstrap.

Este endpoint solo funciona cuando la tabla `Usuario` está vacía y crea un usuario con rol `ADMIN`:

```http
POST /api/auth/bootstrap
Content-Type: application/json

{
    "empresaId": 1,
    "nombreUsuario": "Administrador",
    "email": "admin@empresa.local",
    "password": "UnaClaveSegura123",
    "rol": "ADMIN"
}
```

La empresa indicada debe existir previamente.

La respuesta devuelve un JWT, por lo que se puede reutilizar directamente como Bearer token.

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

Si ya existe algún usuario, `POST /api/auth/bootstrap` responde `409 Conflict`.

En ese caso, se debe utilizar el token de un administrador para crear nuevos usuarios.

### Flujo del SuperAdmin

El primer usuario creado por `POST /api/auth/bootstrap` es `ADMIN`, no
`SuperAdmin`. Para disponer de un SuperAdmin, la cuenta debe existir en la base
de datos con `Rol = "SuperAdmin"` y estar activa (`Activo = true`).

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
POST  /api/agentes/{id}/heartbeat
```

El login de agente recibe `clientId` y `clientSecret` y devuelve un JWT con:

- `token_use = agent`
- `agente_id`
- `empresa_id`
- `sucursal_id`
- rol `AGENTE_SUCURSAL`

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
enrolamiento y `POST /api/fichadas/bulk`. No pueden acceder a las operaciones
web de gestión, empresas, sucursales, usuarios ni fichadas históricas.

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
- La asociación se valida contra la empresa del usuario autenticado antes de guardar o editar.

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
    "DefaultConnection": "Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=dev_user;Pwd=REEMPLAZAR;"
  },
  "Jwt": {
    "Key": "REEMPLAZAR_POR_UNA_CLAVE_LARGA_Y_SEGURA",
    "Issuer": "ControlFichajes.API.Local",
    "Audience": "ControlFichajes.Frontend.Local",
    "ExpireMinutes": "60"
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
- prevención de duplicados en DNI/CUIL
- actualización de empleados activos vía PATCH
- baja lógica (soft delete) de empleados
- enrolamiento de huellas para empleados activos

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
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=dev_user;Pwd=REEMPLAZAR;

Jwt__Key=REEMPLAZAR_POR_UNA_CLAVE_LARGA_Y_SEGURA
Jwt__Issuer=ControlFichajes.API
Jwt__Audience=ControlFichajes.Frontend
Jwt__ExpireMinutes=60

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
http://161.153.193.159:8080
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
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=dev_user;Pwd=REEMPLAZAR;
```

**Después (`pruebas/api-estable-corregida`)**

```env
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=tesis_db;Uid=dev_user;Pwd=REEMPLAZAR;

Jwt__Key=REEMPLAZAR_POR_UNA_CLAVE_LARGA_Y_SEGURA
Jwt__Issuer=ControlFichajes.API
Jwt__Audience=ControlFichajes.Frontend
Jwt__ExpireMinutes=60

Cors__AllowedOrigins__0=http://localhost:5173
```

---

## Seguridad

- No almacenar contraseñas en texto plano.
- No subir `.env.production` al repositorio.
- No subir `appsettings.Development.local.json` si contiene credenciales.
- Utilizar una clave JWT larga y aleatoria en producción.
- Limitar los orígenes CORS a los frontends autorizados.
- Utilizar HTTPS cuando el sistema pase de la etapa de pruebas a un entorno definitivo.

