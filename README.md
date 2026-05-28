# FWHT.Net

A nine-stage Möbius/Yates butterfly over GF(2) on a single `Vector512<ulong>`. One Register (512 bits = 2⁹ elements) in, one Register out — no loops, no memory between stages.

`WalshHadamard.Transform(Vector512<ulong> w)` runs the kernel: six per-qword shift+mask+XOR stages (strides 1, 2, 4, 8, 16, 32), then three cross-qword `AlignRight64`+mask+XOR stages (strides 64, 128, 256). The transform is its own inverse over GF(2): applying it twice returns the original.

## Hardware requirements

- **x86-64**
- **AVX-512F** — required for `Vector512<ulong>`, the per-qword shift (`vpsllq`), and the cross-qword align (`valignq` via `Avx512F.AlignRight64`).

Supported microarchitectures:

| Vendor | Microarch | First product |
|---|---|---|
| Intel | Skylake-X | Xeon Scalable Gen 1 (2017) |
| Intel | Cascade Lake | Xeon Scalable Gen 2 (2019) |
| Intel | Ice Lake | Xeon Scalable Gen 3, Core Gen 11 (2021) |
| Intel | Tiger Lake / Rocket Lake | Core Gen 11/12 client (2021) |
| Intel | Sapphire Rapids / Emerald Rapids | Xeon Scalable Gen 4/5 (2023) |
| Intel | Granite Rapids | Xeon Scalable Gen 6 (2024) |
| AMD | Zen 4 | Ryzen 7000, EPYC 9004 (2022) |
| AMD | Zen 5 | Ryzen 9000, EPYC 9005 (2024) |

No fallback path — running on hardware without AVX-512F throws at runtime.

### Checking your CPU

**Linux:**
```
grep -o avx512f /proc/cpuinfo | head -1
```
Prints `avx512f` if supported, nothing if not.

**Windows (PowerShell):**
```
Get-WmiObject Win32_Processor | Select-Object Name, Description
```
Then cross-reference the model with the table above. Or use [Coreinfo](https://learn.microsoft.com/sysinternals/downloads/coreinfo):
```
coreinfo64.exe -f | findstr AVX512
```

**macOS:** No Mac has ever shipped with AVX-512 (consumer Intel Macs topped out at AVX2; Apple Silicon is ARM). Don't run this here.

**Cross-platform (.NET):**
```csharp
System.Console.WriteLine(System.Runtime.Intrinsics.X86.Avx512F.IsSupported);
```
Prints `True` on supported hardware.

## Benchmark

Measured by piping the kernel through [LinearPipe.Net](https://github.com/jrconradt/LinearPipe.Net) — 1 GiB source mmap'd from tmpfs, halves first-touched on each NUMA node, workers pinned to NUMA-local cores. The pipeline streams **64-byte chunks** (one Register = one `Vector512<ulong>` = one cache line) through a 199-byte native EVEX stub that implements the same nine stages. 1 GiB / 64 B = 16,777,216 chunks per Flow. Output verified via the involution `T(T(x)) == x` before timing. Wall time is bracketed by a 10-byte `rdtsc` stub called via function pointer (two reads per measurement; the stub is two unmanaged indirect calls around a 200-million-cycle Flow loop, ≈ 10⁻⁷ of the measurement).

**Host:** Intel Xeon Gold 6230 (Cascade Lake, 2.10 GHz nominal, 2 sockets × 20 cores × 2 threads), TSC frequency 2.1 GHz, kernel 6.12.

```
size=1024MB chunks=16777216  stub=199B  src halves placed node0/node1
verify involution (src -> Mobius -> Mobius == src) ... PASS

workers   ms/Flow   GB/s_rd   GB/s_rw   ns/chunk_agg   ns/op/wkr
    1    237.201       4.5       9.1       14.138      14.14
    4     57.632      18.6      37.3        3.435      13.74
   10     26.537      40.5      80.9        1.582      15.82
   20     21.541      49.8      99.7        1.284      25.68
```

**Reading the numbers:**

- **ns/op/wkr** is the per-64-byte-chunk latency seen by one worker: ~14 ns at low contention. This is the cost of `vmovdqu64` load (64 B) + 9 EVEX stages + `vmovntdq` store (64 B) on one core, dominated by the memory transactions; the 9 stages of compute slot into the cycles each core was already waiting on load/NT-store and add only ~1 ns over a pure identity transform.
- **Linear scaling 1 → 4 workers**, per-op stays flat at ~13–14 ns. Each worker is independent.
- **10-worker knee** is where per-socket memory controllers begin to saturate; per-op starts to creep.
- **20-worker plateau** is full both-sockets saturation at ~100 GB/s of combined R+NT-W traffic — past this, more workers just queue on the same controllers. This is the bus.

The kernel is **memory-bound, not compute-bound**. The cost of nine register-resident stages is hidden in the streaming traffic at every meaningful concurrency level.

## Build

```
dotnet build FWHT.Net.slnx
```
