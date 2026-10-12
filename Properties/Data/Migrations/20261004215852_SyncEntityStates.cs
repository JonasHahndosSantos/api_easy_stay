using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncEntityStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_entidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    espaco_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidade = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entidade_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dados = table.Column<string>(type: "jsonb", nullable: false),
                    excluido = table.Column<bool>(type: "boolean", nullable: false),
                    data_hora_origem = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_entidades", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_entidades_espacos_sincronizacao_espaco_id",
                        column: x => x.espaco_id,
                        principalTable: "espacos_sincronizacao",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_entidades_espaco_id_entidade_entidade_id",
                table: "sync_entidades",
                columns: new[] { "espaco_id", "entidade", "entidade_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_entidades_espaco_id_excluido",
                table: "sync_entidades",
                columns: new[] { "espaco_id", "excluido" });

            migrationBuilder.Sql("""
                INSERT INTO sync_entidades (
                    id,
                    espaco_id,
                    entidade,
                    entidade_id,
                    dados,
                    excluido,
                    data_hora_origem,
                    data_hora_criado,
                    data_hora_atualizado,
                    data_hora_deletado)
                SELECT
                    estado_atual.id,
                    estado_atual.espaco_id,
                    estado_atual.entidade,
                    estado_atual.entidade_id,
                    COALESCE(estado_atual.dados, '{}')::jsonb,
                    estado_atual.operacao = 'excluir',
                    estado_atual.data_hora_criado,
                    estado_atual.data_hora_criado,
                    estado_atual.data_hora_atualizado,
                    NULL
                FROM (
                    SELECT DISTINCT ON (espaco_id, entidade, entidade_id)
                        id,
                        espaco_id,
                        entidade,
                        entidade_id,
                        dados,
                        operacao,
                        data_hora_criado,
                        data_hora_atualizado
                    FROM sincronizacoes
                    WHERE entidade_id IS NOT NULL
                    ORDER BY
                        espaco_id,
                        entidade,
                        entidade_id,
                        data_hora_criado DESC,
                        id DESC
                ) AS estado_atual
                INNER JOIN espacos_sincronizacao AS espaco
                    ON espaco.id = estado_atual.espaco_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_entidades");
        }
    }
}
