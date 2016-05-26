#define TASKLIKE
using System;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;

class Performance
{
    static void Main()
    {
        var p = new Performance();
        var s = p.MainAsync().GetAwaiter().GetResult();
        Console.WriteLine(s);
    }

    const int iters = 100000000;

    StringBuilder sb;

    async Task<string> MainAsync()
    {
        sb = new StringBuilder();
        sb.AppendLine($"{iters} iterations...");
        int sum = await Warmup();
        var sw = Stopwatch.StartNew();
        GC();

#if TASKLIKE
        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            sum += await M1_AsyncValueTask_CallsDelayConditionally(0);
        }
        sw.Stop();
        var t1a = sw.Elapsed;
        sb.AppendLine($"[1a] {Pretty(t1a)} - {Pretty(t1a.TotalSeconds/iters)}/call - Call and await an async ValueTask<int>-returning method which returns immediately");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            var v = M1_AsyncValueTask_CallsDelayConditionally(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
        }
        sw.Stop();
        var t1b = sw.Elapsed;
        sb.AppendLine($"[1b] {Pretty(t1b)} - {Pretty(t1b.TotalSeconds/iters)}/call - Call and synchronously retrieve an async ValueTask<int>-returning method which returns immediately");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            sum += await M2_AsyncValueTask_CallsDelay(0);
        }
        sw.Stop();
        var t2a = sw.Elapsed;
        sb.AppendLine($"[2a] {Pretty(t2a)} - {Pretty(t2a.TotalSeconds/iters)}/call - Call and await an async ValueTask<int>-returning method which hot-paths through await Task.Delay(0)");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            var v = M2_AsyncValueTask_CallsDelay(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
        }
        sw.Stop();
        var t2b = sw.Elapsed;
        sb.AppendLine($"[2b] {Pretty(t2b)} - {Pretty(t2b.TotalSeconds/iters)}/call - Call and synchronously retrieve an async ValueTask<int>-returning method which hot-paths through await Task.Delay(0)");
        sb.AppendLine(GC());
#endif

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            sum += await M3_TailFoldedValueTask_CallsAsyncTaskWithConditional(0);
        }
        sw.Stop();
        var t3a = sw.Elapsed;
        sb.AppendLine($"[3a] {Pretty(t3a)} - {Pretty(t3a.TotalSeconds/iters)}/call - Call and await a non-async hand-unrolled ValueTask<int>-returning method which wraps up a Task from an async Task-returning method that returns immediately");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            var v = M3_TailFoldedValueTask_CallsAsyncTaskWithConditional(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
        }
        sw.Stop();
        var t3b = sw.Elapsed;
        sb.AppendLine($"[3b] {Pretty(t3b)} - {Pretty(t3b.TotalSeconds/iters)}/call - Call and synchronously retrieve a non-async hand-unrolled ValueTask<int>-returning method which wraps up a Task from an async Task-returning method that returns immediately");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            sum += await M4_TailFoldedValueTask_CallsAsyncTask(0);
        }
        sw.Stop();
        var t4a = sw.Elapsed;
        sb.AppendLine($"[4a] {Pretty(t4a)} - {Pretty(t4a.TotalSeconds/iters)}/call - Call and await a non-async hand-unrolled ValueTask<int>-returning method which wraps up a Task from an async Task-returning method that hot-paths through Task.Delay(0)");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            var v = M4_TailFoldedValueTask_CallsAsyncTask(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
        }
        sw.Stop();
        var t4b = sw.Elapsed;
        sb.AppendLine($"[4b] {Pretty(t4b)} - {Pretty(t4b.TotalSeconds/iters)}/call - Call and synchronously retrieve a non-async hand-unrolled ValueTask<int>-returning method which wraps up a Task from an async Task-returning method that hot-paths through Task.Delay(0)");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            sum += await M5_AsyncTaskWithConditional(0);
        }
        sw.Stop();
        var t5 = sw.Elapsed;
        sb.AppendLine($"[5] {Pretty(t5)} - {Pretty(t5.TotalSeconds/iters)}/call - Call and await an async Task-returning method that returns immediately");
        sb.AppendLine(GC());

        sw.Restart();
        for (var i = 0; i < iters; i++)
        {
            sum += await M6_AsyncTask(0);
        }
        sw.Stop();
        var t6 = sw.Elapsed;
        sb.AppendLine($"[6] {Pretty(t6)} - {Pretty(t6.TotalSeconds/iters)}/call - Call and await an async Task-returning method that hot-paths through Task.Delay(0)");
        sb.AppendLine(GC());

        //

        sb.AppendLine("\nQ. What is the all-up benefit of 'async ValueTask' vs 'async Task' in simple naive code?");
#if TASKLIKE
        Report("1a", t1a, "6", t6);
#endif

        sb.AppendLine("\nQ. What is the all-up benefit of 'hand-optimized non-async ValueTask' vs 'async Task', doing all the manual tweaks we can think of?");
        Report("3b", t3b, "6", t6);
        Report("4b", t4b, "6", t6);

        sb.AppendLine("\nLet's tease out the benefit of each manual tweak...");

        sb.AppendLine("\nQ. What is the benefit of manually avoiding a Task-returning hot-path vs letting the compiler do it?");
