using Xunit;

// The library has process-wide state (Log, ScopeContext, SelfLog); run test classes sequentially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
