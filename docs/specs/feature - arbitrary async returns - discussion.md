# C# design rationale and alternatives: arbitrary async returns

*This document explores the design space for the feature proposal [arbitrary async returns](feature - arbitrary async returns.md).*


## Discuss: back-compat breaks

The tricky scenario is this:

1. Using C#6 we write two overloads, and overload resolution prefers one candidate.
2. Still using C#6, we upgrade one of our library references to a new version in which a type now becomes tasklike.
3. We upgrade to C#7, and our existing code either starts to break with an ambiguity error, or picks a different overload altogether.

In the following examples, suppose we are happily using `ValueTask` in our C#6 code, but the `ValueTask` NuGet package is updated to make it tasklike while we're still on C#6, and subsequently we update to C#7.

> I think there's a general principle of the feature (at least in type inference) to keep in mind: *"all other things being equal, async lambdas still gravitate towards `Task`".*

```csharp
// Example 1: in C#6 this code compiles fine and picks the first overload,
// but in C#7 it picks the second for being better
void f(Func<Task<double>> lambda)
void f(Func<ValueTask<int>> lambda)
f(async () => 3);
```
* __Example 1__ is caused by the proposed change to [Better conversion target](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-target) to make it dig into tasklikes: it says that `TasklikeA<S1> > TasklikeB<S2>` if `S1 > S2`. (I'm writing `>` for "is a better conversion target than").
* We could make it an ambiguity error (rather than a silent change in behavior), in two possible ways:
  * `TasklikeA<S1> > TasklikeB<S2>` if `TasklikeA == TasklikeB && S1 > S2`.
  * `TasklikeA<S1> > TasklikeB<S2>` if `(TasklikeA == TasklikeB || TasklikeA == Task<T>) && S1 > S2`.
  * Both option seem fine. The second follows the general principle of gravitating towards `Task` but its extra complexity doesn't seem worth it.
* We could decide to keep the old behavior:
  * `TasklikeA<S1> > TasklikeB<S2>` if `(TasklikeA == Task && TasklikeB != Task) || S1 > S2`.
  * This option feels arbitrary and confusing. It's not motivated by the general principle outlined above, because in this case all other things *are not equal*.
* We could decide to keep silent change in behavior, as proposed:
  * `TasklikeA<S1> > TasklikeB<S2>` if `S1 > S2`.


```csharp
// Example 2: in C#6 this code compiles fine and picks the first overload,
// but in C#7 it gives an ambiguity error
void g(Func<Task<int>> lambda)
void g(Func<ValueTask<int>> lambda)
g(async () => 3);
```
* __Example 2__ is caused by a new conversion of async lambda to non-task-returning delegate, which always introduces ambiguity errors because it always allows more candidates to become available.
* We could decide to keep the old behavior:
  * If the new overload resolution rules result in ambiguity error, but one of the candidates didn't involve any conversion from async lambda to a non-task-returning delegate, then prefer that candidate
  * This is in keeping with the general principle to prefer `Task` over other tasklikes, all other things being equal.
* We could decide to keep the new ambiguity error:
  * I'm not so keen on this ambiguity error. I prefer the C#6 behavior.

  
```csharp
// Example 3: in C#6 this code compiles fine and picks the first overload with T=Task<int>,
// but in C#7 it picks the second with T=int for being more specific.
void h<T>(Func<T> lambda)
void h<T>(Func<ValueTask<T>> lambda)
h(async () => 3);
```
* __Example 3__ is caused by the new [Better function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-function-member) rule which says that if two candidates `{P1...Pn}` and `{Q1...Qn}` are identical *up to tasklikes* then we should use the "more-specific" tie-breaker.
* We could decide to keep the old behavior:
  * This would fall out by keeping the old behavior for Example 2 (which prefers `Func<Task<int>>` over `Func<ValueTask<int>>`)
  * I think the old behavior is concretely bad in this case because it doesn't let folks write the idioms they want for `ValueTask`.
* We could decide that the new behavior is better:
  * (If we did keep the old behavior in Example 2, then we'd need to ensure that the test for "identical up to tasklikes" is done *before* asking whether one candidate is better than the other candidate).


## Discuss: how to identify tasklikes and find their builder?

**Question.** How does the compiler know which builder type to use for a given tasklike?
```csharp
// Option1: via a static method on the tasklike.
// We'd invoke "var builder = Tasklike.GetAsyncMethodBuilder()" --
// but this doesn’t work with interfaces and doesn’t let you extend third-party types
class MyTasklike { [EditorBrowsable(EditorBrowsableState.Never)] public static BuilderType GetAsyncMethodBuilder(); }

// Option2: via an extension method on the tasklike.
// We'd invoke "var builder = default(Tasklike).GetAsyncMethodBuilder();" and rely on extension method lookup --
// but this doesn’t work with instance methods
public static class Extensions { public static void GetAsyncMethodBuilder(this MyTasklike dummy) => new BuilderType(); }

// Option3: via an attribute on the tasklike type --
// but this doesn't help with third-party types, and requires us to ship the attribute type in some assembly
[AsyncMethodBuilder(typeof(BuilderType))] class MyTasklike { ... }

// Option4: via an attribute on the async method (rather than on the tasklike type),
// which would let you do the magic for Task-returning methods --
// but this is cumbersome, doesn't work with lambda arguments or type inference, and again requires us to ship the attribute type
[AsyncMethodBuilder(typeof(BuilderType))] async Task TestAsync() { ... }
```

If we could have static methods on interfaces, then Option1 would be fine. If we could also have extension static methods, then Options 1+2 would sort of blend together. That would be ideal.

I'm worried that Option2 means  the question of whether something is *tasklike* is no longer a property of the type itself, but is instead the result of an extension member lookup in a given context. Is it scary that context might now affect the outcome of overload resolution and betterness? In the compiler will the context have to be threaded into more places than it is now? I'll have to experiment.

So far I've implemented Option1 because it's the cleanest. I also implemented Option3 with the attribute name `[Tasklike(...)]` to work with interfaces. I think I should implement all four for now, to experiment in the prototype, and I should change the attribute name to `[AsyncMethodBuilder]`.

Options 1 and 2 have the slight advantage of not requiring an attribute to be define+shipped somewhere. They're also the most flexible about the generic arity of the builder: it need not be exactly the same as that of the tasklike.

Will we need to implement multiple options to cover all the use-cases? (If so, what should happen if two different options give different builders?!)

## Discuss: reduce number of heap allocations

**Question.** Can we reduce number of heap allocations on the cold path?

Currently it allocates two heap objects on the cold path – (1) the returned Task itself, (2) the boxed state machine struct, of which the builder struct is a field.

I wonder if we can do better and allocate only one heap object – the builder, which coincidentally is also the returned tasklike type, and which contains the state machine struct as a field?

I’m investigating this avenue now because I think it will be important for efficiency of async iterators [[link](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20async%20iterators.md)]. If we ship C#7 using the two-allocation model, would we additional want to support the hypothesized one-allocation model in future?

Here's similar investigation by [@bbary](https://github.com/bbarry): [Zero allocations ValueTaskEnumerator](https://gist.github.com/bbarry/0fca79b6ac8f9ea642a768024560aaa5)

## Discuss: genericity of tasklike and builder

**Question.** Can we have a builder with method `SetResult(T value)`, i.e. allowing a `return` statement with operand type `T`, even though it's for an async method whose return type is `Tasklike<U>` for some different `U`?

*This  might be possible for top-level methods, but it doesn't work with lambdas and type inference. Let's spell out the related question about type inference:*

**Question.** When the compiler does generic type inference with the argument `async()=>{return 3;}` being passed to a method `void f(Func<MyTask<T>> lambda)`, how does it go from `3` to `int` to `T = int` ?


**Current behavior:** Let's start with the traditional behavior for `Task`:
```csharp
void f(Func<Task<T>> lambda);
var xf = f(async () => {return 3;});
```
* This infers `T = int`


**Proposal:** Under the proposal, it will work like this:
```csharp
void g(Func<MyTasklike<T>> lambda);
var xg = g(async () => {return 3;});
```
* Under the proposal, we required a generic builder `MyBuilder<T>`
* and we first inferred the *result type* `int` from the lambda. (Result type is the type of the return operand)
* and figured out that `T = int`
* and we pick the concrete type `var builder = MyBuilder<int>.Create()`
* and we happily called the `builder.SetResult(3)` method


**More general attempt 1:** But can we make it more general? Like this?
```csharp
void h(Func<MyPerverse<T>> lambda);
var xh = g(async () => {return 3;});
// Imagine if we tried to do something more general...
// class MyPerverseBuilder<U> {
//   public void SetResult(U value) { }
//   public MyPerverse<IEnumerable<U>> Task { get; }
// }
```
* Start from the inferred result type of the lambda `int`
* to see that *the builder* has a SetResult method that takes `U`,
* and therefore `U = int`,
* and therefore the builder was a `MyPerverseBuilder<int>`
* and therefore, by looking at its `Task` property, we get `T = IEnumerable<int>`


**More general attempt 2:** And how about this generalization?
```csharp
void k(Func<MyWeird<T>> lambda);
var xk = k(async () => {return 3;});
// As before, can we do something more general?
// class MyWeirdBuilder {
//   public void SetResult<U>(U value) { }
//   public MyWeird<string> Task { get; }
// }
```
* Start from the inferred result type of the lambda `int`
* to see that *the builder* has a SetResult method that takes `U`
* and therefore `U = int`
* but that doesn't inform us about the builder; instead the builder is just `MyWeirdBuilder`
* and therefore, by looking at its `Task` property, we get `T = string` ?

**Impossible:** The two general attempts aren't possible: they both fall down in the second step, when they attempt to look for `SetResult` methods on the builder type, in order to infer the builder's type. But this is circular since it presupposes knowing the builder type!



## Discuss: AwaitOnCompleted in the tasklike's builder?

**Question:** Why does a tasklike's own builder decide how to implement `AwaitOnCompleted` and `AwaitUnsafeOnCompleted`?

*Option1:* We could say that a tasklike's builder is solely a **Tasklike completion source**, with methods `SetResult` and `SetException`. The job of `Await*OnCompleted` would be hard-coded by the compiler+BCL in terms of how they save execution context and call `OnCompleted`.

*Option2:* We could say that a tasklike's builder has to deal with `Await*OnCompleted` as well.

I think that Option1 is more intellectually pure, and would help the problem that few people could write context-capturing code correctly.

But I picked Option2 because I suspect, if you don't care for capturing, then Option2 allows for better runtime perf. It also allows for some nice functionality for an async method to communicate with its builder. 


## Discuss: async method interacts with the builder instance

In the "async pattern", cancellation and progress are done with parameters:
```csharp
void f(int param, CancellationToken cancel, IProgress<string> progress)
```

But for some tasklikes, cancellation and progress are instead faculties of the tasklike (equivalently, of its builder)...
```csharp
// Windows async tasklikes:
async IAsyncActionWithProgress<string> TestAsync() { ... }
var a = TestAsync();
a.Progress += (s) => Console.WriteLine($"progress {s}");
a.Start();
a.Cancel();
```

```csharp
// IObservable tasklike:
async IObservable<string> TestAsync() { ... }
var s = TestAsync().Subscribe(s => Console.WriteLine(s));
s.Dispose(); // maybe this should cancel the work
```

It's possible, in the proposal as outlined, for the async method body to communicate with its builder. The way is a bit hacky: the async method has to await a custom awaiter, and the builder's `AwaitOnCompleted` method is responsible for recognizing that custom awaiter and doing the right thing:
```csharp
async IAsyncActionWithProgress<string> TestAsync()
{
   var (cancel, progress) = await new MyCustomAwaiter();
   ...
}
```

I think we should make `async` a contextual keyword inside an async method. (Except: for async methods that return `Task` or `Task<T>` or `void`, there's no need to communicate with the builder, and it would be a breaking change if we start reserving the `async` keyword inside these methods.)

```csharp
async IAsyncActionWithProgress<string> TestAsync(HttpClient client)
{
   await client.GetStringAsync(url, async.CancellationToken);
   async.Progress?.Invoke("10%");
}
```

This `async` keyword would be similar to `this` and `base`, but would refer to the *current instance of the async method*. Specifically, it would refer to the `Async` property of the current builder's instance:

```csharp
class IAsyncActionWithProgressBuilder<T>
{
   ...
   public IAsyncActionWithProgressAsync<T> Async {get;}
}

class IAsyncActionWithProgressAsync<T>
{
   public CancellationToken CancellationToken {get;}
   public IProgress<T> Progress {get;}
}
```


## Discuss: overload resolution with async lambdas

There's a thorny issue around type inference and overload resolution. The proposal has one solution for it. I want to outline the problem and discuss alternatives. We have to build up to the problem with some examples...

**Example1:** This is allowed and infers `T = int`. Effectively, type inference can "dig through" `Task<T>`. This is a really common pattern.
```csharp
// Example 1
void f<T>(Func<Task<T>> lambda)
f(async () => 3); // infers T = int
```

**Example2:** This is also allowed and infers `T = Task<int>`. Effectively, type inference can *synthesize* the type `Task<>` based solely on the `async` modifier. It's a weird behavior, one that doesn't happen elsewhere in the language, and is typically the wrong thing to do (because it's rare that you can write a correct `f` which handles both async Task-returning lambdas and non-async lambdas).
```csharp
// Example 2
void f<T>(Func<T> lambda)
f(async () => 3); // infers T = Task<int>
```


**Example3:** When the two examples above are overloaded, the compiler has rules to pick the winner. In particular, if two overload candidates have identical parameter types (after inferred types have been substituted in) then it uses the *more specific candidate* **tie-breaker rule** ([Better function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-function-member)). This *tie-breaker* ends up being used a lot for async lambdas, particularly for things like `Task.Run`.
```csharp
// Example 3
void f<T>(Func<Task<T>> lambda)   // infers T = int and is applicable
void f<T>(Func<T> lambda)         // infers T = Task<int> and is applicable
f(async () => 3);   // picks more specific first overload with T = int
```

**Example4:** We want type inference to be able to dig through other Tasklikes as well, not just `Task`. But we can never change the Example3 rule which gives privileged inference to `Task<T>`:
```csharp
// Example 4
void f<T>(Func<MyTask<T>> lambda)
f(async () => 3);   // we want this to work and infer T = int

void f<T>(Func<T> lambda)
f(async () => 3);   // for back-compat, this will always infer T = Task<int>
```

**Example5:** So what do we want to happen when the two cases of Example4 are overloaded?
```csharp
// Example 5
void f<T>(Func<MyTask<T>> lambda)  // infers T = int and is applicable
void f<T>(Func<T> lambda)          // infers T = Task<int> and is applicable
f(async () => 3);   // what should this do?
```
If we do indeed change type inference to dig through Tasklikes, but we don't change the rules for overload resolution, then it will give an **ambiguous overload** error. That's because it looks at the two candidates `f(Func<MyTask<int>> lambda)` and `f(Func<Task<int>> lambda)`...
* The two don't have identical parameter types, so it won't go through the *tie-breaker* rules about more specific
* Instead it looks at the rules for **better function member**
* This requires to judge whether the conversion from expression `async()=>3` to type `Func<MyTask<int>>` or to type `Func<Task<int>>` is a [better conversion from expression](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-from-expression)
* This requires to judge whether the type `Func<MyTask<int>>` or `Func<Task<int>>` is a [better conversion target](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-target)
* This requires to judge whether there is an implicit conversion from `MyTask<int>` to `Task<int>` or vice versa.

Folks might decide to have user-defined implicit conversions between their tasklikes and `Task`. But that's beside the point. The only way we'll get a good overload resolution disambiguation between the candidates is if we go down the *tie-breaker* path to pick the more specific candidate. Once we instead start down the *better function member* route, it's already too late, and forever will it dominate our destiny.

**Problem statement: Somehow, Example5 should pick the first candidate on grounds that it's more specific.**

### Overload resolution approach 0

**Approach0:** We could just leave Example5 to give an ambiguous overload error, i.e. not do anything.

This would be a shame. It would disallow some common patterns like `Task.Run` from being used on any tasklikes other than `Task` itself. Also, as discussed in Example2, you can't really write a single method `f<T>(Func<T> lambda)` which works right for both normal and async lambdas, so you really do have to provide the second overload, so Example5 is a common case.

I think that folks should be able to come up with their own parallel world of `MyTask` that looks and feels like `Task`, with the same level of compiler support.

### Overload resolution approach 1

**Approach1:** We could use two new pseudo-types `InferredTask` and `InferredTask<T>` as follows:

1. We modify type inference so that, whenever it's asked to infer the *return* type of an async lambda, then in the non-generic case it always gives `InferredTask` and in the generic case it always gives `InferredTask<T>` where `T` is the inferred *result* type of the lambda.
2. These new pseudo-types are merely placeholders for the fact that type inferences wanted to infer a tasklike but is flexible as to which one.
3. When overload resolution attempts to judge whether two parameter sequences `{P1...Pn}` and `{Q1...Qn}` are identical, it treats these pseudo-types as identical to themselves and all tasklikes of the same arity. This would allow it to go down the *more specific* tie-breaker route.
4. If overload resolution picks a winning candidate with one of the pseudo-types, only then does the pseudo-type get collapsed down to the concrete type `Task` or `Task<T>`. 

I struggled to make sense of this approach. Consider what is the principle behind this type inference? One possible principle is this: ***when type inference succeeds and mentions one of the pseudo-types, it means that all tasklikes would be equally applicable in its place***. But that's not really how type inference works in C#. Consider:
```csharp
void f<T>(Func<T> lambda1, Func<T,T> lambda2)
f( ()=>3, (x)=>x.ToString());
```
This will happily infer `T = int` and simply not bother checking whether this candidate for `T` makes sense for the second argument. In general, C# type inference doesn't guarantee that successful type inference will produce parameter types that are applicable to arguments. This is a weak foundation upon which to build "all tasklikes would be applicable".

The opposite possible principle behind type inference is that ***it should aggressively prefer to give type inference results that include the pseudo-types*** rather than any particular concrete tasklikes. The rationale here is that the whole point of the pseudo-types is to encourage candidates to be treated equivalent up to tasklikeness that arises from async lambdas. But this principle doesn't feel very principled... (see Approach 2 below).

Next we'd have to decide how the chosen principle informs how the pseudo-types work with [Fixing](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#fixing). For instance, if a generic type parameter has lower bounds `InferredTask<int>` and `Task<int>` then what should it fix as? What if it has lower bounds `IEnumerable<InferredTask<int>>` and `IEnumerable<Task<int>>` and `IEnumerable<MyTask<int>>`?

We'd also have to decide how the pseudo-types work with [lower bound inference](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#lower-bound-inferences) and upper bound inference and exact bound inference. For instance, if doing a lower bound inference from `InferredTask<int>` to `Task<T>`, we can certainly dig in to do a lower bound inference from `int` to `T`, but should we also do an exact inference from `Task` to this `InferredTask`? How about lower bound inference from `Task<int>` to `InferredTask<T>`? How about an upper bound inference?

We'd also have to decide how the pseudo-types work with [applicable function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#applicable-function-member). Presumably any async lambda has an implicit conversion to a delegate type with return type `InferredTask<T>`. But what about all the other implicit conversions?

In the end, there are too many difficult questions to address here, and we can't give good answers because we don't have good principles to start from.

The next approach takes the second principle, and turns it into something simpler and more intellectually defensible.

### Overload resolution approach 2

**Approach2:** The whole point is that we want tasklikes to go down the "tie-breaker" path. So let's achieve that directly: if no candidate is better, then ***when judging whether `{P1...Pn}` and `{Q1...Qn}` are identical, do so up to tasklikes.*** This way we don't need to mess with pseudo-types.

This is the approach put forwards in the the feature proposal.

I'd initially put it forwards only as a joke. It seems shocking! My first instinct is that we should only allow tasklike equivalence where it relates directly to an async lambda (as in approach 1), not everywhere.

But the thing is, this change in rule only applies if none of the applicable candidates was a better function member. It only ever turns ambiugity-errors into success. So the way to judge the merits of this approach are whether it allows any winners in cases where we think it should really be an ambiguity failure.

Honestly? There aren't any, not that I can think of.

```csharp
// EXAMPLE 1
void f(Task<int> t) => 0;
void f(MyTask<int t) => 1;
Task<int> arg; f(arg); // prefers "0" because it is a better function member (identity)

// EXAMPLE 2
void f<T>(Task<T> t) => 0;
void f<T>(MyTask<T> t) => 1;
Task<int> arg; f(arg); // prefers "0" because the other candidate fails type inference

// EXAMPLE 3
void f<T>(Task<T> t) => 0;
void f(MyTaks<int> t) => 1;
Task<int> arg; f(arg); // prefers "0" because it's a better function member (identity)
```


## Discuss: Debugging support

Visual Studio has excellent support for async debugging -- the ability to debug-step-over an async method and do debug-step-out of an async method, the async callstack, and the Tasks window that shows all outstanding tasks. Users will at least expect the first two to work for tasklike-returning async methods; I'm not sure about the third.

> If we ship this feature in C#7, I think it's likely that the debugging team won't have time to add support for it into the VS async debugging features. Should we delay the feature until we can do it in concert with the debugging team? Or should we ship it ahead of them, on the grounds that this will make it easier for them to add support in the future? (I'd be able to support async callstacks via a VSIX in the interim).

Support for these debugger features will involve further changes to the async method builder pattern –- so if we ship the feature now, and debugger support comes out next release, then everyone who wrote their own tasklike will at that time likely want to augment their builder to light up debugger support. Here's how I suggest that async callstacks would work:

* The Visual Studio IDE might use reflection to attempt to invoke the method `builder.GetCompletionActions()`, where the return type must either implement `System.Threading.Tasks.Task` or be `Action[]`.
  * The idea is that if someone had retrieved the property `var tasklike = builder.Task`, and then called `tasklike.OnCompleted(action)` or `tasklike.UnsafeOnCompleted(action)`, then the IDE needs to be able to get a list of all those `action`s which are still pending.
  * It's common for tasklike types to use a `System.Threading.Tasks.Task` under the hood. We don't have any way to extract the list of actions out of one, but the IDE does, and if you return an object of type `Task` then the IDE will use its techniques (a reflection-based call into `GetDelegatesFromContinuationObject`) to extract those actions and display the callstack.
  * If you return `null` from this method, or if the method is absent or has the wrong return type, then the IDE will never be able to display async callstacks beyond the point of an async tasklike-returning method. This will make users unhappy.


## Compilation notes and edge cases

**Concrete tasklike**. The following kind of thing is conceptually impossible, because the compiler needs to know the *concrete* type of the tasklike that's being constructed (in order to construct it).
```cs
class C<T> where T : MyTasklike {
    async T f() { } // error: the return type must be a concrete type
}
```

**Incomplete builder**. The compiler should recognize the following method `f` as an async method that doesn't need a return statement, and should bind it accordingly. There is nothing wrong with the `async` modifier nor the absence of a `return` keyword. The fact that `MyTasklike`'s builder doesn't fulfill the pattern is an error that comes later on: it doesn't prevent the compiler from binding method `f`.
```cs
class C { async MyTasklike f() { } }
class MyTasklike { public static string CreateAsyncMethodBuilder() => null; }
```

If the builder doesn't fulfill the pattern, well, that's an edge case. It should definitely give errors (like it does today e.g. if you have an async Task-returning method and target .NET4.0), but it doesn't matter to have high-quality errors (again it doesn't have high quality errors today). One unusual case of failed builder is where the builder has the wrong constraints on its generic type parameters. As far as I can tell, constraints aren't taken into account elsewhere in the compiler (the only other well known methods with generic parameters are below, and they're all in mscorlib, so no chance of them ever getting wrong)
```
System.Array.Empty
System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal__AddEventHandler_T
System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal__RemoveEventHandler_T
System_Activator__CreateInstance_T
System_Threading_Interlocked__CompareExchange_T
Microsoft_VisualBasic_CompilerServices_Conversions__ToGenericParameter_T_Object
```

## Discuss: dynamic

I propose we don't change the late-binder.

There’s one odd case to note: when you make a dynamic method invocation, it does as much of the type inference as it can statically and the rest dynamically. We’ll end up in a state where the static part can handle tasklikes but the dynamic part can't.

## Discuss: IObservable

I wonder if this feature could offer native support for producing `IObservable`.

> The question of *consuming* `IObservable` is different. To write imperative (pull-based) code that consumes `IObservable` (push-based) you need a buffer of some sort. That's orthogonal and I don't want to get derailed.

I don't know what kind of semantics you'd expect from an `IObservable`-returning async method. Here are two options:

```csharp
async IObservable<string> Option1()
{
    // This behaves a lot like Task<string>.ToObservable() -- the async method starts the moment
    // you execute Option1, and if you subscribe while it's in-flight then you'll get an OnNext+OnCompleted
    // as soon as the return statement executes, and if you subscribe later then you'll get OnNext+OnCompleted
    // immediately the moment you subscribe (with a saved value).
    // Presumably there's no way for Subscription.Dispose() to cancel the async method...
    await Task.Delay(100);
    return "hello";
}

async IObservable<string> Option2()
{
    // This behaves a lot like Observable.Create() -- a fresh instance of the async method starts
    // up for each subscriber at the moment of subscription, and you'll get an OnNext the moment
    // each yield executes, and an OnCompleted when the method ends.
    await Task.Delay(100);
    yield return "hello";
    await Task.Delay(200);
    yield return "world";
    await Task.Delay(300);
}
```

I think Option2 is best here; Option1 is akin (in the words of Reed Copsey) to an iterator method with only a single yield. Pretty unusual, and pretty easy to write already.

I also don't know how cancellation should work. The scenario I imagine is that an `async IObservable` method has issued a web request, but then the subscriber disposes of its subscription. Presumably you'd want to cancel the web request immediately? Is disposal the primary (sole?) way for `IObservable` folks to cancel their aysnc sequences? Or do they prefer to have a separate stream which receives cancellation requests?

Also, when you call `Dispose` on an iterator it's a bit different. By definition the iterator isn't currently executing. So all it does is resume iterator execution by going straight to the finally blocks. This might be good enough for async - e.g. the finally block could signal a cancellation that was being used throughout the method. (It could even await until the outstanding requests had indeed finished their cancellation, although this would be cumbersome to code.) But it's uneasy, and I wonder if a different form of cancellation is needed?

We need guidance from `IObservable` subject matter experts.
