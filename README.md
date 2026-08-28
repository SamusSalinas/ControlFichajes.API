# ControlFichajes.API

API REST para gestionar empresas, empleados, huellas biométricas y fichadas.
El backend es la fuente de verdad del contrato que consumirán posteriormente
el cliente biométrico y el frontend.

## Requisitos

- .NET 10 SDK
- MySQL 8
- Docker opcional para desplegar en el servidor

## Contrato de la API

Todos los endpoints siguientes, excepto el login y el registro inicial,
requieren:

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

La respuesta contiene `token` y `mensaje`. Las contraseñas almacenadas en
`Usuario.PasswordHash` deben ser hashes generados con
`PasswordHasher<Usuario>`.

### Registro de usuarios

El primer usuario se crea una sola vez mediante el endpoint de bootstrap. Este
endpoint solo funciona cuando la tabla `Usuario` está vacía y crea un usuario
con rol `ADMIN`:

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

La empresa indicada debe existir previamente. La respuesta devuelve un JWT,
por lo que se puede reutilizar directamente como Bearer token.

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

Solo se permiten los roles `ADMIN` y `RRHH`. El administrador no puede crear
usuarios para otra empresa. Las contraseñas se almacenan como hash y nunca se
devuelven en la respuesta.

Si ya existe algún usuario, `POST /api/auth/bootstrap` responde `409 Conflict`.
En ese caso, utiliza el token de un administrador para crear nuevos usuarios.

### Empleados y huellas

```text
GET  /api/empleados
GET  /api/empleados/empresa/{empresaId}
GET  /api/empleados/{id}
POST /api/empleados
POST /api/empleados/enrolar
DELETE /api/empleados/{id}
GET  /api/huellas/empresa/{empresaId}
```

El `empresa_id` del JWT limita todas las operaciones a la empresa del usuario.
El enrolamiento recibe `empleadoId`, `templateHuellaBase64` e `indiceDedo`.
La plantilla debe ser FMD ANSI binaria serializada como Base64; no se acepta
mezclarla con XML.

### Fichadas

```text
GET  /api/fichadas?empleadoId=1&desde=2026-08-01&hasta=2026-09-01&tipo=Entrada&metodo=Biometrico&limite=100
POST /api/fichadas/bulk
```

Los valores válidos son `Entrada` y `Salida` para `TipoRegistro`, y
`Biometrico`, `Biométrico` o `Manual` para `Metodo`. El lote admite como máximo
500 elementos y solo empleados activos de la empresa del token.

Las fechas deben enviarse en formato ISO 8601. El `GET` devuelve `id`,
`empleadoId`, `nombre`, `apellido`, `legajo`, `fechaHora`, `tipo` y `metodo`.

## Configuración local

## Desarrollo local

Abre un túnel SSH hacia MySQL y deja esa terminal abierta:

```powershell
ssh -i "ssh-key-2026-03-27.key" -N -L 3307:127.0.0.1:3306 ubuntu@161.153.193.159
```

Copia `ControlFichajes.API/appsettings.Development.local.json.example` como
`ControlFichajes.API/appsettings.Development.local.json`, sustituye `REEMPLAZAR`
por las credenciales de MySQL y agrega la clave JWT mediante configuración de
usuario o variable de entorno. Configura también los orígenes permitidos en
`Cors:AllowedOrigins`.

Para compilar y validar:

```powershell
dotnet restore
dotnet build ControlFichajes.sln
```

El documento OpenAPI se publica en desarrollo mediante `MapOpenApi`.

## Despliegue en el servidor

En el servidor, comprueba si Compose está instalado:

```bash
docker compose version
```

Si aparece un error o la versión no existe, instala el plugin de Compose:

```bash
sudo apt-get update
sudo apt-get install -y docker-compose-plugin
docker compose version
```

Después clona el repositorio:

```bash
git clone https://github.com/SamusSalinas/ControlFichajes.API.git
cd ControlFichajes.API/ControlFichajes.API
cp .env.production.example .env.production
nano .env.production
docker compose up -d --build
docker compose logs -f api
```

Si tu distribución no ofrece `docker-compose-plugin`, usa el comando legado
equivalente en todos los pasos: `docker-compose up -d --build` y
`docker-compose logs -f api`.

El contenedor usa la red del host para alcanzar el MySQL local en
`127.0.0.1:3306`, sin publicar MySQL a Internet. La API queda disponible en
`http://161.153.193.159:8080`.
Para actualizarla:

```bash
git pull
docker compose up -d --build
```
