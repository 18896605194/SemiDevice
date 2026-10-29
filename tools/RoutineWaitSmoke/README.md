# Operation wait checks

Run from the repository root:

```powershell
dotnet run --project tools/RoutineWaitSmoke
```

Checks synchronous success/failure, wait timeout versus action timeout,
Abort results, module cleanup before waking waiters, operation replacement, concurrent
terminal results, and response handling for all five LoadPort device actions plus the
online/offline (module mode) and auto/manual (LoadPort access mode) switches. Also walks the
E84 element through a load and an unload handoff with simulated signals, and covers gating
(EC switch, Manual, offline), abort, TP1/TP3 timeouts with the alarm, and Retry/Complete recovery.
Alarms: LoadPort/Robot/E84 alarms stay active until a manual Reset (element Reset, reset by
source, reset all), a condition still present is raised again on the next scan, and DI/AI alarms
fire only after their EC debounce time and count toward the owning module's HasAlarm.
Finally checks the EC element: live read/write through EC properties, declaration merge
(fills defaults, keeps existing values), fallback when no EC element is installed, and an
ec.xml write/reload round trip. Also checks the element Init/Abort hooks: children first (Init in
InitOrder), overriding is optional, a module's Init runs Home, and Abort never clears alarms.

Uses in-memory test modules and operations; EC values live in an in-memory EC element, and the
round trip uses a temporary file. Does not start the gRPC host, open hardware connections, or
load/write the machine.xml / ec.xml configuration files. Exits nonzero on a failed check.
