# C# feature proposal: arbitrary async returns

*This document explores the design space for arbitrary async returns.*


## Discuss: connection between tasklike and builder

**Question.** How does the compiler know which builder type to use for a given tasklike?
```csharp
// Option1: via attribute on the tasklike type itself
[Tasklike(typeof(BuilderType))] class MyTasklike { ... }

// Option2: via an attribute on the async method
[Tasklike(typeof(BuilderType))] async Task TestAsync() { ... }

// Option3: via a dummy call to "var builder = default(Tasklike).GetBuilder();"
public static class Extensions
{
	public static void GetBuilder(this MyTasklike dummy) => new BuilderType();
}
```
Option2 has the slight benefit of being able to specify a builder even when you're returning the existing `Task`. But it's worse for the typical `ValueTask` usecase because it requires you to type out the attribute every single time you want to return `ValueTask`. It also doesn't work with lambdas, which don't have a place to hang that attribute.

Option3 is ugly. We could live with that ugliness if it was useful to extend third-party tasklike types, but experience is that the implementation of the builder and the tasklike are closely related, so closely that it's not feasible to build someone else's tasklike. So the ugliness isn't merited.


## Discuss: genericity of tasklike and builder

**Question.** Why do you need a non-generic `MyBuilder` to build a non-generic `MyTask`? And why do you need an arity-1 `MyBuilder<T>` to build an arity-1 `MyTask<T>`? Why can't we be more flexible about arities?

**Question.** Why can't we write `[Tasklike(typeof(MyBuilder<object>))] MyTask` and use an instantiated `MyBuilder<object>` as the builder-type for building a non-generic tasklike `MyTask`?

*These two things might be possible for top-level methods, but they don't work with lambdas and type inference. Let's spell out the related question about type inference:*

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
* to see that the builder has a SetResult method that takes `U`,
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
* to see that the builder has a SetResult method that takes `U`
* and therefore `U = int`
* but that doesn't inform us about the builder; instead the builder is just `MyWeirdBuilder`
* and therefore, by looking at its `Task` property, we get `T = string` ?

**Impossible:** The two general attempts aren't possible: they both fall down in the second step, when they attempt to look for `SetResult` methods on the builder type, in order to infer the builder's type. But this is circular since it presupposes knowing the builder type!



## Discuss: async method interacts with the builder instance

In the "async pattern", cancellation and progress are done with parameters:
```csharp
void f(int param, CancellationToken cancel = default(CancellationToken), IProgress<string> progress = null)
```

But for some tasklikes, cancellation and progress are instead faculties of the tasklike (equivalently, of its builder)...
```csharp
// Windows async tasklikes:
async IAsyncActionWithProgress<string> TestAsync() { ... }
var a = TestAsync();
a.Progress += (s) => Console.WriteLine($"progress {s}");
a.Cancel();
```

```csharp
// IObservable tasklike:
async IObservable<string> TestAsync() { ... }
var s = TestAsync().Subscribe(s => Console.WriteLine(s));
s.Dispose(); // maybe this should cancel the work
```

It's possible, in the proposal as outlined, for the async method body to communicate with its builder. The way is a bit hacky: the async method has to await a custom awaiter, and thebuilder's `AwaitOnCompleted` method is responsible for recognizing that custom awaiter and doing the right thing:
```csharp
async IAsyncActionWithProgress<string> TestAsync()
{
   var (cancel, progress) = await new MyCustomAwaiter();
   ...
}
```

It would be possible to augment the language further, so that within the body of an async method it can use a reserved keyword `this.__builder` to refer in a strongly-typed way its current builder. But that doesn't seem worth it.

I also wonder about having the *caller* could construct and manipulate the builder in code before the async method started, to give it some context. But I don't see any good way to write this.



## Discuss: overload resolution with async lambdas

There's a thorny issue around overload resolution. The proposal has stated has one solution for it. I want to outline the problem and discuss alternatives.

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


