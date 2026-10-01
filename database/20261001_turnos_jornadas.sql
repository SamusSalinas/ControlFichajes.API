-- ControlFichajes.API - turnos, jornadas y marca de fichada manual
-- MySQL 8.0+
-- Repetible: crea los objetos si faltan y conserva columnas heredadas.
-- Ejecutar con respaldo previo. Este archivo no fue aplicado al servidor durante
-- esta revisión; seleccionar tesis_db explícitamente antes de ejecutarlo.

USE `tesis_db`;

CREATE TABLE IF NOT EXISTS `Turno` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Nombre` varchar(100) NOT NULL,
    `ToleranciaMinutos` int NOT NULL DEFAULT 0,
    `EmpresaId` int NOT NULL,
    `Activo` tinyint(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (`Id`),
    KEY `IX_Turno_EmpresaId` (`EmpresaId`),
    CONSTRAINT `FK_Turno_Empresa_EmpresaId`
        FOREIGN KEY (`EmpresaId`) REFERENCES `Empresa` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `TurnoDia` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `TurnoId` int NOT NULL,
    `DiaSemana` int NOT NULL,
    `HoraEntrada` time NOT NULL,
    `HoraSalida` time NOT NULL,
    `MinutosAlmuerzo` int NOT NULL DEFAULT 60,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_TurnoDia_TurnoId_DiaSemana` (`TurnoId`, `DiaSemana`),
    CONSTRAINT `FK_TurnoDia_Turno_TurnoId`
        FOREIGN KEY (`TurnoId`) REFERENCES `Turno` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `EmpleadoTurno` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmpleadoId` int NOT NULL,
    `TurnoId` int NOT NULL,
    `FechaInicio` date NOT NULL,
    `FechaFin` date NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_EmpleadoTurno_EmpleadoId` (`EmpleadoId`),
    KEY `IX_EmpleadoTurno_TurnoId` (`TurnoId`),
    CONSTRAINT `FK_EmpleadoTurno_Empleado_EmpleadoId`
        FOREIGN KEY (`EmpleadoId`) REFERENCES `Empleado` (`Id`),
    CONSTRAINT `FK_EmpleadoTurno_Turno_TurnoId`
        FOREIGN KEY (`TurnoId`) REFERENCES `Turno` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'TurnoDia'
          AND COLUMN_NAME = 'MinutosAlmuerzo'
    ),
    'SELECT 1',
    'ALTER TABLE `TurnoDia` ADD COLUMN `MinutosAlmuerzo` int NOT NULL DEFAULT 60'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE `TurnoDia`
SET `MinutosAlmuerzo` = 60
WHERE `MinutosAlmuerzo` <> 60;

-- Los datos previos guardaban solo Empleado.TurnoId, sin fecha histórica.
-- Migrar únicamente las asignaciones actuales que aún no tengan una fila
-- vigente, usando la fecha de corte de esta migración y sin inventar pasado.
-- No se generan filas TurnoDia automáticamente: la tabla está vacía en la
-- instalación revisada y no hay información que permita inferir días laborables.
INSERT INTO `EmpleadoTurno` (`EmpleadoId`, `TurnoId`, `FechaInicio`, `FechaFin`)
SELECT e.`Id`, e.`TurnoId`, CURRENT_DATE(), NULL
FROM `Empleado` e
WHERE e.`TurnoId` IS NOT NULL
    AND NOT EXISTS (
            SELECT 1
            FROM `EmpleadoTurno` et
            WHERE et.`EmpleadoId` = e.`Id`
                AND et.`FechaFin` IS NULL
    );

