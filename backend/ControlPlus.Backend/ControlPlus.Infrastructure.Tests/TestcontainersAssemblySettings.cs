using Xunit;

// These integration tests provision isolated PostgreSQL Testcontainers. Serializing
// collections avoids concurrent database start-up contention in Docker/WSL.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
