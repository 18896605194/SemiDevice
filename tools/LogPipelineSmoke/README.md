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
- `LogDto` survives the envelope JSON round-trip used across processes (`RemoteEventPublisher` → `RemoteEventSubscriber`);
- `LogQueue.Stop()` stops delivery.

Cross-process check against a running backend:

```powershell
dotnet run --project tools/LogPipelineSmoke -- client 8
```

Builds the real client composition (`RemoteEventConnection` → `RemoteEventSubscriber` → `ClientLog` → `LogViewModel`),
waits the given number of seconds, then prints:

- `connected`: event stream state;
- `viewModel.Logs`: how many log lines the UI view model ended up with — this includes
  backend history pulled on connect via `ILogService.GetRecentAsync`, so a client that
  starts *after* the backend still sees earlier errors;
- `GetRecent.Success` / `Data`: a direct history-query probe against the backend;
- `sample`: the newest line the UI would show.

Exits nonzero when nothing arrived. To exercise the history path, start the backend first
with a broken port (`machine.xml` `PortName=COM99`), wait a few seconds, then run the client:
the startup `驱动连接失败` errors are only reachable through history.
