# Arquitectura

Relevado el 28 de octubre de 2026 en el workspace local. Este archivo está duplicado en `ControlFichajes.API` y `control-accesos-frontend` porque el directorio padre no es un repositorio Git.

## Repositorios presentes

| Repositorio | Remoto | Rol |
| --- | --- | --- |
| `ControlFichajes.API` | `https://github.com/SamusSalinas/ControlFichajes.API.git` | API HTTP, reglas de negocio y persistencia |
| `control-accesos-frontend` | `https://github.com/centralmat12/control-accesos-frontend.git` | Panel de administración en el navegador |

No hay otros repositorios Git dentro del workspace. El programa que habla con el lector biométrico no está en este directorio. La API lo nombra como aplicación o instalador local y le ofrece autenticación de agente.

## Componentes

```text
Navegador
  panel Vite (puerto de desarrollo habitual 5173)
        |  /api y /health por el proxy de Vite
        v
ControlFichajes.API
  misma solución, dos usos:
  - API central: el proceso desplegado que el proxy puede apuntar
  - API local: el mismo proyecto en http://localhost:5052
        |
        |  EF Core + MySQL 8
        v
MySQL
  en local puede alcanzarse por un túnel SSH (el README de la API cita localhost:3307)

Aplicación del lector (fuera de este workspace)
  POST /api/auth/agente
  fichadas, catálogo y GET /api/agentes/sucursal
        |
        v
la misma ControlFichajes.API
```

La API local no es otro código ni una base aislada. El README de la API indica que, en la prueba de túnel, sigue conectada a la base remota.

## Comunicación

- El frontend no abre MySQL. Consume HTTP. En desarrollo, `VITE_API_BASE_URL` vacío hace que el navegador llame al origen de Vite y `vite.config.js` reenvíe `/api` y `/health` a `DEV_API_PROXY_TARGET`. Si esa variable no está, el archivo de Vite tiene un host de respaldo. No copiar direcciones ni `.env.local` a la documentación.
- Usuarios web: `POST /api/Auth/Login` o la ruta documentada en el README de la API. El JWT de `SuperAdmin` no trae `empresa_id`; el panel envía `X-Empresa-Id`. `ADMIN` y `RRHH` usan el `empresa_id` del token.
- Agentes: `POST /api/auth/agente` con `clientId` y `clientSecret`. El JWT lleva `agente_id`, `empresa_id`, `sucursal_id` y rol `AGENTE_SUCURSAL`. El alta, listado, rotación y baja de agentes son de `SuperAdmin`. El secreto se muestra una vez y no se documenta ni se loguea.
- Fichadas biométricas: el agente envía eventos sin inventar `tipoRegistro`. La API clasifica entrada y salida. El panel no debe reemplazar esa clasificación.
- Turnos y jornadas: el panel administra turnos y muestra el resumen que calcula la API.

## Qué vive en cada repositorio

En la API: contrato HTTP, modelos, validación, persistencia, pruebas .NET y el README largo de endpoints.

En el frontend: pantallas, clientes HTTP, checks de `scripts/check-*.js`, `docs/FRONTEND-SECURITY.md` y el README de arranque.

Compartido, y por eso duplicado hasta que exista un repositorio común: arquitectura, estado verificado, pendientes y decisiones de estas cuatro guías.
