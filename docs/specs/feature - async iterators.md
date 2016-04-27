# C# feature proposal: async iterators

(There's already a discussion thread for this: https://github.com/dotnet/roslyn/issues/261)

## Explaining examples

```csharp
// EXAMPLE 1: this is the simple case of an IAsyncEnumerable
async IAsyncEnumerable<int> GetStreamFactory()
{
   await Task.Delay(100);
   yield 1;
   await Task.Delay(200);
   yield 2;
}

foreach (async var x in GetStreamFactory()) { ... }
```

```csharp
// EXAMPLE 2: you can also consume IAsyncEnumerators, and you can produce them
// This is a good opportunity to provide a cancellation token.
async IAsyncEnumerator<int> GetStream(CancellationToken cancel) { ... }

var cts = new CancellationTokenSource();
foreach (async var x in GetStream(cts.Token)) { ... }
```

```csharp
// EXAMPLE 3: we expect the BCL types IAsyncEnumerator+able to implement ConfigureAwait
foreach (async var x in GetStreamFactory().ConfigureAwait(false)) { ... }
```

```csharp
// EXAMPLE 4: we expect the BCL type IAsyncEnumerable to offer a GetAsyncEnumerator method
// that takes a cancellation token. An async iterator method can access this token
// via a new contextual keyword `async` that refers to the current async method in-flight:


// EXPANDS TO THIS:
{
    var enumerator = GetStreamFactory().GetAsyncEnumerator();
    try {
        while (await enumerator.MoveNextAsync()) {
            var x = enumerator.Current;
            ...
        }
    }
    finally {
        enumeratr.Dispose();
    }
}

// EXPANDS TO THIS:
{
    var enumerator = GetStream(cts.Token)();
    try {
        while (await enumerator.MoveNextAsync()) {
            var x = enumerator.Current;
            ...
        }
    }
    finally {
        ... // Dispose enumerator
    }
}
```

```csharp
// EXAMPLE 3: the standard IAsyncEnumerable and IAsyncEnumerator both offer ConfigureAwait methods
// 
