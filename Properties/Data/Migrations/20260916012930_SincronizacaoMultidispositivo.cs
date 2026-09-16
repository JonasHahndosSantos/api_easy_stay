using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class SincronizacaoMultidispositivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "dispositivo_id",
                table: "sincronizacoes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "espaco_id",
                table: "sincronizacoes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "evento_id",
                table: "sincronizacoes",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "forcado",
                table: "sincronizacoes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "nome_dispositivo",
                table: "sincronizacoes",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE sincronizacoes SET evento_id = id " +
                "WHERE evento_id = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name: "ix_sincronizacoes_espaco_id_data_hora_criado",
                table: "sincronizacoes",
                columns: new[] { "espaco_id", "data_hora_criado" });

            migrationBuilder.CreateIndex(
                name: "ix_sincronizacoes_espaco_id_entidade_entidade_id",
                table: "sincronizacoes",
                columns: new[] { "espaco_id", "entidade", "entidade_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sincronizacoes_espaco_id_evento_id",
                table: "sincronizacoes",
                columns: new[] { "espaco_id", "evento_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_sincronizacoes_espaco_id_data_hora_criado",
                table: "sincronizacoes");

            migrationBuilder.DropIndex(
                name: "ix_sincronizacoes_espaco_id_entidade_entidade_id",
                table: "sincronizacoes");

            migrationBuilder.DropIndex(
                name: "ix_sincronizacoes_espaco_id_evento_id",
                table: "sincronizacoes");

            migrationBuilder.DropColumn(
                name: "dispositivo_id",
                table: "sincronizacoes");

            migrationBuilder.DropColumn(
                name: "espaco_id",
                table: "sincronizacoes");

            migrationBuilder.DropColumn(
                name: "evento_id",
                table: "sincronizacoes");

            migrationBuilder.DropColumn(
                name: "forcado",
                table: "sincronizacoes");

            migrationBuilder.DropColumn(
                name: "nome_dispositivo",
                table: "sincronizacoes");
        }
    }
}
