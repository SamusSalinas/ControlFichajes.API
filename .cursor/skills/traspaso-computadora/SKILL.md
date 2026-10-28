---
name: traspaso-computadora
description: Recupera o deja listo el contexto de ControlFichajes.API y control-accesos-frontend al cambiar de computadora. Usar al finalizar una sesión, al empezar en otro equipo, o cuando se pide retomar el desarrollo, revisar pendientes, comparar las guías duplicadas o comprobar rama, commit y cambios sin commit. No hace commit, push, pull, merge, reset ni deploy por su cuenta.
---

# Traspaso entre computadoras

El workspace puede contener dos clones: `ControlFichajes.API` y `control-accesos-frontend`. El directorio padre no es un repositorio. Ejecutar Git dentro de cada clon.

Hay dos momentos. Elegir uno y no mezclar sus resultados.

## Al finalizar una sesión

1. Leer `AGENTS.md`, `docs/ESTADO_ACTUAL.md` y `docs/PENDIENTES.md` del clon en el que se trabajó.
2. Registrar solo hechos comprobados en esa sesión: comandos ejecutados, archivos vistos y resultados. No marcar un problema como resuelto sin esa evidencia.
3. Si el hecho es compartido, actualizar la misma guía en los dos clones. Guías duplicadas: `docs/ARQUITECTURA.md`, `docs/ESTADO_ACTUAL.md`, `docs/PENDIENTES.md`, `docs/DECISIONES_TECNICAS.md`.
4. No editar README ni documentos históricos para reemplazar esas guías.
5. Correr la verificación de Git de abajo. El informe final lista los archivos sin commit, incluidos los de documentación, para que no se pierdan al cambiar de equipo.
6. Parar antes de commit, push, pull, merge, reset o deploy.

## Al iniciar en otra computadora

1. Confirmar que los dos clones existen. Si falta uno, decir cuál falta y no inventar su estado.
2. Leer `AGENTS.md` y, en este orden, `docs/ARQUITECTURA.md`, `docs/ESTADO_ACTUAL.md`, `docs/PENDIENTES.md`, `docs/DECISIONES_TECNICAS.md`.
3. Correr la verificación de Git antes de modificar código.
4. Comparar las cuatro guías duplicadas, por ejemplo con `git hash-object` de cada par. Si difieren, mostrar las diferencias y no elegir una copia en silencio.
5. Resumir rama, último commit, diferencia con el remoto, cambios sin commit y pendientes abiertos. Después seguir el pedido del usuario.

## Verificación de Git

En cada clon, sin cambiar el repositorio:

```text
git status -sb
git branch --show-current
git log -1 --format="%H%n%s"
git rev-parse HEAD
git rev-parse --abbrev-ref --symbolic-full-name @{upstream}
git status --short
```

Si no hay upstream, decirlo. No ejecutar `git fetch`, `git pull`, `git merge` ni `git reset` para “ponerse al día”.

Clasificar la salida de `git status --short`:

- ` M` o `M `: modificado
- `??`: sin seguimiento
- `A`, `D`, `R`: ya preparado en el índice

Esos archivos son trabajo que todavía no está en el remoto. Nombrarlos en el resumen.

## Sincronización documental

Actualizar una guía solo si el dato salió de un comando, un archivo leído o un cambio hecho en la sesión. Un hecho compartido se escribe en las dos copias en la misma sesión. No borrar pendientes ni decisiones porque una copia esté más corta.

## Seguridad

No leer, copiar ni pegar en las guías:

- `.env`, `.env.local` y cualquier `*.local`
- `appsettings.*.local.json`
- connection strings, JWT, `clientSecret`, tokens y variables `CFB_*` con valores reales
- plantillas o imágenes biométricas
- claves privadas (`.key`, `.pem`, `.p12`, `.pfx`)

No proponer sincronizar esos archivos entre computadoras. Pueden existir solo en el equipo local.

## Operaciones que esperan autorización

No ejecutar, aunque el working tree esté sucio o la rama esté detrás del remoto:

- `git commit`, `git push`, `git pull`, `git fetch`
- `git merge`, `git rebase`, `git reset`, `git checkout` de ramas ajenas
- deploy, Docker en un servidor, o cambios de configuración de producción

Si hace falta alguna, pedir confirmación y nombrar el comando. Commit y push quedan para cuando el usuario los pida.

## Informe

Cerrar con:

- momento: cierre de sesión o inicio en otro equipo
- por cada clon: rama, hash, asunto del último commit, si coincide con el upstream, y archivos modificados, nuevos o sin commit
- guías duplicadas: iguales o distintas
- pendientes que sigan abiertos según `docs/PENDIENTES.md`
- operaciones que no se ejecutaron
