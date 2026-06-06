using System.Runtime.InteropServices;

namespace Fwht.Bench;

public static unsafe partial class Affinity
{
    private const int CPU_SET_BYTES = 128;

    [LibraryImport("libc", SetLastError = true)]
    private static partial int sched_setaffinity(int pid,
                                                 nuint cpusetsize,
                                                 byte* mask);

    public static void Pin(int core)
    {
        byte* mask = stackalloc byte[CPU_SET_BYTES];
        for (int i = 0; i < CPU_SET_BYTES; i++)
        {
            mask[i] = 0;
        }
        mask[core >> 3] = (byte)(1 << (core & 7));
        if (sched_setaffinity(0, CPU_SET_BYTES, mask) != 0)
        {
            throw new InvalidOperationException($"Affinity: sched_setaffinity(core={core}) failed (errno {Marshal.GetLastSystemError()}).");
        }
    }
}
