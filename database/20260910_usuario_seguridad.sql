-- ControlFichajes.API - actualización de seguridad de Usuario
-- Compatible con MySQL 8.0.42.
--
-- ORDEN DE DESPLIEGUE:
--   1. Realizar y verificar un respaldo.
--   2. Seleccionar explícitamente la base de datos de destino.
--   3. Ejecutar este script.
--   4. Ejecutar la verificación incluida al final.
--   5. Recién entonces desplegar el binario.
--
-- El script consulta information_schema antes de cada ALTER porque MySQL 8.0.42
-- no ofrece ADD COLUMN IF NOT EXISTS para este caso. Es idempotente, no elimina
-- filas y conserva a todos los usuarios existentes.

SET @schema_name = DATABASE();

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'RequiereCambioPassword'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `RequiereCambioPassword` tinyint(1) NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'IntentosFallidos'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `IntentosFallidos` int NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'BloqueadoHasta'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `BloqueadoHasta` datetime(6) NULL'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'UltimoIntentoFallido'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `UltimoIntentoFallido` datetime(6) NULL'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'PasswordTemporalVenceEn'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `PasswordTemporalVenceEn` datetime(6) NULL'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'PasswordTemporalUsada'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `PasswordTemporalUsada` tinyint(1) NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'Usuario'
          AND COLUMN_NAME = 'TokenVersion'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `TokenVersion` int NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- VERIFICACIÓN POSTERIOR: deben aparecer exactamente estas siete columnas.
SELECT
    COLUMN_NAME,
    COLUMN_TYPE,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'Usuario'
  AND COLUMN_NAME IN (
      'RequiereCambioPassword',
      'IntentosFallidos',
      'BloqueadoHasta',
      'UltimoIntentoFallido',
      'PasswordTemporalVenceEn',
      'PasswordTemporalUsada',
      'TokenVersion'
  )
ORDER BY ORDINAL_POSITION;

-- IMPACTO SOBRE SESIONES:
-- El binario nuevo rechaza los JWT web sin token_version o con una versión
-- distinta. Por lo tanto, los usuarios con sesiones previas deberán iniciar
-- sesión nuevamente. Los tokens de agente no usan esta validación.
--
-- ROLLBACK MANUAL:
-- 1. Reponer el binario anterior compatible con estas columnas.
-- 2. Conservar las columnas: el cambio de esquema es aditivo y dbbabd8 ya
--    mapea TokenVersion, por lo que quitarla con ese binario rompe Usuario.
-- 3. Quitar TokenVersion solo si se despliega una versión cuyo modelo ya no la
--    referencia, después de respaldar y confirmar que su estado no se necesita:
--      ALTER TABLE `Usuario` DROP COLUMN `TokenVersion`;
-- 4. No quitar las otras seis columnas del servidor actual. En una instalación
--    que partió del esquema original, eliminarlas únicamente con un binario que
--    no las mapee y después de confirmar que no se necesita su estado.
