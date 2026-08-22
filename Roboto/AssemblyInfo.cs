using System.Runtime.CompilerServices;

// Roboto.Tests needs access to a few internal members purely for test setup/faking - see
// TelegramAPI.SetClientForTesting, RobotoModuleTemplate.localData (reset between tests since this
// codebase is entirely static-state, not DI-per-instance) - not a general API surface widening.
[assembly: InternalsVisibleTo("Roboto.Tests")]

// Roboto.Migrator (phase 8) drives the same static bootstrap sequence Roboto.cs's own
// startBackground() does (Plugins.initPluginAssemblies(), Plugins.getPluginDataTypes()) from a
// separate entry point, rather than duplicating it - same rationale as Roboto.Tests above.
[assembly: InternalsVisibleTo("Roboto.Migrator")]
