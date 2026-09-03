using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ControlFichajes.API.Migrations
{
    /// <inheritdoc />
    public partial class P1HeartbeatYCatalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Empleado_CUIL",
                table: "Empleado");

            migrationBuilder.DropIndex(
                name: "IX_Empleado_DNI",
                table: "Empleado");

            migrationBuilder.DropIndex(
                name: "IX_Empleado_EmpresaId",
                table: "Empleado");

            migrationBuilder.AddColumn<int>(
                name: "DepartamentoId",
                table: "Empleado",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SucursalId",
                table: "Empleado",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EstadoLector",
                table: "AgenteInstalacion",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SerialLector",
                table: "AgenteInstalacion",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "UltimaSincronizacion",
                table: "AgenteInstalacion",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VersionApp",
                table: "AgenteInstalacion",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_DepartamentoId",
                table: "Empleado",
                column: "DepartamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_EmpresaId_CUIL",
                table: "Empleado",
                columns: new[] { "EmpresaId", "CUIL" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_EmpresaId_DNI",
                table: "Empleado",
                columns: new[] { "EmpresaId", "DNI" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_SucursalId",
                table: "Empleado",
                column: "SucursalId");

            migrationBuilder.AddForeignKey(
                name: "FK_Empleado_Departamento_DepartamentoId",
                table: "Empleado",
                column: "DepartamentoId",
                principalTable: "Departamento",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Empleado_Sucursal_SucursalId",
                table: "Empleado",
                column: "SucursalId",
                principalTable: "Sucursal",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Empleado_Departamento_DepartamentoId",
                table: "Empleado");

            migrationBuilder.DropForeignKey(
                name: "FK_Empleado_Sucursal_SucursalId",
                table: "Empleado");

            migrationBuilder.DropIndex(
                name: "IX_Empleado_DepartamentoId",
                table: "Empleado");

            migrationBuilder.DropIndex(
                name: "IX_Empleado_EmpresaId_CUIL",
                table: "Empleado");

            migrationBuilder.DropIndex(
                name: "IX_Empleado_EmpresaId_DNI",
                table: "Empleado");

            migrationBuilder.DropIndex(
                name: "IX_Empleado_SucursalId",
                table: "Empleado");

            migrationBuilder.DropColumn(
                name: "DepartamentoId",
                table: "Empleado");

            migrationBuilder.DropColumn(
                name: "SucursalId",
                table: "Empleado");

            migrationBuilder.DropColumn(
                name: "EstadoLector",
                table: "AgenteInstalacion");

            migrationBuilder.DropColumn(
                name: "SerialLector",
                table: "AgenteInstalacion");

            migrationBuilder.DropColumn(
                name: "UltimaSincronizacion",
                table: "AgenteInstalacion");

            migrationBuilder.DropColumn(
                name: "VersionApp",
                table: "AgenteInstalacion");

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_CUIL",
                table: "Empleado",
                column: "CUIL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_DNI",
                table: "Empleado",
                column: "DNI",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empleado_EmpresaId",
                table: "Empleado",
                column: "EmpresaId");
        }
    }
}
