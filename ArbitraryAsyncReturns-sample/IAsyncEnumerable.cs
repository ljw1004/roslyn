using System;
using System.Threading.Tasks;

class Program
{

    static void Main()
    {
        MainAsync1().GetAwaiter().GetResult();
        MainAsync2().GetAwaiter().GetResult();
    }

    static async Task MainAsync1()
    {
        var enumerator = f();
        //foreach (await var x in enumerator) Console.WriteLine(x);

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                var x = enumerator.Current;
                Console.WriteLine(x);
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    static async Task MainAsync2()
    {
        var enumerable = g();
        //foreach (await var x in enumerable) Console.WriteLine(x);

        var enumerator = enumerable.GetEnumerator();
        try
        {
            
            while (await enumerator.MoveNextAsync())
            {
                var x = enumerator.Current;
                Console.WriteLine(x);
                break;
            }
        }
        finally
        {
            enumerator.Dispose();
        }
    }

    interface IAsyncEnumerable<T>
    {
        IAsyncEnumerator<T> GetEnumerator();
    }

    interface IAsyncEnumerator<T> : IDisposable
    {
        Task<bool> MoveNextAsync();
        T Current { get; }
    }

    //static async IAsyncEnumerator<int> f()
    //static async IAsyncEnumerable<int> g()
    //{
    //    try {
    //      Console.WriteLine("A");
    //      await Task.Delay(100);
    //      Console.WriteLine("B");
    //      yield return 1;
    //      Console.WriteLine("C");
    //      await Task.Delay(200);
    //      Console.WriteLine("D");
    //    }
    //    finally {
    //      // finally block should be executed NOT at the "yield", but yes everywhere else
    //      Console.WriteLine("E");
    //    }
    //}

    static IAsyncEnumerable<int> g() => new fStateMachineFactory();

    static IAsyncEnumerator<int> f() => new fStateMachine();

    class fStateMachineFactory : IAsyncEnumerable<int>
    {
        public IAsyncEnumerator<int> GetEnumerator() => new fStateMachine();
    }

    class fStateMachine : IAsyncEnumerator<int>
    {
        public int Current { get; private set; }
        int state = 0;
        public void Dispose() { state = 2; var dummy = MoveNextAsync(); }

        public async Task<bool> MoveNextAsync()
        {
            if (state == 2) goto label2;
            try
            {
                switch (state)
                {
                    case 0: goto label0;
                    case 1: goto label1;
                    default: goto label2;
                }
                label0:
                Console.WriteLine("A");
                await Task.Delay(100);
                Console.WriteLine("B");
                Current = 1; state = 1; return true; // yield 1
                label1:
                Console.WriteLine("C");
                await Task.Delay(200);
                Console.WriteLine("D");
                state = 2;
            }
            finally
            {
                if (state == 2) Console.WriteLine("E");
            }
            label2:
            return false;
        }
    }


}

