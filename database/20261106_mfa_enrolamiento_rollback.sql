-- Rollback de la etapa 7C.1. No se ejecuta en la instalación.
-- Quita la operación de enrolamiento y las columnas de conjunto. No toca el secreto TOTP ni los usuarios.

SET NAMES utf8mb4;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OperacionEnrolamientoMfa'
    ),
    'DROP TABLE `OperacionEnrolamientoMfa`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CodigoRecuperacionMfa' AND COLUMN_NAME = 'CreadoEn'
    ),
    'ALTER TABLE `CodigoRecuperacionMfa` DROP COLUMN `CreadoEn`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CodigoRecuperacionMfa' AND COLUMN_NAME = 'ConjuntoId'
    ),
    'ALTER TABLE `CodigoRecuperacionMfa` DROP COLUMN `ConjuntoId`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
