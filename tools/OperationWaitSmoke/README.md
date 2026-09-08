# Operation wait checks

Run from the repository root:

```powershell
dotnet run --project tools/OperationWaitSmoke
```

Checks synchronous success/failure, wait timeout versus action timeout, cancellation,
Abort results, module cleanup before waking waiters, operation replacement, concurrent
terminal results, and response handling for all seven LoadPort RPC methods.

Uses in-memory test modules and operations. Does not start the gRPC host, open hardware
connections, or load/write SC/EC configuration files. Exits nonzero on a failed check.
