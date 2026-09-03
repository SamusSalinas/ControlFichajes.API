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

## Empresas, usuarios, sucursales y departamentos

### Empresas

```text
GET  /api/empresas
POST /api/empresas
```

- `GET /api/empresas`: devuelve la empresa asociada al usuario autenticado.
- `POST /api/empresas`: crea una nueva empresa y solo lo puede hacer un usuario con rol `ADMIN`.

### Usuarios

```text
POST /api/usuarios
```

- Registra un usuario de la empresa autenticada.
- Solo puede ejecutarlo un usuario con rol `ADMIN`.
- Los roles permitidos son `ADMIN` y `RRHH`.

### Sucursales

```text
GET    /api/sucursales
GET    /api/sucursales/{id}
POST   /api/sucursales
PUT    /api/sucursales/{id}
DELETE /api/sucursales/{id}
```

- Las sucursales están vinculadas a la empresa del usuario autenticado.
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

