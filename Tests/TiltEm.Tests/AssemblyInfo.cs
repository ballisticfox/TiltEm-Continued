using Xunit;

//The check groups build shared state as they run, and each is memoized on first use, so
//they must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
