using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LinearPipe;

namespace Fwht.Bench;

public readonly struct PipelineSubject : IBenchmarkSubject
{
    private readonly GCHandle _pipeline;

    public PipelineSubject(LinearPipeline pipeline)
    {
        _pipeline = GCHandle.Alloc(pipeline);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Run()
    {
        ((LinearPipeline)_pipeline.Target!).Flow();
    }

    public void Free()
    {
        _pipeline.Free();
    }
}
