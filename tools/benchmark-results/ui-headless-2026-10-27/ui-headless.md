```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
19/01 2.45GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.403
  [Host]   : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3
  ShortRun : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v3

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                            | Mean         | Error        | StdDev       | Gen0    | Gen1   | Allocated |
|---------------------------------- |-------------:|-------------:|-------------:|--------:|-------:|----------:|
| WidgetChurn64Widgets              |   8,940.6 ns |   2,796.9 ns |    153.31 ns |  1.7700 | 0.0763 |   29696 B |
| LayoutDeepTree                    |     547.5 ns |     172.8 ns |      9.47 ns |       - |      - |         - |
| LayoutWideTree                    |   7,359.3 ns |   2,928.2 ns |    160.51 ns |       - |      - |         - |
| FrameIdle                         |  39,468.4 ns |  23,573.5 ns |  1,292.14 ns |       - |      - |         - |
| FrameAfterResize                  | 263,627.6 ns | 538,621.8 ns | 29,523.68 ns | 20.0195 | 6.3477 |  338704 B |
| HitTestDeepestWidget              |   7,321.8 ns |   1,606.7 ns |     88.07 ns |       - |      - |         - |
| TreeViewRebuild5000Rows           | 122,454.2 ns |  45,233.9 ns |  2,479.42 ns |       - |      - |         - |
| TreeViewFrame5000Items            |   1,893.1 ns |     408.6 ns |     22.40 ns |       - |      - |         - |
| InspectorFrame256Properties       |  64,966.3 ns |  11,940.8 ns |    654.52 ns |  0.8545 |      - |   15216 B |
| InspectorIdleRefresh256Properties |  38,100.2 ns |  39,498.4 ns |  2,165.04 ns |       - |      - |         - |
| DockLayoutSerialize               |   7,169.8 ns |   4,361.4 ns |    239.06 ns |  0.4044 |      - |    6800 B |
| DockLayoutDeserialize             |  10,912.3 ns |   4,041.4 ns |    221.52 ns |  0.3967 |      - |    6776 B |
| DockHostRebuildAndFrame           |  12,926.8 ns |   2,393.7 ns |    131.21 ns |  1.8158 | 0.1373 |   30448 B |
