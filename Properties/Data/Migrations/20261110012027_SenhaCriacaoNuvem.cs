using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations
{
    /// <inheritdoc />
    public partial class SenhaCriacaoNuvem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "senha_criacao_hash",
                table: "espacos_sincronizacao",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "senha_criacao_hash",
                table: "espacos_sincronizacao");
        }
    }
}
