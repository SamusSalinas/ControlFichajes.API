-- Etapa 7C.1. Operación de enrolamiento y conjunto de códigos de recuperación.
-- Idempotente. No reescribe usuarios, hashes, roles, empresas, estados ni TokenVersion.
-- No borra filas. Si ya hay códigos de recuperación, aborta sin agregar columnas y sin asignarles un conjunto.
-- No ejecutar contra producción sin una ventana acordada.
-- Una instalación limpia con 20261023_esquema.sql ya trae estas columnas. Este script no las duplica.

SET NAMES utf8mb4;

SET @tabla = (
    SELECT COUNT(*) FROM information_schema.TABLES
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'OperacionEnrolamientoMfa'
);
SET @ddl = IF(
    @tabla > 0,
    'SELECT 1',
    'CREATE TABLE `OperacionEnrolamientoMfa` (
      `Id` int NOT NULL AUTO_INCREMENT,
      `UsuarioId` int NOT NULL,
      `TokenHash` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
      `TokenVersion` int NOT NULL,
      `SesionId` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
      `CreadaEn` datetime(6) NOT NULL,
      `ExpiraEn` datetime(6) NOT NULL,
      `InvalidadaEn` datetime(6) DEFAULT NULL,
      `ConsumidaEn` datetime(6) DEFAULT NULL,
      `Intentos` int NOT NULL DEFAULT 0,
      PRIMARY KEY (`Id`),
      UNIQUE KEY `UX_OperacionEnrolamientoMfa_TokenHash` (`TokenHash`),
      KEY `IX_OperacionEnrolamientoMfa_UsuarioId` (`UsuarioId`),
      CONSTRAINT `FK_OperacionEnrolamientoMfa_Usuario_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE CASCADE
    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @columna = (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'CodigoRecuperacionMfa' AND COLUMN_NAME = 'ConjuntoId'
);
SET @filas = (SELECT COUNT(*) FROM `CodigoRecuperacionMfa`);
SET @ddl = IF(
    @columna > 0,
    'SELECT 1',
    IF(
        @filas > 0,
        'CALL cf_mfa_codigos_existentes_no_se_reescriben()',
        'ALTER TABLE `CodigoRecuperacionMfa`
            ADD COLUMN `ConjuntoId` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
            ADD COLUMN `CreadoEn` datetime(6) NOT NULL'
    )
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
