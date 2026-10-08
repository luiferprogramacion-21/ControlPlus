using Xunit;

// Each integration test class owns an isolated PostgreSQL Testcontainer. Starting
// several PostgreSQL instances at once is unnecessarily sensitive to Docker/WSL
// resource availability, so keep test collections serialized within this assembly.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
