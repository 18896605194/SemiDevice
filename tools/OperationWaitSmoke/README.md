# Operation wait checks

Run from the repository root:

```powershell
dotnet run --project tools/OperationWaitSmoke
```

Checks synchronous success/failure, wait timeout versus action timeout,
Abort results, module cleanup before waking waiters, operation replacement, concurrent
terminal results, and response handling for all five LoadPort device actions plus the
online/offline (module mode) and auto/manual (LoadPort access mode) switches. Also walks the
E84 component through a load and an unload handoff with simulated signals, and covers gating
(EC switch, Manual, offline), abort, TP1/TP3 timeouts with the alarm, and Retry/Complete recovery.

Uses in-memory test modules and operations. Does not start the gRPC host, open hardware
connections, or load/write SC/EC configuration files. Exits nonzero on a failed check.
