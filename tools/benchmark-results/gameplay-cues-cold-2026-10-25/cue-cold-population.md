```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
19/01 2.45GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Job=InProcess  Toolchain=InProcessEmitToolchain  InvocationCount=1
UnrollFactor=1

```
| Method                       | Population | Mean        | Error     | StdDev    | Gen0      | Gen1      | Gen2      | Allocated   |
|----------------------------- |----------- |------------:|----------:|----------:|----------:|----------:|----------:|------------:|
| **BuildPersistentCuePopulation** | **1000**       |    **235.5 μs** |   **4.69 μs** |   **8.81 μs** |         **-** |         **-** |         **-** |   **306.89 KB** |
| **BuildPersistentCuePopulation** | **10000**      |  **4,337.2 μs** |  **86.73 μs** | **251.61 μs** |         **-** |         **-** |         **-** |  **4866.98 KB** |
| **BuildPersistentCuePopulation** | **100000**     | **20,113.5 μs** | **399.86 μs** | **789.28 μs** | **1000.0000** | **1000.0000** | **1000.0000** | **38922.53 KB** |
