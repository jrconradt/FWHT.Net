# FWHT.Net

![.NET](https://img.shields.io/badge/.NET-10-512BD4) ![Arch](https://img.shields.io/badge/x86--64-AVX--512F-orange) [![License](https://img.shields.io/badge/License-Apache_2.0-blue)](LICENSE)

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

The `FWHT.Net.Bench` project pipes the kernel through [LinearPipe.Net](https://github.com/jrconradt/LinearPipe.Net), consumed as the NuGet package `LinearPipe.Net` (0.1.0). LinearPipe mmaps a tmpfs source, streams **64-byte chunks** (one Register = one `Vector512<ulong>` = one cache line) through a native transform, and writes each result with a non-temporal `vmovntdq` store across a NUMA-pinned worker pool.

The transform is a **154-byte hand-encoded EVEX stub** implementing the same nine stages: six per-qword `vpsllq` (strides 1, 2, 4, 8, 16, 32) and three cross-qword `valignq` (strides 64, 128, 256), each folded back into the running register by a single `vpternlogq` (immediate `0x78` = `a ⊕ (b ∧ c)`) against the stage mask. The nine masks live in a 64-byte-aligned constant pool passed as the stub's third argument and read with compressed-displacement loads. The stub loads with `vmovdqu64` and writes its result to the output pointer; LinearPipe issues the non-temporal store.

The bench cross-checks the stub against the managed `WalshHadamard.Transform` chunk-for-chunk and checks the involution `T(T(x)) == x` for both the managed kernel and the stub, then verifies the involution end-to-end through the pipeline (`src → sink → src`) before timing. Wall time is bracketed by `rdtsc`/`rdtscp` stubs (raw bytes loaded into an executable page, called via function pointer); the harness runs warmup Flows, then measured Flows each bracketed by two TSC reads, and reports min/median/max with the TSC calibrated against `Stopwatch`.

The source is placed for NUMA locality: its two halves are first-touched by threads pinned to node0 and node1 so each half's pages land on that socket, and the LinearPipe workers are pinned via `PipelineOptions.AffinityCores` to distinct physical cores split node0/node1 — each worker reads its NUMA-local half and its non-temporal writes land node-local. The run below streams a 100 GiB source (100 GiB / 64 B = 1,677,721,600 chunks per Flow); the project's default test runs a quick 64 MiB pass.

**Host:** Intel Xeon Gold 6230 (Cascade Lake, 2.10 GHz nominal, 2 sockets × 20 cores × 2 threads), TSC frequency 2.1 GHz, kernel 6.12.

```
size=102400MB chunks=1677721600  stub=154B  src halves first-touched node0/node1

workers   ms/Flow   GB/s_rd   GB/s_rw   ns/chunk_agg   ns/op/wkr
    1   22371.869       4.8       9.6        13.335      13.33
    4    5585.661      19.2      38.4         3.329      13.32
   10    2561.737      41.9      83.8         1.527      15.27
   20    2087.837      51.4     102.9         1.244      24.89
   40    2039.816      52.6     105.3         1.216      48.63
```

**Reading the numbers:**

- **ns/op/wkr** is the per-64-byte-chunk latency seen by one worker: ~13.3 ns at low contention. This is the cost of the `vmovdqu64` load (64 B) + 9 EVEX stages on one core plus the pipeline's non-temporal store (64 B), dominated by the memory transactions; the nine stages of compute slot into the cycles each core was already waiting on load/NT-store and add only ~1 ns over a pure identity transform.
- **Flat 1 → 4 workers**, per-op holds at ~13.3 ns. Each worker is independent.
- **10-worker knee** at ~84 GB/s R+NT-W, where the per-socket memory controllers begin to fill.
- **20 workers reach ~103 GB/s** combined R+NT-W — both sockets' controllers working in parallel, which only happens because the source halves and the workers are placed NUMA-local.
- **40 workers add +2%** (~105 GB/s) for **2× the per-op latency** (48.6 ns). Past ~20 workers there is no bandwidth left to win; the extra workers just queue on the bus. This is the ceiling: ~105 GB/s is the combined two-socket DRAM bandwidth of the host.
- **One worker pays a remote-half penalty** (~13.3 ns vs ~10 ns for a non-split layout): a single node0-pinned worker reads node1's half across the interconnect. The trade-off vanishes by 4 workers and is what unlocks the 2× ceiling above.

The kernel is **memory-bound, not compute-bound**. The cost of nine register-resident stages is hidden in the streaming traffic at every meaningful concurrency level, and the only software lever left is moving less data — NUMA-local placement (above) is worth ~2.6× over a naive layout, where all traffic funnels through one socket's controllers and 20 workers regress below 10.

## Build

```
dotnet build FWHT.Net.slnx
```

Run the benchmark and verification (xUnit, requires AVX-512F):

```
dotnet test src/FWHT.Net.Bench/FWHT.Net.Bench.csproj -c Release
```

The default test streams a 64 MiB source. To reproduce the 100 GiB sweep above, raise `SIZE_BYTES` and extend `WorkerSweep` in `FwhtBenchmarkTests.cs`, and place the source and sink on separate tmpfs mounts so both fit in RAM.

## Status

Research artifact: a single kernel plus its benchmark harness. No published package, no CI; the API may change. AVX-512F is required — there is no fallback path.

## License

Apache-2.0. Copyright 2026 Infalligence Labs LLC — see [LICENSE](LICENSE).
