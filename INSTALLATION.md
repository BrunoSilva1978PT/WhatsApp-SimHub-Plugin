# WhatsApp SimHub Plugin - Installation Guide

Complete guide for installing and configuring the WhatsApp SimHub Plugin.

## System Requirements

| Requirement | Details |
|-------------|---------|
| Operating System | Windows 10 or Windows 11 |
| .NET Framework | 4.8 or higher |
| SimHub | Latest version recommended |
| Display | VoCore or compatible device (for overlay) |
| Internet | Required for WhatsApp connection |

## Installation Methods

### Method 1: Download Release (Recommended)

1. **Download the Plugin**
   - Go to [Releases](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/releases)
   - Download the latest `WhatsAppSimHubPlugin.dll`

2. **Install the Plugin**
   - Locate your SimHub installation folder (usually `C:\Program Files (x86)\SimHub\`)
   - Copy `WhatsAppSimHubPlugin.dll` to this folder
   - Restart SimHub if it's running

3. **First Run**
   - SimHub will detect the new plugin
   - The plugin will automatically download and install:
     - Node.js (portable version)
     - npm packages (whatsapp-web.js, baileys, etc.)
   - This process takes 1-2 minutes on first run

### Method 2: Build from Source

**Prerequisites:**
- Visual Studio 2022 (Community or higher)
- .NET Framework 4.8 SDK

**Steps:**

1. **Clone the Repository**
   ```bash
   git clone https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin.git
   cd WhatsApp-SimHub-Plugin
   ```

2. **Setup Dependencies**
   
   Create a `lib/` folder in the project root and copy these DLLs from your SimHub installation:
   - `SimHub.Plugins.dll`
   - `GameReaderCommon.dll`
   - `SimHub.Plugins.Styles.dll`

3. **Build the Project**
   ```bash
   # Using MSBuild
   "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" WhatsAppSimHubPlugin.csproj -p:Configuration=Release
   ```
   
   Or open `WhatsAppSimHubPlugin.csproj` in Visual Studio and build (Ctrl+Shift+B)

4. **Deploy**
   - Copy `bin/Release/WhatsAppSimHubPlugin.dll` to your SimHub folder
   - Restart SimHub

## Initial Configuration

### Step 1: Open Plugin Settings

1. Launch SimHub
2. Click on the **WhatsApp Plugin** tab in the left sidebar
3. You'll see the Connection tab with several sections

### Step 2: Wait for Dependencies

On first run, the plugin installs required components:

| Component | Purpose | Status Indicator |
|-----------|---------|------------------|
| Node.js | JavaScript runtime | Green checkmark when ready |
| Git | Package management | Green checkmark when ready |
| npm packages | WhatsApp libraries | Green checkmark when ready |

Wait until all three show green checkmarks before proceeding.

### Step 3: Connect to WhatsApp

1. **QR Code will appear** in the Connection tab
2. **On your phone:**
   - Open WhatsApp
   - Tap **Settings** (or Menu icon)
   - Tap **Linked Devices**
   - Tap **Link a Device**
   - Point camera at QR code on screen
3. **Wait for connection** - Status will change to "Connected"

**Note:** The QR code expires after ~60 seconds. If it expires, click **Disconnect** then **Connect** to generate a new one.

### Step 4: Select Display Device

1. Go to the **Display** tab
2. Click **Refresh** to scan for devices
3. Select your VoCore from the dropdown
4. Click **Test** to verify - a test message should appear on your device

### Step 5: Add Contacts

Only messages from Allowed Contacts will be displayed.

**Option A: Import from Chats**
1. Go to **Contacts** tab
2. Click **Refresh** to load your recent WhatsApp chats
3. Select a contact from the dropdown
4. Click **Add**
5. Repeat for each contact you want to allow

**Option B: Add Manually**
1. Enter contact **Name**
2. Enter **Phone Number** in international format (e.g., `+351912345678`)
3. Click **Add**

### Step 6: Configure VIP Contacts (Optional)

1. In the Contacts list, find important contacts
2. Check the **VIP** checkbox next to their name
3. VIP contacts get:
   - Higher priority in queue
   - Longer display time
   - Repeat notifications until read

### Step 7: Configure Keywords (Optional)

1. Go to **Keywords** tab
2. Default keywords: urgent, emergency, hospital, help
3. Add custom keywords that should trigger urgent status
4. Messages containing these words will be marked urgent

### Step 8: Adjust Queue Settings (Optional)

Go to **Queue** tab to customize:

| Setting | Recommendation |
|---------|----------------|
| Normal Duration | 5s for quick glance |
| VIP/Urgent Duration | 10-15s for important messages |
| Max Per Contact | 3-5 to avoid spam |
| Max Queue Size | 10-20 depending on race length |

## Quick Reply Setup

### Step 1: Configure Reply Messages

1. Go to **Quick Replies** tab
2. Edit **Reply 1** text (e.g., "I'm in a race, will call you later")
3. Edit **Reply 2** text (e.g., "If it's urgent please call me")
4. Enable **Show Confirmation** if you want visual feedback

### Step 2: Bind to Steering Wheel

1. Go to SimHub **Controls** settings
2. Find actions:
   - `WhatsAppPlugin.SendReply1`
   - `WhatsAppPlugin.SendReply2`
3. Bind to your preferred steering wheel buttons

## Backend Selection

The plugin supports two WhatsApp backends:

### WhatsApp-Web.js (Recommended)

- **Pros:**
  - More stable and mature
  - Better compatibility
  - Full feature support

- **Cons:**
  - Uses Puppeteer (more resources)
  - Slightly slower startup

### Baileys (Lightweight)

- **Pros:**
  - Faster startup
  - Lower resource usage
  - No browser dependency

- **Cons:**
  - Newer, potentially less stable
  - Some features may differ

**To switch backends:**
1. Go to **Connection** tab
2. Select backend from **Backend Mode** dropdown
3. Click **Disconnect** then **Connect**

## Verifying Installation

### Checklist

- [ ] Plugin appears in SimHub sidebar
- [ ] Dependencies show green checkmarks
- [ ] WhatsApp connected (green status indicator)
- [ ] VoCore device selected and working (Test button)
- [ ] At least one contact added
- [ ] Test message displays on VoCore

### Test Your Setup

1. Send a WhatsApp message to yourself from another device
2. Or ask a contact (that you've added) to send you a message
3. The message should appear on your VoCore display

## Troubleshooting Installation

### Plugin Not Loading

**Symptoms:** Plugin doesn't appear in SimHub

**Solutions:**
1. Verify DLL is in correct SimHub folder
2. Check SimHub logs: **Settings > Plugins > Plugin Logs**
3. Ensure .NET Framework 4.8 is installed
4. Try running SimHub as Administrator

### Dependencies Won't Install

**Symptoms:** Stuck on "Installing dependencies..."

**Solutions:**
1. Check internet connection
2. Disable antivirus temporarily
3. Check Windows Firewall isn't blocking
4. Manually install Node.js from [nodejs.org](https://nodejs.org)
5. Delete `%AppData%\SimHub\WhatsAppPlugin\` and restart SimHub

### QR Code Won't Scan

**Symptoms:** WhatsApp app says "Couldn't link device"

**Solutions:**
1. Ensure phone and PC are on same network
2. Check phone's date/time is correct
3. Update WhatsApp to latest version
4. Try switching backend mode

### VoCore Not Detected

**Symptoms:** No devices in Display dropdown

**Solutions:**
1. Verify VoCore is connected and powered
2. Check VoCore appears in SimHub's device list
3. Click **Refresh** multiple times
4. Restart SimHub

## Updating the Plugin

1. Download new `WhatsAppSimHubPlugin.dll` from Releases
2. Close SimHub
3. Replace old DLL with new one
4. Start SimHub
5. Your settings and contacts are preserved

## Uninstalling

1. Close SimHub
2. Delete `WhatsAppSimHubPlugin.dll` from SimHub folder
3. (Optional) Delete `%AppData%\SimHub\WhatsAppPlugin\` to remove all data

## Getting Help

- **Issues:** [GitHub Issues](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/issues)
- **Discussions:** [GitHub Discussions](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/discussions)

---

**Next:** See [README.md](README.md) for full feature documentation.
