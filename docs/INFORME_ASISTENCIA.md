# Informe de asistencia

Documento para continuar el trabajo en la API. No implementa endpoints, migraciones ni cambios de código productivo.

Fecha: 1 de noviembre de 2026.  
Audiencia: quien retome el backend (Samu, desde Antigravity u otro entorno).  
Estado: el frontend ya muestra un informe operativo componiendo contratos existentes. Ese resultado no es un informe histórico oficial.

## Problema

El frontend puede armar un informe de asistencia con fichadas, calendario de jornada, asignaciones y empleados que la API ya devuelve. Ese cálculo usa el turno y la tolerancia vigentes al momento de la consulta.

No puede garantizar un resultado histórico oficial: si alguien edita el turno, la tolerancia o la asignación, una consulta posterior del mismo período puede cambiar. No hay un cierre mensual ni un snapshot del horario que rigió cada jornada.

Por eso el archivo exportado se llama informe operativo. No es documentación laboral consolidada.

## Qué hace hoy el frontend

Capa de acceso: `frontend/src/api/informe-asistencia.js`, función `obtenerInformeAsistencia(filtros)`.

La vista no clasifica. Llama a esa función y pinta el resultado. La clasificación está en `frontend/src/utils/informe-asistencia.js`.

Contratos que ya consume, sin modificarlos:

- `GET /api/fichadas` con `desde`, `hasta`, `limite` (máximo actual 500) y, si hay un solo empleado, `empleadoId`.
- `GET /api/jornadas/empleados/{id}/calendario?desde&hasta`.
- `GET /api/jornadas/empleados/{id}/asignaciones`, solo para el nombre del turno vigente en la fecha.
- `GET /api/fichadas/alertas/incompletas`, solo para marcar “Pendiente de revisión” si ya existe una alerta. Si responde 404 o 405, el informe sigue sin ese dato.
- `GET /api/empleados`, `GET /api/sucursales`, `GET /api/departamentos`.

No envía `empresaId` en el cuerpo ni en la query. El alcance de empresa sigue siendo el del JWT y, para SuperAdmin, el header de empresa operativa que ya usa el cliente.

### Regla operativa que el frontend aplica

Solo clasifica llegada o salida cuando el calendario del día trae horario configurado, hora de entrada, hora de salida y tolerancia entera mayor o igual que cero, y la jornada no está en estado “Revisar”.

Entrada programada 08:00 y tolerancia 15 minutos:

- hasta 08:00: A tiempo;
- de 08:01 a 08:15: Dentro de tolerancia;
- desde 08:16: Llegada tarde.

Para una entrada 08:22 la diferencia total es 22 minutos y el exceso sobre la tolerancia es 7 minutos.

La salida usa la misma tolerancia del turno. El modelo actual tiene un solo `ToleranciaMinutos` por turno, pensado como minutos posteriores a la entrada. Reutilizarlo para la salida es provisorio: salida a tiempo si es igual o posterior a la prevista; dentro de tolerancia si se adelanta hasta esos minutos; salida anticipada si se adelanta más. El exceso es el adelanto menos la tolerancia.

Si la hora de salida del calendario es anterior o igual a la de entrada, el frontend trata el tramo como nocturno y suma un día a la salida prevista. La fecha operativa de las fichadas es la que ya calcula la clasificación existente (`jornadaFecha`), no un corte nuevo.

Minutos programados: los que informa el calendario del día, si el día tiene turno y horario.  
Minutos trabajados: de la primera a la última fichada, menos `minutosAlmuerzo` del día, una sola vez, y solo si hay entrada y salida. No se inventa un descanso de 60 cuando el dato falta.  
Saldo: trabajados menos programados, solo cuando ambos existen.  
Un día pasado, con turno y sin fichadas, se muestra como “Sin registros”, no como ausencia. No suma como pendiente de revisión.  
Un día libre del calendario, sin fichadas, no entra en el informe.  
Sin horario, sin tolerancia o con la jornada en “Revisar”: “Sin información suficiente”.  
“Jornada incompleta” solo cuando falta una marca y la clasificación existente ya lo indica. Puede convivir con una llegada tarde. No convierte sola a la jornada en “fuera de horario”.  
“Dentro de tolerancia” no es alerta.

