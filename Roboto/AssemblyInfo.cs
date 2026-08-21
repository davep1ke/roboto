using System.Runtime.CompilerServices;

// Roboto.Tests needs access to a few internal members purely for test setup/faking - see
// TelegramAPI.SetClientForTesting, RobotoModuleTemplate.localData (reset between tests since this
// codebase is entirely static-state, not DI-per-instance) - not a general API surface widening.
[assembly: InternalsVisibleTo("Roboto.Tests")]
