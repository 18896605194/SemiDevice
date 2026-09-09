# Log pipeline checks

Run from the repository root:

```powershell
dotnet run --project tools/LogPipelineSmoke
```

In-process checks (no host, no hardware):

- backend queue order and no loss under a single producer (1000 items);
- no loss/duplication under 4 concurrent producers;
- `LogQueue.MinLevel` drops lower levels before they reach the queue;
- `LogHelper` enqueues through the same path;
- the client queue is consumed by `LogViewModel` (UI is the consumer, no polling);
- `LogDto` survives the `EventEnvelope` JSON round-trip used across processes;
- `LogQueue.Stop()` stops delivery.

Cross-process check against a running backend:

```powershell
dotnet run --project tools/LogPipelineSmoke -- client 8
```

Subscribes to the backend event stream with `RemoteEventBus` and prints how many
`LogDto` messages arrived in the given number of seconds (exits nonzero when none did).
