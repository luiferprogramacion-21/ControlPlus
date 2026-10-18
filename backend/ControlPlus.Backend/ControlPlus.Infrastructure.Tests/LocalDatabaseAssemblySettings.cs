using Xunit;

// These integration tests provision isolated local databases. Serializing
// collections avoids contention during migrations and database cleanup.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