CREATE TABLE IF NOT EXISTS `EmpleadoJornadaExcepcion` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmpleadoId` int NOT NULL,
    `Fecha` date NOT NULL,
    `EsDiaLibre` tinyint(1) NOT NULL DEFAULT 0,
    `ToleranciaMinutos` int NULL,
    `HoraEntrada` time NULL,
    `HoraSalida` time NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `UX_EmpleadoJornadaExcepcion_EmpleadoId_Fecha` (`EmpleadoId`, `Fecha`),
    CONSTRAINT `FK_EmpleadoJornadaExcepcion_Empleado_EmpleadoId`
        FOREIGN KEY (`EmpleadoId`) REFERENCES `Empleado` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS `ReposicionHoras` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `EmpleadoId` int NOT NULL,
    `Fecha` date NOT NULL,
    `Minutos` int NOT NULL,
    `Detalle` varchar(500) NOT NULL,
    `CreadoPorUsuarioId` int NULL,
    `CreadoPorNombre` varchar(50) NOT NULL,
    `CreadoEn` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    KEY `IX_ReposicionHoras_EmpleadoId_Fecha` (`EmpleadoId`, `Fecha`),
    KEY `IX_ReposicionHoras_CreadoPorUsuarioId` (`CreadoPorUsuarioId`),
    CONSTRAINT `FK_ReposicionHoras_Empleado_EmpleadoId`
        FOREIGN KEY (`EmpleadoId`) REFERENCES `Empleado` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_ReposicionHoras_Usuario_CreadoPorUsuarioId`
        FOREIGN KEY (`CreadoPorUsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE SET NULL,
    CONSTRAINT `CK_ReposicionHoras_Minutos` CHECK (`Minutos` BETWEEN 1 AND 1440)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Garantiza como máximo una jornada por día de semana y turno, también si la
-- tabla ya existía antes de esta migración. No modifica filas existentes.
SET @turnodia_unique_exists = (
    SELECT COUNT(*)
    FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'TurnoDia'
      AND INDEX_NAME = 'UX_TurnoDia_TurnoId_DiaSemana'
      AND NON_UNIQUE = 0
);
SET @ddl = IF(
    @turnodia_unique_exists > 0,
    'SELECT 1',
    'ALTER TABLE `TurnoDia` ADD UNIQUE KEY `UX_TurnoDia_TurnoId_DiaSemana` (`TurnoId`, `DiaSemana`)'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Fichada ya puede contener estas columnas en instalaciones existentes.
SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'Fichada'
          AND COLUMN_NAME = 'EsManual'
    ),
    'SELECT 1',
    'ALTER TABLE `Fichada` ADD COLUMN `EsManual` tinyint(1) NOT NULL DEFAULT 0'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'Fichada'
          AND COLUMN_NAME = 'Estado'
    ),
    'SELECT 1',
    'ALTER TABLE `Fichada` ADD COLUMN `Estado` varchar(20) NOT NULL DEFAULT ''Completada'''
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

SET @ddl = IF(
    EXISTS (
        SELECT 1 FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'Fichada'
          AND COLUMN_NAME = 'MinutosHastaCorte'
    ),
    'SELECT 1',
    'ALTER TABLE `Fichada` ADD COLUMN `MinutosHastaCorte` int NULL'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- CREATE TABLE IF NOT EXISTS no repara tablas preexistentes. Agregar la FK
-- empresarial solo si falta; si hay filas huérfanas, ALTER TABLE falla sin
-- borrar ni modificar esos datos y se debe resolver primero.
SET @turno_empresa_fk_exists = (
    SELECT COUNT(*)
    FROM information_schema.KEY_COLUMN_USAGE
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'Turno'
      AND COLUMN_NAME = 'EmpresaId'
      AND REFERENCED_TABLE_NAME = 'Empresa'
      AND REFERENCED_COLUMN_NAME = 'Id'
);
SET @ddl = IF(
    @turno_empresa_fk_exists > 0,
    'SELECT 1',
    'ALTER TABLE `Turno` ADD CONSTRAINT `FK_Turno_Empresa_EmpresaId` FOREIGN KEY (`EmpresaId`) REFERENCES `Empresa` (`Id`)'
);
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Verificación de objetos y relaciones relevantes.
SELECT TABLE_NAME, COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME IN ('Turno', 'TurnoDia', 'EmpleadoTurno', 'EmpleadoJornadaExcepcion', 'ReposicionHoras', 'Fichada')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT TABLE_NAME, COLUMN_NAME, REFERENCED_TABLE_NAME, REFERENCED_COLUMN_NAME
FROM information_schema.KEY_COLUMN_USAGE
WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME IN ('Turno', 'TurnoDia', 'EmpleadoTurno', 'EmpleadoJornadaExcepcion', 'ReposicionHoras')
  AND REFERENCED_TABLE_NAME IS NOT NULL
ORDER BY TABLE_NAME, COLUMN_NAME;

-- Validación de duplicados antes de confiar en la restricción de jornada:
SELECT TurnoId, DiaSemana, COUNT(*) AS Cantidad
FROM TurnoDia
GROUP BY TurnoId, DiaSemana
HAVING COUNT(*) > 1;
