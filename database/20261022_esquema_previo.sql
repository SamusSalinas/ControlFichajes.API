-- Esquema estructural inmediatamente anterior a 20261001_turnos_jornadas.sql.
-- No es una instalación limpia ni contiene datos. Sirve para reproducir la
-- actualización. No incluye Turno, TurnoDia, EmpleadoTurno, excepciones,
-- reposiciones ni las columnas de jornada de Fichada.
-- La sesión ya debe tener seleccionada una base vacía.

SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS = 0;

CREATE TABLE `Empresa` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `NombreFantasia` varchar(100) NOT NULL,
  `RazonSocial` varchar(100) NOT NULL,
  `CUIT` varchar(20) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Empresa_CUIT` (`CUIT`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Sucursal` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Nombre` varchar(100) NOT NULL,
  `EmpresaId` int NOT NULL,
  `SerialLector` varchar(100) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Sucursal_Nombre_EmpresaId` (`Nombre`,`EmpresaId`),
  KEY `IX_Sucursal_EmpresaId` (`EmpresaId`),
  CONSTRAINT `FK_Sucursal_Empresa_EmpresaId` FOREIGN KEY (`EmpresaId`) REFERENCES `Empresa` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Departamento` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Nombre` varchar(100) NOT NULL,
  `SucursalId` int NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Departamento_Nombre_SucursalId` (`Nombre`,`SucursalId`),
  KEY `IX_Departamento_SucursalId` (`SucursalId`),
  CONSTRAINT `FK_Departamento_Sucursal_SucursalId` FOREIGN KEY (`SucursalId`) REFERENCES `Sucursal` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Empleado` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EmpresaId` int NOT NULL,
  `Legajo` varchar(20) DEFAULT NULL,
  `DNI` varchar(15) NOT NULL,
  `CUIL` varchar(15) NOT NULL,
  `Nombre` varchar(50) NOT NULL,
  `Apellido` varchar(50) NOT NULL,
  `DepartamentoId` int DEFAULT NULL,
  `Categoria` varchar(50) DEFAULT NULL,
  `SucursalId` int DEFAULT NULL,
  `Horario` varchar(50) DEFAULT NULL,
  `Activo` tinyint(1) NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Empleado_CUIL` (`CUIL`),
  UNIQUE KEY `IX_Empleado_DNI` (`DNI`),
  KEY `IX_Empleado_DepartamentoId` (`DepartamentoId`),
  KEY `IX_Empleado_EmpresaId` (`EmpresaId`),
  KEY `IX_Empleado_SucursalId` (`SucursalId`),
  CONSTRAINT `FK_Empleado_Departamento_DepartamentoId` FOREIGN KEY (`DepartamentoId`) REFERENCES `Departamento` (`Id`),
  CONSTRAINT `FK_Empleado_Empresa_EmpresaId` FOREIGN KEY (`EmpresaId`) REFERENCES `Empresa` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_Empleado_Sucursal_SucursalId` FOREIGN KEY (`SucursalId`) REFERENCES `Sucursal` (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Usuario` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EmpresaId` int NOT NULL,
  `NombreUsuario` varchar(50) NOT NULL,
  `Correo` varchar(100) NOT NULL,
  `PasswordHash` varchar(255) NOT NULL,
  `Rol` varchar(20) NOT NULL,
  `Activo` tinyint(1) NOT NULL,
  `RequiereCambioPassword` tinyint(1) NOT NULL,
  `IntentosFallidos` int NOT NULL,
  `BloqueadoHasta` datetime(6) DEFAULT NULL,
  `UltimoIntentoFallido` datetime(6) DEFAULT NULL,
  `PasswordTemporalVenceEn` datetime(6) DEFAULT NULL,
  `PasswordTemporalUsada` tinyint(1) NOT NULL,
  `TokenVersion` int NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `IX_Usuario_Correo` (`Correo`),
  KEY `IX_Usuario_EmpresaId` (`EmpresaId`),
  CONSTRAINT `FK_Usuario_Empresa_EmpresaId` FOREIGN KEY (`EmpresaId`) REFERENCES `Empresa` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Fichada` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EmpleadoId` int NOT NULL,
  `FechaHora` datetime(6) NOT NULL,
  `TipoRegistro` varchar(10) NOT NULL,
  `Metodo` varchar(20) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Fichada_EmpleadoId` (`EmpleadoId`),
  CONSTRAINT `FK_Fichada_Empleado_EmpleadoId` FOREIGN KEY (`EmpleadoId`) REFERENCES `Empleado` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `FichadaObservacion` (
  `FichadaId` int NOT NULL,
  `Motivo` varchar(40) NOT NULL,
  `Detalle` varchar(500) NOT NULL,
  `CreadoPorUsuarioId` int DEFAULT NULL,
  `CreadoPorNombre` varchar(50) NOT NULL,
  `CreadoEn` datetime(6) NOT NULL,
  `ModificadoPorUsuarioId` int DEFAULT NULL,
  `ModificadoPorNombre` varchar(50) DEFAULT NULL,
  `ModificadoEn` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`FichadaId`),
  KEY `IX_FichadaObservacion_CreadoPorUsuarioId` (`CreadoPorUsuarioId`),
  KEY `IX_FichadaObservacion_ModificadoPorUsuarioId` (`ModificadoPorUsuarioId`),
  CONSTRAINT `FK_FichadaObservacion_Fichada_FichadaId` FOREIGN KEY (`FichadaId`) REFERENCES `Fichada` (`Id`) ON DELETE CASCADE,
  CONSTRAINT `FK_FichadaObservacion_Usuario_CreadoPorUsuarioId` FOREIGN KEY (`CreadoPorUsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE SET NULL,
  CONSTRAINT `FK_FichadaObservacion_Usuario_ModificadoPorUsuarioId` FOREIGN KEY (`ModificadoPorUsuarioId`) REFERENCES `Usuario` (`Id`) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Huella` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `EmpleadoId` int NOT NULL,
  `IndiceDedo` int NOT NULL,
  `TemplateBiometrico` text NOT NULL,
  `FechaRegistro` datetime(6) NOT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_Huella_EmpleadoId` (`EmpleadoId`),
  CONSTRAINT `FK_Huella_Empleado_EmpleadoId` FOREIGN KEY (`EmpleadoId`) REFERENCES `Empleado` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `AgenteInstalaciones` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `ClientId` varchar(100) NOT NULL,
  `ClientSecretHash` varchar(255) NOT NULL,
  `Nombre` varchar(100) NOT NULL,
  `SucursalId` int NOT NULL,
  `Activo` tinyint(1) NOT NULL,
  `UltimoAcceso` datetime(6) DEFAULT NULL,
  PRIMARY KEY (`Id`),
  KEY `IX_AgenteInstalaciones_SucursalId` (`SucursalId`),
  CONSTRAINT `FK_AgenteInstalaciones_Sucursal_SucursalId` FOREIGN KEY (`SucursalId`) REFERENCES `Sucursal` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET FOREIGN_KEY_CHECKS = 1;
