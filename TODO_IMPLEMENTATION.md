# WhatsApp Plugin - Alterações Pendentes

## ❌ ERRO CS0103 - Resolver
**Problema:** Compilador ainda vê `_queue` em linhas 114, 132, 285
**Solução:** Já corrigido no código, provável cache. Limpar bin/obj.

---

## 🎨 2. CORES DO UI - Usar Tema SimHub

**Objetivo:** Remover TODAS as cores hard-coded, usar recursos do SimHub

### Como fazer:
```xml
<!-- ANTES (hard-coded): -->
<Setter Property="Background" Value="#252526"/>
<Setter Property="Foreground" Value="#D4D4D4"/>

<!-- DEPOIS (dinâmico do SimHub): -->
<Setter Property="Background" Value="{DynamicResource SimHub_BackgroundColor}"/>
<Setter Property="Foreground" Value="{DynamicResource SimHub_ForegroundColor}"/>
```

### Recursos SimHub disponíveis:
- `SimHub_BackgroundColor`
- `SimHub_ForegroundColor`
- `SimHub_AccentColor`
- `SimHub_BorderColor`
- `SimHub_HighlightColor`

### Ficheiros a alterar:
- `UI/SettingsControl.xaml` - Todos os estilos

---

## 3. ✅ REMOVER "Save Display Settings"

**Ação:** Apagar botão + método `SaveDisplaySettings_Click`
**Implementar:** Auto-save em TODOS os handlers (sliders, ComboBox changes)

### Auto-save onde:
- `MaxMessagesPerContactSlider_ValueChanged` ✅ (já tem)
- `MaxQueueSizeSlider_ValueChanged` ✅
- `NormalDurationSlider_ValueChanged` ✅
- `UrgentDurationSlider_ValueChanged` ✅
- `ReminderIntervalSlider_ValueChanged` ✅
- `TargetDeviceCombo_SelectionChanged` ❌ (ADICIONAR)
- `PositionCombo_SelectionChanged` ❌ (ADICIONAR)

---

## 4. 🔧 TARGET DEVICE e POSITION não carregam

**Problema:** ComboBox não mostra valor salvo

### Solução em LoadSettings():
```csharp
// Carregar Target Device
var savedDevice = _settings.TargetDevice;
if (!string.IsNullOrEmpty(savedDevice))
{
    foreach (ComboBoxItem item in TargetDeviceComboBox.Items)
    {
        if (item.Tag?.ToString() == savedDevice)
        {
            TargetDeviceComboBox.SelectedItem = item;
            break;
        }
    }
}

// Carregar Position
var savedPosition = _settings.Position ?? "Top"; // Default Top
foreach (ComboBoxItem item in PositionCombo.Items)
{
    if (item.Content.ToString() == savedPosition)
    {
        PositionCombo.SelectedItem = item;
        break;
    }
}
```

---

## 5. ✅ DEFAULT POSITION = "Top"

**Onde:** `Models/PluginSettings.cs`
```csharp
public string Position { get; set; } = "Top"; // Default
```

---

## 6. 📢 AVISO "Disconnected" no VoCore

**Objetivo:** Mostrar overlay quando desconecta do WhatsApp

### Implementação:
1. No `UpdateConnectionStatus()`: quando status = "Disconnected"
2. Criar mensagem especial no overlay: "⚠️ WhatsApp Disconnected"
3. Mostrar até reconectar

### Código:
```csharp
// Em WhatsAppPlugin.cs - quando desconecta
public void HandleDisconnect()
{
    // Criar mensagem de aviso
    var warningMessage = new QueuedMessage
    {
        Id = "SYSTEM_DISCONNECTED",
        From = "System",
        Number = "",
        Body = "⚠️ WhatsApp Disconnected\nCheck SimHub settings",
        Timestamp = DateTime.Now,
        IsVip = true, // Alta prioridade
        IsUrgent = false
    };
    
    _messageQueue.AddMessage(warningMessage);
}

// Remover quando reconectar
public void HandleReconnect()
{
    _messageQueue.RemoveMessage("SYSTEM_DISCONNECTED");
}
```

---

## 7. 🔝 OVERLAY SEMPRE POR CIMA (SEM MEXER NO DASH)

**Desafio:** Injetar no framebuffer do VoCore ANTES do render final

### Abordagem:
SimHub tem API para `BitmapDisplayDevice` que permite acesso ao buffer gráfico.

### Implementação:
```csharp
// 1. Obter device VoCore
var vocoreDevice = pluginManager.GetDevice<BitmapDisplayDevice>(settings.TargetDevice);

// 2. Hook no evento de render
vocoreDevice.OnBeforeRender += (bitmap) =>
{
    // 3. Desenhar overlay DIRETAMENTE no bitmap
    using (Graphics g = Graphics.FromImage(bitmap))
    {
        // Desenhar mensagem por cima de tudo
        DrawMessageOverlay(g, currentMessage);
    }
};
```

### Criar classe `OverlayRenderer.cs`:
- `DrawMessageOverlay(Graphics g, QueuedMessage msg)`
- `DrawConnectionWarning(Graphics g)`
- Suporta diferentes posições (Top/Center/Bottom)
- Suporta diferentes resoluções VoCore

---

## 📁 FICHEIROS A CRIAR/MODIFICAR:

### Criar:
- `Core/OverlayRenderer.cs` - Renderização gráfica no VoCore

### Modificar:
- `UI/SettingsControl.xaml` - Remover cores hard-coded, remover botão Save
- `UI/SettingsControl.xaml.cs` - Auto-save, carregar ComboBox values
- `Models/PluginSettings.cs` - Default Position = "Top"
- `WhatsAppPlugin.cs` - Hook no VoCore render, avisos de disconnect
- `Core/MessageQueue.cs` - Limpar bin/obj para resolver erro

---

## 🚀 ORDEM DE IMPLEMENTAÇÃO:

1. ✅ Limpar erro de compilação (limpar cache)
2. ✅ Cores dinâmicas do SimHub
3. ✅ Auto-save + remover botão
4. ✅ Carregar Target Device e Position
5. ✅ Default Position = Top
6. ✅ Aviso Disconnected
7. ✅ Overlay renderer com hook no VoCore

---

## ⚠️ QUESTÕES TÉCNICAS:

**Q:** Como aceder ao BitmapDisplayDevice do VoCore?
**A:** Via `pluginManager.GetAllDevices()` filtrar por VoCore, fazer cast.

**Q:** Quando fazer o hook no render?
**A:** No `Init()` do plugin, depois de device selecionado.

**Q:** Como saber resolução do VoCore?
**A:** `device.Settings.Width` e `device.Settings.Height`

**Q:** Performance do overlay?
**A:** Desenhar apenas quando há mensagem ativa (não em cada frame).
