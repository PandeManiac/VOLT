```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
19/01 2.45GHz, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.402
  [Host] : .NET 10.0.13 (10.0.13, 10.0.1326.50504), X64 RyuJIT x86-64-v3

Job=InProcess  Toolchain=InProcessEmitToolchain  InvocationCount=1
UnrollFactor=1

```
| Method                                | Mean        | Error       | StdDev      | Median      | Allocated |
|-------------------------------------- |------------:|------------:|------------:|------------:|----------:|
| FirstConstructionAndInitialization    | 11,450.3 ns | 1,308.13 ns | 3,795.13 ns | 13,275.0 ns |   14928 B |
| FirstExecuteWithoutListeners          |    970.7 ns |    45.29 ns |   126.24 ns |    968.0 ns |         - |
| FirstExecuteWithOneListener           |  3,305.6 ns |   104.77 ns |   286.80 ns |  3,206.0 ns |         - |
| FirstHierarchicalDispatch             |  3,588.9 ns |    76.50 ns |    75.14 ns |  3,577.0 ns |         - |
| FirstPersistentCueAdd                 |  1,896.7 ns |    43.08 ns |    78.77 ns |  1,883.0 ns |         - |
| FirstPersistentCueUpdate              |    998.1 ns |    24.69 ns |    56.74 ns |  1,001.0 ns |         - |
| FirstPersistentCueRemove              |  1,516.8 ns |    36.80 ns |   101.36 ns |  1,532.5 ns |         - |
| FirstSubscriptionCreation             |  3,777.8 ns |   122.21 ns |   350.66 ns |  3,748.0 ns |     112 B |
| FirstDictionaryExpansion              |  4,386.8 ns |   282.69 ns |   820.13 ns |  4,252.0 ns |     192 B |
| FirstCueSlotExpansion                 |  4,869.2 ns |   107.86 ns |   309.47 ns |  4,840.0 ns |    4888 B |
| FirstPresentationEnqueueAndDrain      |  1,896.2 ns |    94.29 ns |   265.94 ns |  1,889.0 ns |         - |
| FirstPresentationBufferExpansion      |  2,705.7 ns |    74.59 ns |   215.21 ns |  2,690.0 ns |     312 B |
| FirstEffectBackedCueCreation          | 11,371.6 ns |   230.16 ns |   602.30 ns | 11,356.0 ns |   28984 B |
| FirstEffectBackedCueExpiration        |  5,664.7 ns |   157.98 ns |   450.74 ns |  5,566.0 ns |     288 B |
| FirstEffectBackedCueRemoval           |  5,216.6 ns |   123.58 ns |   356.56 ns |  5,224.0 ns |     336 B |
| FirstActivationOwnedCueCreation       | 17,764.5 ns |   361.92 ns |   824.28 ns | 17,828.0 ns |   70160 B |
| FirstActivationOwnedCueCleanup        |  8,853.3 ns |   181.45 ns |   478.00 ns |  8,677.0 ns |    2384 B |
| RepeatedIndependentColdInitialization |  1,791.2 ns |    16.22 ns |    13.54 ns |  1,794.1 ns |   15093 B |
