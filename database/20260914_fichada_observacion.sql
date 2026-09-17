-- ControlFichajes.API - observaciones 1:1 de fichadas
-- Compatible con MySQL 8.0.42.
--
-- ORDEN DE DESPLIEGUE:
--   1. Realizar y verificar un respaldo.
--   2. Seleccionar explícitamente la base de datos de destino.
--   3. Ejecutar este script.
--   4. Ejecutar la verificación incluida al final.
--   5. Recién entonces desplegar el binario.
--
-- Idempotente y fail-closed: crea la tabla si no existe; si existe con el
-- esquema esperado, no la modifica; si existe con estructura incompatible,
-- aborta con SIGNAL SQLSTATE '45000'. No modifica Fichada, Empleado ni
-- Usuario. No contiene connection strings ni datos de negocio.

SET @schema_name = DATABASE();

DROP PROCEDURE IF EXISTS `_abortar_esquema_FichadaObservacion`;
CREATE PROCEDURE `_abortar_esquema_FichadaObservacion`()
  SIGNAL SQLSTATE '45000'
    SET MESSAGE_TEXT = 'FichadaObservacion existe con un esquema incompatible.';

SET @ddl = IF(
    EXISTS(
        SELECT 1 FROM information_schema.TABLES
        WHERE TABLE_SCHEMA = @schema_name
          AND TABLE_NAME = 'FichadaObservacion'
    ),
    'SELECT 1',
    'CREATE TABLE `FichadaObservacion` (
        `FichadaId` int NOT NULL,
        `Motivo` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
        `Detalle` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        `CreadoPorUsuarioId` int NULL,
        `CreadoPorNombre` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `CreadoEn` datetime(6) NOT NULL,
        `ModificadoPorUsuarioId` int NULL,
        `ModificadoPorNombre` varchar(50) CHARACTER SET utf8mb4 NULL,
        `ModificadoEn` datetime(6) NULL,
        PRIMARY KEY (`FichadaId`),
        KEY `IX_FichadaObservacion_CreadoPorUsuarioId` (`CreadoPorUsuarioId`),
        KEY `IX_FichadaObservacion_ModificadoPorUsuarioId` (`ModificadoPorUsuarioId`),
        CONSTRAINT `FK_FichadaObservacion_Fichada_FichadaId`
            FOREIGN KEY (`FichadaId`) REFERENCES `Fichada` (`Id`) ON DELETE CASCADE,
        CONSTRAINT `FK_FichadaObservacion_Usuario_CreadoPorUsuarioId`
            FOREIGN KEY (`CreadoPorUsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE SET NULL,
        CONSTRAINT `FK_FichadaObservacion_Usuario_ModificadoPorUsuarioId`
            FOREIGN KEY (`ModificadoPorUsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE SET NULL
    ) CHARACTER SET utf8mb4'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @cols_ok = (
    SELECT IF(
        COUNT(*) = 9
        AND SUM(COLUMN_NAME = 'FichadaId' AND DATA_TYPE = 'int' AND IS_NULLABLE = 'NO') = 1
        AND SUM(COLUMN_NAME = 'Motivo' AND DATA_TYPE = 'varchar' AND CHARACTER_MAXIMUM_LENGTH = 40 AND IS_NULLABLE = 'NO') = 1
        AND SUM(COLUMN_NAME = 'Detalle' AND DATA_TYPE = 'varchar' AND CHARACTER_MAXIMUM_LENGTH = 500 AND IS_NULLABLE = 'NO') = 1
        AND SUM(COLUMN_NAME = 'CreadoPorUsuarioId' AND DATA_TYPE = 'int' AND IS_NULLABLE = 'YES') = 1
        AND SUM(COLUMN_NAME = 'CreadoPorNombre' AND DATA_TYPE = 'varchar' AND CHARACTER_MAXIMUM_LENGTH = 50 AND IS_NULLABLE = 'NO') = 1
        AND SUM(COLUMN_NAME = 'CreadoEn' AND DATA_TYPE = 'datetime' AND DATETIME_PRECISION = 6 AND IS_NULLABLE = 'NO') = 1
        AND SUM(COLUMN_NAME = 'ModificadoPorUsuarioId' AND DATA_TYPE = 'int' AND IS_NULLABLE = 'YES') = 1
        AND SUM(COLUMN_NAME = 'ModificadoPorNombre' AND DATA_TYPE = 'varchar' AND CHARACTER_MAXIMUM_LENGTH = 50 AND IS_NULLABLE = 'YES') = 1
        AND SUM(COLUMN_NAME = 'ModificadoEn' AND DATA_TYPE = 'datetime' AND DATETIME_PRECISION = 6 AND IS_NULLABLE = 'YES') = 1,
        1, 0)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = @schema_name
      AND TABLE_NAME = 'FichadaObservacion'
);

