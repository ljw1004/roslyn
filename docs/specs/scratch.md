# Overload resolution for ValueTask

```csharp
async ValueTask<int> GetValueAsync(int d)  // costs 80 bytes heap allocation even if d==0
{
   await Task.Delay(d);
   return 15;
}

// C#6
void f<T>(Func<Task<T>> lambda)
f(async () => 3);
// C#6 type inference digs into the async lambda and the delegate, infers Task<int> as the return type of the lambda,
// infers T = int


// C#6
void f<T>(Func<T> lambda)
void f<T>(Func<Task<T>> lambda)
f( () => 3 );  // it picks the first overload, obviously

f( async () => 3 );  // it picks the second overload. But why??? -- because 2nd overload is "more specific"
[1] infers T=Task<int> to give candidate  void f(Func<Task<int>> lambda)
[2] infers T=int       to give candidate  void f(Func<Task<int>> lambda)
// C#6 says: if two candidates are identical, but one has "more specific generic type parameters", then pick it
// More specific means: you have to dig in deeper to find where the "T" was


// C#7
void f<T>(Func<T> lambda)
void f<T>(Func<ValueTask<T>> lambda)
f( async () => 3 );

[1] infers T=Task<int> to give candidate  void f(Func<Task<int>> lambda)
[2] infers T=int       to give candidate  void f(Func<ValueTask<int>> lambda)
// This will give an AMBIGUITY ERROR unless we add some new overload resolution rules of some sort






// HOW DOES C#6 TYPE INFERENCE WORK?

// STEP 1: look for an "exact match candidate"
g(3)            void g(int i)    // this is an exact match because the argument type exactly matches param type
g(() => 3)      void g(Func<int> lambda)   // this is an exact match because "3" exactly matches "int" (it digs into lambdas)
g(() => 3)      void g(Func<double) lam)   // NOT an exact match
g(async()=>3)   void g(Func<Task<int>> l)  // this is an exact match: it digs into delegate-return and into Task<T>
g(async()=>3)   void g(FUnc<Task<double>> l)  // NOT an exact match
g(async()=>3)   void g(Func<IEnumerable<int>> l) // NOT an exact match because "async lambda" only matches 'Task<T>" return type


// STEP 2: failing that, look for a "better conversion target"
g(1)      void g(double d)   void g(float f)  // implicit conversion from float=>double, therefore g(float f) is a better target
g(()=>3)  void g(Func<double>)  void g(Func<float>)  // digs in; implicit conversion float>double means g(Func<float>) wins
g(()=>3)  void g(Func<double>)  void g(Action)       // digs in, prefers non-void-returning delegate, so g(Func<double>) wins
g(async()=>3)  void g(Func<Task<double>>)   void g(Func<Task<float>>)   // digs into delegate and Task, Func<Task<float>> wins
g(async()=>3)  void g(Func<Task<double>>)   void g(Func<Task>)          // I think the second one isn't even applicable

// STEP 3: failing that, look for a "more specific" candidate
g(async()=>3)  void g<T>(Func<T>)   void g<T>(Func<Task<T>>)   // Func<Task<T>> wins because it's more specific




// CHALLENGE FOR TODAY'S LIVECODING
// Given that we want C#7 to allow a new conversion from async lambda "async () => ..." to Func<ValueTask<T>>,
// every new conversion that's added to the language requires us to think very carefully about overload resolution:
//
// Figure out overload resolution rules so that ValueTask<T> works every bit as well as Task<T> for overload resolution
//    (e.g. when you're passing in an async lambda to a set of overloads)
// Figure out overload resolution rules so that I can introduce ValueTask<T> overloads into my API gently+gracefully
//    (e.g. I've already shipped Json.Net with some Task overloads, and I want to add ValueTask overloads as well)
// Figure out overload resolution rules so the new feature DOESN'T BREAK BACK-COMPAT
//    (e.g. I code in C#6 but reference System.Threading.Tasks.Extension NuGet package which contains ValueTask,
//     and my C#6 code isn't aware that async lambdas can convert to Func<ValueTask>, and I write some overloads
//     that happen to work because of this inability, and then I upgrade my own project to C#7, and now suddenly
//     the C#7 compiler is able to convert async lambdas to Func<ValueTask>, and that changes overload resolution
//     in my existing code. WHICH IS A BACK-COMPAT BREAK.)




// return type based overload resolution?
Task<int> f() {}   // in v1 of the library
ValueTask<int> f() {}   // v2 of the library adds thsi
var x = f();   // how could "return-type-based-overload-resolution" ever know which one to pick?
await f();     // ditto...







// callumc diversion: you want a way to write MAIN:: or ::MAIN or [[Main]] to indicate that the compiler should only lookup namespaces when resulving the word "Main", rather than (as it currently does) looking up types+namespaces

Timer.Foo();   // in winforms app resolves to System.Windows.Forms.Timer
// but in WPF app it is ambiguous between System.Threading.Timer and System.Windows.Forms.Timer
// QUANTUM SUPERPOSITION NAME LOOKUP: let's simultaneoulsy lookup the identifier "Main" as both a type AND a namespace, and when we dot off it, maybe that will disambiguate which one we really meant.

```


# Testing the options for overload resolution

```csharp
// a5. able to write overloads that take sync and async lambdas
void a5<T>(Func<T> lambda)               // expands to "void a5(Func<Task<int>> lambda)
void a5<T>(Func<ValueTask<T>> lambda)    // expands to "void a5(Func<ValueTask<int>> lambda)
a5(async () => 3);                       //  <-- This should invoke the second overload

// OPTION1:
1. exact match. Is "async () => 3" an exact match
for Func<Task<int>> ? -- yes
for Func<ValueTask<int>> ? -- yes, because of the new rule "and for any other Tasklike<int>"
2. Implicit conversion ValueTask<int> -> Task<int> or vice versa?
  if VT->T then prefers Func<ValueTask<T>>, so second candidate wins as desired
  if T<-VT then prefers Func<Task<T>>, XXX which isn't what we want!
  otherwise falls through
3. Neither had a conversion to Task-returning delegate in unexpanded paramter type
4. THey are identical up to taskliness, and ValueTask<T> is more specific than T, so second candidate wins as desired.


// a6. Exact match better candidate
void a6(Func<ValueTask<int>> lambda)
void a6(Func<ValueTask<double>> lambda)
a6(async () => 3);                       //  <-- This should prefer the "int" candidate

// OPTION 1:
it works fine!

// a7. Dig into better candidate
void a7(Func<ValueTask<short>> lambda)
void a7(Func<ValueTask<byte>> lambda)
a7(async () => 3);                       //  <-- This should prefer the "byte" candidate

// oPTION1:
// 1. Exact match? -- no
// 2. Implicit COnversion ValueTask<short> <- or -> ValueTask<int>? -- no
// 2b. Dig into ValueTask<short> vs ValueTask<byte>; dig into short vs byte. There is an IC byte->short, therefore
// the "byte" candidate wins as desired.

```
