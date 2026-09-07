using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class CriacaoInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    cpf = table.Column<string>(type: "text", nullable: true),
                    telefone = table.Column<string>(type: "text", nullable: true),
                    endereco = table.Column<string>(type: "text", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_clientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_perfis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    perfil_interno = table.Column<bool>(type: "boolean", nullable: false),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_perfis", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "config_permissoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_permissoes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "empresas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    documento = table.Column<string>(type: "text", nullable: true),
                    telefone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    endereco = table.Column<string>(type: "text", nullable: true),
                    logo_url = table.Column<string>(type: "text", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_empresas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quartos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    capacidade = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    preco_diaria = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    tipo_quarto_booking = table.Column<string>(type: "text", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quartos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sincronizacoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entidade = table.Column<string>(type: "text", nullable: true),
                    entidade_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operacao = table.Column<string>(type: "text", nullable: true),
                    dados = table.Column<string>(type: "text", nullable: true),
                    data_hora_sincronizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sincronizacoes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    senha = table.Column<string>(type: "text", nullable: true),
                    ativo = table.Column<bool>(type: "boolean", nullable: false),
                    perfil_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                    table.ForeignKey(
                        name: "fk_usuarios_config_perfil_perfil_id",
                        column: x => x.perfil_id,
                        principalTable: "config_perfis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "config_perfil_permissoes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    perfil_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permissao_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_perfil_permissoes", x => x.id);
                    table.ForeignKey(
                        name: "fk_config_perfil_permissoes_config_perfis_perfil_id",
                        column: x => x.perfil_id,
                        principalTable: "config_perfis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_config_perfil_permissoes_config_permissoes_permissao_id",
                        column: x => x.permissao_id,
                        principalTable: "config_permissoes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "configuracoes_sistema",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome_sistema = table.Column<string>(type: "text", nullable: false),
                    cor_primaria_modo_claro = table.Column<string>(type: "text", nullable: true),
                    cor_primaria_modo_escuro = table.Column<string>(type: "text", nullable: true),
                    modo_escuro_padrao = table.Column<bool>(type: "boolean", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracoes_sistema", x => x.id);
                    table.ForeignKey(
                        name: "fk_configuracoes_sistema_empresas_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reservas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    criado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_entrada = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_saida = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    quantidade_hospedes = table.Column<int>(type: "integer", nullable: false),
                    valor_total = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    observacoes = table.Column<string>(type: "text", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservas", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservas_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservas_quartos_quarto_id",
                        column: x => x.quarto_id,
                        principalTable: "quartos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservas_usuarios_criado_por_usuario_id",
                        column: x => x.criado_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lancamentos_financeiros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: true),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    valor = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    data_lancamento = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reserva_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data_hora_criado = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    data_hora_atualizado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    data_hora_deletado = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos_financeiros", x => x.id);
                    table.ForeignKey(
                        name: "fk_lancamentos_financeiros_reservas_reserva_id",
                        column: x => x.reserva_id,
                        principalTable: "reservas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_config_perfil_permissoes_perfil_id_permissao_id",
                table: "config_perfil_permissoes",
                columns: new[] { "perfil_id", "permissao_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_config_perfil_permissoes_permissao_id",
                table: "config_perfil_permissoes",
                column: "permissao_id");

            migrationBuilder.CreateIndex(
                name: "ix_config_permissoes_nome",
                table: "config_permissoes",
                column: "nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_configuracoes_sistema_empresa_id",
                table: "configuracoes_sistema",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_lancamentos_financeiros_reserva_id",
                table: "lancamentos_financeiros",
                column: "reserva_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_cliente_id",
                table: "reservas",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_criado_por_usuario_id",
                table: "reservas",
                column: "criado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservas_quarto_id",
                table: "reservas",
                column: "quarto_id");

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuarios_perfil_id",
                table: "usuarios",
                column: "perfil_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "config_perfil_permissoes");

            migrationBuilder.DropTable(
                name: "configuracoes_sistema");

            migrationBuilder.DropTable(
                name: "lancamentos_financeiros");

            migrationBuilder.DropTable(
                name: "sincronizacoes");

            migrationBuilder.DropTable(
                name: "config_permissoes");

            migrationBuilder.DropTable(
                name: "empresas");

            migrationBuilder.DropTable(
                name: "reservas");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "quartos");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropTable(
                name: "config_perfis");
        }
    }
}
