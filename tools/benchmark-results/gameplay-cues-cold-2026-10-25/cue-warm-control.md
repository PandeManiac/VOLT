```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
19/01 2.45GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Job=InProcess  Toolchain=InProcessEmitToolchain

```
| Method                  | ListenerCount | Mean      | Error     | StdDev    | Allocated |
|------------------------ |-------------- |----------:|----------:|----------:|----------:|
| **Execute_NoListeners**     | **1**             |  **8.108 ns** | **0.0364 ns** | **0.0323 ns** |         **-** |
| Execute_OneListener     | 1             | 48.906 ns | 0.2962 ns | 0.2626 ns |         - |
| AddAndRemovePersistent  | 1             | 88.994 ns | 1.3936 ns | 1.3036 ns |         - |
| BufferedEnqueueAndDrain | 1             | 18.963 ns | 0.2467 ns | 0.2308 ns |         - |
| SubscribeAndUnsubscribe | 1             | 25.697 ns | 0.2695 ns | 0.2250 ns |         - |
| **Execute_NoListeners**     | **1000**          |  **8.091 ns** | **0.0535 ns** | **0.0446 ns** |         **-** |
| Execute_OneListener     | 1000          | 50.272 ns | 0.8640 ns | 0.8081 ns |         - |
| AddAndRemovePersistent  | 1000          | 88.361 ns | 0.2324 ns | 0.1941 ns |         - |
| BufferedEnqueueAndDrain | 1000          | 18.957 ns | 0.0787 ns | 0.0614 ns |         - |
| SubscribeAndUnsubscribe | 1000          | 25.760 ns | 0.3712 ns | 0.3472 ns |         - |