**Example3:** When the two examples above are overloaded, the compiler has rules to pick the winner. In particular, if two overload candidates have identical parameter types (after inferred types have been substituted in) then it uses the *more specific candidate* tie-breaker rule ([Better function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-function-member)). This tie-breaker ends up being used a lot, particularly for things like `Task.Run`.
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
If we do indeed change type inference to dig through Tasklikes, but we don't change the rules for overload resolution, then it will give an **ambiguous overload** error. That's because it looks at the two candidates `f(Func<MyTask<int>>)` and `f(Func<Task<int>>)`...
* The two don't have identical parameter types, so it won't go through the tie-breaker rules about *more specific*
* Instead it looks at the rules for *better function member*
* This requires to judge whether the conversion from expression `async()=>3` to type `Func<MyTask<int>>` or to type `Func<Task<int>>` is a [better conversion from expression](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-from-expression)
* This requires to judge whether the type `Func<MyTask<int>>` or `Func<Task<int>>` is a [better conversion target](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-target)
* This requires to judge whether there is an implicit conversion from `MyTask<int>` to `Task<int>` or vice versa.* 

In general there won't be implicit conversions between arbitrary tasklikes and `Task`, and we shouldn't expect there to be. But that's beside the point. The only way we'll get a good overload resolution disambiguation between the candidates is if we go down the *tie-breaker* path to pick the more specific candidate. Once we've started down the *better function member* route, it's already too late.

**Problem statement: Somehow, Example5 should pick the first candidate on grounds that it's more specific.**

### Overload resolution approach 0

**Approach0:** We could just leave Example5 to give an ambiguous overload error, i.e. not do anything.

This would be a shame. It would disallow some common patterns like `Task.Run`. Also, as discussed in Example2, you can't really write a single method `f<T>(Func<T> lambda)` which works right for both normal and async lambdas, so you really do have to provide the second overload, so Example5 is a common case.

I think that folks should be able to come up with their own parallel world of `MyTask` that looks and feels like `Task`, with the same level of compiler support.

### Overload resolution approach 1

**Approach1:** We could use two new pseudo-types `InferredTask` and `InferredTask<T>` as follows:
1. We modify type inference so that, whenever it's asked to infer the *return* type of an async lambda, then in the non-generic case it always gives `InferredTask` and in the generic case it always gives `InferredTask<T>` where `T` is the inferred *result* type of the lambda.
2. These new pseudo-types are merely placeholders for the fact that type inferences wanted to infer a tasklike but is flexible as to which one.
3. When overload resolution attempts to judge whether two parameter sequences `{P1...Pn}` and `{Q1...Qn}` are identical, it treats these pseudo-types as identical to themselves and all tasklikes of the same arity. This would allow it to go down the *more specific* tie-breaker route.
4. If overload resolution picks a winning candidate with one of the pseudo-types, only then does it get collapsed down to the concrete type `Task` or `Task<T>`. 

I struggled to make sense of this approach. Consider what is the *principle* behind type inference? One possible principle is this: *when type inference gives a result that mentions one of the pseudo-types, it is a statement that **all tasklikes would be equally applicable in its place**.* But that's not really how type inference works in C#. Consider:
```csharp
void f<T>(Func<T> lambda1, Func<T,T> lambda2)
f( ()=>3, (x)=>x.ToString());
```
This will happily infer `T = int` and simply not bother checking whether this candidate for `T` makes sense for the second argument. In general, C# type inference doesn't guarantee that successful type inference will produce parameter types that are applicable to arguments. We don't have a strong enough foundation upon which to build "all tasklikes would be applicable".

The opposite possible principle behind type inference is that *it should aggressively *prefer* to give type inference results that include the pseudo-types* rather than any particular concrete tasklikes. The rationale here is that the whole point of the pseudo-types is to encourage candidates to be treated equivalent up to tasklikeness that arises from async lambdas. But this principle doesn't feel very principled...

Next we'd have to decide how the chosen principle informs how the pseudo-types work with [Fixing](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#fixing). For instance, if a generic type parameter has lower bounds `InferredTask<int>` and `Task<int>` then what should it fix as? What if it has lower bounds `IEnumerable<InferredTask<int>>` and `IEnumerable<Task<int>>` and `IEnumerable<MyTask<int>>`?

