-- Etapa 7A. Persistencia de MFA sobre una base que ya tiene las tablas de autenticación.
-- Idempotente. Agrega columnas y el índice único de códigos.
-- No reescribe PasswordHash, Rol, EmpresaId, Activo, TokenVersion ni los campos TOTP ya existentes.
-- No borra filas. Si hay hashes de recuperación duplicados, aborta y no crea el índice.
-- No ejecutar contra producción sin una ventana acordada.
-- Una instalación limpia con 20261023_esquema.sql ya trae estas columnas. Este script no las duplica.

SET NAMES utf8mb4;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaObligatorio'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `MfaObligatorio` tinyint(1) NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpPendienteCifrado'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `TotpPendienteCifrado` varchar(512) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpPendienteExpiraEn'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `TotpPendienteExpiraEn` datetime(6) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaIntentosFallidos'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `MfaIntentosFallidos` int NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaBloqueadoHasta'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `MfaBloqueadoHasta` datetime(6) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'TokenVersion'
    ),
    'SELECT 1',
    'ALTER TABLE `DesafioMfa` ADD COLUMN `TokenVersion` int NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'Generacion'
    ),
    'SELECT 1',
    'ALTER TABLE `DesafioMfa` ADD COLUMN `Generacion` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'ModoSesion'
    ),
    'SELECT 1',
    'ALTER TABLE `DesafioMfa` ADD COLUMN `ModoSesion` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'DesafioMfa' AND COLUMN_NAME = 'Intentos'
    ),
    'SELECT 1',
    'ALTER TABLE `DesafioMfa` ADD COLUMN `Intentos` int NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @duplicados := (
    SELECT COUNT(*)
    FROM (
        SELECT `CodigoHash`
        FROM `CodigoRecuperacionMfa`
        GROUP BY `CodigoHash`
        HAVING COUNT(*) > 1
    ) `duplicados`
);

SET @indice := (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'CodigoRecuperacionMfa'
      AND INDEX_NAME = 'UX_CodigoRecuperacionMfa_CodigoHash'
);

SET @ddl = IF(
    @duplicados > 0,
    'CALL `cf_mfa_codigo_hash_duplicado_no_se_borran_filas`()',
    IF(
        @indice = 0,
        'ALTER TABLE `CodigoRecuperacionMfa` ADD UNIQUE KEY `UX_CodigoRecuperacionMfa_CodigoHash` (`CodigoHash`)',
        'SELECT 1'
    )
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