SET @pk_ok = (
    SELECT IF(COUNT(*) = 1 AND SUM(k.COLUMN_NAME = 'FichadaId') = 1, 1, 0)
    FROM information_schema.TABLE_CONSTRAINTS t
    INNER JOIN information_schema.KEY_COLUMN_USAGE k
        ON k.CONSTRAINT_SCHEMA = t.CONSTRAINT_SCHEMA
       AND k.CONSTRAINT_NAME = t.CONSTRAINT_NAME
       AND k.TABLE_NAME = t.TABLE_NAME
    WHERE t.TABLE_SCHEMA = @schema_name
      AND t.TABLE_NAME = 'FichadaObservacion'
      AND t.CONSTRAINT_TYPE = 'PRIMARY KEY'
);

SET @fk_fichada_ok = (
    SELECT IF(COUNT(*) = 1, 1, 0)
    FROM information_schema.REFERENTIAL_CONSTRAINTS r
    INNER JOIN information_schema.KEY_COLUMN_USAGE k
        ON k.CONSTRAINT_SCHEMA = r.CONSTRAINT_SCHEMA
       AND k.CONSTRAINT_NAME = r.CONSTRAINT_NAME
       AND k.TABLE_NAME = r.TABLE_NAME
    WHERE r.CONSTRAINT_SCHEMA = @schema_name
      AND r.TABLE_NAME = 'FichadaObservacion'
      AND k.COLUMN_NAME = 'FichadaId'
      AND k.REFERENCED_TABLE_SCHEMA = @schema_name
      AND k.REFERENCED_TABLE_NAME = 'Fichada'
      AND k.REFERENCED_COLUMN_NAME = 'Id'
      AND r.DELETE_RULE = 'CASCADE'
);

SET @fk_creado_ok = (
    SELECT IF(COUNT(*) = 1, 1, 0)
    FROM information_schema.REFERENTIAL_CONSTRAINTS r
    INNER JOIN information_schema.KEY_COLUMN_USAGE k
        ON k.CONSTRAINT_SCHEMA = r.CONSTRAINT_SCHEMA
       AND k.CONSTRAINT_NAME = r.CONSTRAINT_NAME
       AND k.TABLE_NAME = r.TABLE_NAME
    WHERE r.CONSTRAINT_SCHEMA = @schema_name
      AND r.TABLE_NAME = 'FichadaObservacion'
      AND k.COLUMN_NAME = 'CreadoPorUsuarioId'
      AND k.REFERENCED_TABLE_SCHEMA = @schema_name
      AND k.REFERENCED_TABLE_NAME = 'Usuario'
      AND k.REFERENCED_COLUMN_NAME = 'Id'
      AND r.DELETE_RULE = 'SET NULL'
);

SET @fk_modificado_ok = (
    SELECT IF(COUNT(*) = 1, 1, 0)
    FROM information_schema.REFERENTIAL_CONSTRAINTS r
    INNER JOIN information_schema.KEY_COLUMN_USAGE k
        ON k.CONSTRAINT_SCHEMA = r.CONSTRAINT_SCHEMA
       AND k.CONSTRAINT_NAME = r.CONSTRAINT_NAME
       AND k.TABLE_NAME = r.TABLE_NAME
    WHERE r.CONSTRAINT_SCHEMA = @schema_name
      AND r.TABLE_NAME = 'FichadaObservacion'
      AND k.COLUMN_NAME = 'ModificadoPorUsuarioId'
      AND k.REFERENCED_TABLE_SCHEMA = @schema_name
      AND k.REFERENCED_TABLE_NAME = 'Usuario'
      AND k.REFERENCED_COLUMN_NAME = 'Id'
      AND r.DELETE_RULE = 'SET NULL'
);

SET @ix_creado_ok = (
    SELECT IF(COUNT(*) >= 1, 1, 0)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = @schema_name
      AND TABLE_NAME = 'FichadaObservacion'
      AND COLUMN_NAME = 'CreadoPorUsuarioId'
);

SET @ix_modificado_ok = (
    SELECT IF(COUNT(*) >= 1, 1, 0)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = @schema_name
      AND TABLE_NAME = 'FichadaObservacion'
      AND COLUMN_NAME = 'ModificadoPorUsuarioId'
);

SET @esquema_ok = IF(
    IFNULL(@cols_ok, 0) = 1
    AND IFNULL(@pk_ok, 0) = 1
    AND IFNULL(@fk_fichada_ok, 0) = 1
    AND IFNULL(@fk_creado_ok, 0) = 1
    AND IFNULL(@fk_modificado_ok, 0) = 1
    AND IFNULL(@ix_creado_ok, 0) = 1
    AND IFNULL(@ix_modificado_ok, 0) = 1,
    1, 0);

SET @verificar = IF(
    @esquema_ok = 1,
    'SELECT 1',
    'CALL `_abortar_esquema_FichadaObservacion`()');
PREPARE stmt FROM @verificar;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

DROP PROCEDURE IF EXISTS `_abortar_esquema_FichadaObservacion`;

SELECT
    COLUMN_NAME,
    COLUMN_TYPE,
    IS_NULLABLE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME = 'FichadaObservacion'
ORDER BY ORDINAL_POSITION;
