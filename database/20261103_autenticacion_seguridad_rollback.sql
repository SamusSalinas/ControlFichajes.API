-- Revierte 20261103_autenticacion_seguridad.sql.
-- Elimina las tablas nuevas y las columnas agregadas a Usuario.
-- No modifica PasswordHash, Rol, Activo ni TokenVersion.
-- No forma parte de la instalación. No ejecutarlo en producción sin respaldo:
-- al quitar CorreoVerificadoEn se pierde la marca de verificación.

SET NAMES utf8mb4;

DROP TABLE IF EXISTS `CodigoRecuperacionMfa`;
DROP TABLE IF EXISTS `DesafioMfa`;
DROP TABLE IF EXISTS `RefreshToken`;
DROP TABLE IF EXISTS `RecuperacionPassword`;
DROP TABLE IF EXISTS `InvitacionUsuario`;
DROP TABLE IF EXISTS `AuditoriaSeguridad`;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'UltimoPasoTotp'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `UltimoPasoTotp`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaOmitidoHasta'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `MfaOmitidoHasta`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpSecretoCifrado'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `TotpSecretoCifrado`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpHabilitado'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `TotpHabilitado`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'InvitacionAceptadaEn'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `InvitacionAceptadaEn`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'InvitadoEn'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `InvitadoEn`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'CorreoVerificadoEn'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `CorreoVerificadoEn`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
