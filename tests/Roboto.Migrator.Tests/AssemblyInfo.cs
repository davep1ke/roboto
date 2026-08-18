using Xunit;

// Same reasoning as Roboto.Bot.Tests' own AssemblyInfo.cs - several tests here import a synthetic
// catalog into CardCatalog's shared static and check the result; sequential execution removes any
// chance of that racing against another test in this same process.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
