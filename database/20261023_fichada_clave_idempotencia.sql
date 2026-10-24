-- Clave única nullable para eventos biométricos.
-- MySQL admite varias filas con NULL, así que las fichadas manuales y las
-- filas ya existentes no reciben clave y no se reescriben.
-- Idempotente: no elimina fichadas ni duplica el índice.
-- La sesión ya debe tener seleccionada la base de destino.

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'Fichada'
          AND COLUMN_NAME = 'ClaveIdempotencia'
    ),
    'SELECT 1',
    'ALTER TABLE `Fichada` ADD COLUMN `ClaveIdempotencia` varchar(40) NULL'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @indice = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Fichada'
      AND INDEX_NAME = 'UX_Fichada_ClaveIdempotencia'
      AND NON_UNIQUE = 0
);
SET @ddl = IF(
    @indice > 0,
    'SELECT 1',
    'ALTER TABLE `Fichada` ADD UNIQUE KEY `UX_Fichada_ClaveIdempotencia` (`ClaveIdempotencia`)'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