No se calculan feriados, licencias, francos, vacaciones ni ausencias. El calendario puede marcar un día libre; eso no se traduce a una licencia.

Límite conocido: `GET /api/fichadas` corta en 500 registros. Con todos los empleados, el informe puede quedar incompleto. En ese caso no se inventan días “Sin registros”, porque las fichadas faltantes podrían estar fuera del corte.

## Objetivo de API

Proveer un informe autoritativo de asistencia por empresa, período, empleado, departamento, sucursal y tipo de incidencia.

El cálculo oficial tiene que vivir en el servidor, con la empresa del usuario autenticado, y tiene que poder repetirse después de un cierre sin cambiar porque alguien editó el turno.

## Contrato propuesto

No está implementado.

### Consulta

`GET /api/jornadas/informe-asistencia`

Filtros:

| Parámetro | Uso |
| --- | --- |
| `desde` | Fecha inclusive `AAAA-MM-DD` |
| `hasta` | Fecha inclusive `AAAA-MM-DD` |
| `empleadoId` | Opcional |
| `departamentoId` | Opcional |
| `sucursalId` | Opcional |
| `incidencia` | `todas`, `llegada-tarde`, `salida-anticipada`, `dentro-tolerancia`, `incompleta`, `sin-informacion` |
| `pagina` | Desde 1 |
| `tamano` | Tope acordado, por ejemplo 100 en la consulta y un máximo mayor en la exportación |

La empresa no viaja como filtro libre de un usuario ADMIN o RRHH. SuperAdmin sigue eligiendo la empresa por el mecanismo ya existente.

Respuesta sugerida:

```json
{
  "desde": "2026-11-01",
  "hasta": "2026-11-30",
  "calculadoEn": "2026-11-30T21:00:00-03:00",
  "criterio": {
    "version": "asistencia-v1",
    "toleranciaSalida": "reutiliza-tolerancia-del-turno",
    "zonaHoraria": "America/Argentina/Buenos_Aires",
    "consolidado": false
  },
  "resumen": {
    "llegadasTarde": 0,
    "salidasAnticipadas": 0,
    "jornadasIncompletas": 0,
    "minutosFueraDeHorario": 0
  },
  "empleados": [],
  "jornadas": [],
  "paginacion": { "pagina": 1, "tamano": 100, "total": 0 }
}
```

Cada jornada debería traer fecha, empleado, legajo, departamento, sucursal, nombre de turno, horario previsto, tolerancia aplicada, entrada, salida, resultados (puede haber más de uno), diferencia, exceso, minutos programados, minutos trabajados, saldo y estado de revisión si existe.

Cada fila de empleado debería traer jornadas evaluadas, llegadas tarde, minutos de tardanza (exceso, no la diferencia bruta), salidas anticipadas, minutos de salida anticipada, incompletas, minutos programados, minutos trabajados y saldo. Si un total no se puede calcular, `null`, no un cero inventado.

“Dentro de tolerancia” no debe mezclarse con los contadores de incumplimiento.

### Exportación

Preferible un endpoint aparte para poder limitar tamaño, registrar auditoría y devolver el archivo sin paginar la UI:

`GET /api/jornadas/informe-asistencia/exportar?vista=detalle|resumen`

Mismos filtros que la consulta. La exportación tiene que usar exactamente el mismo criterio que la consulta. Si más adelante se reutiliza el GET principal, el servidor igual debe imponer un máximo de filas y no dejar que el cliente pagine “a ojo” para armar el archivo.

UTF-8 con BOM, separador `;` si se mantiene el criterio actual de Excel regional, y protección de celdas que empiecen con `=`, `+`, `-` o `@`. El pie o el nombre del archivo operativo puede decir “Informe operativo” hasta que el período esté consolidado.

El CSV actual de Fichadas sigue siendo el informe operativo de marcas. No reemplaza a este.

## Reglas de negocio pendientes

Hay que cerrarlas en la API antes de tratar el informe como oficial:

