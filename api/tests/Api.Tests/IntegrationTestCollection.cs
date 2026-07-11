namespace Api.Tests;

/// <summary>
/// Every test class that boots a WebApplicationFactory[Program] must opt into
/// this collection. xUnit parallelizes across different collections by
/// default, and each factory instance re-runs DbInitializer.SeedAsync
/// (migrations + reference-data seed) against the same shared local MySQL on
/// startup — two of those racing concurrently causes real MySQL deadlocks on
/// the seed inserts. Sharing one collection makes xUnit run them serially.
/// </summary>
[CollectionDefinition("Integration")]
public class IntegrationTestCollection;
