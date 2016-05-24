# C# feature proposal: arbitrary async returns

In C#6, async methods must return either `void` or `Task` or `Task<T>`. This proposed feature allows them to return any *tasklike* type.

> * **TRY IT OUT ONLINE:** [tryroslyn.azurewebsites.net](http://is.gd/Yjvb2P)
> * **Download:** [ArbitraryAsyncReturns.zip](https://github.com/ljw1004/roslyn/raw/features/async-return/ArbitraryAsyncReturns.zip) [22mb]
> * **Install:** Unzip the file. Quit VS. Double-click to install in this order: (1) Roslyn.VisualStudio.Setup.vsix, (2) Roslyn.Compilers.Extension.vsix, (3) ExpressionEvaluatorPackage.vsix. I don't think the others are needed.
> * **Test:** the zip file contains a sample project
> * **Uninstall:** I've usually been able to go to within Visual Studio to Tools>Extensions, search for Roslyn, and uninstall in the order (1) Expression Evaluators, (2) Compilers, (3) Language Service. Once that resulted in a broken VS that was unable to load C# projects, and I unbroke it by deleting the entire folder `%userprofile%\AppData\Roaming\Microsoft\VisualStudio` and `%userprofile%\AppData\Local\Microsoft\VisualStudio`. Doing so reset all my VS settings to default.
> * **Watch:** I coded this prototype live in livecoding.tv, and you can watch recordings if you want: [livecoding.tv/ljw1004](https://www.livecoding.tv/ljw1004/)
> * **Discuss:** please read the [Design rationale and alternatives](feature - arbitrary async returns - discussion.md), and then go to the [discussion thread](https://github.com/dotnet/roslyn/issues/10902)


The primary benefit is to allow a `ValueTask<T>` that reduces the "async overhead cost" on the hot path:
![perf](feature - arbitrary async returns.png)
* Such a `ValueTask<T>` has already been checked into corefx ([corefx#4857](https://github.com/dotnet/corefx/pull/4857))
* Some teams decide the perf benefits of `ValueTask<T>` are so great that they're worth the cumbersome manual code you have to write today (rightmost column) -- e.g. `System.Xml` ([corefx#4936](https://github.com/dotnet/corefx/pull/4936)), which had previously been using an internal form of `ValueTask`.
* Other teams decide that the cumbersome code is so ugly they're willing to forego the perf benefits for now, at least until the C# compiler gets support for `ValueTask<T>` -- e.g. ASP.NET FormReader ([aspnet#556](https://github.com/aspnet/HttpAbstractions/pull/556#issuecomment-199974553)). Using `ValueTask<T>` would have saved [10% of heap allocations for 3k requests](https://github.com/aspnet/HttpAbstractions/issues/553#issue-131415562), up to [90% for 30k requests](https://github.com/aspnet/HttpAbstractions/pull/556#issuecomment-199980832).

> (This feature proposal merely allows the middle column, to remove the necessity of heap allocation and to improve perf somewhat. Could we build upon this feature proposal to allow the rightmost column, with additional compiler unrolling to achieve optimum perf? It seems hard because the unrolled version has different semantics aroud context-capture, and the incremental perf wins seem comparitively minor. See [issue#10449](https://github.com/dotnet/roslyn/issues/10449).)



The secondary benefit is to allow async methods to return other domain-specific tasklike values. Here are some examples:
```csharp
// IAsyncAction in Windows programming. Today you write wrapper code like this:
//    IAsyncAction TestAsync(int p, int q) => TestAsyncInner(p, q).AsAsyncAction();
//    async Task TestAsyncInner(int p, int q) { ... }
// It would be neater if you could just return directly:
async IAsyncAction TestAsync(int p, int q) { ... }

// ITask<out T>. The Task that we're forced to use today is invariant. Sometimes you want covariance:
async ITask<string> TestAsync() { ... }
ITask<object> = TestAsync();

// IObservable. Today you write wrapper code like this:
//    IObservable<int> TestAsync(int p, int q) => TestAsyncInner(p,q).ToObservable();
//    async Task<int> TestAsyncInner(int p, int q) { ... }
// It would be neater if you could just return directly:
async IObservable<int> TestAsync(int p, int q) { ... }
```
Actually that `IObservable` example is still up for discussion. It's not clear whether `IObservable` would prefer to be like an async method or an async enumerable method. We need to hear from `IObservable` experts.


A third "benefit" (it's arguable whether this is a benefit at all) is that people will be able to weave hooks into their async methods, for instance to call into their own function before and after every cold-path await:
```csharp
async InstrumentedTask TestAsync()
{
    await InstrumentedTask.Configure(actionBeforeEachAwait, actionAfterEachAwait);
    await Task.Delay(10);
	await Task.Delay(20);
}
```
Some other examples: implement a C# version of Haskell's `Maybe` monad with `do` notation [[link](https://github.com/ckimes89/arbitrary-async-return-nullable/)]; implement async iterator methods [[link](https://github.com/ckimes89/arbitrary-async-return-nullable/blob/list-tasklike/NullableTaskLike/ListTaskLike.cs)].


# Key uses cases

These use-cases are written out as unit-tests at the [end of this proposal](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#unit-tests).

1. I should be able to use `ValueTask` as a wholesale replacement for `Task`, every bit as good. [[link](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-use-valuetask-as-a-wholesale-replacement-for-task-every-bit-as-good)]
2. I should be able to migrate my existing API over to `ValueTask`, maintaining source-compatibility and binary-compatibility. [[link](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-migrate-my-existing-api-over-to-valuetask)]
3. I don't want to break backwards-compatibility. In particular, suppose I have a C#6 app that references a NuGet library in which `ValueTask` is already tasklike. When I upgrade to C#7, I don't want the behavior of my code to change. [[link](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-dont-want-to-break-backwards-compatibility)]



# Proposal

__Rule 1: Tasklike.__ Define:
* A *non-generic tasklike* is any non-generic type with a single public static method `CreateAsyncMethodBuilder()`, or the type `System.Threading.Tasks.Task`.
* A *generic tasklike* is any generic type with arity 1 with the same method, or the type `System.Threading.Tasks.Task<T>`.

```csharp
struct ValueTask<T>
{
   [EditorBrowsable(EditorBrowsableState.Never)]
   public static ValueTaskBuilder<T> CreateAsyncMethodBuilder() { ... }
   ...
}
```

> This uses a static method `CreateAsyncMethodBuilder()` to distinguish a Tasklike type and locate its builder. I've also suggested some other techniques [here](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns%20-%20discussion.md#discuss-connection-between-tasklike-and-builder): (1) static method, (2) extension method, (3) attribute on tasklike type, (4) attribute on async method. I believe at least one technique will be best, but I don't know which one

__Rule 2: async methods.__ The rules for [async functions](https://github.com/ljw1004/csharpspec/blob/gh-pages/classes.md#async-functions) currently allow an async method to return either `void` or `Task` or `Task<T>`; this will be changed to allow it to return either `void`, or any non-generic `Tasklike`, or generic `Tasklike<T>`.

```csharp
async ValueTask<int> TaskAsync(int delay)
{
   await Task.Delay(delay);
   return 10;
}
```

__Rule 3: async lambdas.__ The rules for [anonymous function conversion](https://github.com/ljw1004/csharpspec/blob/gh-pages/conversions.md#anonymous-function-conversions) currently allow an async lambda to be converted to a delegate type whose return type is either `void` or `Task` or `Task<T>`; this will be changed to let them its return type be `void` or any non-generic `Tasklike` or any generic `Tasklike<T>`. (Note that any new conversions introduce ambiguity errors; see the [discussion](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns%20-%20discussion.md#discuss-back-compat-breaks) on this matter).

```csharp
Func<int, ValueTask<int>> lambda = async (x) => { return x; };
```

__Rule 4: inferred return type.__ The [inferred return type](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#inferred-return-type) of a lambda expression currently takes into account the parameter types of the delegate to which the lambda is being converted. To make type inference aware of the new conversion in Rule 3, the inferred return type of an async lambda now also takes into account the return type of that delegate:
* If the async lambda has inferred *result type* `void`:
  * if the return type of the delegate is `U` where `U` is a non-generic tasklike, then the inferred *return type* is `U`
  * otherwise the inferred *return type* is `Task`
* Otherwise, the async lambda has inferred *result type* `V1`:
  * if the return type of the delegate is `U<V2>` where `U` is a generic tasklike, then the inferred *return type* is `U<V1>`
  * otherwise the inferred *return type* is `Task<V1>`

```csharp
f(async (x) => {return x;})
void f<T>(Func<int,T> lambda);            // inferred lambda return type is Task<int>, giving T = Task<int>
void f<U>(Func<int,Task<U>> lambda);      // inferred lambda return type is Task<int>, giving U = int
void f<U>(Func<int,ValueTask<U>> lambda); // currently: inferred lambda return type is Task<int>, giving a type inference failure
void f<U>(Func<int,ValueTask<U>> lambda); // proposal:  inferred lambda return type is ValueTask<int>, giving U = int
```

__Rule 5: overload resolution.__ There are two different options for how to deal with overload resolution. They're dealt with below.

__Rule 6: evaluation of async functions.__ The rules for [evaluation of task-returning async functions](https://github.com/ljw1004/csharpspec/blob/gh-pages/classes.md#evaluation-of-a-task-returning-async-function) currently talk in general terms about "generating an instance of the returned task type" and "initially incomplete state" and "moved out of the incomplete state". These will be changed to spell out how that returned tasklike is constructed and how its state is transitioned, as detailed in the following subsection.

```csharp
struct ValueTaskBuilder<T>
{
    public static MyTaskBuilder<T> Create();
    public void SetStateMachine(IAsyncStateMachine stateMachine);
    public void Start<TSM>(ref TSM stateMachine) where TSM:IAsyncStateMachine;
    public void AwaitOnCompleted<TA, TSM>(ref TA awaiter, ref TSM stateMachine) where TA:INotifyCompletion where TSM:IAsyncStateMachine;
    public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA awaiter, ref TSM stateMachine) where TA:ICriticalNotifyCompletion where TSM:IAsyncStateMachine;
    public void SetResult(T result);
    public void SetException(Exception ex);
    public ValueTask<T> Task {get;}
}
```

## Semantics for execution of an async method

The *builder type* of a tasklike is the return type of the static method `CreateAsyncMethodBuilder()` on that tasklike. (Except: if the tasklike is `System.Threading.Tasks.Task` then the builder type is `System.Runtime.CompilerService.AsyncTaskMethodBuilder`; and if the tasklike is `System.Threading.Tasks.Task<T>` then the builder type is `System.Runtime.CompilerService.AsyncTaskMethodBuilder<T>`).

When an async tasklike-returning method is invoked,
* It creates `var sm = new CompilerGeneratedStateMachineType()` where this compiler-generated state machine type represents the async tasklike method, and may be a struct or a class, and has a field `BuilderType builder` in it, and implements `IAsyncStateMachine`.
* It assigns `sm.builder = Tasklike.CreateAsyncMethodBuilder()` to create a new instance of the builder type. (Except: if the tasklike is `Task` or `Task<T>`, then the assignment is instead `sm.builder = BuilderType.Create()`.)
* It then calls the `void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine` method on `builder`. It is an error if this instance method doesn't exist or isn't public or has a different signature or constraints. The `sm` variable is that same `sm` as was constructed earlier. Upon being given this `sm` variable, the builder must invoke `sm.MoveNext()` on it exactly once, either now in the `Start` method or in the future. 
* It then retrieves the `U Task {get;}` property on `sm.builder`. The value of this property is then returned from the async method. It is an error if this instance property doesn't exist or isn't public or if its property type `U` isn't identical to the return type of the async tasklike-returning method.

Execution of `sm.MoveNext()` might cause other builder methods to be invoked:
* If the async method completes succesfully, it invokes the method `void SetResult()` on `sm.builder` (in case of a non-generic tasklike), or the `void SetResult(T result)` method with the operand of the return statement (in case of a generic tasklike). It is an error if this instance method doesn't exist or isn't public.
* If the async method fails with an exception, it invokes the method `void SetException(System.Exception ex)` on `sm.builder`. It is an error if this instance method doesn't exist or isn't public.
* If the async method executes an `await e` operation, it invokes `var awaiter = e.GetAwaiter()`.
  * If this awaiter implements `ICriticalNotifyCompletion` and the `IsCompleted` property is false, then it calls the method `void AwaitUnsafeOnCompleted<TA,TSM>(ref TA awaiter, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine` on `sm.builder`. It is an error if this instance method doesn't exist or isn't public or has the wrong constraints. The builder is expected to call `awaiter.UnsafeOnCompleted(action)` with some `action` that will cause `sm.MoveNext()` to be invoked once; or, instead, the builder may call `sm.MoveNext()` once itself.
  * If this awaiter implements `INotifyCompletion` and the `IsCompleted` property is false, then it calls the method `void AwaitOnCompleted<TA,TSM>(ref TA awaiter, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine` on `sm.builder`. It is an error if this instance method doesn't exist or isn't public or has the wrong constraints. Again the builder is expected to call `awaiter.OnCompleted(action)` similarly, or call `sm.MoveNext()` itself.

In the case where the builder type is a struct, and `sm` is also a struct, it's important to consider what happens should the builder decide to *box* the struct, e.g. by doing `IAsyncStateMachine boxed_sm = sm`. This will always create a new copy of `sm`, which will in turn contain a new copy of `sm.builder`.
* The builder is at liberty anytime to call `boxed_sm.SetStateMachine(boxed_sm)`. The implementation of this method is compiler-generated, but its only effect is to invoke the `void SetStateMachine(IAsyncStateMachine boxed_sm)` method on `boxed_sm.builder`. It is an error if this instance method on the builder doesn't exist or isn't public.
* This mechanism is typically used by struct builder types so they can box only once the `sm` parameter they receive in their `Start` or `AwaitOnCompleted` or `AwaitUnsafeOnCompleted` methods; on subsequent calls, they ignore that parameter and used the version they have already boxed. 


## Overload resolution

The situation is that we've added a new conversion from async lambdas to delegates that return non-`Task` tasklike types (and we've added type inference rules to go with this). Whenever you add a new conversion, it means that overload resolution will be impact We have a few different options on the table for overload resolution, to make the impact as small as possible. Neither option is perfect, but we'll rank them on how well each option satisfies the unit tests.

* __[Option "IC"]__ Don't change overload resolution; instead rely on a user-defined implicit conversion from `ValueTask` to `Task`. (note: this actually does require a small change to overload resolution, detailed below).
* __[Option "E"]__ Make overload resolution treat tasklikes equivalently as it treats `Task` today, but build in a preference for `Task` over `ValueTask` to preserve back-compat.

Overload resolution is tricky. Before even defining these proposals, let's write a rough gist of current C#6 overload resolution algorithm, or at least those parts of it that are relevant. These steps are performed only after ruling out inapplicable candidates, and only after susbstituing in all generic type arguments.

1. If the arguments [exactly match](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#exactly-matching-expression) one candidate but not the other, then the exactly-matching candidate wins. An async lambda `async () => 3` is considered an exact match for a delegate with return type `Task<int>`.
2. Otherwise, if neither is an exact match and there's an implicit conversion from one type to the other but not vice versa, then the "from" parameter wins as a [better conversion target](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-target). Otherwise recursively dig in: if both parameters are delegates then dig into their return types and also prefer non-void over void; if both parameters are `Task<T>` then dig into `T`.
3. Otherwise, if the two candidates have identical parameter types but one candidate *before substitution* is more specific then prefer it.

Let's informally rewrite the proposals:

__[Option "IC"]__: Don't change overload resolution; instead rely on a user-defined implicit conversion from `ValueTask` to `Task`. 

1. If the arguments exactly match one candidate, it wins. An async lambda `async () => 3` is considered an exact match for a delegate with return type `Task<int>` ***and any other `Tasklike<int>`***. *(We need this small tweak because, without it, the implicit conversion never even gets a look-in.)*
2. Otherwise, if neither is an exact match and there's an implicit conversion from one parameter type but not vice versa, then the "from" parameter wins as a [better conversion target](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-target). Otherwise recursively dig in: if both parameters are delegates then dig into their return types and also prefer non-void over void; if both parameters are `Task<T>` then dig into `T`.
3. Otherwise, if the two candidates have identical parameter types but one candidate before substitution is more specific then prefer it.

__[Option "E"]__: Make overload resolution treat tasklikes equivalently as it treats `Task` today, but build in a preference for `Task` over `ValueTask` to preserve back-compat.

1. If the arguments exactly match one candidate, it wins. An async lambda `async () => 3` is considered an exact match for a a delegate with return type `Task<int>` ***and any other `Tasklike<int>`.***
2. Otherwise, if neither is an exact match and there's an implicit conversion from one type to the other but not vice versa, then the "from" parameter wins. Otherwise recursively dig in: if both parameters are delegates then dig into their return types and prefer non-void over void; if both parameters are `Task<T>` then dig into `T`; ***if both parameters are the same `TasklikeA<T>` then dig into `T`.***
3. ***Otherwise, if one candidate converted an async lambda to a task but the other converted it to a tasklike, the first candidate wins. This only applies to tasklikes in the unexpanded parameter types.***
4. Otherwise, if the two candidates have identical parameter types ***up to all tasklikes being considered the same*** but one candidate before substitution is more specific then prefer it.


# Unit tests

***[TODO: I had misunderstood the rules for "exact match". The above text is now correct. I need to go back and revise the following table.]***

***[TODO: I don't understand the "exact match" rules in the spec. How is the "inferred return type" subclause any different from the bullet that follows it?]***

I will compare the options for overload resolution against a load of "language-design unit tests": (a) I can use `ValueTask` every bit as good as `Task` for a wholesale replacement; (b) I can incrementally migrate my API over to `ValueTask`; (c) I don't want to break back-compat. I've written 28 unit tests in total, but only 13 are interested and I've summarized them in the table below. Conclusions:

* *The option "IC: don't change overload resolution; instead rely on a user-defined implicit conversion from `ValueTask` to `Task`" is best.*
* Some of the important criteria (a5,a8,b7,b7n) conflict with maintaining 100% back-compat (c3,c3n,c4,c4n). **I think we should esteem (a,b) higher** since they are mainstream scenarios; the back-compat ones are niche.
* A useful scenario (b2) is to add an overload which differs only in return type. This is currently disallowed in C#. **I think we should add a new modreq `hidden void f()`** with the meaning that this method is emitted in IL and is allowed to differ only in return-type (to maintain binary compatibility) but will never be seen by the C# compiler. However, this issue is orthogonal.
* An user-defined implicit conversion from `ValueTask` to `Task` is needed for (b7,b7n) APIs to offer both `Func<Task>` and `Func<ValueTask>` overloads and prefer the more efficient `ValueTask`; an implicit conversion in the reverse direction will break this scenario and also the important (a5) of allowing a method to accept both sync and async lambdas.
* There are some niche cases of digging in to prefer one mediocre candidate over another mediocre candidate. It's not worth adding rules for these cases: having betterness dig into tasklikes as well as task brings a tiny benefit (a7), and having async lambdas prefer task over other tasklies brings a tiny benefit (c2)

For sake of this table, I've added another option "0" which is to use overload resolution exactly as-is, without even the tasklike tweak to the "exact match" criterion. And I've shown where the idea of a modreq `hidden` would help binary-compatibility.

![comparison](https://raw.githubusercontent.com/ljw1004/roslyn/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns%20-%20comparison%20table.png)


## I should be able to use `ValueTask` as a wholesale replacement for `Task`, every bit as good.

__TEST a1:__ async methods should be able to return `ValueTask`

```csharp
async Task<int> a1() { ... }         //  <-- I can write this in C#6 using Task
async ValueTask<int> a1() { ... }    //  <-- I should be able to write this instead with the same method body
```

__TEST a2:__ async lambdas should be able to return `ValueTask`

```csharp
Func<Task<int>> a2 = async () => { ... };       //  <-- I can write this in C#6
Func<ValueTask<int>> a2 = async () => { ... };  //  <-- I should be able to write this instead
```

__TEST a3:__ async lambdas are applicable in overload resolution

```csharp
a3(async () => 3);
void a3(Func<Task<int>> lambda)       //  <-- This can be invoked in C#6
void a3(Func<ValueTask<int>> lambda)  //  <-- If I write this instead, it should be invokable
```

__TEST a4:__ async lambda type inference should work with `ValueTask` like it does with `Task`

```csharp
a4(async () => 3);
void a4<T>(Func<Task<T>> lambda)       //  <-- This infers T=int
void a4<T>(Func<ValueTask<T>> lambda)  //  <-- If I write this instead, it should also infer T=int
```

__TEST a5:__ able to write overloads that take sync and async lambdas

```csharp
void a5<T>(Func<T> lambda)
void a5<T>(Func<ValueTask<T>> lambda)
a5(() => 3);                             //  <-- This should invoke the first overload
a5(async () => 3);                       //  <-- This should invoke the second overload
```

__TEST a6:__ exact match better candidate

```csharp
void a6(Func<ValueTask<int>> lambda)
void a6(Func<ValueTask<double>> lambda)
a6(async () => 3);                       //  <-- This should prefer the "int" candidate
```

__TEST a7:__ dig into better candidate

```csharp
void a7(Func<ValueTask<short>> lambda)
void a7(Func<ValueTask<byte>> lambda)
a7(async () => 3);                       //  <-- This should prefer the "byte" candidate
```

__TEST a8:__ prefer over void

```csharp
void a8(Action lambda)
void a8(Func<ValueTask> lambda)
a8(async () => {});                       // <-- This should prefer the "ValueTask" candidate
```

## I should be able to migrate my existing API over to `ValueTask`

As I migrate, I want to maintaining source-compatibility and binary-compatibility for users of my API. And the reason I'm migrating is because I want the better performance of `ValueTask`, so I want users to get that as easily as possible.

__TEST b1:__ change async return type to be `ValueTask`

```csharp
async Task<int> b1()         //  <-- library v1 has this API
async ValueTask<int> b1()    //  <-- library v2 has this API *instead*
await v = b1();              //  <-- This code will of course work in v2 of the library
Task<int> t = b1();          //  <-- Either this code should work in v2 of the library...
Task<int> t = b1().AsTask(); //  <-- or this one as a workaround

async Task b1n()             //  <-- library v1 has this API
async ValueTask b1n()        //  <-- library v2 has this API *instead*
await v = b1n();      //  <-- This code will of course work in v2 of the library
Task t= b1n();               //  <-- Either this could should work in v2 of the library...
Task t = b1n().AsTask();     //  <-- or this one as a workaround
```

__TEST b2:__ add overload where async return type is `ValueTask`. ***[this test is doomed to fail]***

```csharp
async Task<int> b2()       //  <-- library v1 has this API
async ValueTask<int> b2()  //  <-- library v2 has this API *additionally*
var t = b2();              //  <-- This code should work on either version of the library

async Task b2n()           //  <-- library v1 has this API
async ValueTask b2n()      //  <-- library v2 has this API *additionally*
var t = b2n();             //  <-- This code should work on either version of the library
```

__TEST b3:__ change argument to `ValueTask`

```csharp
void b3(Task<int> t)       //  <-- library has this API
ValueTask<int> vt;
b3(vt);                    //  <-- This code should work...
b3(vt.AsTask());           //  <-- or, if not, then at least this one should

void b3n(Task t)           //  <-- library has this API
ValueTask vt;
b3n(vt)                    //  <-- This code should work...
b3n(vt.AsTask());          //  <-- or, if not, then at least this one should
```

__TEST b4:__ change parameter to `ValueTask`

```csharp
void b4(Task<int> t)         //  <-- library v1 has this API
void b4(ValueTask<int> t)    //  <-- library v2 has this API *instead*
Task<int> t;
b4(t);                       //  <-- Either this code should work in v2 of the library...
b4(t.AsValueTask());         //  <-- or this one as a workaround

void b4n(Task t)             //  <-- library v1 has this API
void b4n(ValueTask t)        //  <-- library v2 has this API instead
Task t;
b4n(t);                      //  <-- Either this code should work in v2 of the library...
b4n(t.AsValueTask());        //  <-- or this one as a workaround
```

__TEST b5:__ add overload with parameter `ValueTask`

```csharp
void b5(Task<int> t)       //  <-- library v1 has this API
void b5(ValueTask<int> t)  //  <-- library v2 has this API *additionally*
Task<int> t;
b5(t);                     //  <-- This could should work in v2 of the library and pick the Task overload
ValueTask<int> vt;
b5(vt);                    //  <-- This code should work in v2 of the library and pick ValueTask overload

void b5n(Task t)           //  <-- library v1 has this API
void b5n(ValueTask t)      //  <-- library v2 has this API *additionally*
Task t;
b5n(t);                    //  <-- This code should work in v2 of the library and pick the Task overload
ValueTask vt;
b5n(vt);                   //  <-- This code should work in v2 of the library and pick ValueTask overload
```

__TEST b6:__ change parameter to `Func<ValueTask<T>>`

```csharp
void b6(Func<Task<int>> lambda)      //  <-- library v1 has this API
void b6(Func<ValueTask<int>> lambda) //  <-- library v2 has this API *instead*
b6(async () => 3);                   //  <-- This code should work in v2 of the library

void b6n(Func<Task> lambda)          //  <-- library v1 has this API
void b6n(Func<ValueTask> lambda)     //  <-- library v2 has this API *instead*
b6n(async () => {});                 //  <-- This code should work in v2 of the library
```

__TEST b7:__ add overload with parameter `Func<ValueTask<T>>`

```csharp
void b7(Func<Task<int>> lambda)      //  <-- library v1 has this API
void b7(Func<ValueTask<int>> lambda) //  <-- library v2 has this API *additionally*
b7(async () => 3);                   //  <-- This code should work in v2 and pick the ValueTask overload, for efficiency

void b7n(Func<Task> lambda)          //  <-- library v1 has this API
void b7n(Func<ValueTask> lambda)     //  <-- library v2 has this API *additionally*
b7n(async () => {});                 //  <-- This code should work in v2 and pick the ValueTask overload, for efficiency
```

## I don't want to break backwards-compatibility.

In particular, suppose I have a C#6 app that references a NuGet library in which `ValueTask` is already tasklike. When I upgrade my project to C#7, I don't want the behavior of my code to change.

__TEST c1:__ don't now prefer a previously-inapplicable `ValueTask` due to exact match

```csharp
void c1(Func<Task<double>> lambda)
void c1(Func<ValueTask<int>> lambda)
c1(async () => 3);                    //  <-- When I upgrade, this should still pick the Task overload
```

__TEST c2:__ don't now prefer a previously-inapplicable `ValueTask` due to digging in

```csharp
void c2(Func<Task<short>> lambda)
void c2(Func<ValueTask<byte>> lambda)
c2(async () => 3);                    //  <-- When I upgrade, this should still pick the Task overload
```

__TEST c3:__ don't introduce ambiguity errors about newly applicable candidates [conflicts with [Test b7](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-migrate-my-existing-api-over-to-valuetask)]

```csharp
void c3(Func<Task<int>> lambda)
void c3(Func<ValueTask<int>> lambda)
c3(async () => 3);                    //  <-- When I upgrade, this should still pick the Task overload

void c3n(Func<Task> lambda)
void c3n(Func<ValueTask> lambda)
c3n(async () => {});                  //  <-- when I upgrade, this should still pick the Task overload
```

__TEST c4:__ don't now prefer a previously-inapplicable ValueTask due to tie-breakers [conflicts with [Test a5](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-use-valuetask-as-a-wholesale-replacement-for-task-every-bit-as-good)]

```csharp
void c4<T>(Func<T> lambda)
void c4<T>(Func<ValueTask<T>> lambda)
c4(async () => 3);                     //  <-- When I upgrade, this should still pick the "T" overload

void c4n(Action lambda)
void c4n(Func<ValueTask> lambda)
c4n(async () => {});                   //  <-- When I upgrade, this should still pick the Action overload
```


# Design rationale and alternatives

For explanation of why the proposal is this way, and to see alternatives, please read the [Design rationale and alternatives](feature - arbitrary async returns - discussion.md).
