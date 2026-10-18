using Xunit;

// Each integration test class owns an isolated local database. Keep collections
// serialized to avoid contention during migrations and database cleanup.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
