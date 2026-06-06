using System.Diagnostics;

namespace Fwht.Bench;

public static unsafe class Tsc
{
    private static readonly byte[] BeginCode =
    {
        0x0f, 0xae, 0xe8,
        0x0f, 0x31,
        0x48, 0xc1, 0xe2, 0x20,
        0x48, 0x09, 0xd0,
        0xc3
    };

    private static readonly byte[] EndCode =
    {
        0x0f, 0x01, 0xf9,
        0x48, 0xc1, 0xe2, 0x20,
        0x48, 0x09, 0xd0,
        0x0f, 0xae, 0xe8,
        0xc3
    };

    private static readonly delegate* unmanaged[Cdecl, SuppressGCTransition]<ulong> BeginFn =
        (delegate* unmanaged[Cdecl, SuppressGCTransition]<ulong>)NativeLoader.Load(BeginCode);

    private static readonly delegate* unmanaged[Cdecl, SuppressGCTransition]<ulong> EndFn =
        (delegate* unmanaged[Cdecl, SuppressGCTransition]<ulong>)NativeLoader.Load(EndCode);

    public static readonly double TicksPerNanosecond = Calibrate();

    public static ulong Begin()
    {
        return BeginFn();
    }

    public static ulong End()
    {
        return EndFn();
    }

    private static double Calibrate()
    {
        long swFreq = Stopwatch.Frequency;
        long swTarget = Stopwatch.GetTimestamp() + swFreq / 50;
        long swStart = Stopwatch.GetTimestamp();
        ulong tscStart = BeginFn();
        while (Stopwatch.GetTimestamp() < swTarget)
        {
        }
        ulong tscEnd = EndFn();
        long swEnd = Stopwatch.GetTimestamp();
        double seconds = (swEnd - swStart) / (double)swFreq;
        return (tscEnd - tscStart) / seconds / 1_000_000_000.0;
    }
}
