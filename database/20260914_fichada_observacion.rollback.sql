-- ControlFichajes.API - rollback de observaciones de fichadas
-- Compatible con MySQL 8.0.42.
--
-- Elimina únicamente la tabla `FichadaObservacion`.
-- No modifica Fichada, Empleado ni Usuario.
-- No contiene connection strings ni datos de negocio.
--
-- ORDEN:
--   1. Reponer un binario que no mapee FichadaObservacion.
--   2. Realizar y verificar un respaldo.
--   3. Seleccionar explícitamente la base de datos de destino.
--   4. Ejecutar este script.

DROP TABLE IF EXISTS `FichadaObservacion`;
