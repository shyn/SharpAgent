# Deployment

This guide covers installing and running TinyClaw in various configurations.

## Prerequisites

- Windows 10/11 or Windows Server 2019+
- .NET 8.0 Runtime
- API keys for your chosen providers:
  - [Anthropic API Key](https://console.anthropic.com/) for Claude
  - [OpenAI API Key](https://platform.openai.com/) for GPT models

## Installation Methods

### Method 1: Windows Service (Recommended for Production)

1. **Build the solution**:
   ```powershell
   dotnet publish src/TinyClaw.Service -c Release -o C:\TinyClaw
   dotnet publish src/TinyClaw.App -c Release -o C:\TinyClaw\UI
   ```

2. **Install as Windows Service**:
   ```powershell
   sc create TinyClaw binPath= "C:\TinyClaw\TinyClaw.Service.exe" start= auto
   sc start TinyClaw
   ```

3. **Configure**:
   - Run `C:\TinyClaw\UI\TinyClaw.App.exe`
   - Go to Settings
   - Add API keys for your providers
   - Add bot tokens and configure agents
   - Save settings

4. **Restart service**:
   ```powershell
   sc stop TinyClaw
   sc start TinyClaw
   ```

### Method 2: Standalone Application

Run the service as a console application:

```powershell
cd src/TinyClaw.Service
dotnet run
```

Or use the published executable:

```powershell
.\TinyClaw.Service.exe
```

### Method 3: Docker (Planned)

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY bin/Release/net8.0/publish/ /app
WORKDIR /app
ENTRYPOINT ["dotnet", "TinyClaw.Service.dll"]
```

## File Locations

### Configuration Directory
`C:\ProgramData\TinyClaw\`

| File/Directory | Purpose |
|----------------|---------|
| `settings.json` | Main configuration file |
| `tinyclaw.db` | SQLite message queue database |
| `files\` | Downloaded attachments |
| `logs\` | Application logs |

### Application Directory
Default installation: `C:\TinyClaw\`

```
C:\TinyClaw\
├── TinyClaw.Service.exe    # Windows Service executable
├── TinyClaw.Core.dll       # Shared library
├── TinyClaw.App.exe        # WPF UI (in UI\ subfolder)
└── ...
```

### Session Storage
`{workspace}\.sessions\`

Session files are stored as JSONL files for conversation persistence.

## Configuration

### Initial Setup

1. **Create workspace directory**:
   ```powershell
   mkdir C:\TinyClawWorkspace
   ```

2. **Configure via UI**:
   - Run `TinyClaw.App.exe` as Administrator (first time only)
   - Set workspace path
   - Add API keys (Anthropic/OpenAI)
   - Add bot tokens
   - Configure agents

3. **Or configure via settings.json**:
   ```json
   {
     "workspace": {
       "path": "C:\\TinyClawWorkspace"
     },
     "models": {
       "api_keys": {
         "anthropic": "YOUR_ANTHROPIC_API_KEY"
       },
       "anthropic": {
         "model": "sonnet"
       }
     },
     "channels": {
       "telegram": {
         "bot_token": "YOUR_TELEGRAM_TOKEN"
       }
     }
   }
   ```

### API Key Configuration

API keys can be configured in three ways (in priority order):

1. **Per-agent in settings.json**:
   ```json
   {
     "agents": {
       "my-agent": {
         "api_key": "sk-ant-api..."
       }
     }
   }
   ```

2. **Global in settings.json**:
   ```json
   {
     "models": {
       "api_keys": {
         "anthropic": "sk-ant-api...",
         "openai": "sk-..."
       }
     }
   }
   ```

3. **Environment variables**:
   ```powershell
   $env:ANTHROPIC_API_KEY="sk-ant-api..."
   $env:OPENAI_API_KEY="sk-..."
   ```

## Service Management

### Using sc.exe

```powershell
# Create service
sc create TinyClaw binPath= "C:\TinyClaw\TinyClaw.Service.exe" start= auto

# Start service
sc start TinyClaw

# Stop service
sc stop TinyClaw

# Delete service
sc delete TinyClaw
```

### Using PowerShell

```powershell
# Create service
New-Service -Name "TinyClaw" -BinaryPathName "C:\TinyClaw\TinyClaw.Service.exe" -StartupType Automatic

# Start service
Start-Service -Name "TinyClaw"

# Stop service
Stop-Service -Name "TinyClaw"

# Remove service
Remove-Service -Name "TinyClaw"
```

### Using Services.msc

1. Press `Win + R`, type `services.msc`
2. Find "TinyClaw" in the list
3. Right-click for Start/Stop/Restart options

## Permissions

### Service Account

By default, the service runs as `LocalSystem`. To use a specific account:

```powershell
sc config TinyClaw obj= "DOMAIN\Username" password= "Password"
```

### Required Permissions

| Resource | Permission |
|----------|------------|
| `C:\ProgramData\TinyClaw\` | Full Control |
| `C:\TinyClawWorkspace\` | Full Control |
| `{workspace}\.sessions\` | Full Control |

## Logging

Logs are stored in `C:\ProgramData\TinyClaw\logs\`

View logs:
```powershell
Get-Content "C:\ProgramData\TinyClaw\logs\tinyclaw.log" -Tail 50
```

Or use the WPF UI Logs page.

## Updating

1. **Stop the service**:
   ```powershell
   sc stop TinyClaw
   ```

2. **Backup configuration**:
   ```powershell
   Copy-Item "C:\ProgramData\TinyClaw\settings.json" "C:\ProgramData\TinyClaw\settings.json.bak"
   ```

3. **Replace binaries**:
   ```powershell
   dotnet publish src/TinyClaw.Service -c Release -o C:\TinyClaw
   ```

4. **Start the service**:
   ```powershell
   sc start TinyClaw
   ```

## Troubleshooting

### Service won't start

Check Event Viewer:
```powershell
Get-EventLog -LogName Application -Source "TinyClaw" -Newest 10
```

Common issues:
- Missing .NET 8.0 runtime
- Incorrect path in service configuration
- Missing permissions to config directory

### UI can't save settings

Run as Administrator:
```powershell
Start-Process "C:\TinyClaw\UI\TinyClaw.App.exe" -Verb RunAs
```

### Agents not responding

1. Check API keys are configured:
   - Verify in settings.json or environment variables
   - Ensure keys have available credits

2. Check service logs for errors

3. Verify working directories exist and have correct permissions

### Model errors

1. Verify the model ID is correct for the provider
2. Check that your API keys are valid
3. Verify network connectivity to API endpoints
4. Check if using a custom `base_url` that it's correct

### Session issues

1. Check session directory exists:
   ```powershell
   Test-Path "C:\TinyClawWorkspace\.sessions"
   ```

2. Verify service has write permissions to session directory

3. Review session files for corruption

## Security Best Practices

1. **Use a dedicated service account** instead of LocalSystem
2. **Restrict config directory permissions** to service account only
3. **Use environment variables** for sensitive tokens in CI/CD
4. **Keep API keys secure**: Don't commit them to version control
5. **Enable Windows Firewall** and restrict access to necessary ports
6. **Rotate API keys** regularly

## Uninstallation

1. Stop and remove service:
   ```powershell
   sc stop TinyClaw
   sc delete TinyClaw
   ```

2. Remove application files:
   ```powershell
   Remove-Item -Recurse "C:\TinyClaw"
   ```

3. Optionally remove data:
   ```powershell
   Remove-Item -Recurse "C:\ProgramData\TinyClaw"
   Remove-Item -Recurse "C:\TinyClawWorkspace"
   ```
