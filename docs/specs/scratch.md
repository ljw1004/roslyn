# Overload resolution for ValueTask

```csharp
void f<T>(Func<T> lambda)
void f<T>(Func<Task<T>> lambda)
```
