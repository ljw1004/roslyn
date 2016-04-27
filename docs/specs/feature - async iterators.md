# C# feature proposal: async iterators

(There's already a discussion thread for this: https://github.com/dotnet/roslyn/issues/261)

## Explaining examples

**Example1:** You can use `IAsyncEnumerable<T>` in much the same way as you use `IEnumerable<T>` today: it can be produced by async iterator methods which can use `await` as well as `yield`, and you consume it with `foreach (async ...)`. (Rationale: it would be too jarring not to have this similarity with `IEnumerable`.)
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

**Example2:** You can also use `IAsyncEnumerator<T>` similar to `IEnumerator<T>`: it can be produced with async iterator methods. What's new is that you can consume it with `foreach (async ...)` as well, while regular `IEnumerator`s can't be consumed in this way. (Rationale: streams, rather than stream factories, are much more common in the async world).
```csharp
// EXAMPLE 2: you can also consume IAsyncEnumerators, and you can produce them
// This is a good opportunity to provide a cancellation token!
async IAsyncEnumerator<int> GetStream(CancellationToken cancel) { ... }

var cts = new CancellationTokenSource();
foreach (async var x in GetStream(cts.Token)) { ... }
```

**Example3:** You can use `.ConfigureAwait(false)` on the enumerable/enumerator itself. That's not a language feature: it's just part of the BCL definition of `IAsyncEnumerable` and `IAsyncEnumerator`.
```csharp
// EXAMPLE 3: ConfigureAwait support
foreach (async var x in GetStreamFactory().ConfigureAwait(false)) { ... }
foreach (async var x in GetStream().ConfigureAwait(false)) { ... }
```

**Example4:** One of the BCL `IAsyncEnumerable` features is its `GetEnumerator()` method takes an optional cancellation token.
```csharp
// EXAMPLE 4: cancellation when consuming IAsyncEnumerable
var cts = new CancellationTokenSource();
foreach (async var x in GetStreamFactory().GetEnumerator(cts.Token)) { ... }
```

**Example5:** 
```csharp
// EXAMPLE 5:
async IAsyncEnumerable<int> GetStreamAsync()
{
    var cancel = async.Cancellatio
}
```

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
