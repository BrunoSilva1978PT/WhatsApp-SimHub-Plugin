# WhatsApp SimHub Plugin

A professional WhatsApp notification plugin for SimHub that displays messages during sim racing sessions, featuring intelligent queue management, priority system, and quick replies via steering wheel buttons.

## 👨‍💻 Author

**Bruno Silva**

## 🎯 Features

### Core Features
- ✅ **Dual Backend Support**: Choose between WhatsApp-Web.js (recommended) or Baileys (lightweight)
- ✅ **QR Code Authentication**: Quick and secure WhatsApp connection
- ✅ **Intelligent Message Queue**: Smart prioritization (Urgent > VIP > Normal)
- ✅ **Message Grouping**: Automatic grouping of messages from the same contact
- ✅ **VoCore Overlay Integration**: Transparent message display on external devices
- ✅ **Quick Replies**: Respond to messages using steering wheel buttons
- ✅ **Priority Badges**: Visual indicators (⭐ VIP, 🚨 Urgent)
- ✅ **Re-notifications**: Important messages repeat until read

### Advanced Features
- ✅ **Allowed Contacts Management**: Add contacts from active chats or manually
- ✅ **VIP Contact System**: Mark important contacts for priority treatment
- ✅ **Keyword-based Urgency**: Automatic urgent marking based on message content
- ✅ **Configurable Display Durations**: Separate timings for normal/VIP/urgent messages
- ✅ **Queue Size Limits**: Per-contact and global queue management
- ✅ **Toast Notifications**: Non-intrusive UI feedback system
- ✅ **Live Connection Status**: Real-time WhatsApp connection monitoring
- ✅ **Device Auto-discovery**: Automatic VoCore device detection

## 📋 Requirements

- SimHub installed
- .NET Framework 4.8
- Node.js (automatically installed on first run)
- Windows OS

## 🚀 Installation

### Option 1: Build from Source

1. Clone the repository:
```bash
git clone https://github.com/your-username/whatsapp-plugin.git
```

2. Open the project in Visual Studio 2019 or later

3. Build the project in Release mode

4. The DLL will be generated in `bin/Release/WhatsAppSimHubPlugin.dll`

5. Copy `WhatsAppSimHubPlugin.dll` to your SimHub installation folder

### Option 2: Download Release

