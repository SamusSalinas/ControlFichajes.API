-- Revierte 20261105_mfa_persistencia.sql.
-- Quita las columnas nuevas y el índice único de códigos.
-- No toca TotpHabilitado, TotpSecretoCifrado, MfaOmitidoHasta ni UltimoPasoTotp.
-- No modifica PasswordHash, Rol, EmpresaId, Activo ni TokenVersion.
-- No forma parte de la instalación. No ejecutarlo en producción sin respaldo.

SET NAMES utf8mb4;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'CodigoRecuperacionMfa'
          AND INDEX_NAME = 'UX_CodigoRecuperacionMfa_CodigoHash'
    ),
    'ALTER TABLE `CodigoRecuperacionMfa` DROP INDEX `UX_CodigoRecuperacionMfa_CodigoHash`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'Intentos'
    ),
    'ALTER TABLE `DesafioMfa` DROP COLUMN `Intentos`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'ModoSesion'
    ),
    'ALTER TABLE `DesafioMfa` DROP COLUMN `ModoSesion`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'Generacion'
    ),
    'ALTER TABLE `DesafioMfa` DROP COLUMN `Generacion`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'TokenVersion'
    ),
    'ALTER TABLE `DesafioMfa` DROP COLUMN `TokenVersion`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaBloqueadoHasta'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `MfaBloqueadoHasta`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaIntentosFallidos'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `MfaIntentosFallidos`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpPendienteExpiraEn'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `TotpPendienteExpiraEn`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpPendienteCifrado'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `TotpPendienteCifrado`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaObligatorio'
    ),
    'ALTER TABLE `Usuario` DROP COLUMN `MfaObligatorio`',
    'SELECT 1'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
