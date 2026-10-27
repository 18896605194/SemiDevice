---
name: xyz-wpf-mvvm-conventions
description: Apply the xyz WPF project's MVVM and XAML conventions when creating or modifying ViewModels, commands, initialization, views, or styles in this repository.
---

# xyz WPF MVVM conventions

Use these rules for WPF client work in the xyz repository.

## Architecture / layering

- This is a front-end/back-end separated project. Client/UI projects must not reference any backend project (`xyz.Service`, `xyz.Modules`, `xyz.Elements`, ...); they only use the contracts in `xyz.Shared`.
- Client calls the backend through the code-first gRPC contracts in `xyz.Shared/Services`, getting the proxy from `GrpcClientFactory.Create<IXxxService>()` (`xyz.Client.Common`, it wraps `CreateGrpcService<T>()`); do not build channels or proxies yourself.
- Every call takes `RpcRequest` and returns `RpcResponse`; there are no per-call request classes. Parameters go by name into `RpcRequest.Parameters`, and the names are the constants in `xyz.Shared.Rpc.RpcParams`: client `RpcRequest.Of((RpcParams.Module, name))`, server `request.TryGet<int>(RpcParams.Index, out var index)`.
- Service interfaces and DTOs belong in the contract layer `xyz.Shared`. Do not define service interfaces in Client projects.
- Keep `xyz.Shared` organized: service contracts in `xyz.Shared/Services`, DTOs (including `RpcRequest`, `RpcResponse`, `HandleResult`) in `xyz.Shared/Dtos`, error codes in `xyz.Shared/Errors`, permission codes in `xyz.Shared/Permissions`, `RpcParams` / `RpcHeaders` in `xyz.Shared/Rpc`.
- Mapping uses Mapster. Client: `dto.Adapt<XxxModel>()` turns a contract DTO into a display model. Backend: go through the mapper element (`IMapper`; platform rules in `MapperElement.AddRules`, which projects override) instead of hand-written `ToDto` methods. Example chain: `RoleEntity` (database, `xyz.Database`) -> DTO in `xyz.Shared/Dtos` -> `RoleModel` (client display). Keep identity properties aligned (`long Id`) so mapping stays simple.
- A service contract looks like `IRoleService`: `[ServiceContract]`, each method `[OperationContract] Task<RpcResponse> XxxAsync(RpcRequest request, CallContext context = default)`.
- `xyz.Service` holds the service implementations; they inherit `BaseService` and reply with its `Reply` / `ReplyData` / `Fail` / `NotInstalled` / `InvalidRequest`. `xyz.GrpcHost` finds the one implementation of each contract and maps it at startup, so adding a service needs no registration.

## ViewModels

- Every ViewModel must inherit the shared `xyz.Client.DataModels.ViewModels.BaseViewModel`. Do not inherit `ObservableObject` directly in a ViewModel.
- Do not use `[ObservableProperty]`, `[RelayCommand]`, or other source-generator attributes for properties and commands.
- Write observable properties with a private backing field, a public property, and `SetProperty`. Put property-change side effects and command-state refreshes in the property setter.
- Every observable property consists of a pair that must stay together: the private backing field immediately above the public property. Do not group all private fields separately from their properties.
- Expose commands as `IRelayCommand` or `IAsyncRelayCommand` properties. Instantiate them explicitly with `new RelayCommand(...)` or `new AsyncRelayCommand(...)` in the constructor.
- A ViewModel constructor may instantiate collections, dependencies, objects, and commands only. Do not load menus, users, roles, sample data, database data, or service data there.
- Put data loading in an override of `Init()`. Invoke `Init()` from the View or Window after the DataContext and constructor-created objects are ready. Never call a virtual `Init()` from the `BaseViewModel` constructor.

## ViewModel file layout

- Organize every ViewModel with `#region` blocks in this order:
  1. `#region Column` — observable properties, backing fields, and bound display/column data.
  2. `#region Command` — command properties and related command definitions.
  3. `#region Service` — only service/dependency declarations (fields), not methods.
  4. Constructor — below the regions; may instantiate collections, dependencies, objects, and commands.
- All methods (`Init`, `Can...`, `Do...`, helpers, data loading) go below the constructor, not inside `#region Service`.
- Name command execution methods with a `Do` prefix, e.g. `DoCreateUser`, `DoCreateRole`, `DoSave`.
- Do not add `Do` to methods that are not directly bound to a command, such as `CanExecute` methods, helpers, service methods, or data-loading methods.
- Commands that may involve service calls, database access, or other blocking/IO work should use `AsyncRelayCommand` / `IAsyncRelayCommand`. Their `Do` methods must return `Task` and should not block the UI thread.

## UI text and bound data

