using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ApiEasyStay.Properties.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261012220000_PreserveSyncPayloadFields")]
public sealed class PreserveSyncPayloadFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Rebuild partial states already stored by older API versions.
        migrationBuilder.Sql("""
            WITH latest_properties AS (
                SELECT DISTINCT ON (
                    event.espaco_id, event.entidade, event.entidade_id, lower(property.key))
                    event.espaco_id, event.entidade, event.entidade_id,
                    property.key, property.value
                FROM sincronizacoes AS event
                CROSS JOIN LATERAL jsonb_each(COALESCE(event.dados, '{}')::jsonb) AS property
                WHERE event.entidade_id IS NOT NULL
                ORDER BY event.espaco_id, event.entidade, event.entidade_id,
                    lower(property.key), event.data_hora_criado DESC, event.id DESC
            ), restored_payloads AS (
                SELECT espaco_id, entidade, entidade_id,
                    jsonb_object_agg(key, value) AS dados
                FROM latest_properties
                GROUP BY espaco_id, entidade, entidade_id
            )
            UPDATE sync_entidades AS state
            SET dados = restored.dados,
                excluido = state.excluido OR EXISTS (
                    SELECT 1 FROM jsonb_each(restored.dados) AS property
                    WHERE lower(property.key) = 'datahoradeletado'
                        AND property.value != 'null'::jsonb
                )
            FROM restored_payloads AS restored
            WHERE state.espaco_id = restored.espaco_id
                AND state.entidade = restored.entidade
                AND state.entidade_id = restored.entidade_id;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Restored fields are retained when downgrading.
    }
}
