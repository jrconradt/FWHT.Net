using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Fwht;

public static class WalshHadamard
{
    private static readonly Vector512<ulong> M0 = Vector512.Create(0xAAAAAAAAAAAAAAAAUL);
    private static readonly Vector512<ulong> M1 = Vector512.Create(0xCCCCCCCCCCCCCCCCUL);
    private static readonly Vector512<ulong> M2 = Vector512.Create(0xF0F0F0F0F0F0F0F0UL);
    private static readonly Vector512<ulong> M3 = Vector512.Create(0xFF00FF00FF00FF00UL);
    private static readonly Vector512<ulong> M4 = Vector512.Create(0xFFFF0000FFFF0000UL);
    private static readonly Vector512<ulong> M5 = Vector512.Create(0xFFFFFFFF00000000UL);
    private static readonly Vector512<ulong> M6 = Vector512.Create(0UL, ~0UL, 0UL, ~0UL, 0UL, ~0UL, 0UL, ~0UL);
    private static readonly Vector512<ulong> M7 = Vector512.Create(0UL, 0UL, ~0UL, ~0UL, 0UL, 0UL, ~0UL, ~0UL);
    private static readonly Vector512<ulong> M8 = Vector512.Create(0UL, 0UL, 0UL, 0UL, ~0UL, ~0UL, ~0UL, ~0UL);

    public static Vector512<ulong> Transform(Vector512<ulong> w)
    {
        w ^= (w << 1) & M0;
        w ^= (w << 2) & M1;
        w ^= (w << 4) & M2;
        w ^= (w << 8) & M3;
        w ^= (w << 16) & M4;
        w ^= (w << 32) & M5;
        w ^= Avx512F.AlignRight64(w, Vector512<ulong>.Zero, 7) & M6;
        w ^= Avx512F.AlignRight64(w, Vector512<ulong>.Zero, 6) & M7;
        w ^= Avx512F.AlignRight64(w, Vector512<ulong>.Zero, 4) & M8;
        return w;
    }
}
