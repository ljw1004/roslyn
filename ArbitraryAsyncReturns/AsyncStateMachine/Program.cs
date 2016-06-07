using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// THIS CODE HAS BUGS.
// Either the state machine or the builder for IAsyncEnumerator
// is wrong.

class Program
{
    static void Main()
    {
        TestProductionAsync().GetAwaiter().GetResult();
        //TestConsumptionAsync().GetAwaiter().GetResult();
    }

    static async Task TestConsumptionAsync()
    {
        var enumerable = new AsyncEnumerable();
        //foreach (await var x in enumerable) Console.Write(x);
        using (var en = enumerable.GetEnumerator())
            while (await en.MoveNextAsync()) Console.Write(en.Current);

        var enumerator = new AsyncEnumerator();
        //foreach (await var x in enumerator) Console.Write(x);
        try { while (await enumerator.MoveNextAsync()) Console.Write(enumerator.Current); }
        finally { enumerator.Dispose(); }
    }

    class Enumerable
    {
        public Enumerator GetEnumerator() => new Enumerator();
    }

    class Enumerator : IDisposable
    {
        public int Current { get; set; }
        public void Dispose() { }
        public bool MoveNext() { return ++Current < 4; }
    }


    class AsyncEnumerable
    {
        public AsyncEnumerator GetEnumerator() => new AsyncEnumerator();
    }

    class AsyncEnumerator : IDisposable
    {
        public int Current { get; set; }
        public void Dispose() { }
        public async Task<bool> MoveNextAsync() { await Task.Delay(0); return ++Current < 4; }
    }

    static async Task TestProductionAsync()
    {
        var enumerator = f(2);
        //foreach (await var x in enumerator) Console.WriteLine(x);
        try { while (await enumerator.MoveNextAsync()) Console.WriteLine(enumerator.Current); }
        finally { enumerator.Dispose(); }
    }


    interface IAsyncEnumerator<T> : IDisposable
    {
        Task<bool> MoveNextAsync();
        T Current { get; }
    }

    //static async IAsyncEnumerator<int> f(int p)
    //{
    //    try { 
    //        Console.WriteLine("A");
    //        await Task.Delay(100);
    //        Console.WriteLine("B");
    //        yield return p;
    //        Console.WriteLine("C");
    //        await Task.Delay(200);
    //        Console.WriteLine("D");
    //    }
    //    finally {
    //        // finally block should be executed NOT at the "yield", but yes everywhere else
    //        Console.WriteLine("E");
    //    }
    //}

    static IAsyncEnumerator<int> f(int p)
    {
        var sm = new fStateMachine() { p = p };
        sm.builder = AsyncEnumeratorBuilder<int>.Create();
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
        public void Dispose() { isDisposing = true; MoveNext(); }
        public bool IsCompleted => state == 4;

        private bool isDisposing;
        private TaskAwaiter awaiter;

        public void MoveNext()
        {
            try
            {
                switch (state)
                {
                    case 0: goto try_entry;
                    case 1: goto try_entry;
                    case 2: goto try_entry;
                    case 3: goto try_entry;
                    case 4: goto label4;
                    default: throw new InvalidOperationException("oops");
                }
                try_entry:
                try
                {
                    switch (state)
                    {
                        case 0: goto label0;
                        case 1: goto label1;
                        case 2: goto label2;
                        case 3: goto label3;
                        default: throw new InvalidOperationException("oops");
                    }
                    label0:
                    // *** Console.WriteLine("A");
                    Console.WriteLine("A");
                    // *** await Task.Delay(100);
                    awaiter = Task.Delay(100).GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        state = 1;
                        builder.AwaitOnCompleted(ref awaiter, ref this);
                        return;
                    }
                    label1:
                    if (isDisposing) return;
                    awaiter.GetResult();
                    // *** Console.WriteLine("B");
                    Console.WriteLine("B");
                    // *** yield return p;
                    state = 2;
                    builder.YieldResult(p);
                    return;
                    label2:
                    if (isDisposing) return;
                    // *** Console.WriteLine("C");
                    Console.WriteLine("C");
                    // *** await Task.Delay(200);
                    awaiter = Task.Delay(200).GetAwaiter();
                    if (!awaiter.IsCompleted)
                    {
                        state = 3;
                        builder.AwaitOnCompleted(ref awaiter, ref this);
                        return;
                    }
                    label3:
                    if (isDisposing) return;
                    awaiter.GetResult();
                    // *** Console.WriteLine("D");
                    Console.WriteLine("D");
                }
                finally
                {
                    if (state == 3 || (isDisposing && state != 4))
                    {
                        // *** Console.WriteLine("E");
                        Console.WriteLine("E");
                        state = 4;
                    }
                }
                if (!isDisposing) { builder.SetResult(); state = 4; }
            }
            catch (Exception ex)
            {
                builder.SetException(ex);
                state = 4;
            }
            label4:;
        }
    }


}
