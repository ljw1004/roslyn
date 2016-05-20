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
* Other teams decide that the cumbersome code is so ugly they're willing to forego the perf benefits for now, at least until the C# compiler gets support for `ValueTask<T>` -- e.g. ASP.NET FormReader ([aspnet#556](https://github.com/aspnet/HttpAbstractions/pull/556#issuecomment-199974553)). Using `ValueTask<T>` would have gotten memory down from 440MB to 0.5MB in the test reported in that thread.

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

We have two different options on the table for overload resolution:

1. Make overload resolution treat tasklikes the same as it treats `Task` today. But to avoid back-compat-breaks, prefer candidates which don't involve converting an async lambda to a non-Task-returning delegate parameter. 
2. Don't change overload resolution. Instead rely upon `ValueTask` having an implicit conversion to `Task`.

Neither option is perfect. We'll rank how well each option satisfies the unit tests. ***TODO: fill out this table...***

|      | Option1 | Option1 with VT->T | Option1 with VT<-T | Option1 with VT<->T | Option2 with VT->T |
|------|---------|--------------------|--------------------|---------------------|--------------------|
| [Is VT as good as T?](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-use-valuetask-as-a-wholesale-replacement-for-task-every-bit-as-good) | ? | ? | ? | ? | ? |
| [Can I migrate from T to VT?](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-migrate-my-existing-api-over-to-valuetask) | ? | ? | ? | ? | ? |
| [Is back-compat okay?](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-dont-want-to-break-backwards-compatibility) | ? | ? | ? | ? | ? |

### Overload resolution option 1: treat tasklikes same as `Task`

__Rule 5a: overload resolution betterness.__ The overload resolution rules for [better conversion from expression](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-from-expression) currently list two things that make the implicit conversion `CP` from `Ex` to `Px` better than the implicit conversion `CQ` from `Ex` to `Qx`. We will add a third: if `Ex` is an async lambda, and the unexpanded parameter type `Rx` of `Px` is a delegate whose return type is `Task` or `Task<T>`, and the unexpanded parameter type `Sx` of `Qx` is not, then `CP` is a better conversion than `CQ`.

```csharp
f(async () => 3);
void f(Func<Task<int>> lambda)       // prefers this candidate
void f(Func<ValueTask<int>> lambda)
```

__Rule 5b: overload resolution tie-breakers.__ The overload resolution rules for [better function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-function-member) currently say that if neither candidate is better, and also the two applicable candidates have identical parameter types `{P1...Pn}` and `{Q1...Qn}` then we attempt  tie-breakers to determine which is the better one, otherwise it is an ambiguity error. With this feature, this will be modified so that if neither candidate is better and also the parameter types are identical *"up to tasklikes"* then attempt the tie-breakers: more precisely, for purposes of this identity comparison, all non-generic `Tasklike`s are deemed identical to each other, and all generic `Tasklike<T>`s for a given `T` are deemed identical to each other.

```csharp
f(async () => 3); 
void f<T>(Func<T> lambda)             // infers T = Task<int> [as it always has], and is applicable
void f<T>(Func<ValueTask<T>> lambda)  // infers T = int [under rule 4 of the proposal], and is applicable
// With rule 5b, both candidates are identical up-to-tasklikes, and the second is more specific
```

