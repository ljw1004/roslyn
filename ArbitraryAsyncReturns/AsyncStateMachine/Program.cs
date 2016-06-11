using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

class Program
{
    static void Main()
    {
        Attempt1.MainAsync().GetAwaiter().GetResult();

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
    //        Console.WriteLine("E");
    //    }
    //}



}