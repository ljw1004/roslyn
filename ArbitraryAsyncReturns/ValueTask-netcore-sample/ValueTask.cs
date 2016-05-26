public struct ValueTask
{
    public static implicit operator ValueTask(System.Threading.Tasks.Task t) => new ValueTask(t);
    private static readonly System.Threading.Tasks.Task _doneTask = System.Threading.Tasks.Task.FromResult(0);
    internal readonly System.Threading.Tasks.Task _task;
    public ValueTask(System.Threading.Tasks.Task task) { _task = task; if (_task == null) throw new System.ArgumentNullException(nameof(task)); }
    public System.Threading.Tasks.Task AsTask() => _task ?? _doneTask;
    public static ValueTask<T> FromResult<T>(T result) => new ValueTask<T>(result);
    public static ValueTask<T> FromTask<T>(System.Threading.Tasks.Task<T> t) => new ValueTask<T>(t);
    public static ValueTask FromResult() => new ValueTask();
    public static ValueTask FromTask(System.Threading.Tasks.Task t) => new ValueTask(t);
    public override string ToString() => _task == null ? System.Threading.Tasks.TaskStatus.RanToCompletion.ToString() : _task.Status.ToString();
    public override int GetHashCode() => _task != null ? _task.GetHashCode() : 0;
    public override bool Equals(object obj) => obj is ValueTask && Equals((ValueTask)obj);
    public bool Equals(ValueTask other) => _task != null || other._task != null ? _task == other._task : true;
    public static bool operator ==(ValueTask left, ValueTask right) => left.Equals(right);
    public static bool operator !=(ValueTask left, ValueTask right) => !left.Equals(right);
    public bool IsCompleted => _task == null || _task.IsCompleted;
    public bool IsCompletedSuccessfully => _task == null || _task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion;
    public bool IsFaulted => _task != null && _task.IsFaulted;
    public bool IsCanceled => _task != null && _task.IsCanceled;
    public System.Runtime.CompilerServices.ValueTaskAwaiter2 GetAwaiter() => new System.Runtime.CompilerServices.ValueTaskAwaiter2(this);
    public System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable2 ConfigureAwait(bool continueOnCapturedContext) => new System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable2(this, continueOnCapturedContext: continueOnCapturedContext);
    public static System.Runtime.CompilerServices.ValueTaskMethodBuilder2 CreateAsyncMethodBuilder() => new System.Runtime.CompilerServices.ValueTaskMethodBuilder2();
}
public struct ValueTask<T>
{
    public static implicit operator ValueTask<T>(System.Threading.Tasks.Task<T> task) => new ValueTask<T>(task);
    public static implicit operator ValueTask<T>(T result) => new ValueTask<T>(result);
    internal readonly System.Threading.Tasks.Task<T> _task;
    internal readonly T _result;
    public ValueTask(System.Threading.Tasks.Task<T> task) { _task = task; _result = default(T); if (_task == null) throw new System.ArgumentNullException(nameof(task)); }
    public ValueTask(T result) { _task = null; _result = result; }
    public System.Threading.Tasks.Task<T> AsTask() => _task ?? System.Threading.Tasks.Task.FromResult(_result);
    public override string ToString() => _task == null ? _result.ToString() : _task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? _task.Result.ToString() : _task.Status.ToString();
    public override int GetHashCode() => _task != null ? _task.GetHashCode() : _result != null ? _result.GetHashCode() : 0;
    public override bool Equals(object obj) => obj is ValueTask<T> && Equals((ValueTask<T>)obj);
    public bool Equals(ValueTask<T> other) => _task != null || other._task != null ? _task == other._task : System.Collections.Generic.EqualityComparer<T>.Default.Equals(_result, other._result);
    public static bool operator ==(ValueTask<T> left, ValueTask<T> right) => left.Equals(right);
    public static bool operator !=(ValueTask<T> left, ValueTask<T> right) => !left.Equals(right);
    public bool IsCompleted => _task == null || _task.IsCompleted;
    public bool IsCompletedSuccessfully => _task == null || _task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion;
    public bool IsFaulted => _task != null && _task.IsFaulted;
    public bool IsCanceled => _task != null && _task.IsCanceled;
    public System.Runtime.CompilerServices.ValueTaskAwaiter2<T> GetAwaiter() => new System.Runtime.CompilerServices.ValueTaskAwaiter2<T>(this);
    public System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable2<T> ConfigureAwait(bool continueOnCapturedContext) => new System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable2<T>(this, continueOnCapturedContext: continueOnCapturedContext);
    public static System.Runtime.CompilerServices.ValueTaskMethodBuilder2<T> CreateAsyncMethodBuilder() => new System.Runtime.CompilerServices.ValueTaskMethodBuilder2<T>();
}
namespace System.Runtime.CompilerServices
{
    public struct ValueTaskMethodBuilder2
    {
        internal AsyncTaskMethodBuilder _taskBuilder; internal bool GotBuilder;
        internal bool GotResult;
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();
        public void SetStateMachine(IAsyncStateMachine stateMachine) { EnsureTaskBuilder(); _taskBuilder.SetStateMachine(stateMachine); }
        public void SetResult() { if (GotBuilder) _taskBuilder.SetResult(); GotResult = true; }
        public void SetException(Exception exception) { EnsureTaskBuilder(); _taskBuilder.SetException(exception); }
        private void EnsureTaskBuilder() { if (!GotBuilder && GotResult) throw new InvalidOperationException(); if (!GotBuilder) _taskBuilder = AsyncTaskMethodBuilder.Create(); GotBuilder = true; }
        public ValueTask Task { get { if (GotResult && !GotBuilder) return new ValueTask(); EnsureTaskBuilder(); return new ValueTask(_taskBuilder.Task); } }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { EnsureTaskBuilder(); _taskBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { EnsureTaskBuilder(); _taskBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine); }
    }
    public struct ValueTaskMethodBuilder2<T>
    {
        internal AsyncTaskMethodBuilder<T> _taskBuilder; internal bool GotBuilder;
        internal T _result; internal bool GotResult;
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();
        public void SetStateMachine(IAsyncStateMachine stateMachine) { EnsureTaskBuilder(); _taskBuilder.SetStateMachine(stateMachine); }
        public void SetResult(T result) { if (GotBuilder) _taskBuilder.SetResult(result); else _result = result; GotResult = true; }
        public void SetException(Exception exception) { EnsureTaskBuilder(); _taskBuilder.SetException(exception); }
        private void EnsureTaskBuilder() { if (!GotBuilder && GotResult) throw new InvalidOperationException(); if (!GotBuilder) _taskBuilder = AsyncTaskMethodBuilder<T>.Create(); GotBuilder = true; }
        public ValueTask<T> Task { get { if (GotResult && !GotBuilder) return new ValueTask<T>(_result); EnsureTaskBuilder(); return new ValueTask<T>(_taskBuilder.Task); } }
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine { EnsureTaskBuilder(); _taskBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine); }
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine) where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine { EnsureTaskBuilder(); _taskBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine); }
    }
    public struct ValueTaskAwaiter2<T> : ICriticalNotifyCompletion
    {
        private readonly ValueTask<T> _value;
        internal ValueTaskAwaiter2(ValueTask<T> value) { _value = value; }
        public bool IsCompleted => _value.IsCompleted;
        public T GetResult() => (_value._task == null) ? _value._result : _value._task.GetAwaiter().GetResult();
        public void OnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(continueOnCapturedContext: true).GetAwaiter().OnCompleted(continuation);
        public void UnsafeOnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(continueOnCapturedContext: true).GetAwaiter().UnsafeOnCompleted(continuation);
    }
    public struct ConfiguredValueTaskAwaitable2<T>
    {
        private readonly ValueTask<T> _value;
        private readonly bool _continueOnCapturedContext;
        internal ConfiguredValueTaskAwaitable2(ValueTask<T> value, bool continueOnCapturedContext) { _value = value; _continueOnCapturedContext = continueOnCapturedContext; }
        public ConfiguredValueTaskAwaiter2 GetAwaiter() => new ConfiguredValueTaskAwaiter2(_value, _continueOnCapturedContext);
        public struct ConfiguredValueTaskAwaiter2 : ICriticalNotifyCompletion
        {
            private readonly ValueTask<T> _value;
            private readonly bool _continueOnCapturedContext;
            internal ConfiguredValueTaskAwaiter2(ValueTask<T> value, bool continueOnCapturedContext) { _value = value; _continueOnCapturedContext = continueOnCapturedContext; }
            public bool IsCompleted => _value.IsCompleted;
            public T GetResult() => _value._task == null ? _value._result : _value._task.GetAwaiter().GetResult();
            public void OnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(_continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(_continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
        }
    }
    public struct ValueTaskAwaiter2 : ICriticalNotifyCompletion
    {
        private readonly ValueTask _value;
        internal ValueTaskAwaiter2(ValueTask value) { _value = value; }
        public bool IsCompleted => _value.IsCompleted;
        public void GetResult() => _value._task?.GetAwaiter().GetResult();
        public void OnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(continueOnCapturedContext: true).GetAwaiter().OnCompleted(continuation);
        public void UnsafeOnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(continueOnCapturedContext: true).GetAwaiter().UnsafeOnCompleted(continuation);
    }
    public struct ConfiguredValueTaskAwaitable2
    {
        private readonly ValueTask _value;
        private readonly bool _continueOnCapturedContext;
        internal ConfiguredValueTaskAwaitable2(ValueTask value, bool continueOnCapturedContext) { _value = value; _continueOnCapturedContext = continueOnCapturedContext; }
        public ConfiguredValueTaskAwaiter2 GetAwaiter() => new ConfiguredValueTaskAwaiter2(_value, _continueOnCapturedContext);
        public struct ConfiguredValueTaskAwaiter2 : ICriticalNotifyCompletion
        {
            private readonly ValueTask _value;
            private readonly bool _continueOnCapturedContext;
            internal ConfiguredValueTaskAwaiter2(ValueTask value, bool continueOnCapturedContext) { _value = value; _continueOnCapturedContext = continueOnCapturedContext; }
            public bool IsCompleted => _value.IsCompleted;
            public void GetResult() => _value._task?.GetAwaiter().GetResult();
            public void OnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(_continueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
            public void UnsafeOnCompleted(Action continuation) => _value.AsTask().ConfigureAwait(_continueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
        }
    }
}