- Llegada a tiempo: entrada en o antes del horario previsto. No suma tardanza.
- Llegada dentro de tolerancia: posterior al horario y hasta el horario más la tolerancia. Es válida. No es alerta.
- Llegada tarde: posterior al horario más la tolerancia. El exceso es esa diferencia menos la tolerancia.
- Salida dentro de tolerancia y salida anticipada: hoy el frontend reutiliza la tolerancia única del turno. Falta decidir si la salida tiene una tolerancia propia.
- Jornadas nocturnas: la salida prevista pasa al día siguiente cuando la hora de salida es menor o igual que la de entrada. La fecha operativa tiene que ser la de la jornada, no la del reloj civil de la última marca. No inventar otro corte.
- Días sin fichadas: un día laborable pasado, con horario, sin marcas, es “Sin registros”. No es ausencia mientras no existan francos, licencias, vacaciones, feriados y excepciones.
- Licencias, vacaciones, francos y feriados: no están en el contrato actual. No se deben inferir.
- Cambios de turno durante el período: el calendario de hoy proyecta la asignación vigente. El informe oficial tiene que guardar el turno y la tolerancia que regían esa fecha.
- Correcciones manuales: una fichada manual o una observación puede cambiar entrada, salida o el estado de revisión. El informe abierto se recalcula. El consolidado no cambia en silencio.
- Reapertura de períodos: hace falta un acto explícito, con usuario, fecha y motivo, que vuelva a generar el mes y deje la versión anterior.
- Zona horaria de la empresa: el resumen actual usa `America/Argentina/Buenos_Aires`. El informe debe usar la zona de la empresa, no la del servidor ni la del navegador. Hoy el producto opera en Argentina.

## Persistencia histórica

No hace falta un backup constante de cada consulta.

Propuesta:

- Período abierto: cálculo en vivo con el turno, la tolerancia y las fichadas actuales.
- Cierre mensual: consolidar el mes por empresa. A partir de ahí el GET devuelve el consolidado, no un recálculo libre.
- Guardar, por jornada o en un snapshot equivalente, el turno efectivo, el horario, la tolerancia, los minutos programados y la versión del criterio.
- Después de una corrección en un mes cerrado, no regenerar solo. Exigir reapertura o un recálculo controlado.
- Trazar usuario, fecha y motivo de cada cierre, reapertura y exportación si ya hay infraestructura de auditoría.
- Guardar la versión del criterio (`asistencia-v1` o la que se defina).
- Prohibido modificar en silencio un informe ya consolidado.

## Seguridad

- Aislar por empresa. Un ADMIN o RRHH no elige `empresaId` arbitrario.
- Autorizar con las reglas ya usadas para jornadas: SuperAdmin, ADMIN y RRHH. Confirmar que RRHH puede leer y no escribir.
- No devolver DNI, CUIL ni datos biométricos.
- Limitar el rango de fechas y el tamaño de la exportación.
- Registrar la exportación si el proyecto ya tiene auditoría. Si no existe, dejarlo anotado y no inventar una tabla en esta etapa.

## Criterios de aceptación de la futura API

Pruebas que tienen que existir cuando se implemente. No se escriben en esta tarea.

- Tolerancia exacta en el límite: 08:15 con tolerancia 15 es dentro de tolerancia y exceso 0.
- Un minuto fuera: 08:16 es llegada tarde y exceso 1.
- Llegada anterior al horario: a tiempo, sin exceso.
- Salida anticipada, con la tolerancia que se termine de definir.
- Una jornada con llegada tarde y salida anticipada: las dos incidencias, y los minutos programados y trabajados de esa jornada una sola vez.
- Jornada incompleta: no se inventa la salida ni se la cuenta como fuera de horario si no hay marca.
- Empleado sin turno: sin información suficiente, sin minutos programados inventados.
- Mes sin resultados: resumen en cero y detalle vacío.
- Cambio de turno a mitad de mes: cada día usa el turno que regía esa fecha, no el último editado.
- Corrección posterior de una fichada en un período abierto: el informe cambia. En un período cerrado: no cambia hasta una reapertura.
- Empresa distinta: no ve los datos de otra.
- Exportación consistente con la consulta, mismos totales.
- Zona horaria `America/Argentina/Buenos_Aires`: una fichada de las 22:30 locales no pasa al día siguiente por estar en UTC.
- Datos históricos que no cambian después del cierre aunque se edite el turno.

## Fuera de esta entrega

No se crean migraciones, controladores, DTOs ni cambios en `Program.cs`. El frontend queda preparado para reemplazar `obtenerInformeAsistencia` por el GET propuesto sin rediseñar la pantalla.
