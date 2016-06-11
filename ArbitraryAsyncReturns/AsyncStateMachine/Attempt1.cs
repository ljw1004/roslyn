using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

class Attempt1
{
    // * Builder is a field of the state-machine struct
    // * Builder is a class which implements IAsyncEnumerator<int>
    // * Builder boxes the struct immediately, in Start()
    

    public static async Task MainAsync()
    {
        var enumerator = f(2);
        //foreach (await var x in enumerator) Console.WriteLine(x);
        try
        {
            while (await enumerator.MoveNextAsync()) Console.WriteLine(enumerator.Current);
        }
        finally { enumerator.Dispose(); }
    }


    interface IAsyncEnumerator<T> : IDisposable
    {
        Task<bool> MoveNextAsync();
        T Current { get; }
    }

    static IAsyncEnumerator<int> f(int p)
    {
        var sm = new fStateMachine() { p = p };
        sm.builder = AsyncEnumeratorBuilder<int>.Create();
        sm.state = -1;
        sm.builder.Start(ref sm);
        return sm.builder.Task;
    }

    class AsyncEnumeratorBuilder<T> : IAsyncEnumerator<T>
    {
        public IAsyncStateMachine2 sm;
        public TaskCompletionSource<bool> CurrentMoveNext;

        // BUILDER STUFF
        public static AsyncEnumeratorBuilder<T> Create() => new AsyncEnumeratorBuilder<T>();
        public void Start<TSM>(ref TSM sm) where TSM : IAsyncStateMachine2 { this.sm = sm; }
        public void SetStateMachine(IAsyncStateMachine sm) { }
        public IAsyncEnumerator<T> Task => this;
        public void AwaitOnCompleted<TA, TSM>(ref TA awaiter, ref TSM sm) where TA : INotifyCompletion where TSM : IAsyncStateMachine2
        {
            awaiter.OnCompleted(this.sm.MoveNext);
        }
        public void YieldResult(T value) { Current = value; CurrentMoveNext.SetResult(true); }
        public void SetResult() { CurrentMoveNext.SetResult(false); }
        public void SetException(Exception ex) { CurrentMoveNext.SetException(ex); }

        // IASYNCENUMERATOR STUFF
        public T Current { get; private set; }
        public void Dispose()
        {
            if (CurrentMoveNext != null && !CurrentMoveNext.Task.IsCompleted) throw new InvalidOperationException("only dispose at a yield point");
            CurrentMoveNext = new TaskCompletionSource<bool>();
            sm.Dispose();
            System.Diagnostics.Debug.Assert(sm.IsCompleted);
        }
        public Task<bool> MoveNextAsync()
        {
            if (sm.IsCompleted) return System.Threading.Tasks.Task.FromResult(false);
            CurrentMoveNext = new TaskCompletionSource<bool>();
            sm.MoveNext();
            return CurrentMoveNext.Task;
        }
    }

    interface IAsyncStateMachine2 : IAsyncStateMachine, IDisposable
    {
        bool IsCompleted { get; }
    }

    struct fStateMachine : IAsyncStateMachine2
    {
        public int p;
        public int state;
        public AsyncEnumeratorBuilder<int> builder;
        public void SetStateMachine(IAsyncStateMachine stateMachine) => builder.SetStateMachine(stateMachine);
        public void Dispose() { if (state == 1) state = 99; MoveNext(); }
        public bool IsCompleted => state == -2;

        private TaskAwaiter awaiter;

        public void MoveNext()
        {
            // state = -2: finished
            // state = -1: default control flow
            // state = 0..n: should jump in to resume after an await or yield
            // state = 99: dispose should jump here
            try
            {
                switch (state)
                {
                    case -2: return;
                    case -1: break;
                    case 0: goto try_entry;
                    case 1: goto try_entry;
                    case 2: goto try_entry;
                    case 99: goto try_entry;
                    default: throw new InvalidOperationException("oops");
                }
                try_entry:
                // *** try { 
                try
                {
                    switch (state)
                    {
                        case -2: throw new InvalidOperationException("oops");
                        case -1: break;
                        case 0: goto label0;
                        case 1: goto label1;
                        case 2: goto label2;
                        case 99: state = -1; goto label_finally1;
                        default: throw new InvalidOperationException("oops");
                    }
                    // *** Console.WriteLine("A");
                    Console.WriteLine("A");
                    // *** await Task.Delay(100);
                    awaiter = Task.Delay(100).GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        state = 0;
                        builder.AwaitOnCompleted(ref awaiter, ref this);
                        return;
                    }
                    label0:
                    state = -1;
                    awaiter.GetResult();
                    // *** Console.WriteLine("B");
                    Console.WriteLine("B");
                    // *** yield return p;
                    state = 1;
                    builder.YieldResult(p);
                    return;
                    label1:
                    state = -1;
                    // *** Console.WriteLine("C");
                    Console.WriteLine("C");
                    // *** await Task.Delay(200);
                    awaiter = Task.Delay(200).GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        state = 2;
                        builder.AwaitOnCompleted(ref awaiter, ref this);
                        return;
                    }
                    label2:
                    state = -1;
                    awaiter.GetResult();
                    // *** Console.WriteLine("D");
                    Console.WriteLine("D");
                    label_finally1:;
                }
                finally
                {
                    if (state == -1)
                    {
                        // *** Console.WriteLine("E");
                        Console.WriteLine("E");
                    }
                }
                state = -2;
                builder.SetResult();
            }
            catch (Exception ex)
            {
                state = -2;
                builder.SetException(ex);
            }
        }
    }

}
