# WhatsApp SimHub Plugin - Build Instructions

## Prerequisites

- Visual Studio 2019 or later
- .NET Framework 4.8
- SimHub installed

## Building the Plugin

### Method 1: Default SimHub Path

If SimHub is installed at the default location `C:\Program Files (x86)\SimHub\`:

1. Open `WhatsAppSimHubPlugin.sln` in Visual Studio
2. Build the solution (Ctrl+Shift+B)
3. The DLL will be in `bin\Debug\` or `bin\Release\`

### Method 2: Custom SimHub Path

If SimHub is installed elsewhere:

1. Open `WhatsAppSimHubPlugin.sln` in Visual Studio
2. Edit `WhatsAppSimHubPlugin.csproj` and change:
   ```xml
   <SimHubPath>C:\Your\Custom\Path\SimHub\</SimHubPath>
   ```
3. Build the solution

### Method 3: Command Line with Custom Path

```bash
msbuild WhatsAppSimHubPlugin.sln /p:SimHubPath="C:\Your\Custom\Path\SimHub\"
```

## DLL References

The plugin uses the following DLLs from SimHub installation:
- `Newtonsoft.Json.dll`
- `SimHub.Plugins.dll`

**Important:** The plugin does NOT include these DLLs. It references them directly from SimHub's folder to avoid version conflicts.

## Installation

1. Build the plugin
2. Copy `WhatsAppSimHubPlugin.dll` to SimHub installation folder
3. Restart SimHub
4. The plugin will appear in Settings → Plugins

## Troubleshooting

### "Could not load file or assembly"

This means the plugin can't find SimHub's DLLs. Solutions:

1. Check if SimHub is installed at `C:\Program Files (x86)\SimHub\`
2. If not, edit the `.csproj` file with the correct path
3. Rebuild the solution

### Build Errors

If you get build errors about missing references:

1. Make sure SimHub is installed
2. Check that `Newtonsoft.Json.dll` and `SimHub.Plugins.dll` exist in SimHub folder
3. Update the `SimHubPath` in `.csproj`

## Note on lib/ Folder

The `lib/` folder is **no longer used** and can be deleted. The plugin now references DLLs directly from SimHub's installation folder.

This ensures:
- No version conflicts
- Automatic updates when SimHub updates
- Smaller plugin distribution size
