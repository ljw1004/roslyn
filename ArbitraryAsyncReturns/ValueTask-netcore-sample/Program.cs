using System;
using System.Threading.Tasks;

namespace ConsoleApplication
{
    public class Program
    {
        public static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        public static async Task MainAsync()
        {
            perf();

            for (int iter=0; iter<1000000; iter++)
            {
                await TestTask(0);
            }
            Console.WriteLine("1,000,000 iterations with Task:      " + perf());

            for (int iter=0; iter<1000000; iter++)
            {
                await TestValueTask(0);
            }
            Console.WriteLine("1,000,000 iterations with ValueTask: " + perf());

        }

        public static async Task<int> TestTask(int d)
        {
            await Task.Delay(d);
            return 10;
        }

        public static async ValueTask<int> TestValueTask(int d)
        {
            await Task.Delay(d);
            return 10;
        }

        static int gen0, gen1, gen2;
        static System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        static TimeSpan elapsed;
        static string perf()
        {
            int newGen0 = GC.CollectionCount(0), newGen1 = GC.CollectionCount(1), newGen2 = GC.CollectionCount(2);
            var newElapsed = stopwatch.Elapsed;
            var s = $"{(newElapsed-elapsed).Milliseconds,3}ms, {newGen0 - gen0,2}*gen0 + {newGen1 - gen1,1}*gen1 + {newGen2 - gen2,1}*gen2 GC";
            GC.Collect();
            gen0 = GC.CollectionCount(0);
            gen1 = GC.CollectionCount(1);
            gen2 = GC.CollectionCount(2);
            elapsed = stopwatch.Elapsed;
            return s;
        }
    }
}

