using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class SyncCompanyDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "documento_empresa_hash",
                table: "espacos_sincronizacao",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_espacos_sincronizacao_documento_empresa_hash",
                table: "espacos_sincronizacao",
                column: "documento_empresa_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_espacos_sincronizacao_documento_empresa_hash",
                table: "espacos_sincronizacao");

            migrationBuilder.DropColumn(
                name: "documento_empresa_hash",
                table: "espacos_sincronizacao");
        }
    }
}
