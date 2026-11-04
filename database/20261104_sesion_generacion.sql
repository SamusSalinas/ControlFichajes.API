-- Etapa 6A.1. Agrega la generación a una base que ya tiene RefreshToken.
-- Idempotente: no recrea la tabla ni toca las filas existentes. Generacion queda nula en esas filas.

SET @existe := (
  SELECT COUNT(*)
  FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'RefreshToken'
    AND COLUMN_NAME = 'Generacion'
);

SET @sql := IF(
  @existe = 0,
  'ALTER TABLE `RefreshToken` ADD COLUMN `Generacion` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL, ADD KEY `IX_RefreshToken_Generacion` (`Generacion`)',
  'SELECT 1'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
