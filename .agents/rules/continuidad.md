---
trigger: always_on
description: Consultar y mantener las guías de arquitectura, estado, pendientes y decisiones antes de cambiar comportamiento.
---

# Continuidad del proyecto

`AGENTS.md` es la instrucción compartida. Las guías viven en un solo lugar:

- @../../docs/ARQUITECTURA.md
- @../../docs/ESTADO_ACTUAL.md
- @../../docs/PENDIENTES.md
- @../../docs/DECISIONES_TECNICAS.md

Antes de modificar comportamiento, leer esos cuatro archivos. No copiar su contenido a esta regla.

Esos cuatro documentos también existen en el otro repositorio del workspace. Si cambia un hecho compartido, actualizar las dos copias en la misma sesión. No borrar README ni documentos históricos para reemplazarlos por estas guías.

Al cerrar un cambio verificado, actualizar el documento que corresponda:

- comportamiento nuevo o corregido con evidencia: `docs/ESTADO_ACTUAL.md`
- tarea abierta o fallo reproducido: `docs/PENDIENTES.md`
- elección que haya que respetar después: `docs/DECISIONES_TECNICAS.md`

No anotar secretos, connection strings, tokens, `clientSecret` ni datos biométricos. No dar por desplegado o mergeado un cambio que no se haya publicado.