- Keep static user-facing text in XAML, including prompts, validation wording, empty-state messages, button labels, and status sentence fragments, and take it from the language pack: `{DynamicResource setting.role.create}` in XAML, `L10n.Get(key, args)` in code. Every key exists in both `Strings.zh-CN.xaml` and `Strings.en-US.xaml` (platform text in `xyz.Client.Presentation/Localization`, machine-only text in the machine project's `Presentation/Localization`).
- ViewModels expose only the state and data needed for display, such as booleans, names, counts, selected objects, and operation results. Compose the final sentence in XAML with bindings, runs, templates, and external styles.
- Domain data such as menu names, role descriptions, and values returned by a service may remain in models or ViewModels because it is data rather than fixed interface copy.

## Views and DI

- Do not declare `<UserControl.DataContext>` in View XAML.
- In the View constructor, only assign `DataContext` from the IoC container:
  ```csharp
  DataContext = IocHelper.GetRequiredService<XxxViewModel>();
  ```
- Do not call `Init()` inside View constructors. After DI registration is complete, retrieve all registered `BaseViewModel` instances centrally and call `Init()` once:
  ```csharp
  foreach (var viewModel in Services.GetServices<BaseViewModel>())
  {
      viewModel.Init();
  }
  ```
- Register ViewModels in the corresponding module `ServiceExtensions.AddXyzXxxServices()` (e.g. `AddXyzAlarmServices`) and register each concrete ViewModel as a `BaseViewModel` alias for centralized initialization.
- Use the native DI container / `IocHelper`; do not let XAML create ViewModels directly.
- All pages stay in the main window's content area (`PageHost`) from startup and are laid out once; switching menus only toggles `Visibility` (current `Visible`, others `Hidden`). So `Loaded` / `Unloaded` fire once, not per navigation — to react to a page being shown or hidden use `IsVisibleChanged`, and stop per-frame work (e.g. `CompositionTarget.Rendering`) while `IsVisible` is false.

## Text / number input

- Use `xyz.Client.Presentation.Controls.InputTextBox` for every text or number input; do not add bare `TextBox` input fields.
- Bind `Value` (the committed legal value, two-way by default), never `Text` (`Text` is the raw text being typed and carries the control's validation).
- Set `DataType` (`Text` / `Integer` / `Decimal`). Range limits (`Minimum` / `Maximum`) and `Unit` only apply to numbers; `Text` has no range.
- For EC-backed parameters set `EcKey="<element full path>.<parameter name>"` (e.g. `LoadPort1.LoadTimeout`); type, limits and unit come from the EC definition. Values written in XAML override EC; nothing configured means no validation.
- Validation: format is checked while typing (only input that can never become valid is flagged); range is checked on commit (Enter / focus loss). Invalid input shows the red error and is not passed to `Value`, which keeps the last legal value.
- When a command sends the value (e.g. a "send" button), bind `HasError` with `Mode=OneWayToSource` and do not send while it is true. Inside table rows set `materialDesign:ValidationAssist.UsePopup="True"` so the message is not clipped.

## Time ranges and charts

- For a time-range query use two `xyz.Client.Presentation.Controls.DateTimePicker` (date box + plain hour 00–23 and minute 00–59 dropdowns; no clock-dial time picker) bound to `DateTime?` `Value`; do not use a bare `DatePicker`. Build the query range with `QueryDateRange.Of(start, end)`: minute precision, the end minute is included (`[start, end + 1 min)`); `QueryDateRange.Today()` / `LastDays(n)` / `LastHours(n)` give the toolbar shortcuts.
- Plot time series with `xyz.Client.Presentation.Controls.TrendChart` (ScottPlot 5 wrapper) fed an `ObservableCollection<TrendSeries>`; do not use ScottPlot directly in a page. `TrendSeries` holds OADate `Xs` / `Ys` (`NaN` = gap) and the info-row values (`CursorValue`, `Min`, `Max`, `Avg`); change data only through `SetData` / `Append`. The chart assigns colors and plots every series, analog and digital, on the single left Y axis: no per-unit or right-side axes, and no automatic lanes or hidden axes for digital signals (a digital series is simply a step line at 0 and 1; the unit is shown on the axis only when all curves share it). By default a line sits at its real value; only a user-entered `TrendSeries.Offset` (info table) shifts it to value + offset (drawn with the plot's own `OffsetY`, data not copied), while the hover card and info table keep showing real values. Reference lines (`ShowXLines` / `ShowYLines`) live in `TrendChart.ReferenceLines.cs`. It reports `VisibleStart` / `VisibleEnd` / `CursorTime` and `FollowRange` back two-way. Built-in interaction (keep it, do not add operation hint text on the chart): sample dots when points are not crowded, hover snaps to the nearest sample and shows a value card (time, name, value), wheel zooms time, left-drag pans, middle-drag box-zooms (time + value range), double-click resets.
- Data pipelines in chart pages use Rx (`System.Reactive`): query requests go through `Throttle` + `Select(Observable.FromAsync(...))` + `Switch()` so a newer request cancels the older one; hop back to the UI thread with `ObserveOn(SynchronizationContext)` before touching bound objects.

## XAML resources

- Do not declare page-level or window-level styles/resources in business View XAML. Reusable component XAML such as `Wafer.xaml` is exempt when local resources are part of the component.
- Put control and page styles in `xyz.Client.Presentation/Styles` and merge them through the application/design-time resource dictionaries.
- Reference font sizes from `Styles/FontSize.xaml`; do not hard-code `FontSize` values in Views.
- A page's top toolbar is a `Border` with `ToolbarBorderStyle` and uses the 34 px toolbar control styles (`ToolbarButtonStyle`, `ToolbarDangerButtonStyle`, `ToolbarComboBoxStyle`, `ToolbarTextBoxStyle`, `DateTimePicker`) with `Margin="0,0,10,0"` between inputs; keep toolbars low, do not use the 40–44 px default controls there.
- Reference dark-theme colors from `Styles/DarkColors.xaml`; do not hard-code theme colors in Views.

When related existing code is touched, bring it into compliance with these conventions instead of adding a second coding pattern.
