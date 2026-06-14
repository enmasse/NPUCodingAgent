# NPU Coding Agent MVP

A minimal coding assistant that uses Foundry Local to start a local model and run locally on the device NPU, either through its local web endpoint or the in-process SDK client.

## Features

- 🤖 **Local AI inference** through Foundry Local with web-endpoint or in-process SDK execution
- 📡 **Smoke verification** with a quick ping command
- 🧠 **Best-effort NPU detection** from Foundry Local runtime metadata
- 💬 **Interactive chat** with coding assistance
- 📁 **Workspace awareness** - list and read project files
- ✏️ **AI-assisted editing** - modify files with confirmation and automatic backups
- 🔒 **Safe operations** - file size limits, path validation, backup creation

## Prerequisites

- **Windows 11** (22H2 or later) with DirectML support
- **NPU-capable hardware** (or GPU/CPU fallback)
- **.NET 8** SDK or later
- **Windows AI Foundry Local SDK support**

## Setup

### 1. Restore the Foundry Local packages

The app uses the documented Windows packages:
- `Microsoft.AI.Foundry.Local.WinML`
- `OpenAI`

### 2. Choose a model alias

Optionally set this environment variable before running:
- `FOUNDRY_LOCAL_MODEL=phi-3.5-mini`

By default the app uses `phi-3.5-mini`.

### 3. Run the app

On first run, Foundry Local can download execution providers, start the model, and expose a local OpenAI-compatible endpoint dynamically.

### 4. Build and Run

```bash
dotnet build
dotnet run --project NPUCodingAgent
```

## Usage

### Commands

| Command | Description |
|---------|-------------|
| `<message>` | Chat with the coding assistant |
| `/list` | List all source files in the workspace |
| `/read <file>` | Read and display a specific file |
| `/status` | Show the active model and local runtime details |
| `/ping` | Run a quick model smoke test |
| `/models` | List available Foundry Local model aliases |
| `/edit <file>` | Edit a file with AI assistance |
| `help` | Show help message |
| `exit` | Exit the application |

### Example Workflow

```
> /list
Found 15 files:
  Program.cs
  Services\LocalModelService.cs
  ...

> /read Program.cs
--- Program.cs ---
[file contents]
--- End ---

> /status
Provider: Foundry Local SDK
Accelerator: NPU (QNN)
NPU: Detected

> /ping
Ping response: pong

> /edit Program.cs
Describe the change you want: Add error handling to the Main method
[AI generates proposed changes]
Apply these changes? (yes/no): yes
✓ Backup created: Program.cs.backup
✓ File updated successfully

> How does the LocalModelService work?
Assistant: The LocalModelService is responsible for...
```

## Architecture

```
NPUCodingAgent/
├── Program.cs                      # Entry point and command loop
├── Services/
│   ├── LocalModelService.cs       # Foundry Local SDK integration
│   ├── WorkspaceService.cs        # File discovery and reading
│   └── FileEditService.cs         # Safe file writing with backups
└── NPUCodingAgent.csproj          # Project configuration
```

### Key Components

- **LocalModelService**: Starts Foundry Local, lists available aliases, loads the selected model, and talks to the local model through the SDK runtime
- **LocalModelService**: Starts Foundry Local, lists available aliases, loads the selected model, surfaces runtime metadata, and talks to the local model through the SDK runtime
- **WorkspaceService**: Safely enumerates and reads files with extension filtering and size limits
- **FileEditService**: Handles file modifications with automatic backup creation
- **Program**: Orchestrates the interactive loop and command dispatching

## Configuration

### Supported File Extensions

The workspace service scans for these file types:
- `.cs`, `.csproj`, `.sln`, `.slnx`
- `.json`, `.xml`, `.txt`, `.md`
- `.js`, `.ts`, `.html`, `.css`
- `.py`, `.java`, `.cpp`, `.h`

### Limits

- **Max file size**: 1 MB (for reading and writing)
- **Chat history**: 20 turns (sliding window)
- **Max response length**: 2048 tokens

### Foundry Local Runtime

- The app does not hardcode a localhost port.
- Foundry Local may expose a local web endpoint at runtime, or it may use the in-process SDK client.
- Use `/status` to see the active model and runtime details.
- Use `/ping` for a quick connectivity check against the active model.
- Use `/models` to inspect available model aliases.

### NPU Detection

- The app reports **best-effort** NPU detection from Foundry Local runtime metadata.
- It uses the selected model variant's `DeviceType` and `ExecutionProvider` values.
- A result like `NPU (QNN)` is a strong signal that the model is running on the NPU.
- `no / unknown` means Foundry Local did not expose enough metadata to confirm NPU usage.

### Generation Parameters

- **Temperature**: 0.7 (balance between creativity and consistency)
- **Max length**: 2048 tokens

## Safety Features

1. **Path Validation**: Prevents directory traversal attacks
2. **File Size Limits**: Prevents loading/writing extremely large files
3. **Automatic Backups**: Creates `.backup` files before any modification
4. **Explicit Confirmation**: User must approve all file edits
5. **Directory Filtering**: Skips `bin`, `obj`, `node_modules`, `.git`, `.vs`, `packages`

## Troubleshooting

### Model Not Available
```
Error initializing Foundry Local
```
**Solution**: Verify the configured model alias exists in the Foundry Local catalog and allow the first-run download to complete.

### NPU Not Available
The application will fall back to GPU or CPU if NPU is not available. Performance may vary.

### Build Errors
Ensure you have:
- .NET 8 SDK installed
- Windows 10.0.26100.0 SDK or later
- Visual Studio 2026 or compatible tooling

## Performance Notes

- **NPU**: Best performance and power efficiency on Copilot+ PCs
- **GPU**: Good performance via DirectML on any DirectX 12 capable GPU
- **CPU**: Slower but works on any system

## License

This is an MVP demonstration project. For production use, review and update as needed.

## Resources

- [Foundry Local quickstart](https://learn.microsoft.com/en-us/azure/foundry-local/get-started)
