# C# feature proposal: arbitrary async returns

In C#6, methods must return either `void` or `Task` or `Task<T>`. This proposed feature allows them to return any *tasklike* type.

----------------------------------
* **Download:** [ArbitraryAsyncReturns.zip](https://github.com/ljw1004/roslyn/raw/features/async-return/ArbitraryAsyncReturns.zip) [22mb]
* **Install:** Unzip the file. Quit VS. Double-click to install in this order: (1) Roslyn.VisualStudio.Setup.vsix, (2) Roslyn.Compilers.Extension.vsix, (3) ExpressionEvaluatorPackage.vsix. I don't think the others are needed.
* **Test:** the zip file contains a sample project
* **Uninstall:** I've usually been able to go to within Visual Studio to Tools>Extensions, search for Roslyn, and uninstall in the order (1) Expression Evaluators, (2) Compilers, (3) Language Service. Once that resulted in a broken VS that was unable to load C# projects, and I unbroke it by deleting the entire folder `%userprofile%\AppData\Roaming\Microsoft\VisualStudio` and `%userprofile%\AppData\Local\Microsoft\VisualStudio`. Doing so reset all my VS settings to default.
* **Watch:** I coded this prototype live in livecoding.tv, and you can watch recordings if you want: [livecoding.tv/ljw1004](https://www.livecoding.tv/ljw1004/)
* **Discuss:** please go to the [discussion thread](...)
----------------------------------

The primary benefit is to allow a `ValueTask<T>` that reduces the "async overhead cost" on the hot path:
![perf](feature - arbitrary async returns.png)
* Such a `ValueTask<T>` has already been checked into corefx ([corefx#4857](https://github.com/dotnet/corefx/pull/4857))
* Some teams decide the perf benefits of `ValueTask<T>` are so great that they're worth the cumbersome manual code today -- e.g. `System.Xml` ([corefx#4936](https://github.com/dotnet/corefx/pull/4936)), which had previously been using an internal form of it.
* Other teams decide that the cumbersome code is so ugly they're willing to forego the perf benefits for now, at least until the C# compiler gets support for `ValueTask<T>` -- e.g. ASP.NET FormReader ([aspnet#556](https://github.com/aspnet/HttpAbstractions/pull/556#issuecomment-199974553)). Using `ValueTask<T>` would have gotten memory down from 440MB to 0.5MB.
* I believe most folks will continue to use `Task<T>` most of the time, just like they don't bother with `.ConfigureAwait(false)` most of the time. That's because `Task` has so many nice combinators.

> (Could we build upon ValueTask-returning asyncs with additional compiler unrolling, to achieve the optimum perf? The unrolled version has different semantics around context-capture so it seems doubtful [issue#10449](https://github.com/dotnet/roslyn/issues/10449). In any case, the huge perf win comes from eliminating the heap allocation, and further perf improvements are negligble.)



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
Actually that `IObservable` example is still up for discussion. It's not clear whether `IObservable` would prefer to be like an async method or an async enumerable method.


A third "benefit" (it's arguable whether this is a benefit at all) is that people will be able to weave hooks into their async methods, for instance to call into their own function before and after every cold-path await:
```csharp
async InstrumentedTask TestAsync()
{
    await InstrumentedTask.Configure(actionBeforeEachAwait, actionAfterEachAwait);
    await Task.Delay(10);
	await Task.Delay(20);
}
```


# Proposal

A *non-generic tasklike* is any non-generic type with the attribute `[System.Runtime.CompilerServices.Tasklike(typeof(...))]` on it, or the type `System.Threading.Tasks.Task`.

A *generic tasklike* is any generic type with arity 1 with the same attribute, or the type `System.Threading.Tasks.Task<T>`.

The rules for [async functions](https://github.com/ljw1004/csharpspec/blob/gh-pages/classes.md#async-functions) currently allow an async method to return either `void` or `Task` or `Task<T>`; this will be changed to allow it to return any nong-generic `Tasklike` or generic `Tasklike<T>`.

The rules for [evaluation of task-returning async functions](https://github.com/ljw1004/csharpspec/blob/gh-pages/classes.md#evaluation-of-a-task-returning-async-function) currently talk in general terms about "generating an instance of the returned task type" and "initially incomplete state" and "moved out of the incomplete state". These will be changed to spell out how that returned tasklike is constructed and how its state is transitioned, as detailed below.

The rules for [anonymous function conversion](https://github.com/ljw1004/csharpspec/blob/gh-pages/conversions.md#anonymous-function-conversions) currently allow an async lambda to be converted to a delegate type whose return type is either `void` or `Task` or `Task<T>`; this will be changed to let them return any non-generic `Tasklike` or generic `Tasklike<T>`.

The [inferred return type](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#inferred-return-type) of a lambda expression currently takes into account the parameter types of the delegate to which the lambda is being converted. With this feature, it now also takes into account the return type of that delegate: if the lambda is async, and the return type of the delegate is `U<T>` where `U` is a tasklike, then the inferred return type is `U<InferredResultType>`; otherwise it remains `Task<InferredResultType>`.

The overload resolution rules for [better function member](https://github.com/ljw1004/csharpspec/blob/gh-pages/expressions.md#better-function-member) currently say that if two applicable candidates have identical parameter types `{P1...Pn}` and `{Q1...Qn}` then we use tie-breakers to determine which is the better one. With this feature, this is modified to use tie-breakers if the parameter types *are identical up to tasklikes*: for purposes of this identity comparison, all non-generic `Tasklike`s are deemed identical to each other, and all generic `Tasklike<T>`s for a given `T` are deemed identical to each other.

**Semantics for execution of an async method**

Define the *builder type* of a tasklike as follows:
* For non-generic `Tasklike` with attribute `[Tasklike(typeof(Builder))]`, the builder type is `Builder`.
* For generic `Tasklike<T>` with attribute `[Tasklike(typeof(Builder<>))]`, the builder type is `Builder<T>`.
* If a non-generic tasklike lacks the attribute and is `System.Threading.Tasks.Task`, the builder type is `System.Runtime.CompilerService.AsyncTaskMethodBuilder`
* If a generic tasklike lacks the attribute and is `System.Threading.Tasks.Task<T>`, the builder type is `System.Runtime.CompilerServices.AsyncTaskMethodBuilder<T>`.
* The builder type must be public. It is an error if the builder's generic arity doesn't match the tasklike's generic arity.

When an async tasklike-returning method is invoked,
* It calls `var builder = BuilderType.Create()` to create a new instance of the builder type. It is an error if this static method doesn't exist or isn't public or doesn't return `BuilderType`.
* It then calls the `void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine` method on `builder`. It is an error if this instance method doesn't exist or isn't public or has a different signature or constraints. The `sm` variable is a mostly opaque type which represents the current state of execution of the body of the async method. Upon being given this `sm` variable, the builder must invoke `sm.MoveNext()` on it exactly once, either now in the `Start` method or in the future. 
* It then retrieves the `U Task {get;}` property on `builder`. It is an error if this instance property doesn't exist or isn't public or if its property type `U` isn't identical to the return type of the async tasklike-returning method.

Execution of `sm.MoveNext()` might cause other builder methods to be invoked:
* The first cold await might cause a copy of the builder (if `BuilderType` is a struct) or a copy of a reference to the builder (if `BuilderType` is a class) to be placed on the heap. In this case the builder's `void SetStateMachine(IAsyncStateMachine sm)` method is invoked. It is an error if this instance method doesn't exist or isn't public. (TODO: I'm not sure why it calls this!)
* If the async method completes succesfully, it invokes the builder's `void SetResult()` method (in case of a non-generic tasklike), or the builder's `void SetResult(T result)` method (in case of a generic tasklike). It is an error if this instance method doesn't exist or isn't public.
* If the async method fails with an exception, it invokes the builder's `void SetException(System.Exception ex)` method. It is an error if this instance method doesn't exist or isn't public.
* If the async method executes an `await e` operation, it invokes `var awaiter = e.GetAwaiter()`.
  * If this awaiter implements `ICriticalNotifyCompletion` and the `IsCompleted` property is false, then it calls the builder's `void AwaitUnsafeOnCompleted<TA,TSM>(ref TA awaiter, ref TSM sm) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine`. It is an error if this instance method doesn't exist or isn't public or has the wrong constraints. The builder is expected to call `awaiter.UnsafeOnCompleted(action)` with some `action` that will cause `sm.MoveNext()` to be invoked once.
  * If this awaiter implements `INotifyCompletion` and the `IsCompleted` property is false, then it calls the builder's `void AwaitOnCompleted<TA,TSM>(ref TA awaiter, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine`. It is an error if this instance method doesn't exist or isn't public or has the wrong constraints. Again the builder is expected to call `awaiter.OnCompleted(action)` similarly.

There's one final requirement:
* The Visual Studio IDE might invoke the method `builder.GetCompletionActions()`, where the return type must either implement `System.Threading.Tasks.Task` or be `Action[]`. It is an error if this instance method doesn't exist or doesn't have the right return type.
  * The IDE calls this in order to display an "async callstack".
  * The idea is that if someone had retrieved the property `var tasklike = builder.Task`, and then called `tasklike.OnCompleted(action)` or `tasklike.UnsafeOnCompleted(action)`, then the IDE needs to be able to get a list of all those `action`s which are still pending.
  * It's common for tasklike types to use a `System.Threading.Tasks.Task` under the hood. We don't have any way to extract the list of actions out of one, but the IDE does, and if you return an object of type `Task` then the IDE will use its techniques.
  * If you return `null` from this method, then the IDE will never be able to display async callstacks beyond the point of an async tasklike-returning method. This will make users disappointed.



# Discussion


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
Option2 is bad for the typical usecase: you shouldn't have to type out the attribute every single time you want to return `ValueTask`. I also don't think Option2 would work well for lambdas where you don't have the option to put an attribute.

Option3 is ugly. I don't think there's much benefit to allowing non-owners of types to do the work of making builders -- a builder is very closely tied to the type -- so the ugliness isn't merited.


## Discuss: genericity of tasklike and builder

**Question.** Why does the builder have to have exactly the same arity as its tasklike? Or: When the compiler does generic type inference with the argument `async()=>{return 3;}` being passed to a method `void f(Func<MyTask<T>> lambda)`, how does it go from `3` to `int` to `T = int` ?


Let's start with the traditional behavior for `Task`:
```csharp
void f(Func<Task<T>> lambda);
var xf = f(async () => {return 3;});
// Infers T = int
```

Under the proposal, it will work like this:
```csharp
void g(Func<MyTasklike<T>> lambda);
var xg = g(async () => {return 3;});
```
* Under the proposal, we required a generic builder `MyBuilder<T>`
* and we first inferred the *result type* `int` from the lambda. (Result type is the type of the return operand)
* and figured out that `T = int`
* and we happily called the `builder.SetResult(3)` method


But can we make it more general? Like this?
```csharp
void h(Func<MyPerverse<T>> lambda);
var xh = g(async () => {return 3;});
// Imagine if we tried to do something more general...
// class MyPerverseBuilder<U> {
//   public void SetResult(U value) { }
//   public MyPerverse<IEnumerable<U>> Task { get; }
// }
```
* Is there any way we can go from the inferred result type of the lambda `int`
* to see that the builder has a SetResult method that takes `U`,
* and therefore `U = int`,
* and therefore the builder was a `MyPerverseBuilder<int>`
* and therefore, by looking at its `Task` property, we get `T = IEnumerable<int>` ?


And how about this generalization?
```csharp
void k(Func<MyWeird<T>> lambda);
var xk = k(async () => {return 3;});
// As before, can we do something more general?
// class MyWeirdBuilder {
//   public void SetResult<U>(U value) { }
//   public MyWeird<string> Task { get; }
// }
```
* Is there any way we can go from the inferred result type of the lambda `int`
* to see that the builder has a SetResult method that takes `U`
* and therefore `U = int`
* but that doesn't inform us about the builder; instead the builder is just `MyWeirdBuilder`
* and therefore, by looking at its `Task` property, we get `T = string` ?
```

The two general things aren't possible: there's no way to even know which `SetResult` candidate methods to look at unless we've fixed the builder to one particular type.



## Discuss: async method interacts with the builder instance


## Discuss: async type inference

## Discuss: overload resolution



----------------------
TODO:


## Limitations

This scheme wouldn't be able to represent the WinRT types `IAsyncOperationWithProgress` or `IAsyncActionWithProgress`. It also wouldn't be able to represent the fact that WinRT async interfaces have a cancel method upon them. We might consider allowing the async method to access its own builder instance via some special keyword, e.g. `_thisasync.cancel.ThrowIfCancellationRequested()`, but that seems too hacky and I think it's not worth it.

## Compilation notes and edge cases

**Concrete tasklike**. The following kind of thing is conceptually impossible, because the compiler needs to know the *concrete* type of the tasklike that's being constructed (in order to construct it).
```cs
class C<T> where T : MyTasklike {
    async T f() { } // error: the return type must be a concrete type
}
```

**Incomplete builder: binding**. The compiler should recognize the following as an async method that doesn't need a return statement, and should bind it accordingly. There is nothing wrong with the `async` modifier nor the absence of a `return` keyword. The fact that `MyTasklike`'s builder doesn't fulfill the pattern is an error that comes later on: it doesn't prevent the compiler from binding method `f`.
```cs
class C { async MyTasklike f() { } }
[Tasklike(typeof(string))] class MyTasklike {}
```

**Wrong generic**. To be a tasklike, a type must (1) have a [Tasklike] attribute on it, (2) have arity 0 or 1. If it has the attribute but the wrong arity then it's not a Tasklike.
```cs
class C { async MyTasklike f() { } } // okay: return type has arity 0
class C { async MyTasklike<int> f() { return 1;} } // okay: return type has arity 1:int
class C { async MyTasklike<int> f() { return "s";} } // error: should return an int, not a string
class C { async MyTasklike<int,int> f() { } } // error
```

**Incomplete builder: codegen**. If the builder doesn't fulfill the pattern, well, that's an edge case. It should definitely give errors (like it does today e.g. if you have an async Task-returning method and target .NET4.0), but it doesn't matter to have high-quality errors (again it doesn't have high quality errors today). One unusual case of failed builder is where the builder has the wrong constraints on its generic type parameters. As far as I can tell, constraints aren't taken into account elsewhere in the compiler (the only other well known methods with generic parameters are below, and they're all in mscorlib, so no chance of them ever getting wrong)
```
System.Array.Empty
System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal__AddEventHandler_T
System_Runtime_InteropServices_WindowsRuntime_WindowsRuntimeMarshal__RemoveEventHandler_T
System_Activator__CreateInstance_T
System_Threading_Interlocked__CompareExchange_T
Microsoft_VisualBasic_CompilerServices_Conversions__ToGenericParameter_T_Object
```