# WhatsApp SimHub Plugin

A professional WhatsApp notification plugin for SimHub that displays messages on VoCore displays during sim racing sessions. Features intelligent queue management, priority system, and quick replies via steering wheel buttons.

## Author

**Bruno Silva** - [GitHub](https://github.com/BrunoSilva1978PT)

## Features

### Core Features
- **Dual Backend Support**: Choose between WhatsApp-Web.js (recommended) or Baileys (lightweight)
- **QR Code Authentication**: Quick and secure WhatsApp connection via linked devices
- **Intelligent Message Queue**: Smart prioritization system (Urgent+VIP > Urgent > VIP > Normal)
- **Message Grouping**: Automatic grouping of multiple messages from the same contact
- **VoCore Overlay Integration**: Transparent message display on external VoCore devices
- **Quick Replies**: Respond to messages using steering wheel buttons during races
- **Priority Badges**: Visual indicators for VIP and Urgent messages
- **Re-notification System**: Important messages repeat until acknowledged

### Contact Management
- **Allowed Contacts**: Only display messages from approved contacts
- **VIP Contacts**: Mark important contacts for priority treatment
- **Import from Chats**: Load contacts directly from your WhatsApp conversations
- **Manual Entry**: Add contacts by name and international phone number

### Keyword System
- **Urgent Keywords**: Automatically mark messages containing specific words as urgent
- **Default Keywords**: urgent, emergency, hospital, help
- **Customizable**: Add or remove keywords as needed

### Queue Management
- **Configurable Display Durations**: 
  - Normal messages: 5-30 seconds
  - VIP/Urgent messages: 10-60 seconds
- **Queue Limits**:
  - Max messages per contact: 1-10
  - Max total queue size: 1-50
- **Reminder System**: VIP/Urgent messages can repeat at configurable intervals

### Quick Replies
- **2 Configurable Replies**: Pre-written responses for common situations
- **Steering Wheel Integration**: Send replies via SimHub actions
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

2. Open `WhatsAppSimHubPlugin.csproj` in Visual Studio 2022

3. Copy SimHub DLLs to the `lib/` folder:
   - `SimHub.Plugins.dll`
   - `GameReaderCommon.dll`
   - `SimHub.Plugins.Styles.dll`

4. Build in Release mode

5. Copy `bin/Release/WhatsAppSimHubPlugin.dll` to SimHub folder

## First Time Setup

1. **Launch SimHub** and go to the WhatsApp Plugin settings

2. **Wait for Dependencies**: On first run, the plugin will install Node.js and npm packages (this may take 1-2 minutes)

3. **Scan QR Code**:
   - A QR code will appear in the Connection tab
   - Open WhatsApp on your phone
   - Go to **Settings > Linked Devices > Link a Device**
   - Scan the QR code

4. **Select Display Device**: In the Display tab, select your VoCore device

5. **Add Contacts**: In the Contacts tab, add contacts whose messages you want to see

## Configuration

### Connection Tab

| Setting | Description |
|---------|-------------|
| Backend Mode | WhatsApp-Web.js (stable) or Baileys (lightweight) |
| Debug Logging | Enable detailed logs for troubleshooting |
| Backend Versions | Install/update WhatsApp libraries |
| Scripts | View and update server script versions |

### Contacts Tab

| Feature | Description |
|---------|-------------|
| Load Recent Chats | Import contacts from your WhatsApp conversations |
| Manual Add | Add contact by name and phone number |
| VIP Toggle | Mark contacts as VIP for priority treatment |
| Delete | Remove contacts from allowed list |

### Keywords Tab

| Feature | Description |
|---------|-------------|
| Add Keyword | Add words that trigger urgent status |
| Delete | Remove keywords from the list |

### Display Tab

| Setting | Description |
|---------|-------------|
| Target Device | Select VoCore display for overlay |
| Refresh | Rescan for connected devices |
| Test | Send test message to verify overlay |

### Queue Tab

| Setting | Default | Range | Description |
|---------|---------|-------|-------------|
| Normal Duration | 5s | 5-30s | Display time for normal messages |
| VIP/Urgent Duration | 10s | 10-60s | Display time for priority messages |
| Max Per Contact | 5 | 1-10 | Max messages grouped from one contact |
| Max Queue Size | 10 | 1-50 | Total queue capacity |
| Remove After Display | Off | - | Remove VIP/Urgent after first display |
| Reminder Interval | 3 min | 30s-10min | Repeat interval for priority messages |

### Quick Replies Tab

| Setting | Description |
|---------|-------------|
| Reply 1/2 Text | Message content to send |
| Show Confirmation | Display overlay when reply is sent |

## SimHub Properties

Use these properties in custom dashboards:

| Property | Type | Description |
|----------|------|-------------|
| `WhatsApp.showmessage` | Boolean | True when displaying a message |
| `WhatsApp.sender` | String | Sender name |
| `WhatsApp.typemessage` | String | "VIP", "URGENT", or empty |
| `WhatsApp.totalmessages` | Integer | Messages in current group |
| `WhatsApp.message0` - `WhatsApp.message9` | String | Message text (up to 10) |

## SimHub Actions

Bind these actions to steering wheel buttons:

| Action | Description |
|--------|-------------|
| `WhatsAppPlugin.SendReply1` | Send Quick Reply 1 |
| `WhatsAppPlugin.SendReply2` | Send Quick Reply 2 |

## How It Works

### Architecture

```
┌─────────────────┐     WebSocket      ┌──────────────────┐     WhatsApp API     ┌──────────────┐
│  SimHub Plugin  │ ◄─────────────────► │  Node.js Server  │ ◄──────────────────► │   WhatsApp   │
│     (C#)        │    JSON Messages   │  (JS/MJS)        │                      │   Servers    │
└─────────────────┘                    └──────────────────┘                      └──────────────┘
        │
        │ Overlay Rendering
        ▼
┌─────────────────┐
│  VoCore Device  │
└─────────────────┘
```

### Message Flow

1. WhatsApp message received by Node.js backend
2. Backend sends message to C# plugin via WebSocket
3. Plugin checks if sender is in Allowed Contacts
4. Message checked for keywords (urgent) and sender VIP status
5. Message added to appropriate priority queue
6. Queue processor displays messages on VoCore overlay
7. User can send quick reply via steering wheel button

### Priority System

| Priority | Condition | Display Duration |
|----------|-----------|------------------|
| 0 (Highest) | VIP + Urgent | VIP/Urgent duration |
| 1 | Urgent only | VIP/Urgent duration |
| 2 | VIP only | VIP/Urgent duration |
| 3 (Normal) | Neither | Normal duration |

## Troubleshooting

### QR Code Not Appearing

- Wait for Node.js dependencies to install (check Dependencies section)
- Try switching backend mode
- Click Disconnect then Connect
- Check SimHub logs for errors

### Messages Not Displaying

- Verify sender is in Allowed Contacts list
- Check VoCore device is selected in Display tab
- Click Test button to verify overlay works
- Ensure queue isn't full

### Connection Drops Frequently

- Check internet connection stability
- Try switching from WhatsApp-Web.js to Baileys or vice-versa
- Click Reset Session to clear authentication and reconnect

### Node.js Installation Fails

- Check internet connection
- Verify antivirus isn't blocking downloads
- Manually install Node.js from [nodejs.org](https://nodejs.org)

## File Locations

| Location | Description |
|----------|-------------|
| `%AppData%\SimHub\WhatsAppPlugin\` | Plugin data folder |
| `%AppData%\SimHub\WhatsAppPlugin\node\` | Node.js scripts and packages |
| `%AppData%\SimHub\WhatsAppPlugin\data\` | Session authentication data |
| `%AppData%\SimHub\WhatsAppPlugin\logs\` | Debug logs (when enabled) |

## Changelog

### Version 1.0.0
- Initial release
- Dual backend support (WhatsApp-Web.js + Baileys)
- Intelligent message queue with 4-level priority system
- VoCore overlay integration with auto-discovery
- Quick replies via steering wheel buttons
- VIP contact system with persistent notifications
- Keyword-based urgency detection
- Configurable queue limits and display durations
- Toast notification system
- Live connection status monitoring
- Automatic dependency management

## Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/NewFeature`)
3. Commit your changes (`git commit -m 'Add NewFeature'`)
4. Push to branch (`git push origin feature/NewFeature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- **SimHub Team** - For the amazing sim racing platform
- **WhatsApp-Web.js** - For the WhatsApp Web API library
- **Baileys** - For the lightweight WhatsApp implementation
- **Sim Racing Community** - For feedback and testing

## Support

- **Issues**: [GitHub Issues](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/issues)
- **Discussions**: [GitHub Discussions](https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin/discussions)

---

**Made with passion for the Sim Racing community by Bruno Silva**
