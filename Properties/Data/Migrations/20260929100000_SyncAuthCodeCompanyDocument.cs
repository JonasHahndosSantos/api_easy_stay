using ApiEasyStay.Properties.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ApiEasyStay.Properties.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260929100000_SyncAuthCodeCompanyDocument")]
public partial class SyncAuthCodeCompanyDocument : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "documento_empresa_hash",
            table: "sync_auth_codes",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_sync_auth_codes_ident_doc_expira",
            table: "sync_auth_codes",
            columns: new[]
            {
                "identificador_hash",
                "documento_empresa_hash",
                "expira_em"
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_sync_auth_codes_ident_doc_expira",
            table: "sync_auth_codes");

        migrationBuilder.DropColumn(
            name: "documento_empresa_hash",
            table: "sync_auth_codes");
    }
}
