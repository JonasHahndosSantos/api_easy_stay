using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class EspacosSincronizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "espacos_sincronizacao",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    nome = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_espacos_sincronizacao", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_espacos_sincronizacao_ativo",
                table: "espacos_sincronizacao",
                column: "ativo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "espacos_sincronizacao");
        }
    }
}
