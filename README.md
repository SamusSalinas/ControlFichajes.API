# ControlFichajes.API

## Desarrollo local

Abre un túnel SSH hacia MySQL y deja esa terminal abierta:

```powershell
ssh -i "ssh-key-2026-03-27.key" -N -L 3307:127.0.0.1:3306 ubuntu@161.153.193.159
```

Copia `ControlFichajes.API/appsettings.Development.local.json.example` como
`ControlFichajes.API/appsettings.Development.local.json`, sustituye `REEMPLAZAR`
por la contraseña y ejecuta la API desde Visual Studio o con `dotnet run`.

## Despliegue en el servidor

En el servidor, instala Docker y clona el repositorio:

```bash
git clone https://github.com/SamusSalinas/ControlFichajes.API.git
cd ControlFichajes.API/ControlFichajes.API
cp .env.production.example .env.production
nano .env.production
docker compose up -d --build
docker compose logs -f api
```

El contenedor usa la red del host para alcanzar el MySQL local en
`127.0.0.1:3306`, sin publicar MySQL a Internet. La API queda disponible en
`http://161.153.193.159:8080`.
Para actualizarla:

```bash
git pull
docker compose up -d --build
```
