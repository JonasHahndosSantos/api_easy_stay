using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncAuthByCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "identificador_hash",
                table: "espacos_sincronizacao",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sync_access_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    espaco_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dispositivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nome_dispositivo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_access_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_access_tokens_espacos_sincronizacao_espaco_id",
                        column: x => x.espaco_id,
                        principalTable: "espacos_sincronizacao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_auth_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    identificador_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    codigo_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expira_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tentativas = table.Column<int>(type: "integer", nullable: false),
                    nome_dispositivo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_auth_codes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_espacos_sincronizacao_identificador_hash",
                table: "espacos_sincronizacao",
                column: "identificador_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_access_tokens_espaco_id_ativo",
                table: "sync_access_tokens",
                columns: new[] { "espaco_id", "ativo" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_access_tokens_token_hash",
                table: "sync_access_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_auth_codes_identificador_hash_expira_em",
                table: "sync_auth_codes",
                columns: new[] { "identificador_hash", "expira_em" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_access_tokens");

            migrationBuilder.DropTable(
                name: "sync_auth_codes");

            migrationBuilder.DropIndex(
                name: "ix_espacos_sincronizacao_identificador_hash",
                table: "espacos_sincronizacao");

            migrationBuilder.DropColumn(
                name: "identificador_hash",
                table: "espacos_sincronizacao");
        }
    }
}
