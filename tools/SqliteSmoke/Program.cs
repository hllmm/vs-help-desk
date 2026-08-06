using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VSHelpDesk.Infrastructure.Persistence;

var dbPath = Environment.GetEnvironmentVariable("SQLITE_SMOKE_PATH")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")?.Replace("Data Source=", "", StringComparison.OrdinalIgnoreCase)
    ?? "/tmp/sqlite-smoke.db";

// Support both direct path and Data Source=... connection string
if (dbPath.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
{
    dbPath = dbPath.Substring("Data Source=".Length);
}

dbPath = dbPath.Trim();
if (string.IsNullOrWhiteSpace(dbPath))
{
    dbPath = "/tmp/sqlite-smoke.db";
}

// Ensure directory exists
var dir = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
{
    Directory.CreateDirectory(dir);
}

Console.WriteLine($"SQLite smoke: using database at {dbPath}");

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;

await using (var context = new ApplicationDbContext(options))
{
    Console.WriteLine("Ensuring deleted...");
    await context.Database.EnsureDeletedAsync();
    Console.WriteLine("Ensuring created...");
    await context.Database.EnsureCreatedAsync();
    Console.WriteLine("Database created via EnsureCreatedAsync.");
}

// Verify via raw sqlite connection
using var connection = new SqliteConnection($"Data Source={dbPath}");
await connection.OpenAsync();

var tablesCmd = connection.CreateCommand();
tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EFMigrationsHistory' ORDER BY name;";
var tables = new List<string>();
using (var reader = await tablesCmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        tables.Add(reader.GetString(0));
    }
}

Console.WriteLine("Found tables: " + string.Join(", ", tables));

string[] expectedTables = [
    "ApplicationParameters",
    "ParameterChangeLogs",
    "ProcessedEmailMessages",
    "SystemLogs",
    "TicketAttachments",
    "TicketMessages",
    "Tickets",
    "UserAuditEvents",
    "Users"
];

var missingTables = expectedTables.Where(t => !tables.Contains(t)).ToList();
if (missingTables.Count > 0)
{
    Console.Error.WriteLine($"Missing expected tables: {string.Join(", ", missingTables)}");
    Environment.Exit(1);
}

Console.WriteLine($"All {expectedTables.Length} expected tables present.");

var indexesCmd = connection.CreateCommand();
indexesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' ORDER BY name;";
var indexes = new List<string>();
using (var reader = await indexesCmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        indexes.Add(reader.GetString(0));
    }
}

Console.WriteLine("Found indexes: " + string.Join(", ", indexes));

string[] expectedIndexes = [
    "IX_Tickets_LastActivityAt_TicketNumber",
    "IX_Tickets_Status_LastActivityAt_TicketNumber",
    "IX_TicketMessages_TicketId_CreatedAt_Id"
];

var missingIndexes = expectedIndexes.Where(idx => !indexes.Contains(idx)).ToList();
if (missingIndexes.Count > 0)
{
    Console.Error.WriteLine($"Missing expected indexes: {string.Join(", ", missingIndexes)}");
    Environment.Exit(1);
}

Console.WriteLine($"All {expectedIndexes.Length} expected indexes present.");

// Verify that SQLite model does NOT contain xmin/phr_trgm artifacts (Postgres-only)
// Check that Tickets table does not have xmin column (should be integer Version instead)
var pragmaCmd = connection.CreateCommand();
pragmaCmd.CommandText = "PRAGMA table_info(Tickets);";
var columns = new List<string>();
using (var reader = await pragmaCmd.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        // column name is at index 1
        columns.Add(reader.GetString(1));
    }
}

Console.WriteLine("Tickets columns: " + string.Join(", ", columns));

if (columns.Contains("xmin"))
{
    Console.Error.WriteLine("Unexpected xmin column found in SQLite Tickets table — provider-specific model should not contain xmin for SQLite.");
    Environment.Exit(1);
}

if (!columns.Contains("Version"))
{
    Console.Error.WriteLine("Missing Version column in SQLite Tickets table.");
    Environment.Exit(1);
}

Console.WriteLine("SQLite smoke verification passed.");
