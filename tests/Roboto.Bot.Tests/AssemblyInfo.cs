using Xunit;

// CardCatalogOverrideTests mutates CardCatalog's shared static Questions/Answers (restoring them
// afterward) - xUnit's default per-collection parallelization would let that race against any other
// test reading CardCatalog concurrently (nearly everything in Xyzzy/). Sequential execution costs
// nothing meaningful at this suite's size (a few seconds either way) and removes the hazard
// entirely rather than trying to scope it away with collection attributes.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