#if TASKLIKE
        Report("1a", t1a, "2a", t2a);
        Report("1b", t1b, "2b", t2b);
#endif
        Report("3a", t3a, "4a", t4a);
        Report("3b", t3b, "4b", t4b);
        Report("5", t5, "6", t6);

        sb.AppendLine("\nQ. What is the benefit of manually avoiding a ValueTask-returning hot-path vs letting the compiler do it?");
#if TASKLIKE
        Report("1b", t1b, "1a", t1a);
        Report("2b", t2b, "2a", t2a);
#endif
        Report("3b", t3b, "3a", t3a);
        Report("4b", t4b, "4a", t4a);

        sb.AppendLine("\nQ. What is the benefit of hand-unrolling an async Value-Task returning method ('tail await elimination')?");
#if TASKLIKE
        Report("3a", t3a, "1a", t1a);
        Report("3b", t3b, "1b", t1b);
        Report("4a", t4a, "2a", t2a);
        Report("4b", t4b, "2b", t2b);
#endif
        return sb.ToString();
    }

    void Report(string name1, TimeSpan ms1, string name2, TimeSpan ms2)
    {
        var slow_per_iter = ms2.TotalSeconds / iters;
        var fast_per_iter = ms1.TotalSeconds / iters;
        var diff_per_iter = slow_per_iter - fast_per_iter;
        sb.AppendLine($"  [{name1}] vs [{name2}]: {ms2.TotalSeconds / ms1.TotalSeconds:F2} x faster, shaving {Pretty(diff_per_iter)} off each {Pretty(slow_per_iter)} call");
    }

    static string Pretty(TimeSpan s) => Pretty(s.TotalSeconds);

    static string Pretty(double s)
    {
        if (s < 0) return $"-{Pretty(-s)}";
        if (s > 1.0) return $"{s:F2}s";
        s *= 1000; if (s > 1.0) return $"{s:F2}ms";
        s *= 1000; if (s > 1.0) return $"{s:F2}us";
        s *= 1000; return $"{s:F2}ns";
    }

    int gen0, gen1, gen2;
    string GC()
    {
        int newGen0 = System.GC.CollectionCount(0), newGen1 = System.GC.CollectionCount(1), newGen2 = System.GC.CollectionCount(2);
        var s = $"gen0:{newGen0 - gen0} gen1:{newGen1 - gen1} gen2:{newGen2 - gen2}";
        System.GC.Collect();
        gen0 = System.GC.CollectionCount(0);
        gen1 = System.GC.CollectionCount(1);
        gen2 = System.GC.CollectionCount(2);
        return s;
    }

    private async Task<int> Warmup()
    {
        var sum = 0; ValueTask<int> v;
        for (var i = 0; i < 10; i++)
        {
#if TASKLIKE
            sum += await M1_AsyncValueTask_CallsDelayConditionally(0);
            v = M1_AsyncValueTask_CallsDelayConditionally(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
            //
            sum += await M2_AsyncValueTask_CallsDelay(0);
            v = M2_AsyncValueTask_CallsDelay(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
#endif
            //
            sum += await M3_TailFoldedValueTask_CallsAsyncTaskWithConditional(0);
            v = M3_TailFoldedValueTask_CallsAsyncTaskWithConditional(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
            //
            sum += await M4_TailFoldedValueTask_CallsAsyncTask(0);
            v = M4_TailFoldedValueTask_CallsAsyncTask(0);
            sum += v.IsCompletedSuccessfully ? v.Result : await v;
            //
            sum += await M5_AsyncTaskWithConditional(0);
            sum += await M6_AsyncTask(0);
        }

        return sum;
    }

#if TASKLIKE
    static async ValueTask<int> M1_AsyncValueTask_CallsDelayConditionally(int delay)
    {
        if (delay > 0)
        {
            await Task.Delay(delay);
        }
        return delay;
    }

    static async ValueTask<int> M2_AsyncValueTask_CallsDelay(int delay)
    {
        await Task.Delay(delay);
        return delay;
    }
#endif

    static ValueTask<int> M3_TailFoldedValueTask_CallsAsyncTaskWithConditional(int delay)
    {
        if (delay > 0)
        {
            return new ValueTask<int>(M5_AsyncTaskWithConditional(delay));
        }
        return new ValueTask<int>(delay);
    }

    static ValueTask<int> M4_TailFoldedValueTask_CallsAsyncTask(int delay)
    {
        if (delay > 0)
        {
            return new ValueTask<int>(M6_AsyncTask(delay));
        }
        return new ValueTask<int>(delay);
    }

    static async Task<int> M5_AsyncTaskWithConditional(int delay)
    {
        if (delay > 0)
        {
            await Task.Delay(delay);
        }
        return delay;
    }

    static async Task<int> M6_AsyncTask(int delay)
    {
        await Task.Delay(delay);
        return delay;
    }

}
