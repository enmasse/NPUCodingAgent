# NPU Coding Agent MVP

A minimal coding assistant that uses Foundry Local to start a local model and run locally on the device NPU, either through its local web endpoint or the in-process SDK client.

This repository includes two applications:
- **NPUCodingAgent**: Interactive coding assistant with chat, file workspace operations, and AI-powered editing
- **NPUCodingAgent.Embeddings**: Text embedding explorer for semantic similarity analysis

## Features

### Coding Agent (NPUCodingAgent)
- 🤖 **Local AI inference** through Foundry Local with web-endpoint or in-process SDK execution
- 📡 **Smoke verification** with a quick ping command
- 🧠 **Best-effort NPU detection** from Foundry Local runtime metadata
- 💬 **Interactive chat** with coding assistance
- 📁 **Workspace awareness** - list and read project files
- ✏️ **AI-assisted editing** - modify files with confirmation and automatic backups
- 🔒 **Safe operations** - file size limits, path validation, backup creation

### Embeddings Explorer (NPUCodingAgent.Embeddings)
- 🔢 **Text embedding generation** using qwen3-embedding-0.6b on NPU
- 📊 **Pairwise similarity analysis** with cosine similarity metrics
- 📐 **Vector angle computation** in both radians and degrees
- 🎯 **Multi-sentence comparison** for semantic relationship exploration

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

#### For Coding Agent (NPUCodingAgent)
Optionally set this environment variable before running:
- `FOUNDRY_LOCAL_MODEL=phi-3-mini-128k-instruct-qnn-npu:4`

By default the app uses `phi-3-mini-4k`.

You can also inspect the local Foundry catalog at runtime with `/models` and switch to any selectable alias or variant ID with `/use <model>`.

#### For Embeddings Explorer (NPUCodingAgent.Embeddings)
Optionally set this environment variable before running:
- `FOUNDRY_LOCAL_EMBEDDING_MODEL=qwen3-embedding-0.6b`

By default the embeddings app uses `qwen3-embedding-0.6b`.

### 3. Run the app

On first run, Foundry Local can download execution providers, start the model, and expose a local OpenAI-compatible endpoint dynamically.

### 4. Build and Run

#### Coding Agent
```bash
dotnet build
dotnet run --project NPUCodingAgent
```

#### Embeddings Explorer
```bash
dotnet build
dotnet run --project NPUCodingAgent.Embeddings
```

Or run with command-line arguments:
```bash
dotnet run --project NPUCodingAgent.Embeddings -- "The quick brown fox" "A fast auburn canine" "The weather is sunny"
```

## Usage

### Coding Agent Commands

| Command | Description |
|---------|-------------|
| `<message>` | Chat with the coding assistant |
| `/list` | List all source files in the workspace |
| `/read <file>` | Read and display a specific file |
| `/status` | Show the active model and local runtime details |
| `/ping` | Run a quick model smoke test |
| `/models` | List selectable Foundry Local aliases and variant IDs |
| `/use <model>` | Switch to a specific Foundry Local model alias or ID |
| `/edit <file>` | Edit a file with AI assistance |
| `help` | Show help message |
| `exit` | Exit the application |

### Coding Agent Example Workflow

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

### Embeddings Explorer Usage

The embeddings explorer accepts 2 or more sentences either from command-line arguments or interactive prompts. It generates embeddings using the configured model (default: `qwen3-embedding-0.6b`) and displays pairwise comparisons.

#### Command-line Arguments
```bash
dotnet run --project NPUCodingAgent.Embeddings -- "The cat sits on the mat" "A feline rests on the rug" "The weather is sunny"
```

#### Interactive Mode
```bash
dotnet run --project NPUCodingAgent.Embeddings
```
Then enter sentences one per line, press Enter on empty line when done (minimum 2 sentences required).

#### Example Output
```
┌─ Pairwise Embedding Comparisons ───────────────────────────────────────┐
│ Pair    Cosine Similarity  Angle (Radians)  Angle (Degrees) │
├─────────────────────────────────────────────────────────────┤
│ 1 ↔ 2   0.912456          0.415827         23.82°          │
│ 1 ↔ 3   0.654321          0.858963         49.21°          │
│ 2 ↔ 3   0.631245          0.896354         51.34°          │
└─────────────────────────────────────────────────────────────┘

┌─ Input Sentences ───────────────────────────────────────────┐
│ 1. The cat sits on the mat                                  │
│ 2. A feline rests on the rug                                │
│ 3. The weather is sunny                                     │
└─────────────────────────────────────────────────────────────┘
```

**Interpreting Results:**
- **Cosine Similarity**: Ranges from -1 (opposite) to 1 (identical). Higher values indicate more semantic similarity.
- **Angle (Radians)**: The angle between vectors in radians (0 to π).
- **Angle (Degrees)**: The angle in degrees (0° to 180°). Smaller angles indicate higher similarity.

## Architecture

```
NPUCodingAgent/
├── NPUCodingAgent/
│   ├── Program.cs                      # Chat agent entry point
│   ├── Services/
│   │   ├── LocalModelService.cs       # Foundry Local SDK integration (chat & embeddings)
│   │   ├── WorkspaceService.cs        # File discovery and reading
│   │   └── FileEditService.cs         # Safe file writing with backups
│   └── NPUCodingAgent.csproj
├── NPUCodingAgent.Embeddings/
│   ├── Program.cs                      # Embeddings explorer entry point
│   ├── VectorMath.cs                   # Cosine similarity and angle calculations
│   └── NPUCodingAgent.Embeddings.csproj
└── NPUCodingAgent.Tests/
    ├── LocalModelServiceSelectionTests.cs     # Model selection unit tests
    ├── LocalModelServiceIntegrationTests.cs   # Chat integration tests
    ├── VectorMathTests.cs                     # Vector math unit tests
    └── EmbeddingsIntegrationTests.cs          # Embeddings integration tests
```

### Key Components

- **LocalModelService**: Starts Foundry Local, supports both chat and embedding models, enumerates selectable aliases and variant IDs from the catalog, loads the selected model, surfaces runtime metadata, and talks to the local model through the SDK runtime or web endpoint
- **VectorMath**: Implements cosine similarity and vector angle calculations with proper validation and floating-point clamping
- **WorkspaceService**: Safely enumerates and reads files with extension filtering and size limits (chat agent only)
- **FileEditService**: Handles file modifications with automatic backup creation (chat agent only)
- **Program (NPUCodingAgent)**: Orchestrates the interactive chat loop and command dispatching
- **Program (NPUCodingAgent.Embeddings)**: Handles embeddings workflow with pairwise comparisons

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
- Use `/models` to inspect every selectable alias and variant ID in the local catalog.
- Use `/use <model>` to switch to a specific Foundry Local alias or variant ID without restarting the app.

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
**Solution**: Verify the configured model alias or variant ID exists in the Foundry Local catalog, inspect `/models`, and allow the first-run download to complete.

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
