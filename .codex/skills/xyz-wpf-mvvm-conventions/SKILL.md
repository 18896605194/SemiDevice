---
name: xyz-wpf-mvvm-conventions
description: Apply the xyz WPF project's MVVM and XAML conventions when creating or modifying ViewModels, commands, initialization, views, or styles under D:\Common.
---

# xyz WPF MVVM conventions

Use these rules for WPF client work in the xyz repository.

## Architecture / layering

- This is a front-end/back-end separated project. Client/UI projects must not reference the server-side `xyz.Service` project directly.
- Client calls backend through shared RPC/contracts in `xyz.Shared` (`RpcClient`, `IRpcService`, `RpcRequest`, `RpcResponse`).
- Service interfaces and DTOs belong in the contract layer `xyz.Shared`. Do not define service interfaces in Client projects.
- Keep `xyz.Shared` organized: service contracts in `xyz.Shared/Services`, DTO/models in `xyz.Shared/Models`, RPC transport helpers in `xyz.Shared/Rpc`.
- Use Mapster for model mapping on both front-end and back-end: `RoleEntity` (database) <-> `RoleInfo` (contract) <-> `RoleModel` (client display). Keep identity properties aligned (`long Id`) so mapping stays simple.
- For dedicated services, define a direct code-first gRPC contract in `xyz.Shared/Services` (e.g. `IRoleService` with `[ServiceContract]`/`[OperationContract]`). Client creates the proxy with `GrpcChannel.CreateGrpcService<T>()`; no custom JSON RPC proxy needed.
- `xyz.Service` is backend business service code. `xyz.GrpcHost` is the server host that references `xyz.Service` and maps its service classes with `MapGrpcService<T>()`.

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
- Name command execution methods with a `Do` prefix, e.g. `DoCreateUser`, `DoSaveRolePermissions`, `DoSelectAllPermissions`.
- Do not add `Do` to methods that are not directly bound to a command, such as `CanExecute` methods, helpers, service methods, or data-loading methods.
- Commands that may involve service calls, database access, or other blocking/IO work should use `AsyncRelayCommand` / `IAsyncRelayCommand`. Their `Do` methods must return `Task` and should not block the UI thread.

## UI text and bound data

- Keep static user-facing text in XAML, including prompts, validation wording, empty-state messages, button labels, and status sentence fragments.
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
- Register ViewModels in the corresponding module `ServiceCollectionExtensions.AddXxxServices()` and register each concrete ViewModel as a `BaseViewModel` alias for centralized initialization.
- Use the native DI container / `IocHelper`; do not let XAML create ViewModels directly.

## XAML resources

- Do not declare page-level or window-level styles/resources in business View XAML. Reusable component XAML such as `Wafer.xaml` is exempt when local resources are part of the component.
- Put control and page styles in `xyz.Client.Presentation/Styles` and merge them through the application/design-time resource dictionaries.
- Reference font sizes from `Styles/FontSize.xaml`; do not hard-code `FontSize` values in Views.
- Reference dark-theme colors from `Styles/DarkColors.xaml`; do not hard-code theme colors in Views.

When related existing code is touched, bring it into compliance with these conventions instead of adding a second coding pattern.