__Rule 5c: overload resolution betterness.__ The overload resolution rules currently "dig into" `Task<T>` for determining betterness... In [Exactly matching expression](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#exactly-matching-expression) it says that an async lambda *exactly matches* a delegate with return type `Task<Y>` if its return statement operands exactly match `Y`; this will be amended to say it exactly matches a delegate with return type `Tasklike<Y>`. Likewise the rules for [Better conversion target](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-conversion-target) say that `Task<S1>` is a better conversion target than `Task<S2>` (written `Task<S1> > Task<S2>`) if `S1 > S2`; this will be amended to say that `TasklikeA<S1> > TaslikeB<S2>` if `S1 > S2`.

```csharp
f(async () => 3);
void f(Func<MyTask<double>> lambda)     
void f(Func<ValueTask<int>> lambda)     // better
```


### Overload resolution option 2: rely on implicit conversion `ValueTask` to `Task`

We trust that library authors write `ValueTask<T>` with an implicit conversion to `Task<T>`, and make no changes to overload resolution.

```csharp
f(async () => 3);
void f(Func<Task<int>> lambda)       
void f(Func<ValueTask<int>> lambda)  // prefers this candidate because `ValueTask<T>` is a better conversion target than Task<T>

g(async () => 3); 
void g<T>(Func<T> lambda)             // infers T = Task<int> [as it always has], and is applicable
void g<T>(Func<ValueTask<T>> lambda)  // infers T = int, and is applicable, and is a better conversion target

h(async () => 3);
void h(Func<Task<double>> lambda)     // applicable
void h(Func<ValueTask<int>> lambda)   // applicable; both are applicable, so it's an ambiguity error
```



# Unit tests

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

__TEST a6:__ able to dig in to better candidate
```csharp
void a6(Func<ValueTask<int>> lambda)
void a6(Func<ValueTask<double>> lambda)
a6(async () => 3);                       //  <-- This should also prefer the "int" candidate
```


## I should be able to migrate my existing API over to `ValueTask`

As I migrate, I want to maintaining source-compatibility and binary-compatibility for users of my API. And the reason I'm migrating is because I want the better performance of `ValueTask`, so I want users to get that as easily as possible.

__TEST b1:__ change async return type to be `ValueTask`
```csharp
async Task<int> b1()         //  <-- library v1 has this API
async ValueTask<int> b1()    //  <-- library v2 has this API *instead*
var v = b1();                //  <-- This code will of course work in v2 of the library
Task<int> t = b1();          //  <-- Either this code should work in v2 of the library...
Task<int> t = b1().AsTask(); //  <-- or this one as a workaround
```

__TEST b2:__ add overload where async return type is `ValueTask`. ***[this test is doomed to fail]***
```csharp
async Task<int> b2()       //  <-- library v1 has this API
async ValueTask<int> b2()  //  <-- library v2 has this API *additionally*
var t = b2();              //  <-- This code should work on either version of the library
```

__TEST b3:__ change argument to `ValueTask`
```csharp
void b3(Task<int> t)       //  <-- library has this API
ValueTask<int> vt;
b3(vt);                    //  <-- This code should work...
b3(vt.AsTask());           //  <-- or, if not, then at least this one should
```

__TEST b4:__ change parameter to `ValueTask`
```csharp
void b4(Task<int> t)         //  <-- library v1 has this API
void b4(ValueTask<int> t)    //  <-- library v2 has this API *instead*
Task<int> t;
b4(t);                       //  <-- Either this code should work in v2 of the library...
b4(t.AsValueTask());         //  <-- or this one as a workaround
```

__TEST b5:__ add overload with parameter `ValueTask`
```csharp
void b5(Task<int> t)         //  <-- library v1 has this API
void b5(ValueTask<int> t)    //  <-- library v2 has this API *additionally*
Task<int> t;
b5(t);                       //  <-- This could should work in v2 of the library and pick the Task overload
ValueTask<int> vt;
b5(vt);                      //  <-- This code should work in v2 of the library and pick ValueTask overload
```

__TEST b6:__ change parameter to `Func<ValueTask>`
```csharp
void b6(Func<Task<int>> lambda)      //  <-- library v1 has this API
void b6(Func<ValueTask<int>> lambda) //  <-- library v2 has this API *instead*
b6(async () => 3);                   //  <-- This code should work in v2 of the library
```

__TEST b7:__ add overload with parameter `Func<ValueTask>`
```csharp
void b7(Func<Task<int>> lambda)      //  <-- library v1 has this API
void b7(Func<ValueTask<int>> lambda) //  <-- library v2 has this API *additionally*
b7(async () => 3);                   //  <-- This code should work in v2 and pick the ValueTask overload, for efficiency
```

## I don't want to break backwards-compatibility.

In particular, suppose I have a C#6 app that references a NuGet library in which `ValueTask` is already tasklike. When I upgrade my to C#7, I don't want the behavior of my code to change.

__TEST c1:__ don't now prefer a previously-inapplicable `ValueTask` due to digging in
```csharp
void c1(Func<Task<double>> lambda)
void c1(Func<ValueTask<int>> lambda)
c1(async () => 3);                    //  <-- When I upgrade, this should still pick the Task overload
```

__TEST c2:__ don't introduce ambiguity errors about newly applicable candidates [conflicts with [Test b7](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-migrate-my-existing-api-over-to-valuetask)]
```csharp
void c2(Func<Task<int>> lambda)
void c2(Func<ValueTask<int>> lambda)
c2(async () => 3);                    //  <-- When I upgrade, this should still pick the Task overload
```

__TEST c3:__ don't now prefer a previously-inapplicable ValueTask due to tie-breakers [conflicts with [Test a5](https://github.com/ljw1004/roslyn/blob/features/async-return/docs/specs/feature%20-%20arbitrary%20async%20returns.md#i-should-be-able-to-use-valuetask-as-a-wholesale-replacement-for-task-every-bit-as-good)]
```csharp
void c3<T>(Func<T> lambda)
void c3<T>(Func<ValueTask<T>> lambda)
c3(async () => 3);                     //  <-- When I upgrade, this should still pick the first overload
```


# Design rationale and alternatives

For explanation of why the proposal is this way, and to see alternatives, please read the [Design rationale and alternatives](feature - arbitrary async returns - discussion.md).
