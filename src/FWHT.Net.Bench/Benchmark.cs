namespace Fwht.Bench;

public interface IBenchmarkSubject
{
    void Run();
}

public readonly struct BenchmarkResult
{
    public readonly int Iterations;
    public readonly double MinNanos;
    public readonly double MedianNanos;
    public readonly double MaxNanos;

    public BenchmarkResult(int iterations,
                           double minNanos,
                           double medianNanos,
                           double maxNanos)
    {
        Iterations = iterations;
        MinNanos = minNanos;
        MedianNanos = medianNanos;
        MaxNanos = maxNanos;
    }

    public static BenchmarkResult FromSamples(long[] samples)
    {
        Array.Sort(samples);
        int n = samples.Length;
        double perNano = Tsc.TicksPerNanosecond;
        return new BenchmarkResult(n,
                                   samples[0] / perNano,
                                   samples[n / 2] / perNano,
                                   samples[n - 1] / perNano);
    }
}

public sealed class Benchmark<T> where T : unmanaged, IBenchmarkSubject
{
    private readonly int _warmup;
    private readonly int _measured;

    public Benchmark(int warmup, int measured)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(warmup);
        ArgumentOutOfRangeException.ThrowIfLessThan(measured, 1);
        _warmup = warmup;
        _measured = measured;
    }

    public BenchmarkResult Run(T subject)
    {
        for (int i = 0; i < _warmup; i++)
        {
            subject.Run();
        }

        long[] samples = new long[_measured];
        for (int i = 0; i < _measured; i++)
        {
            ulong start = Tsc.Begin();
            subject.Run();
            ulong end = Tsc.End();
            samples[i] = (long)(end - start);
        }

        return BenchmarkResult.FromSamples(samples);
    }
}
