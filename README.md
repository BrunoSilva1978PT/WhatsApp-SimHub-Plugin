# WhatsApp SimHub Plugin

A professional WhatsApp notification plugin for SimHub that displays messages on VoCore displays during sim racing sessions. Features intelligent queue management, priority system, sound notifications, LED effects, Philips Hue integration, and quick replies via steering wheel buttons.

## Author

**Bruno Silva** - [GitHub](https://github.com/BrunoSilva1978PT)

## Features

### Core Features
- **Dual Backend Support**: Choose between WhatsApp-Web.js (recommended) or Baileys (lightweight)
- **QR Code Authentication**: Quick and secure WhatsApp connection via linked devices
- **Intelligent Message Queue**: Smart prioritization system (Urgent+VIP > Urgent > VIP > Normal)
- **Message Grouping**: Automatic grouping of multiple messages from the same contact
- **VoCore Auto-Configuration**: Automatic device detection and overlay configuration with auto-recovery
- **Dashboard Layer System**: Single layer (direct) or dual layer (auto-merge with user's dashboard)
- **Per-VoCore Dashboard Settings**: Independent layer count and dashboard selection per VoCore device
- **VR & Screen Overlay Support**: Separate overlay dashboard for VR headsets or monitor overlays
- **Quick Replies**: Respond to messages using steering wheel buttons during races
- **Sound Notifications**: Per-priority audio alerts for Normal, VIP and Urgent messages (.wav and .mp3)
- **LED Effects**: Flash connected LED devices, RGB matrices, Arduino LEDs and Philips Hue lights on message arrival
- **Priority Badges**: Visual indicators for VIP and Urgent messages
- **Re-notification System**: Important messages repeat until acknowledged
- **Pending Messages Indicator**: Shows "+X" when more messages are queued from same contact
- **Auto-Update System**: Automatic update detection from GitHub releases with one-click install
- **Auto-Reconnect**: Automatic reconnection with 15-second retry interval (up to 5 attempts)
- **QR Code Overlay Notification**: Shows "Scan QR Code in SimHub" on VoCore/overlay when authentication needed
- **Two-VoCore Support**: Backend supports 2 simultaneous VoCore devices

### Contact Management
- **Allowed Contacts**: Only display messages from approved contacts
- **VIP Contacts**: Mark important contacts for priority treatment
- **Google Contacts Integration**: Import contacts directly from your Google account
- **WhatsApp Verification**: Automatically verifies if a phone number has WhatsApp before adding
- **LID Support**: Handles community-based contacts with LinkedID matching
- **Search & Filter**: Type to search/filter in contact dropdowns
- **Manual Entry**: Add contacts by name and international phone number (format: +[country code][number])

### Keyword System
- **Urgent Keywords**: Automatically mark messages containing specific words as urgent
- **Default Keywords**: urgent, emergency, hospital, help
- **Customizable**: Add or remove keywords as needed

### Sound Notifications
- **Per-Priority Sounds**: Separate sounds for Normal, VIP and Urgent messages
- **Per-Priority Toggles**: Enable/disable sound independently for each priority level
- **19 Default Sounds**: Included notification sounds (bells, tones, alerts)
- **Format Support**: .wav and .mp3 files
- **Import Custom Sounds**: Add your own notification sounds
- **Delete Sounds**: Remove any sound including defaults
- **Preview**: Play button to test sounds before selecting
- **One-Shot Per Contact**: Sound plays only once per contact until message is dismissed (Urgent overrides)
- **Auto-Extraction**: Default sounds extracted from plugin on first run to `%AppData%\SimHub\WhatsAppPlugin\sounds\`

### LED Effects
- **Multi-Device Support**: Up to 8 LED devices simultaneously
- **Device Types**:
  - LED Module devices (embedded in steering wheels, DD2/DD1 bases)
  - RGB Matrix panels (8x8+ with envelope icon pattern)
  - Arduino RGB LEDs (via SimHub Serial driver)
  - Arduino RGB Matrix
  - Philips Hue smart lights (via SimHub Ambient Lights)
- **Priority-Based Colors**: Different flash colors for Normal, VIP and Urgent messages
- **Priority Toggles**: Enable/disable LED effects per priority level
- **Configurable Flash Speed**: 100-1000ms interval via slider (applies to all device types)
- **Effect Duration**: Matches message display duration from Notifications tab
- **Test Buttons**: 3 test buttons (Normal/VIP/Urgent) on every device card
- **Expand/Collapse**: Click device headers to show/hide settings
- **Auto-Discovery**: Devices detected automatically from SimHub, with change detection on reconnect
- **Matrix Display Modes**: Flash All (solid color) or Envelope Icon (8x8 pattern)
- **One-Shot Per Contact**: LED effect triggers once per contact, Urgent overrides

### Philips Hue Integration
- **Hue Groups**: Loaded automatically from SimHub's Ambient Lights configuration
- **Real Light Names**: Displays actual light names from Hue bridge
- **Per-Priority Light Selection**: Choose which lights activate for Normal, VIP and Urgent independently
- **Flash Effect**: All selected lights flash same color (on/off)
- **Alternating Effect**: Lights alternate between 2 colors (requires 2+ lights)
- **Per-Priority Effects**: Choose Flash or Alternating independently for each priority
- **Per-Priority Color Pairs**: Color 1 + Color 2 for alternating effect per priority

### Queue Management
- **Configurable Display Durations**:
  - Normal messages: 5-30 seconds
  - VIP/Urgent messages: 10-60 seconds
- **Queue Limits**:
  - Max messages per contact: 1-10
  - Max total queue size (non VIP/Urgent): 1-50
  - VIP/Urgent messages have no queue limit
- **Priority Queue**: VIP/Urgent messages always take priority over normal messages
- **Queue Overflow**: When full, oldest non-VIP/Urgent messages are removed first
- **Reminder System**: VIP/Urgent messages can repeat at configurable intervals (1-10 min)
- **Remove After First Display**: Option to prevent VIP/Urgent message repetition

### Quick Replies
- **2 Configurable Replies**: Pre-written responses for common situations
- **Steering Wheel Integration**: Send replies via SimHub actions
- **One-Shot Protection**: Prevents duplicate replies for same message
- **Confirmation Display**: Optional overlay confirmation when reply is sent

## Requirements

- **SimHub** (latest version recommended)
- **Windows 10/11**
- **.NET Framework 4.8**
- **Node.js** (automatically installed if not present)
- **VoCore Display** (for overlay functionality)

## Installation

### Option 1: Download Release (Recommended)

1. Download the latest release from [Releases](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/releases)
2. Extract `WhatsAppSimHubPlugin.dll` to your SimHub installation folder
3. Restart SimHub
4. The plugin will automatically install Node.js dependencies on first run

### Option 2: Build from Source

1. Clone the repository:
```bash
git clone https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin.git
```

2. Build using MSBuild (required for XAML projects):
```bash
build-and-deploy.bat
```

3. Copy `bin/Release/WhatsAppSimHubPlugin.dll` to SimHub folder

## First Time Setup

1. **Launch SimHub** and go to the WhatsApp Plugin settings

2. **Wait for Dependencies**: On first run, the plugin will install Node.js and npm packages (this may take 1-2 minutes)

3. **Scan QR Code**:
   - A QR code will appear in the Connection tab
   - Open WhatsApp on your phone
   - Go to **Settings > Linked Devices > Link a Device**
   - Scan the QR code

4. **Select Display Device**: In the Display tab, select your VoCore device (auto-detected)

5. **Add Contacts**: In the Contacts tab, add contacts whose messages you want to see

6. **Configure Sounds** (optional): In the Notifications tab, choose notification sounds for each priority level

7. **Configure LED Effects** (optional): In the LED Effects tab, enable and configure LED devices

## Configuration

### Connection Tab

| Setting | Description |
|---------|-------------|
| Backend Mode | WhatsApp-Web.js (stable) or Baileys (lightweight) |
| Debug Logging | Enable detailed logs for troubleshooting |
| Backend Versions | Install/update WhatsApp libraries |
| Scripts | View and update server script versions |
| Google Contacts | Connect Google account to import contacts |

### Contacts Tab

| Feature | Description |
|---------|-------------|
| Google Contacts | Connect to Google account and import contacts directly |
| Manual Add | Add contact by name and phone number (format: +351912345678) |
| WhatsApp Verification | Automatically checks if number has WhatsApp before adding |
| VIP Toggle | Mark contacts as VIP for priority treatment |
| Remove | Remove contacts from allowed list |

### Keywords Tab

| Feature | Description |
|---------|-------------|
| Add Keyword | Add words that trigger urgent status |
| Delete | Remove keywords from the list |

### Display Tab

| Setting | Description |
|---------|-------------|
| VoCore Device | Auto-detected VoCore devices with serial numbers |
| Layer Count | 1 layer (direct) or 2 layers (merge with your dashboard) |
| Layer 1 | Base dashboard (Default = WhatsAppPlugin) |
| Layer 2 | Second dashboard to merge with (2-layer mode) |
| Send Test Message | Send test notification to selected VoCore devices |
| Merge Dashboards | Create merged dashboard from selected layers |

**VoCore Auto-Configuration:**
- Plugin automatically enables Information Overlay when needed
- Automatically sets correct dashboard (WhatsAppPlugin or merged)
- Auto-recovery: If user manually disables overlay or changes dashboard, plugin reconfigures automatically
- No manual configuration needed

**Dashboard Layer System:**
- **1 Layer**: Uses a single dashboard directly on the VoCore
- **2 Layers**: Merges your existing dashboard with the WhatsApp overlay, so you keep your dashboard and get notifications on top

### Notifications Tab

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| Normal Sound | Off | - | Enable sound for normal messages |
| VIP Sound | On | - | Enable sound for VIP messages |
| Urgent Sound | On | - | Enable sound for Urgent messages |
| Normal Duration | 5s | 5-30s | Display time for normal messages |
| VIP/Urgent Duration | 10s | 10-60s | Display time for priority messages |
| Max Per Contact | 5 | 1-10 | Max messages grouped from one contact |
| Max Queue Size | 10 | 1-50 | Total queue capacity (non VIP/Urgent) |
| Remove After Display | Off | - | Remove VIP/Urgent after first display |
| Reminder Interval | 3 min | 1-10 min | Repeat interval for priority messages |

### LED Effects Tab

| Setting | Default | Description |
|---------|---------|-------------|
| Enable LED Effects | Off | Master toggle for LED notifications |
| Trigger on Normal | On | Trigger LED effects on normal messages |
| Trigger on VIP | On | Trigger LED effects on VIP messages |
| Trigger on Urgent | On | Trigger LED effects on Urgent messages |
| Flash Speed | 250ms | Flash interval (100-1000ms, applies to all devices) |

**Per-Device Settings:**
- Enable/disable per device
- Expand/collapse device cards
- Flash colors for Normal, VIP and Urgent
- Test buttons (Normal/VIP/Urgent) for each device

**Hue-Specific Settings (per priority):**
- Select which lights to activate
- Choose effect type: Flash or Alternating
- Color 1 + Color 2 (for alternating mode)

### Quick Replies Tab

| Setting | Description |
|---------|-------------|
| Reply 1/2 Text | Message content to send |
| Button Binding | Map to steering wheel buttons via SimHub Controls & Events |
| Show Confirmation | Display overlay when reply is sent |

### VR & Screen Overlay Support

The plugin automatically installs a separate overlay dashboard called **"Simhub WhatsApp Plugin Overlay"** that can be used in VR headsets or as a screen overlay.

**Setup:**
1. Go to **SimHub > Dash Studio > Overlay settings**
2. Add **"Simhub WhatsApp Plugin Overlay"** as an overlay
3. Configure position, size and opacity as needed

## SimHub Properties

Use these properties in custom dashboards:

| Property | Type | Description |
|----------|------|-------------|
| `WhatsAppPlugin.showmessage` | Boolean | True when displaying a message |
| `WhatsAppPlugin.sender` | String | Sender name (with +X if more pending) |
| `WhatsAppPlugin.typemessage` | String | "VIP", "URGENT", or empty |
| `WhatsAppPlugin.totalmessages` | Integer | Messages in current group |
| `WhatsAppPlugin.message[0]` - `[9]` | String | Message text (HH:mm + body) |
| `WhatsAppPlugin.vocore1enabled` | Boolean | VoCore 1 display enabled |
| `WhatsAppPlugin.vocore2enabled` | Boolean | VoCore 2 display enabled |
| `WhatsAppPlugin.vocoreenabled` | Boolean | Any VoCore enabled (legacy) |
| `WhatsAppPlugin.ConnectionStatus` | String | Connection status text |
| `WhatsAppPlugin.ConnectedNumber` | String | Connected WhatsApp number |

## SimHub Actions

Bind these actions to steering wheel buttons in **Controls and Events**:

| Action | Description |
|--------|-------------|
| `WhatsAppPlugin.SendReply1` | Send Quick Reply 1 to current message sender |
| `WhatsAppPlugin.SendReply2` | Send Quick Reply 2 to current message sender |
| `WhatsAppPlugin.DismissMessage` | Dismiss current message and move to next |

## How It Works

### Architecture

```
┌─────────────────┐     WebSocket      ┌──────────────────┐     WhatsApp API     ┌──────────────┐
│  SimHub Plugin  │ <=================> │  Node.js Server  │ <==================> │   WhatsApp   │
│     (C#)        │    JSON Messages    │  (JS/MJS)        │   Baileys/WWJS       │   Servers    │
└─────────────────┘                     └──────────────────┘                      └──────────────┘
        |
        | Dashboard Properties, Actions & LED Effects
        v
    SimHub Core
        |
        v
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  VoCore Device  │     │  LED / Arduino   │     │  Philips Hue    │
│  (Overlay)      │     │  (Color Effects) │     │  (Ambient Light)│
└─────────────────┘     └──────────────────┘     └─────────────────┘
```

### Message Flow

1. WhatsApp message received by Node.js backend
2. Backend sends message to C# plugin via WebSocket (JSON)
3. Plugin checks if sender is in Allowed Contacts
4. Message checked for urgent keywords and sender VIP status
5. Message added to appropriate priority queue
6. Queue processor displays messages on VoCore overlay
7. Sound notification plays based on priority and enabled toggles
8. LED effects trigger on configured devices
9. User can send quick reply via steering wheel button
10. Message automatically removed from queue after display timeout

### Priority System

| Priority | Condition | Display Duration | Sound | LED |
|----------|-----------|------------------|-------|-----|
| 0 (Highest) | VIP + Urgent | VIP/Urgent duration | Urgent sound | Urgent colors |
| 1 | Urgent only | VIP/Urgent duration | Urgent sound | Urgent colors |
| 2 | VIP only | VIP/Urgent duration | VIP sound | VIP colors |
| 3 (Normal) | Neither | Normal duration | Normal sound | Normal colors |

Urgent messages always take priority over VIP. VIP/Urgent messages have no queue limit and are never removed to make room for normal messages.

### LED Effects Architecture

The LED effects system uses SimHub's Color Effects pipeline:

1. **Device Discovery**: `DeviceDiscoveryManager` iterates all SimHub devices (standalone + composite)
2. **Container Injection**: `LedEffectsManager` injects `ScriptedContentContainer` into each device's Color Effects profile
3. **Slot System**: Up to 8 devices, 128 LEDs per slot, controlled via SimHub properties (`WhatsAppPlugin.S{slot}_Led{index}`)
4. **Effect Rendering**: On message arrival, colors are written per-LED based on priority, effect type and selected lights
5. **Flash/Alternating**: Timer toggles colors at configurable interval

## Troubleshooting

### QR Code Not Appearing
- Wait for Node.js dependencies to install (check Dependencies section in Connection tab)
- Try switching backend mode
- Click Disconnect then Connect
- Check SimHub logs for errors

### Messages Not Displaying
- Verify sender is in Allowed Contacts list
- Check VoCore device is selected in Display tab
- Click Send Test Message to verify overlay works
- Ensure queue isn't full

### VoCore Not Working
- Click Refresh Devices button to rescan
- Plugin automatically configures overlay - no manual setup needed
- If user dashboard was active, plugin creates merged dashboard automatically
- Check that VoCore is properly connected to SimHub

### No Sound Playing
- Ensure sounds are enabled in Notifications tab for the desired priority
- Check that sound files exist (try Play button to test)
- Normal sound is disabled by default - enable it in Notifications tab
- Import a new sound if defaults were deleted

### LED Effects Not Working
- Ensure LED Effects are enabled (master toggle)
- Check that devices are detected (they appear automatically)
- Verify the device is enabled (checkbox on device card header)
- Use the Test buttons to verify the device responds
- For Hue: ensure lights are configured in SimHub's Ambient Lights first

### Connection Drops Frequently
- Check internet connection stability
- Try switching from WhatsApp-Web.js to Baileys or vice-versa
- Click Reset Session to clear authentication and reconnect

## File Locations

| Location | Description |
|----------|-------------|
| `%AppData%\SimHub\WhatsAppPlugin\` | Plugin data folder |
| `%AppData%\SimHub\WhatsAppPlugin\config\` | Settings, contacts, keywords, debug config |
| `%AppData%\SimHub\WhatsAppPlugin\node\` | Node.js scripts and packages |
| `%AppData%\SimHub\WhatsAppPlugin\data\` | WhatsApp session data (WhatsApp-Web.js) |
| `%AppData%\SimHub\WhatsAppPlugin\data_baileys\` | WhatsApp session data (Baileys) |
| `%AppData%\SimHub\WhatsAppPlugin\data_google\` | Google Contacts cache |
| `%AppData%\SimHub\WhatsAppPlugin\sounds\` | Notification sound files (.wav, .mp3) |
| `%AppData%\SimHub\WhatsAppPlugin\logs\` | Debug logs (when enabled) |

## Project Structure

```
WhatsAppPlugin/
├── WhatsAppPlugin.cs                  # Main plugin (init, events, properties, actions)
├── Models/
│   ├── PluginSettings.cs              # All settings (backend, VoCore, queue, sound, LED, replies)
│   ├── Contact.cs                     # Contact model (name, number, VIP flag)
│   ├── QueuedMessage.cs               # Message model (body, sender, priority, timestamp)
│   ├── VoCoreDevice.cs                # VoCore device state model
│   └── LedDeviceConfig.cs             # LED device configuration (colors, effects, lights)
├── Core/
│   ├── MessageQueue.cs                # Queue system (VIP/Urgent + Normal, timers, display)
│   ├── WebSocketManager.cs            # Node.js communication and process management
│   ├── VoCoreManager.cs               # VoCore auto-config and dashboard management
│   ├── DeviceDiscoveryManager.cs      # Unified device discovery (VoCore + LED + Hue)
│   ├── LedEffectsManager.cs           # LED effects engine (flash, alternating, matrix icons)
│   ├── DashboardInstaller.cs          # Extract dashboards from embedded resources
│   ├── DashboardMerger.cs             # Merge 2 dashboards into 1 with layers
│   ├── SoundManager.cs                # Sound playback, import, delete (WPF MediaPlayer)
│   └── DependencyManager.cs           # Node.js, Git, npm auto-installation
├── UI/
│   ├── SettingsControl.xaml           # Main settings UI (7 tabs + version info)
│   ├── ConfirmDialog.xaml             # Dark-themed confirmation dialogs
│   └── Tabs/
│       ├── ConnectionTab.xaml         # Backend mode, versions, scripts, dependencies
│       ├── ContactsTab.xaml           # Google Contacts, manual add, contact list
│       ├── KeywordsTab.xaml           # Add/remove urgent keywords
│       ├── DisplayTab.xaml            # VoCore selection, dashboard layers, test
│       ├── NotificationsTab.xaml      # Sounds, durations, queue limits, VIP behavior
│       ├── LedEffectsTab.xaml         # LED devices, Hue groups, colors, effects, test
│       └── QuickRepliesTab.xaml       # Reply text, button binding, confirmation
└── Resources/
    ├── WhatsAppPluginVocore1.simhubdash    # VoCore 1 dashboard
    ├── WhatsAppPluginVocore2.simhubdash    # VoCore 2 dashboard
    ├── Simhub WhatsApp Plugin Overlay.simhubdash  # VR/Screen overlay dashboard
    ├── whatsapp-server.js             # WhatsApp-Web.js backend script
    ├── baileys-server.mjs             # Baileys backend script
    ├── google-contacts.js             # Google Contacts integration script
    ├── package.json                   # npm dependencies
    └── Sounds/                        # 19 default notification sounds (.wav, .mp3)
```

## Changelog

### Version 1.1.0
- **LED Effects System**: Complete LED notification system
  - Support for LED Module, RGB Matrix, Arduino LEDs and Philips Hue
  - Per-priority colors (Normal/VIP/Urgent) on all device types
  - Flash and Alternating effects for Hue lights
  - Per-priority Hue light selection (choose which lights per priority)
  - Real Hue light names from bridge
  - Configurable flash speed (100-1000ms slider)
  - Matrix envelope icon display mode (8x8 pattern)
  - 3 test buttons (Normal/VIP/Urgent) on all device cards
  - Expand/collapse device cards
  - Auto-discovery with change detection
  - Unified device discovery (DeviceDiscoveryManager)
- **Per-Mode Dashboard Settings**: Independent layer count and dashboards per VoCore
- **Normal Sound Support**: Sound notifications now available for normal messages too
- **Per-Priority Sound Toggles**: Enable/disable sound independently per priority
- **Urgent Sound Override**: Urgent messages always play sound even if already played for contact
- **UI Improvements**:
  - Standardized info box icons across all tabs
  - Blue style buttons on Display tab
  - Text wrapping fix for info boxes at various resolutions

### Version 1.0.6
- Version bump and internal improvements

### Version 1.0.5
- Dashboard merge improvements
- Multi-screen merge support
- Auto-refresh dropdowns

### Version 1.0.2
- **Sound Notifications**: Customizable audio alerts for VIP and Urgent messages
  - 19 default notification sounds included
  - Import custom sounds, delete any sound including defaults
  - Play/preview button for testing sounds
- **VoCore Architecture Rewrite**: Complete redesign for zero reflection
  - VoCoreManager class for clean device management
  - Serial number-based identification
  - Auto-configuration via DataUpdate()
  - Auto-recovery when user changes settings
- **Code Quality**: All comments translated to English

### Version 1.0.1
- **Auto-Update System**: Checks GitHub releases for updates
- **Google Contacts Integration**: Import contacts from Google account
- **WhatsApp Number Verification**: Verifies phone numbers before adding
- **Auto-Reconnect System**: 15-second intervals, up to 5 attempts
- **QR Code Overlay Notification**: Shows scan prompt on VoCore

### Version 1.0.0
- Initial release
- Dual backend support (WhatsApp-Web.js + Baileys)
- Intelligent message queue with 4-level priority system
- VoCore overlay integration with auto-discovery
- Quick replies via steering wheel buttons
- VIP contact system with persistent notifications
- Keyword-based urgency detection

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch from `develop` (`git checkout -b feature/NewFeature`)
3. Commit your changes (`git commit -m 'Add NewFeature'`)
4. Push to branch (`git push origin feature/NewFeature`)
5. Open a Pull Request to `develop` branch

**Branch Structure:**
- `main` - Stable releases only
- `develop` - Active development branch
- `feature/*` - Feature branches

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- **SimHub Team** - For the amazing sim racing platform
- **WhatsApp-Web.js** - For the WhatsApp Web API library
- **Baileys** - For the lightweight WhatsApp implementation
- **Mixkit, Freesound, Pixabay** - For default notification sounds
- **Sim Racing Community** - For feedback and testing

## Support

- **Issues**: [GitHub Issues](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/issues)
- **Discussions**: [GitHub Discussions](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/discussions)

---

**Made with passion for the Sim Racing community by Bruno Silva**
