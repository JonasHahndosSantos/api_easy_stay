using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncBootstrapOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "carga_inicial_concluida",
                table: "espacos_sincronizacao",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "dispositivo_carga_inicial_id",
                table: "espacos_sincronizacao",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carga_inicial_concluida",
                table: "espacos_sincronizacao");

            migrationBuilder.DropColumn(
                name: "dispositivo_carga_inicial_id",
                table: "espacos_sincronizacao");
        }
    }
}
