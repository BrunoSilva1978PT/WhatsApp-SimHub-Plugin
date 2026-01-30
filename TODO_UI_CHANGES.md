# TODO: UI Changes Pending

## ✅ COMPLETED:
1. ✅ Removed automatic timer from DeviceManager
2. ✅ Added Refresh Devices button in Display tab
3. ✅ Fixed "VoCore or monitor" text to just "VoCore"
4. ✅ Replaced Message Grouping + Queue Settings with sliders
5. ✅ Removed "First time setup" text from Connection tab
6. ✅ Added Foreground="#FFFFFF" to titles for better contrast

## ⏳ PENDING:

### Code-behind (SettingsControl.xaml.cs):

1. **Add RefreshDevicesButton_Click handler:**
```csharp
private void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
{
    _plugin.GetDeviceManager().RefreshDevices();
    LoadAvailableDevices();
    MessageBox.Show("Devices refreshed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
}
```

2. **Add slider ValueChanged handlers:**
```csharp
private void MaxMessagesPerContactSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (MaxMessagesPerContactValue != null)
    {
        MaxMessagesPerContactValue.Text = ((int)e.NewValue).ToString();
        _settings.MaxGroupSize = (int)e.NewValue;
    }
}

private void MaxQueueSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (MaxQueueSizeValue != null)
    {
        MaxQueueSizeValue.Text = ((int)e.NewValue).ToString();
        _settings.MaxQueueSize = (int)e.NewValue;
    }
}

private void NormalDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (NormalDurationValue != null)
    {
        NormalDurationValue.Text = $"{(int)e.NewValue}s";
        _settings.NormalDuration = (int)e.NewValue * 1000; // Convert to ms
    }
}

private void UrgentDurationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (UrgentDurationValue != null)
    {
        UrgentDurationValue.Text = $"{(int)e.NewValue}s";
        _settings.UrgentDuration = (int)e.NewValue * 1000; // Convert to ms
    }
}

private void ReminderIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
{
    if (ReminderIntervalValue != null)
    {
        ReminderIntervalValue.Text = $"{(int)e.NewValue} min";
        _settings.ReminderInterval = (int)e.NewValue; // Store in minutes
    }
}
```

3. **Update LoadSettings() to load slider values:**
```csharp
// In LoadSettings():
MaxMessagesPerContactSlider.Value = _settings.MaxGroupSize;
MaxQueueSizeSlider.Value = _settings.MaxQueueSize;
NormalDurationSlider.Value = _settings.NormalDuration / 1000; // Convert from ms to seconds
UrgentDurationSlider.Value = _settings.UrgentDuration / 1000; // Convert from ms to seconds
ReminderIntervalSlider.Value = _settings.ReminderInterval; // Already in minutes
```

4. **Update SaveSettings() - sliders auto-save via ValueChanged, so may not need changes**

5. **Fix Disconnect/Reconnect button states based on connection:**
```csharp
private void UpdateConnectionStatus(string status, string number = null)
{
    Dispatcher.Invoke(() =>
    {
        StatusText.Text = status;
        
        if (status == "Connected")
        {
            StatusIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0E7A0D"));
            
            // Disconnect enabled, Reconnect disabled
            DisconnectButton.IsEnabled = true;
            DisconnectButton.Opacity = 1.0;
            ReconnectButton.IsEnabled = false;
            ReconnectButton.Opacity = 0.5;
            
            if (!string.IsNullOrEmpty(number))
            {
                ConnectedNumberText.Text = $"Connected: {number}";
            }
        }
        else if (status == "Disconnected" || status == "Error")
        {
            StatusIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F48771"));
            
            // Disconnect disabled, Reconnect enabled
            DisconnectButton.IsEnabled = false;
            DisconnectButton.Opacity = 0.5;
            ReconnectButton.IsEnabled = true;
            ReconnectButton.Opacity = 1.0;
            
            ConnectedNumberText.Text = "No number connected";
            QRCodeImage.Visibility = Visibility.Collapsed;
            QRCodeInstructions.Visibility = Visibility.Collapsed;
        }
        else if (status == "Connecting")
        {
            StatusIndicator.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFA500"));
            
            // Both disabled while connecting
            DisconnectButton.IsEnabled = false;
            DisconnectButton.Opacity = 0.5;
            ReconnectButton.IsEnabled = false;
            ReconnectButton.Opacity = 0.5;
        }
    });
}
```

6. **Remove old TextBox references:**
- Remove MaxGroupSizeText
- Remove GroupWaitTimeText
- Remove GroupDurationText
- Remove MaxQueueSizeText (replaced by slider)
- Remove NormalDurationText (replaced by slider)
- Remove UrgentDurationText (replaced by slider)
- Remove EnableGroupingCheck

7. **Add GetDeviceManager() to WhatsAppPlugin.cs:**
```csharp
public DeviceManager GetDeviceManager()
{
    return _deviceManager;
}
```

## NOTES:
- Sliders use seconds for durations (convert to/from ms in code)
- Reminder interval uses minutes
- Max messages per contact replaces old "MaxGroupSize"
- No more "EnableGrouping" checkbox - auto-enabled if max > 1
