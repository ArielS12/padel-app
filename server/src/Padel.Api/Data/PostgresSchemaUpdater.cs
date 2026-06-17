using Microsoft.EntityFrameworkCore;
using Padel.Api.Data;

namespace Padel.Api.Data;

public static class PostgresSchemaUpdater
{
    public static async Task ApplyAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (!db.Database.IsNpgsql())
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            ALTER TABLE "PlayerPaymentMethods"
            ADD COLUMN IF NOT EXISTS "MercadoPagoAccountEmail" text NULL;
            """,
            cancellationToken);
    }
}