We'd also have to decide how the pseudo-types work with [lower bound inference](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#lower-bound-inferences) and upper bound inference and exact bound inference. For instance, if doing a lower bound inference from `InferredTask<int>` to `Task<T>`, we can certainly dig in to do a lower bound inference from `int` to `T`, but should we also do an exact inference from `Task` to this `InferredTask`? How about lower bound inference from `Task<int>` to `InferredTask<T>`? How about an upper bound inference?

We'd also have to decide how the pseudo-types work with [applicable function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#applicable-function-member). Presumably any async lambda has an implicit conversion to a delegate type with return type `InferredTask<T>`. But what about all the other implicit conversions?

In the end, there are too many difficult questions to address here, and we can't give good answers because we don't have good principles to start from.

The next approach takes the second principle, and turns it into something simpler and more intellectually defensible.

### Overload resolution approach 2

**Approach2:** The whole point is that we want tasklikes to go down the "tie-breaker" path. So let's achieve that directly: *when judging whether `{P1...Pn}` and `{Q1...Qn}` are identical, do so **up to tasklikes**.* This way we don't need to mess with pseudo-types.

This is the approach adopted by the feature proposal.

I'd initially put it forwards only as a joke. It seems shocking! My first instinct is that we should only allow tasklike equivalence where it relates directly to an async lambda (as in approach 1), not everywhere. So let's explore the ramifications...

This equivalence-up-to-tasklike is of course performed only after [applicability](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#applicable-function-member). In some cases when comparing two applicable candidates, this approach will cheat us of the *better function member* comparison, and shift us down the *tie-breaker* comparison. So the key question to ask is: **Are there any situations where *better function member* would have given us *better results* than the *tie-breaker* for choosing between two applicable candidates?**

*Better function member* means that we're comparing a pair of candidates like these and there's an implicit conversion from `Pi` to `Qi`. (The full definition is of course a bit more precise).
```
// [1]        // [2]              // [3]              // [4]
f(Pi x);      f(Func<Pi> x);      f(Task<Pi> x);      f(Func<Task<Pi>> x);
f(Qi x);      f(Func<Qi> x);      f(Task<Qi> x);      f(Func<Task<Qi>> x);
```
Under what circumstances would two applicable candidates, which differ only in tasklikes, have such an implicit conversion? The fact that delegate return types are at best covariant and `Task` is invariant means there aren't many possibilities...
* There might be a user-defined conversion from one tasklike to another, although this only applies at the top level [1]
* There might be a subtype relation from one tasklike to another, and this only applies at top level [1] or nested inside covariant delegate return types [2].

**User-defined tasklike conversions.** I bet that user-defined conversions will be common. `ValueTask` in corefx already has an implicit conversion *from* `Task`. I bet folks will likely add implicit conversions *to* `Task`, e.g. so they can do `Task.WhenAll(mytasklike1, mytasklike2)`.
```csharp
[Tasklike(...)] struct ValueTask<T> {
	public static implicit operator ValueTask<T>(Task<T> task) => ...
}

[Tasklike(...)] struct MyTask<T> {
	public static implicit operator MyTask<T>(Task<T> task) => ...
	public static implicit operator Task<T>(MyTask<T> mytask) => ...
}
```

Let's explore the scenarios in which *better function member* might give a better overload resolution for user-defined conversions than *tie-breaker*:

```csharp
// CASE1: ??? I don't know why my prototype prefers the second candidate
void f<T>(MyTask<T> t)
void f<T>(Task<T> t)
Task<int> t = ...; f(t);

// CASE2: ??? I don't know why my prototype prefers the second candidate
void f<T>(MyTask<T> t, long l)
void f<T>(Task<T> t, long l)
Task<int> t = ...; int i=1; f(t,i);


```









[TODO]


## Discuss: overload resolution

## Discuss: IObservable

----------------------
TODO:



