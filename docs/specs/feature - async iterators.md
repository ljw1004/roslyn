# C# feature proposal: async iterators

> * The current prototype supports *consumption* of async enumerators and async enumerables, `foreach (await var x in e)`, but not *production*
> * **Download:** [ArbitraryAsyncReturns.zip](https://github.com/ljw1004/roslyn/raw/features/async-return/ArbitraryAsyncReturns.zip) [22mb]
> * **Install:** Unzip the file. Quit VS. Double-click to install in this order: (1) Roslyn.VisualStudio.Setup.vsix, (2) Roslyn.Compilers.Extension.vsix, (3) ExpressionEvaluatorPackage.vsix. I don't think the others are needed.
> * **Test:** the zip file contains a sample project
> * **Uninstall:** I've usually been able to go to within Visual Studio to Tools>Extensions, search for Roslyn, and uninstall in the order (1) Expression Evaluators, (2) Compilers, (3) Language Service. Once that resulted in a broken VS that was unable to load C# projects, and I unbroke it by deleting the entire folder `%userprofile%\AppData\Roaming\Microsoft\VisualStudio` and `%userprofile%\AppData\Local\Microsoft\VisualStudio`. Doing so reset all my VS settings to default.
> * **Discuss:** in the [discussion thread](https://github.com/dotnet/roslyn/issues/261)

The way I've written this proposal is with a tiny language change which leaves almost everything up to the libraries. The language doesn't hard-code anything specifically about `IAsyncEnumerable`. I'll start by writing the end-to-end of what coders will experience, then I'll get to the actual tiny language proposal.

But first, I want to address head-on the key design issues

## Key design issues

```csharp
async IAsyncEnumerator<int> f() { yield 1; await t; }  // [Q1] can you produce an enumerator?
async IAsyncEnumerable<int> g() { yield 1; await t; }  // [Q2] can you produce an enumerable?

foreach (await var x in f()) { ... }  // [Q3] can you consume an enumerator?
foreach (await var x in g()) { ... }  // [Q4] can you consume an enumerable?

async IAsyncEnumerable<int> g() { yield 1; async.CancellationToken.ThrowIfCancellationRequested(); }
// [Q5] do we need a novel language feature for the body of the async iterator to get hold of context?
```

**Key issue: `IDisposable`.** When you write an async iterator method, you *certainly* expect its `finally` blocks to be executed. Therefore the associated enumerator must implement `IDisposable`. Consuming enumerables [Q4] deals with this just fine (by getting the enumerator then disposing it). But what about consuming enumerators [Q3]? We don't want folks to fall into a pit of failure where they have to write boilerplate disposal code after every async-foreach that they write.

> ***[Q3] if foreach can consume an enumerator, it must dispose of it as well.***

This looks and feels weird but has one saving grace: the meaning of an enumerator (stream) is that it can be consumed exactly once, so if you async-foreach over it once, then no one can ever foreach over it again, so it doesn't matter that async-foreach disposes of it.


**Key issue: CancellationToken.** There is debate over the granularity of cancellation: (1) at the level of each *enumerable*, or (2) at the level of each *enumerator*, or (3) at the level of each *MoveNextAsync*.

The level of *enumerable* could be done by passing it to the original iterator method `g(cts.Token)` which works okay, but means that one cancellation will affect all subsequent streams. Or it could be done similar to LINQ `TakeWhile`, e.g. `foreach (await var x in g().WithCancellation(cts.Token))`, but it needs [Q5] a way for the body of the async iterator method to get hold of that token.

The level of *enumerator* feels just as clean as the `TakeWhile`. If only async enumerators can be produced, then enumerator-level cancellation is a fine and complete story. But if async enumerables can be produced, `foreach (await var x in g().GetEnumerator(cts.Token))`, then again it needs [Q5] a way to get hold of that token.

The level of *MoveNextAsync* doesn't work because it's too hard to write an async iterator method which has to deal with a potentially different token after every `yield`, and in any case there's no way in the async-foreach construct to provide a different token after each one.

> ***[Q2 => Q5] if you can produce an enumerable, then the async iterator body needs a way to get hold of context.***


**Key issue: IAsyncDisposable.** If your async iterator method has await in its finally block, and you do `IDisposable.Dispose` on the iterator, then when do those awaits gets executed? Answer: we must either disallow awaits in finally blocks in async iterators, or use `IAsyncDisposable`.


**Key design options.** In the light of the two key issues above, let's talk through the design options...

* **Option1:** Streams (async enumerators) are foremost, and cancellation is at the level of enumerators. [Q1] you can only produce enumerators. [Q3] you can only consume enumerators, and consuming them disposes of them.

* **Option2:** Async enumerables are foremost, and cancellation is at the level of enumerables. [Q1+Q2] you can produce enumerators and enumerables. [Q4] you can only consume enumerables. This option could be done either with or without [Q5] a way to get hold of the cancellation token from inside the body of the async enumerable iterator.

* **Option3:** Enumerators and enumerables are both equally foremost, and cancellation is at the level of enumerators. [Q1+Q2] you can produce enumerators and enumerables. [Q3+Q4] you can consume enumerators and enumerables. [Q5] you need a way to get hold of the cancellation token from inside the body of an async enumerable iterator. (Key to this option is the *symmetry* of async-foreach: that's what allows you to either consume `foreach (await var x in GetStreamFactory())` or `foreach (await var x in GetStreamFactory().GetEnumerator(token))`, and not have to think about it.)

In the rest of this proposal I'm going to write out a spec out all of [Q1+Q2+Q3+Q4+Q5]. It's obviously possible to strike out any part, depending on which design option is taken.



## Explaining examples

**Example1:** You can use `IAsyncEnumerable<T>` in much the same way as you use `IEnumerable<T>` today: it can be produced by async iterator methods which can use `await` as well as `yield`, and you consume it with `foreach (await ...)`. (Rationale: it would be too jarring not to have this similarity with `IEnumerable`.)
```csharp
// EXAMPLE 1: this is the simple case of an IAsyncEnumerable
async IAsyncEnumerable<int> GetStreamFactory()
{
   await Task.Delay(100);
   yield 1;
   await Task.Delay(200);
   yield 2;
}

foreach (await var x in GetStreamFactory()) { ... }
```

**Example2:** You can also use `IAsyncEnumerator<T>` similar to `IEnumerator<T>`: it can be produced with async iterator methods. What's new is that you can consume it with `foreach (await ...)` as well, while regular `IEnumerator`s can't be consumed in this way. (Rationale: streams, rather than stream factories, are much more common in the async world).
```csharp
// EXAMPLE 2: you can also consume IAsyncEnumerators, and you can produce them
// This is a good opportunity to provide a cancellation token!
async IAsyncEnumerator<int> GetStream(CancellationToken cancel) { ... }

var cts = new CancellationTokenSource();
foreach (await var x in GetStream(cts.Token)) { ... }
```

**Example3:** You can use `.ConfigureAwait(false)` on the enumerable/enumerator itself. That's not a language feature: it's just part of the BCL definition of `IAsyncEnumerable` and `IAsyncEnumerator`.
```csharp
// EXAMPLE 3: ConfigureAwait support
foreach (await var x in GetStreamFactory().ConfigureAwait(false)) { ... }
foreach (await var x in GetStream().ConfigureAwait(false)) { ... }
```

**Example4:** One of the BCL `IAsyncEnumerable` features is its `GetEnumerator()` method takes an optional cancellation token. Again, this is part of the BCL and not a language feature.
```csharp
// EXAMPLE 4: cancellation when consuming IAsyncEnumerable
var cts = new CancellationTokenSource();
foreach (await var x in GetStreamFactory().GetEnumerator(cts.Token)) { ... }
```

**Example5:** There's a new contextual keyword `async` inside eligible async iterator and async tasklike methods, similar to `this` and `base` except it refers to the current async *method instance*. We'll see later that this is a general-purpose mechanism needed for a variety of async methods, but for now the library just uses the mechanism to let the async iterator get hold of the cancellation token that was passed to `GetEnumerator`.
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
// foreach (await T x in e) embedded_statement;

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

**Example7:** On the production side, again everything is pattern-based. For an async method with return type `C`, it looks up a builder `CB` for that type, and the builder's responsible for handling the `await` operator as today, and also the `yield` statement and the new `async` contextual keyword. (Rationale: we've already established that awaits are better handled by a builder rather than solely by the language; also, this will allow for `IObservable` and `IAsyncActionWithProgress`).
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


## Proposal

```antlr
foreach_statement
    : ...
    | 'foreach' '(' 'async' local_variable_type identifier 'in' expression ')' embedded_statement
    ;
```

There is a new form of [foreach statement](https://github.com/ljw1004/csharpspec/blob/f12213c4ffe77a51dbc5412250bef6af75333f32/statements.md#the-foreach-statement) distinguished by the `async` contextual keyword. Note that this is not a parse ambiguity, but it requires lookahead to determine whether `async` is a typename or a modifier.

This async-foreach statement expands as per Example6 above. It is an error if the static type of `expression` doesn't implement `IDisposable`. It is an error if any of the expansions fail to bind. (This requires either `expression.GetEnumerator()` to bind, or `expression.MoveNextAsync()` to bind and be awaitable and also `expression.Current` to bind and have a value and also `expression` to implement `IDisposable`). It is in error to use an async-foreach in a method that doesn't allow `await`.

The rest of this proposal builds upon the [arbitrary async return](feature%20-%20arbitrary%20async%20returns.md) feature proposal...

Define:
* If a method has the `async` modifier and no `yield` statements then it is an *async method*.
* If a method has the `async` modifier and one or more `yield` statements then it is an *async iterator method*

Async iterator methods:
* It is an error for an async iterator method to have a `return` statement.
* It is an error for an async iterator method to be a lambda.
* The return type for an async iterator method must be a *generic Tasklike* `Tasklike<T>`.
* Its builder `Builder<T>` must have public instance method `YieldValue<TSM>(T value, ref TSM sm) where TSM : IAsyncStateMachine`; every `yield return e` is translated into `builder.YieldValue(e, ref sm); return; label:`, and it is an error if `e` is not implicitly convertible to `T`. (The `sm` parameter is to allow for builders that box at the first `yield`, but can be ignored otherwise).
* Its builder must have a public instance method `SetResult()`; when the async iterator method encounters `yield break` or the closing brace then it calls `builder.SetResult()`.
* Calls into `builder.SetException` and `builder.AwaitOnCompleted` and `builder.AwaitUnsafeOnCompleted` are as before.
* If the return type is specifically `System.Collections.Generic.IAsyncEnumerable<T>` with builder `System.Runtime.CompilerServices.AsyncEnumerableMethodBuilder<T>`, or the return type is `System.Collections.Generic.IAsyncEnumerator<T>` with builder `System.Runtime.CompilerServices.AsyncEnumeratorMethodBuilder<T>`, then the compiler is at liberty to *assume* it knows the shape and implementations of these types, and is at liberty to *hard-code* a different more efficient implementation.
 
```antlr
primary_no_array_creation_expression
   : ...
   | async_access;
   
async_access
   | `async`;
```

An *async_access* consists of the contextual keyword `async`. The only context where it's a keyword, and where *async_access* is allowed, is inside the body of a method of an async or async iterator method whose return type isn't `void` and whose builder type has a public field or property named `Async`. It is classified as a value and is readonly. Its type is the type of that field or property, and its value is `builder.Async`.


## Design rationale and discussion

### Discuss: cancellation token to `MoveNextAsync`

Some folks suggest that each individual call to `MoveNextAsync` should have its own cancellation token. This feels weird. We've always passed in cancellation token at the granularity of an async method, and there's no reason to change that: from the perspective of users, the async method they see is the *async iterator method*.

It's also weird because on the consumer side, in the *async-foreach* construct, there's no good place for the user to write a cancellation token that will go into each `MoveNextAsync`: from the perspective of the users, they don't even see the existence of `MoveNextAsync`.

Also: it would be weird to attempt to write an async iterator method where the cancellation-token that you want to listen to gets changed out under your feet every time you do a yield:
```csharp
// Here I'm tring to write an async iterator that can handle a fresh CancellationToken
// from each MoveNextAsync...
async IAsyncEnumerable<int> f()
{
   var cts = CancellationTokenSource.CreateLinkedTokenSource(async.CancellationToken);
   var t1 = Task.Delay(100, cts.Token);
   var t2 = Task.Delay(100, cts.Token);
   await t1;
   yield 1;
   
   // ??? at this point can I even trust "cts" any more?
   // or do I need to create a new linked token source for further work that I do now?
   
   yield 2;
   
   await t2;
   // ?? is this even valid anymore, given that it's using an outdated cancellation token?
   // If someone cancels that latest freshest token passed in the latest MoveNextAsync,
   // how will that help cancel the existing outstanding tasks?
}
```


### Discuss: expected shape of `IAsyncEnumerable<T>`

Here's my rough draft of what `IAsyncEnumerable<T>` could look like. I figure that there's not much benefit in having `MoveNextAsync` return `ValueTask<bool>` over `Task<bool>` since neither will result in any heap allocations. I wasn't sure if configuring the await was worth splitting into separate `IConfiguredAsyncEumerable / IConfigureAsyncEnumerator`, or if it'd feel simpler just to use the same single type.

```csharp
interface IAsyncEnumerable<T>
{
   IAsyncEnumerator<T> GetEnumerator(CancellationToken cancel = default(CancellationToken));
   IAsyncEnumerable<T> ConfigureAwait(bool b);
}

interface IAsyncEnumerator<T>
{
   IAsyncEnumerator<T> ConfigureAwait(bool b);
   T Current {get;}
   ConfiguredTaskAwaitable<bool> MoveNextAsync();
}
```


### Discuss: `async` modifier but no `iterator` modifier

* *Async* is signalled by the `async` modifier
* *Iterators* are signalled by the presence of `yield return` or `yield break` inside the method

In this proposal I've stuck to the same convention: an *async iterator* is signalled by both the `async` modifier and the `yield` inside it.

(We could separately imagine that the keyword `iterator` be allowed optionally for iterator and async-iterator methods).

Under this proposal, I can imagine people using async iterators just to build normal iterators (in case they want more flexibility that what the standard iterators provide). In that case it'd feel weird for them to have to use the `async` modifier.

Under this proposal I've used the `async` contextual keyword, to tie it back to the `async` modifier. That might also feel weird for folks writing pure iterators with the feature.

Under this proposal, I've disallowed async iterator lambdas. That's to follow C# precedent where iterator lambdas aren't allowed. Last time we discussed it at C# LDM, there was almost no support for iterator lambdas. (VB will continue to allow them of course).


### Discuss: `async` contextual keyword

In this proposal I've used `async` as a new contextual keyword. It will only exist in async methods that return a non-void tasklike whose builder has an `Async` property on it. Note that `Task` and `Task<T>` today do not meet this criterion: therefore `async` will not be a keyword inside existing async methods:

```csharp
class async { void f() {} }

async Task g()
{
  async async = new async();  // refers to type "async" and declares an identifier named "async"
  async.f();                  // refers to identifier named "async"
}


async IAsyncEnumerable<int> h()
{
   async async = new async(); // error because async is a contextual keyword due to IAsyncEnumerable
   async.f();                 // only allowed if IAsyncEnumerable's builder has an Async property with f() on it
}
```

It has been suggested to use `await` as the contextual keyword, e.g. `var x = await.CancellationToken`. This would let the existing `Task` and `Task<T>` async methods refer to their builder. However there's no point, since there are no interesting interactions that an async method can do with its Task's builder.

It has been suggested that this mechanism would avoid the need to pass cancellation tokens around everywhere. That's not true. You still need to write your async methods to take cancellation token parameters. (1) It's an established good design pattern to use cancellation token parameters because it composes better. (2) If you don't do it this way, and you want some other mechanism like F#'s ambient cancellation-token state, you can already do this with a global variable.


### Discuss: syntax for async-foreach

```csharp
foreach (var x in asyncStream) { ... } // implicitly does an async-foreach if it fits the pattern
foreach async (var x in asyncStream) { ... }
foreach (async var x in asyncStream) { ... } 
foreach await (var x in asyncStream) { ... }
foreach (await var x in asyncStream) { ... }
await foreach (var x in asyncStream) { ... }
```

There'll be lots of debate over which exact syntax to use for an async-foreach. I don't know which is best. I'm inclined to write a prototype which supports *all* of them to see how they all feel in practice.


### Discussion: type inference

Async iterator methods are not allowed to be lambdas in C#. Therefore there are no type inference concerns about them.

(As for VB, it will need to figure out type inference and overload resolution for them, similar to how it has to figure it out for arbitrary async returns. The default return type for an async lambda is `IAsyncEnumerable<T>`.)


### Discussion: `IAsyncOperationWithProgress<TResult,TProgress>`

The Windows type `IAsyncOperationWithProgress<TResult,TProgress>` represents something that can report progress of type `TProgress`, and can also return a value of type `TResult`.

Under the current proposal you can't write an async iterator method with this return type. That's because (1) we only work with arity-1 generic tasklikes, (2) we require a `builder.SetResult()` method that takes no arguments.

The reason for restricting to arity-1 generic tasklikes is to allow type inference of lambdas: there has to be a way for the compiler to infer from a lambda containing `return 5` that its return type is `Task<int>`. And in VB there has to be a way for the compiler to infer from a lambda containing `Yield 5` that its return type is `IEnumerable<int>`. We solved this problem by requiring the tasklike to have arity 1, and its generic type parameter is the type of the operand.

If we wanted to support lambdas with higher arity, we'd have to enshrine the convention that an "arity 2 builder" has two generic type parameters, the first relating to any `return` statements inside it, the second relating to any `yield` statements. This is plausible but a bit weird.

C# doesn't have to suppport iterator lambdas, nor async iterator lambdas, so the problem disappears. For C# we could decide to allow arbitrary arity builders for an async iterator lambda. But I don't like this because (1) it feels awkward, (2) it would preclude C# from ever having iterator lambdas in the future.


### Discussion: no warning about async iterator lacking `await`

Currently, if an async method lacks any `await` operators, it gives a warning. This was an important part of helping understand the async feature when it first came out.

I don't think we need give a similar warning for async iterator methods that lack any `await` operator. (1) There are legitimate use-cases where you want to use the feature solely to write a custom non-async iterator; (2) I think people understand async well enough now that it's not needed.

We might consider allowing a builder which lacks `AwaitOnCompleted` and `AwaitUnsafeOnCompleted` methods, and uses this absence to indicate that the `await` operator isn't allowed in the method. I don't know. That'd only feel natural inside a method with a new `iterator` modifier.
