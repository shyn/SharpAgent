# Handoff（2026-02-11）

## 本轮目标与结论

目标：

1. 为 `examples/Sharp.Gui` 的设置页改成双栏（master-detail）结构。
2. 新增 `Providers` 入口：左侧 provider 列表（built-in + config），右侧显示 provider 配置与模型列表。
3. 对 OAuth provider（当前 `google-antigravity`）在“无凭证”时显示登录按钮，并走真实 OAuth 登录。
4. 凭证不要写入 config；改为独立存储。

最终结论：以上目标已落地。当前实现为：

- GUI 提供真实 OAuth 登录入口（非手工粘贴 JSON）。
- OAuth 登录流程已内聚到 `Sharp.AI`，GUI 仅调用库接口。
- OAuth 凭证默认保存到 `~/.sharp/auth.json`（由 `Sharp.Core` 统一管理）。
- runtime 读取凭证优先级：`环境变量 > auth.json > providers[].apiKey(兼容)`。

---

## 关键改动

### 1) Settings UI 改为双栏 + Providers 详情页

文件：

- `/Users/deepwind/repo/SharpAgent/examples/Sharp.Gui/Views/SettingsView.axaml`
- `/Users/deepwind/repo/SharpAgent/examples/Sharp.Gui/ViewModels/SettingsViewModel.cs`

结果：

- 左侧为设置菜单（`General` / `Providers`）。
- `Providers` 页左侧显示 provider 列表（built-in + config）。
- 右侧显示选中 provider 的：
  - `id` / `api` / `baseUrl`
  - 凭证状态（env / auth store / legacy config）
  - model 列表
- 对 OAuth provider 且无凭证时显示 `Login with OAuth`。

### 2) `Sharp.AI` 内建 Antigravity OAuth 登录流程

新增文件：

- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityOAuthLoginService.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityOAuthConstants.cs`

并对齐/复用：

- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityBearerTokenSource.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityCredentialEnvelope.cs`

登录流程（对齐 `pi-mono/packages/ai/src/utils/oauth/google-antigravity.ts`）：

1. PKCE (`S256`)。
2. 打开浏览器到 Google OAuth 页面。
3. 本地回调监听 `http://localhost:51121/oauth-callback`。
4. `code` 换 `access_token`/`refresh_token`。
5. 查询 user email（可选）。
6. 调用 `loadCodeAssist` 发现 `projectId`（prod/sandbox 双端点回退）。
7. 产出 credential envelope（`access/refresh/expires/projectId/email`）。

### 3) OAuth 凭证独立存储（`auth.json`）

新增文件：

- `/Users/deepwind/repo/SharpAgent/Sharp.Core/Configuration/OAuthCredentialStore.cs`

关键改动：

- `/Users/deepwind/repo/SharpAgent/Sharp.Core/Configuration/AgentConfigurationService.cs`

新增能力：

- `DefaultAuthStorePath(string? agentDirectory = null)`，默认 `~/.sharp/auth.json`。
- `BuildRuntimeOptions` 支持从 auth store 读取 OAuth provider 凭证。
- `ValidateConfig` 新增可选参数 `agentDirectory`，校验时也会考虑 auth store。
- 错误提示会包含 OAuth 场景指引（auth store 路径）。

### 4) GUI 登录结果改写 `auth.json`，不改 config 凭证

文件：

- `/Users/deepwind/repo/SharpAgent/examples/Sharp.Gui/ViewModels/SettingsViewModel.cs`

行为：

- 登录成功后调用 `OAuthCredentialStore.SaveToFile(...)` 写入 `~/.sharp/auth.json`。
- 不再把 OAuth credential 写入 `providers[].apiKey`。
- Provider 详情状态优先显示：
  - 环境变量
  - auth store
  - config file（标记为 legacy）

---

## 当前语义与优先级

### 凭证解析优先级（runtime）

`AgentConfigurationService.BuildRuntimeOptions(...)`：

1. 环境变量（`SHARP_<PROVIDER>_*` / `<PROVIDER>_*` 及别名）
2. auth store（仅 OAuth provider，目前 `google-antigravity`）
3. `providers[].apiKey`（兼容兜底）

