using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MiniCambio.Payments.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResultadoPublicadoNoKafka : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "result_published_at",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            // Pagamentos finalizados antes do Kafka já foram lidos pelo Orders por polling: não há o que publicar.
            migrationBuilder.Sql(
                "UPDATE payments SET result_published_at = updated_at WHERE status IN ('Completed', 'Failed');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "result_published_at",
                table: "payments");
        }
    }
}
