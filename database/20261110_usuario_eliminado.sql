-- Baja lógica de usuarios. Agrega Usuario.EliminadoEn si falta.
-- Idempotente. No reescribe hashes, roles, empresas, estados ni TokenVersion.
-- No borra filas ni fichadas.

SET NAMES utf8mb4;

SET @columna = (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'EliminadoEn'
);
SET @ddl = IF(
    @columna > 0,
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `EliminadoEn` datetime(6) DEFAULT NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
