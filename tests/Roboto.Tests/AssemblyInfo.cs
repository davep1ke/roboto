using Xunit;

// This whole codebase is static-global state (Roboto.Settings/Roboto.Store/Plugins.plugins,
// TelegramAPI's cached client, ...), not per-instance/DI - there's no way to give each test its own
// isolated instance the way the abandoned rewrite branch's DI-per-test-class TestBot could.
// TestHarness.Reset() resets the shared state between tests instead, which only works if tests never
// run concurrently with each other - xUnit parallelizes different test classes by default, so that's
// turned off assembly-wide here.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
