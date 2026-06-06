using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using Fwht;
using LinearPipe;
using Xunit;
using Xunit.Abstractions;

namespace Fwht.Bench;

public sealed unsafe class FwhtBenchmarkTests
{
    private const long SIZE_BYTES = 64L << 20;
    private const int WARMUP = 5;
    private const int MEASURED = 30;
    private static readonly int[] WorkerSweep = { 1, 4, 10, 20 };

    private readonly ITestOutputHelper _output;

    public FwhtBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Stub_Matches_Managed_Kernel()
    {
        Assert.True(Avx512F.IsSupported);

        byte* consts = Masks.Build();
        nint code = NativeLoader.Load(Stub.Code);
        var fn = (delegate* unmanaged[Cdecl, SuppressGCTransition]<ulong*, byte*, byte*, void>)code;

        byte* input = (byte*)NativeMemory.AlignedAlloc(64, 64);
        byte* nativeOut = (byte*)NativeMemory.AlignedAlloc(64, 64);
        byte* nativeBack = (byte*)NativeMemory.AlignedAlloc(64, 64);
        byte* managedOut = (byte*)NativeMemory.AlignedAlloc(64, 64);

        try
        {
            Random rng = new Random(7);
            for (int i = 0; i < 64; i++)
            {
                input[i] = (byte)rng.Next(256);
            }

            Vector512<ulong> w = Vector512.Load((ulong*)input);
            Vector512<ulong> transformed = WalshHadamard.Transform(w);
            Vector512<ulong> involuted = WalshHadamard.Transform(transformed);
            Assert.Equal(w, involuted);
            transformed.Store((ulong*)managedOut);

            fn((ulong*)nativeOut, input, consts);
            fn((ulong*)nativeBack, nativeOut, consts);

            for (int i = 0; i < 64; i++)
            {
                Assert.Equal(managedOut[i], nativeOut[i]);
                Assert.Equal(input[i], nativeBack[i]);
            }
        }
        finally
        {
            NativeMemory.AlignedFree(input);
            NativeMemory.AlignedFree(nativeOut);
            NativeMemory.AlignedFree(nativeBack);
            NativeMemory.AlignedFree(managedOut);
            NativeLoader.Unload(code);
            Masks.Free(consts);
        }
    }

    [Fact]
    public void Benchmark_Flow_Reports_Throughput()
    {
        Assert.True(Avx512F.IsSupported);

        byte* consts = Masks.Build();
        nint code = NativeLoader.Load(Stub.Code);
        var fn = (delegate* unmanaged[Cdecl, SuppressGCTransition]<ulong*, byte*, byte*, void>)code;

        string src = NewSourcePath();
        string mid = src + ".t1";
        string back = src + ".t2";

        try
        {
            WriteNumaSplitSource(src, SIZE_BYTES, 0, 1);

            using (LinearPipeline forward = new LinearPipeline(src, mid, fn, consts))
            {
                forward.Flow();
            }
            using (LinearPipeline inverse = new LinearPipeline(mid, back, fn, consts))
            {
                inverse.Flow();
            }
            AssertFilesEqual(src, back);
            _output.WriteLine($"verify involution (src -> Mobius -> Mobius == src) ... PASS");

            long chunks = SIZE_BYTES / 64;
            _output.WriteLine($"size={SIZE_BYTES >> 20}MB chunks={chunks}  stub={Stub.Code.Length}B  src halves first-touched node0/node1");
            _output.WriteLine($"workers   ms/Flow   GB/s_rd   GB/s_rw   ns/chunk_agg   ns/op/wkr");

            foreach (int workers in WorkerSweep)
            {
                PipelineOptions options = new PipelineOptions
                {
                    WorkerCount = workers,
                    AffinityCores = NumaCores(workers)
                };
                using LinearPipeline pipe = new LinearPipeline(src, mid, fn, consts, options);
                PipelineSubject subject = new PipelineSubject(pipe);
                Benchmark<PipelineSubject> bench = new Benchmark<PipelineSubject>(WARMUP, MEASURED);
                BenchmarkResult result = bench.Run(subject);
                subject.Free();

                double msPerFlow = result.MedianNanos / 1_000_000.0;
                double gbRead = SIZE_BYTES / result.MedianNanos;
                double gbReadWrite = 2.0 * gbRead;
                double nsPerChunk = result.MedianNanos / chunks;
                double nsPerOpWorker = nsPerChunk * workers;
                _output.WriteLine($"{workers,5}   {msPerFlow,7:F3}   {gbRead,7:F1}   {gbReadWrite,7:F1}   {nsPerChunk,12:F3}   {nsPerOpWorker,8:F2}");
                Assert.True(result.MedianNanos > 0);
            }
        }
        finally
        {
            NativeLoader.Unload(code);
            Masks.Free(consts);
            Delete(src);
            Delete(mid);
            Delete(back);
        }
    }

    private static int[] NumaCores(int workers)
    {
        int[] cores = new int[workers];
        int node0Count = (workers + 1) / 2;
        for (int t = 0; t < workers; t++)
        {
            if (t < node0Count)
            {
                cores[t] = t * 2;
            }
            else
            {
                cores[t] = (t - node0Count) * 2 + 1;
            }
        }
        return cores;
    }

    private static string NewSourcePath()
    {
        string root = Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath();
        return Path.Combine(root, $"fwht-bench-{Guid.NewGuid():N}.dat");
    }

    private static void WriteNumaSplitSource(string path, long length, int node0Core, int node1Core)
    {
        using MappedFile map = MappedFile.Create(path, length);
        byte* basePtr = map.Base;
        long half = (length / 2) & ~4095L;
        Thread lower = new Thread(() =>
        {
            Affinity.Pin(node0Core);
            FillRandom(basePtr, 0, half, 11);
        });
        Thread upper = new Thread(() =>
        {
            Affinity.Pin(node1Core);
            FillRandom(basePtr, half, length, 22);
        });
        lower.Start();
        upper.Start();
        lower.Join();
        upper.Join();
    }

    private static void FillRandom(byte* basePtr, long from, long to, int seed)
    {
        byte[] buffer = new byte[1 << 20];
        new Random(seed).NextBytes(buffer);
        long pos = from;
        while (pos < to)
        {
            int take = (int)Math.Min(buffer.Length, to - pos);
            Marshal.Copy(buffer, 0, (nint)(basePtr + pos), take);
            pos += take;
        }
    }

    private static void AssertFilesEqual(string a, string b)
    {
        using FileStream sa = File.OpenRead(a);
        using FileStream sb = File.OpenRead(b);
        Assert.Equal(sa.Length, sb.Length);
        byte[] ba = new byte[1 << 20];
        byte[] bb = new byte[1 << 20];
        while (true)
        {
            int ra = ReadFully(sa, ba);
            int rb = ReadFully(sb, bb);
            Assert.Equal(ra, rb);
            if (ra == 0)
            {
                return;
            }
            for (int i = 0; i < ra; i++)
            {
                Assert.Equal(ba[i], bb[i]);
            }
        }
    }

    private static int ReadFully(FileStream stream, byte[] buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }
            total += read;
        }
        return total;
    }

    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
