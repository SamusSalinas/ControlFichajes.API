-- Columnas y tablas para invitación, recuperación, refresh, TOTP y auditoría.
-- Idempotente. No borra usuarios, no reescribe PasswordHash, Rol ni TokenVersion.
-- La sesión ya debe tener seleccionada la base de destino.
-- No ejecutar después de una instalación limpia con 20261023_esquema.sql:
-- ese archivo ya incluye estas tablas y columnas.
-- No ejecutar contra producción sin una ventana acordada.

SET NAMES utf8mb4;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'CorreoVerificadoEn'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `CorreoVerificadoEn` datetime(6) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'InvitadoEn'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `InvitadoEn` datetime(6) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'InvitacionAceptadaEn'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `InvitacionAceptadaEn` datetime(6) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpHabilitado'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `TotpHabilitado` tinyint(1) NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'TotpSecretoCifrado'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `TotpSecretoCifrado` varchar(512) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'MfaOmitidoHasta'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `MfaOmitidoHasta` datetime(6) NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'Usuario' AND COLUMN_NAME = 'UltimoPasoTotp'
    ),
    'SELECT 1',
    'ALTER TABLE `Usuario` ADD COLUMN `UltimoPasoTotp` bigint NULL'
);
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

UPDATE `Usuario`
SET `CorreoVerificadoEn` = UTC_TIMESTAMP(6)
WHERE `CorreoVerificadoEn` IS NULL
  AND `InvitadoEn` IS NULL
  AND `PasswordHash` <> '';

CREATE TABLE IF NOT EXISTS `InvitacionUsuario` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UsuarioId` int NOT NULL,
  `TokenHash` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ExpiraEn` datetime(6) NOT NULL,
  `UsadaEn` datetime(6) DEFAULT NULL,
  `CreadaEn` datetime(6) NOT NULL,
  `CreadaPorUsuarioId` int DEFAULT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UX_InvitacionUsuario_TokenHash` (`TokenHash`),
  KEY `IX_InvitacionUsuario_UsuarioId` (`UsuarioId`),
  CONSTRAINT `FK_InvitacionUsuario_Usuario_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `RecuperacionPassword` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UsuarioId` int NOT NULL,
  `TokenHash` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ExpiraEn` datetime(6) NOT NULL,
  `UsadaEn` datetime(6) DEFAULT NULL,
  `CreadaEn` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UX_RecuperacionPassword_TokenHash` (`TokenHash`),
  KEY `IX_RecuperacionPassword_UsuarioId` (`UsuarioId`),
  CONSTRAINT `FK_RecuperacionPassword_Usuario_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `RefreshToken` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UsuarioId` int NOT NULL,
  `TokenHash` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `FamiliaId` char(36) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ExpiraEn` datetime(6) NOT NULL,
  `RevocadaEn` datetime(6) DEFAULT NULL,
  `ReemplazadaPorId` int DEFAULT NULL,
  `CreadaEn` datetime(6) NOT NULL,
  `Ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `UserAgent` varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `TokenVersion` int NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UX_RefreshToken_TokenHash` (`TokenHash`),
  KEY `IX_RefreshToken_UsuarioId` (`UsuarioId`),
  KEY `IX_RefreshToken_FamiliaId` (`FamiliaId`),
  CONSTRAINT `FK_RefreshToken_Usuario_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `CodigoRecuperacionMfa` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UsuarioId` int NOT NULL,
  `CodigoHash` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `UsadoEn` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_CodigoRecuperacionMfa_UsuarioId` (`UsuarioId`),
  CONSTRAINT `FK_CodigoRecuperacionMfa_Usuario_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `DesafioMfa` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `UsuarioId` int NOT NULL,
  `TokenHash` char(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `ExpiraEn` datetime(6) NOT NULL,
  `ConsumidoEn` datetime(6) DEFAULT NULL,
  `Proposito` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `UX_DesafioMfa_TokenHash` (`TokenHash`),
  KEY `IX_DesafioMfa_UsuarioId` (`UsuarioId`),
  CONSTRAINT `FK_DesafioMfa_Usuario_UsuarioId` FOREIGN KEY (`UsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE IF NOT EXISTS `AuditoriaSeguridad` (
  `Id` bigint NOT NULL AUTO_INCREMENT,
  `FechaUtc` datetime(6) NOT NULL,
  `UsuarioId` int DEFAULT NULL,
  `Correo` varchar(100) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Evento` varchar(60) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `Resultado` varchar(20) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  `Ip` varchar(64) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `UserAgent` varchar(256) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  `Detalle` varchar(300) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AuditoriaSeguridad_FechaUtc` (`FechaUtc`),
  KEY `IX_AuditoriaSeguridad_UsuarioId` (`UsuarioId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
