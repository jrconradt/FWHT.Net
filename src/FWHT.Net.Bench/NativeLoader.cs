using System.Runtime.InteropServices;

namespace Fwht.Bench;

public static unsafe partial class NativeLoader
{
    private const int PROT_READ = 0x1;
    private const int PROT_WRITE = 0x2;
    private const int PROT_EXEC = 0x4;
    private const int MAP_PRIVATE = 0x2;
    private const int MAP_ANONYMOUS = 0x20;
    private const int PAGE = 4096;

    [LibraryImport("libc", SetLastError = true)]
    private static partial nint mmap(nint addr,
                                     nuint length,
                                     int prot,
                                     int flags,
                                     int fd,
                                     nint offset);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int mprotect(nint addr,
                                        nuint length,
                                        int prot);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int munmap(nint addr,
                                      nuint length);

    public static nint Load(byte[] code)
    {
        nint page = mmap(0,
                         PAGE,
                         PROT_READ | PROT_WRITE,
                         MAP_PRIVATE | MAP_ANONYMOUS,
                         -1,
                         0);
        if (page == -1)
        {
            throw new InvalidOperationException($"NativeLoader: mmap failed (errno {Marshal.GetLastSystemError()}).");
        }
        Marshal.Copy(code, 0, page, code.Length);
        if (mprotect(page, PAGE, PROT_READ | PROT_EXEC) != 0)
        {
            throw new InvalidOperationException($"NativeLoader: mprotect failed (errno {Marshal.GetLastSystemError()}).");
        }
        return page;
    }

    public static void Unload(nint page)
    {
        munmap(page, PAGE);
    }
}