### auth store 路径

- 默认：`~/.sharp/auth.json`
- 代码入口：`AgentConfigurationService.DefaultAuthStorePath(...)`

### auth.json 结构

```json
{
  "version": 1,
  "providers": {
    "google-antigravity": {
      "credential": "{\"access\":\"...\",\"refresh\":\"...\",\"expires\":1730000000000,\"projectId\":\"...\",\"email\":\"...\"}",
      "updatedAt": "2026-02-11T00:00:00+00:00"
    }
  }
}
```

---

## 验证记录

### Build

1. GUI：

```bash
dotnet build /Users/deepwind/repo/SharpAgent/examples/Sharp.Gui/Sharp.Gui.csproj -m:1 -nr:false -v minimal -p:UsedAvaloniaProducts=
```

结果：通过（0 error）。

说明：在当前沙箱中，Avalonia telemetry 会因写入 `~/Library/Application Support/AvaloniaUI/BuildServices/buildtasks.log` 权限失败；使用 `-p:UsedAvaloniaProducts=` 规避 telemetry 任务。

2. Core.Tests：

```bash
dotnet build /Users/deepwind/repo/SharpAgent/Sharp.Core.Tests/Sharp.Core.Tests.csproj -m:1 -nr:false -v minimal
```

结果：通过（0 error）。

### Test（定向）

```bash
dotnet test /Users/deepwind/repo/SharpAgent/Sharp.Core.Tests/Sharp.Core.Tests.csproj -m:1 -nr:false -v minimal --filter "FullyQualifiedName~AgentConfigurationServiceTests.BuildRuntimeOptions_CanonicalGoogleAntigravityProvider_UsesAuthStoreCredential|FullyQualifiedName~AgentConfigurationServiceTests.ValidateConfig_CanonicalGoogleAntigravityProvider_WithAuthStoreCredential_IsValid"
```

结果：2 passed / 0 failed。

### 观测到的非阻断告警

- `NU1900`（nuget vulnerability source 不可达）
- `xUnit1031`（`CompactionIntegrationTests` 既有测试告警，与本改动无关）

---

## 本轮新增/修改测试

文件：

- `/Users/deepwind/repo/SharpAgent/Sharp.Core.Tests/AgentConfigurationServiceTests.cs`

新增用例：

1. `BuildRuntimeOptions_CanonicalGoogleAntigravityProvider_UsesAuthStoreCredential`
2. `ValidateConfig_CanonicalGoogleAntigravityProvider_WithAuthStoreCredential_IsValid`

---

## 仍需后续决策/工作

1. 是否彻底移除 OAuth provider 对 `providers[].apiKey` 的 fallback（目前保留兼容）。
2. `auth.json` 目前是明文存储；若要提高安全性，需要切 Keychain/DPAPI/SecretService 抽象。
3. GUI 目前只有 `login`，没有 `logout/remove credential`。
4. 若要“内建 provider 无需任何 config provider entry”，需要明确是否在 `LoadFromFile` 层做“built-in providers 强制并集”策略（当前运行时仍以 `Config.Providers` 为准）。

---

## 相关文件索引（本轮核心）

- `/Users/deepwind/repo/SharpAgent/examples/Sharp.Gui/Views/SettingsView.axaml`
- `/Users/deepwind/repo/SharpAgent/examples/Sharp.Gui/ViewModels/SettingsViewModel.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityOAuthLoginService.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityOAuthConstants.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityBearerTokenSource.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.AI/AntigravityCredentialEnvelope.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.Core/Configuration/OAuthCredentialStore.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.Core/Configuration/AgentConfigurationService.cs`
- `/Users/deepwind/repo/SharpAgent/Sharp.Core.Tests/AgentConfigurationServiceTests.cs`

---

## 工作区说明

当前仓库是脏工作区（存在大量与本轮无关改动）。本次 handoff 仅覆盖与 GUI 设置页、Antigravity OAuth、auth store 相关的增量。
