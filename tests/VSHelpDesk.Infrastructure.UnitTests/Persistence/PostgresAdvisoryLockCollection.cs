namespace VSHelpDesk.Infrastructure.UnitTests.Persistence;

/// <summary>Shared collection so PostgreSQL advisory-lock facts never run in parallel.</summary>
[CollectionDefinition("PostgresAdvisoryLocks", DisableParallelization = true)]
public sealed class PostgresAdvisoryLockCollection;
