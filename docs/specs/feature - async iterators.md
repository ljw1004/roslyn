# C# feature proposal: async iterators

(There's already a discussion thread for this: https://github.com/dotnet/roslyn/issues/261)

The way I've written this proposal is with a tiny language change which leaves almost everything up to the libraries. Even an `IAsyncEnumerable`-returning async iterator method is generated chiefly by the libraries. I'll start by writing the end-to-end experience of how users will experience, and then I'll get to the actual tiny language proposal.

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

**Example4:** One of the BCL `IAsyncEnumerable` features is its `GetEnumerator()` method takes an optional cancellation token. Again, this is part of the BCL and not a language feature.
```csharp
// EXAMPLE 4: cancellation when consuming IAsyncEnumerable
var cts = new CancellationTokenSource();
foreach (async var x in GetStreamFactory().GetEnumerator(cts.Token)) { ... }
```

**Example5:** There's a new contextual keyword `async` inside async iterator and async tasklike methods, similar to `this` and `base` except it refers to the current async *method instance*. We'll see later that this is a general-purpose mechanism needed for a variety of async methods, but for now the library just uses the mechanism to let the async iterator get hold of the cancellation token that was passed to `GetEnumerator`.
```csharp
// EXAMPLE 5:
async IAsyncEnumerable<int> GetStreamAsync()
{
    await Task.Delay(100, async.CancellationToken);
    yield 1;
    await Task.Delay(200, async.CancellationToken);
    yield 2;
}
```

**Example6:** On the consumption side, everything is pattern-based. (Rationale: to support `ConfigureAwait` we need the option of a `MoveNextAsync` that returns an awaitable other than `Task`; and folks might prefer to avoid boxing for performance; and the current spec rules about `foreach` are a confusing mix of pattern and type).
```csharp
// EXAMPLE 6:
// foreach (T x in e) embedded_statement;

// if e.GetEnumerator() binds, then it expands to
foreach (T x in e.GetEnumerator()) embedded_statement;

// otherwise it expands to
{
    try {
        while (await e.MoveNextAsync()) {
            T x = e.Current;
            ...
        }
    }
    finally {
        (e as IDisposable).Dispose();
    }
}
```

**Example7:** On the production side, again everything is pattern-based. Building upon the [ValueTask proposal](feature%20-%20arbitrary%20async%20returns.md), for an async method with return type `C`, it looks up a builder `CB` for that type, and the builder's responsible for handling the `await` operator, the `yield` statement, and the new `async` contextual keyword. (Rationale: we've already established that awaits are better handled by a builder rather than solely by the language; also, this will allow for `IObservable` and `IAsyncActionWithProgress`).
```csharp
// EXAMPLE 7:
// async C f()
// {
//   var ct = async.CancellationToken;
//   await Task.Delay(1, ct);
//   yield 1;
// }
// expands to this:

C f()
{
   var sm = new fStateMachine();
   sm.builder = CB.Create();
   sm.builder.Start(ref sm);
   return sm.Task;
}

struct fStateMachine()
{
  int state;
  CB builder;
  void MoveNext()
  {
    // "var ct = async.CancellationToken" expands to this:
    var ct = builder.Async.CancellationToken;
    
    // "var x = await t" expands to this:
    var awaiter = t.GetAwaiter();
    if (!awaiter.IsCompleted)
    {
      builder.AwaitOnCompleted(ref awaiter, ref this);
      return;
    }
    label:
    var x = awaiter.GetResult();
    awaiter = default(...);
    
    // "yield e" expands to this:
    sm.builder.YieldResult(e);
    return;
    label:
    
    // when the method exits:
    sm.builder.SetResult();
  }
}
```