1. Download the latest release from the [Releases page](https://github.com/your-username/whatsapp-plugin/releases)
2. Extract the ZIP file
3. Copy `WhatsAppSimHubPlugin.dll` to SimHub's root folder
4. Restart SimHub

## ⚙️ Configuration

### First Time Setup

1. Launch SimHub
2. Go to **Settings → Plugins**
3. Find **WhatsApp Plugin** in the list
4. Wait ~2 minutes while Node.js dependencies are installed automatically
5. After installation, a QR code will appear
6. Open WhatsApp on your phone → **Settings → Linked Devices**
7. Tap **"Link a Device"** and scan the QR code

### Connection Tab

**Backend Selection:**
- **WhatsApp-Web.js (Recommended)**: Full-featured, stable
- **Baileys (Lightweight)**: Faster, lower resource usage

**Connection Status:**
- Real-time status indicator (Connected/Disconnected/Connecting)
- QR code display with instructions
- Disconnect/Reconnect buttons

### Contacts Tab

**Add from Active Chats:**
1. Click **Refresh** to load your recent WhatsApp chats
2. Select a contact from the dropdown
3. Click **Add** to allow messages from this contact

**Add Manually:**
1. Enter contact name
2. Enter phone number in international format (e.g., +351912345678)
3. Click **Add**

**VIP Contacts:**
- Toggle ⭐ VIP checkbox for priority contacts
- VIP messages stay in queue until read
- VIP messages display longer

### Keywords Tab

**Urgent Keywords:**
1. Enter keywords that trigger urgent status (e.g., "urgent", "emergency", "911")
2. Messages containing these keywords are automatically marked urgent
3. Urgent messages have higher priority and longer display time

### Display Tab

**Target Device:**
1. Select your VoCore device from the dropdown
2. Click **Refresh** if device doesn't appear
3. Click **Test** to send a test message to the device

### Queue Tab

**Message Display Duration:**
- **Normal messages**: 5-30 seconds (default: 5s)
- **VIP/Urgent messages**: 10-60 seconds (default: 10s)

**Queue Limits:**
- **Max messages per contact**: 1-10 (default: 5)
- **Max queue size**: 1-50 (default: 10)

**VIP/Urgent Behavior:**
- Option to remove after first display or keep repeating

### Quick Replies Tab

**Configure Up to 2 Quick Replies:**
1. Set a trigger button (e.g., Button 5)
2. Choose press type (Press/Long Press/Double Press)
3. Enter reply text
4. Enable/disable confirmation overlay

## 🎮 Usage

### During a Race

1. **Receiving Messages:**
   - Normal messages: Display once for 5 seconds
   - VIP messages: Repeat every 5 minutes until read
   - Urgent messages: Display for 10 seconds with 🚨 badge

2. **Message Display:**
   - Sender name
   - Message text
   - Timestamp
   - Priority badges (⭐ VIP, 🚨 Urgent)

3. **Quick Replies:**
   - Press configured button to send pre-written reply
   - Confirmation appears on overlay (if enabled)
   - Message automatically marked as read

## 📊 SimHub Properties

The plugin exposes these properties for use in custom dashboards:

| Property | Type | Description |
|----------|------|-------------|
| `[WhatsApp.ConnectionStatus]` | String | "Connected", "Disconnected", "Connecting", "QR" |
| `[WhatsApp.ConnectedNumber]` | String | Connected phone number |
| `[WhatsApp.HasMessage]` | Boolean | true if message is currently displayed |
| `[WhatsApp.CurrentSender]` | String | Name of message sender |
| `[WhatsApp.CurrentMessage]` | String | Message text content |
| `[WhatsApp.MessageTime]` | String | Message timestamp |
| `[WhatsApp.IsVip]` | Boolean | true if sender is VIP |
| `[WhatsApp.IsUrgent]` | Boolean | true if message is urgent |
| `[WhatsApp.QueueCount]` | Integer | Number of messages in queue |
| `[WhatsApp.BackendMode]` | String | "whatsapp-web.js" or "baileys" |

## 🔧 Troubleshooting

### Plugin Doesn't Load

**Solutions:**
- Verify `WhatsAppSimHubPlugin.dll` is in SimHub folder
- Check SimHub logs: **Settings → Plugins → Plugin Logs**
- Ensure .NET Framework 4.8 is installed
- Restart SimHub

### Node.js Installation Fails

**Solutions:**
- Check internet connection
- Manually install Node.js from [nodejs.org](https://nodejs.org)
- Check folder permissions: `%AppData%\SimHub\WhatsAppPlugin\`
- Check antivirus isn't blocking downloads

### WhatsApp Won't Connect

**Solutions:**
- Ensure you scanned the QR code within 60 seconds
- Check internet connection
- Try switching backend (WhatsApp-Web.js ↔ Baileys)
- Disconnect other linked devices (WhatsApp allows max 4)
- Click **Reconnect** button

### Messages Don't Appear

**Solutions:**
- Verify sender is in **Allowed Contacts** list
- Check VoCore device is selected and online
- Click **Test** button to verify overlay works
- Check queue isn't full (increase max queue size)
- Verify message isn't filtered by keywords

### QR Code Takes Long to Appear

**Solutions:**
- Wait for Node.js modules to install (first run only)
- Check Node.js is running: Task Manager → node.exe
- Try switching backend mode
- Check `%AppData%\SimHub\WhatsAppPlugin\node\` folder exists

## 📁 File Structure

```
WhatsAppPlugin/
├── WhatsAppSimHubPlugin.dll          # Main plugin
├── Core/
│   ├── DependencyManager.cs          # Manages Node.js/npm dependencies
│   ├── MessageQueue.cs               # Intelligent queue management
│   └── WebSocketManager.cs           # Backend communication
├── Models/
│   ├── Contact.cs                    # Contact data model
│   ├── QueuedMessage.cs              # Message data model
│   └── PluginSettings.cs             # Configuration model
├── UI/
│   ├── SettingsControl.xaml          # Main settings UI
│   ├── SettingsControl.xaml.cs       # UI logic
│   └── SetupControl.xaml             # Setup wizard UI
├── Resources/
│   ├── whatsapp-server.js            # WhatsApp-Web.js backend
│   ├── baileys-server.mjs            # Baileys backend (ES Module)
│   └── package.json                  # Node.js dependencies
└── WhatsAppPlugin.cs                 # Main plugin class

Runtime Files (Auto-generated):
%AppData%\SimHub\WhatsAppPlugin\
├── node\                             # Node.js installation
│   ├── node_modules\                 # Dependencies
│   └── auth_info_baileys\            # Session data
├── settings.json                     # Plugin configuration
└── .setup-complete                   # Setup flag
```

## 🏗️ Architecture

### Backend Communication
- **WebSocket**: Real-time bidirectional communication
- **JSON Messages**: Structured data exchange
- **Event-driven**: React to WhatsApp events instantly

### Queue Management
- **Priority System**: Urgent > VIP > Normal
- **Grouping**: Multiple messages from same contact
- **Limits**: Per-contact and global limits
- **Re-notifications**: VIP/Urgent messages repeat

### Session Management
- **Persistent**: Login once, stays connected
- **Auto-reconnect**: Handles network interruptions
- **Multi-device**: Works alongside WhatsApp on phone

## 🤝 Contributing

Contributions are welcome! Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

**Coding Standards:**
- Follow C# naming conventions
- Add XML documentation comments
- Include error handling
- Test all features before submitting

## 📝 Changelog

### Version 1.0.0 (Current)
- ✅ Dual backend support (WhatsApp-Web.js + Baileys)
- ✅ Intelligent message queue with priorities
- ✅ VoCore overlay integration
- ✅ Quick replies via steering wheel buttons
- ✅ VIP contact system
- ✅ Keyword-based urgency detection
- ✅ Toast notification system
- ✅ Live connection status
- ✅ Auto-device discovery

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- **SimHub Team** - For the amazing sim racing platform
- **WhatsApp-Web.js** - For the robust WhatsApp API
- **Baileys** - For the lightweight WhatsApp implementation
- **Sim Racing Community** - For feedback and support

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/your-username/whatsapp-plugin/issues)
- **Discussions**: [GitHub Discussions](https://github.com/your-username/whatsapp-plugin/discussions)

---

**Made with ❤️ for the Sim Racing community by Bruno Silva**
