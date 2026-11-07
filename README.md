# ControlFichajes.API

API REST para gestionar empresas, usuarios, empleados, agentes biometricos,
turnos y fichadas. La API es la fuente de verdad del contrato para el frontend
y el cliente biometrico.

```mermaid
flowchart LR
    Web["Frontend web"] --> API["ControlFichajes.API"]
    Reader["Agente biometrico"] --> API
    API --> DB[("MySQL 8")]
```

## Documentacion

La documentacion detallada del contrato HTTP y de la operacion del proyecto
esta organizada en la [boveda de documentacion](<Documentacion API control de fichadas/Inicio.md>).
