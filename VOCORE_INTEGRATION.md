# VoCore Overlay Integration - Research

## 🎯 OBJETIVO
Injetar gráficos (mensagens WhatsApp) diretamente no framebuffer do VoCore ANTES do render final.

## 📦 CLASSES SIMHUB RELEVANTES

### `BitmapDisplayDevice<T>`
- Classe base para devices que renderizam bitmaps (VoCore, Nextion, etc)
- Localização: `SimHub.Plugins.dll`
- Generic type `T` = Settings class (ex: `VOCORESettings`)

### `BitmapDisplayBase`
- Interface/base para gestão de bitmaps
- Tem propriedade `CurrentBitmap` ou similar

### `DeviceInstance`
- Wrapper para devices no SimHub
- Propriedade `BitmapDisplayInstance` dá acesso ao BitmapDisplayBase

## 🔍 ESTRATÉGIAS POSSÍVEIS

### Estratégia 1: Hook no evento de render ✅ (PREFERIDA)
```csharp
// 1. Obter device VoCore
var devices = pluginManager.GetAllDevices(true);
var vocoreDevice = devices.FirstOrDefault(d => 
    d.GetType().FullName.Contains("VOCORE") && 
    ((dynamic)d).CustomName == _settings.TargetDevice
);

// 2. Obter BitmapDisplayInstance
var bitmapDisplay = ((dynamic)vocoreDevice).BitmapDisplayInstance;

// 3. Subscribir evento (se existir)
// OPÇÃO A: Evento OnBeforeRender
bitmapDisplay.OnBeforeRender += (bitmap) => {
    _overlayRenderer.RenderOverlay(bitmap);
};

// OPÇÃO B: Evento OnPaint
bitmapDisplay.OnPaint += (sender, args) => {
    _overlayRenderer.RenderOverlay(args.Bitmap);
};

// OPÇÃO C: Override de método
// Se não houver eventos, podemos tentar criar um wrapper
```

### Estratégia 2: Polling do CurrentBitmap ❌ (NÃO RECOMENDADA)
```csharp
// Timer que acede ao bitmap a cada frame
_renderTimer.Tick += (s, e) => {
    var bitmap = bitmapDisplay.CurrentBitmap;
    if (bitmap != null) {
        _overlayRenderer.RenderOverlay(bitmap);
    }
};
```
**Problema:** Pode causar flickering, não garante "sempre por cima"

### Estratégia 3: Custom Screen Plugin Extension 🤔
```csharp
// SimHub permite estender devices com DeviceExtension
public class WhatsAppOverlayExtension : DeviceExtension
{
    public override void OnRender(Bitmap bitmap)
    {
        _overlayRenderer.RenderOverlay(bitmap);
    }
}

// Registar extensão
vocoreDevice.DeviceExtensions.Add(new WhatsAppOverlayExtension());
```

## 🔨 IMPLEMENTAÇÃO NO PLUGIN

### WhatsAppPlugin.cs - Alterações necessárias:

```csharp
public class WhatsAppPlugin : IPlugin, IDataPlugin
{
    private OverlayRenderer _overlayRenderer;
    private object _vocoreDevice;
    private bool _overlayAttached = false;

    public void Init(PluginManager pluginManager)
    {
        // ... código existente ...
        
        // Criar renderer
        _overlayRenderer = new OverlayRenderer(_settings);
        
        // Tentar anexar ao VoCore (se já selecionado)
        if (!string.IsNullOrEmpty(_settings.TargetDevice))
        {
            AttachOverlayToVoCore();
        }
    }

    public void ApplyDisplaySettings()
    {
        // Chamado quando Target Device muda
        DetachOverlay();
        
        if (!string.IsNullOrEmpty(_settings.TargetDevice))
        {
            AttachOverlayToVoCore();
        }
    }

    private void AttachOverlayToVoCore()
    {
        try
        {
            // 1. Obter todos os devices
            var devices = this.GetAllDevices(true);
            
            // 2. Encontrar o VoCore com nome matching
            _vocoreDevice = devices.FirstOrDefault(d => 
            {
                try
                {
                    var deviceType = d.GetType();
                    if (!deviceType.FullName.Contains("VOCORE")) 
                        return false;
                    
                    var nameProp = deviceType.GetProperty("MainDisplayName") ?? 
                                  deviceType.GetProperty("CustomName");
                    var name = nameProp?.GetValue(d)?.ToString();
                    
                    return name == _settings.TargetDevice;
                }
                catch
                {
                    return false;
                }
            });
            
            if (_vocoreDevice == null)
            {
                SimHub.Logging.Current.Error("WhatsApp Plugin: VoCore device not found!");
                return;
            }
            
            // 3. Obter BitmapDisplayInstance
            var deviceType = _vocoreDevice.GetType();
            var bitmapProp = deviceType.GetProperty("BitmapDisplayInstance");
            var bitmapDisplay = bitmapProp?.GetValue(_vocoreDevice);
            
            if (bitmapDisplay == null)
            {
                SimHub.Logging.Current.Error("WhatsApp Plugin: BitmapDisplayInstance not found!");
                return;
            }
            
            // 4. Hook no render (via reflection)
            // TENTAR encontrar evento OnBeforeRender, OnPaint, etc
            var bitmapType = bitmapDisplay.GetType();
            
            // OPÇÃO A: Procurar evento OnBeforeRender
            var eventInfo = bitmapType.GetEvent("OnBeforeRender");
            if (eventInfo != null)
            {
                var handler = new Action<Bitmap>((bitmap) => {
                    _overlayRenderer.RenderOverlay(bitmap);
                });
                eventInfo.AddEventHandler(bitmapDisplay, handler);
                _overlayAttached = true;
                SimHub.Logging.Current.Info("WhatsApp Plugin: Overlay attached via OnBeforeRender!");
                return;
            }
            
            // OPÇÃO B: Procurar evento OnPaint
            eventInfo = bitmapType.GetEvent("OnPaint");
            if (eventInfo != null)
            {
                // TODO: Adaptar handler conforme assinatura do evento
                _overlayAttached = true;
                SimHub.Logging.Current.Info("WhatsApp Plugin: Overlay attached via OnPaint!");
                return;
            }
            
            // OPÇÃO C: Polling como fallback
            SimHub.Logging.Current.Warn("WhatsApp Plugin: No render event found, using polling fallback");
            // TODO: Implementar timer polling
        }
        catch (Exception ex)
        {
            SimHub.Logging.Current.Error($"WhatsApp Plugin: Failed to attach overlay - {ex.Message}");
        }
    }

    private void DetachOverlay()
    {
        if (_vocoreDevice != null && _overlayAttached)
        {
            // TODO: Remover event handlers
            _overlayAttached = false;
        }
    }

    // Atualizar quando mensagem muda
    private void OnMessageDisplay(QueuedMessage message)
    {
        _overlayRenderer.SetMessage(message);
        // Força refresh se necessário
    }
}
```

## ⚠️ PONTOS CRÍTICOS

1. **Descobrir nome exato do evento de render**
   - Pode ser: `OnBeforeRender`, `OnPaint`, `OnFrameUpdate`, `BeforeDraw`
   - Usar reflection para listar TODOS os eventos disponíveis

2. **Thread safety**
   - Render pode acontecer em thread diferente
   - Usar locks se necessário

3. **Performance**
   - Overlay deve ser RÁPIDO (<1ms)
   - Cachear objetos Graphics quando possível

4. **Resolução dinâmica**
   - VoCore pode ser 480x800, 320x480, etc
   - Ajustar tamanhos de font dinamicamente

## 📝 PRÓXIMOS PASSOS

1. ✅ Criar OverlayRenderer.cs
2. ⏳ Adicionar código de attach no WhatsAppPlugin.cs
3. ⏳ Testar com VoCore real
4. ⏳ Descobrir evento correto via reflection
5. ⏳ Implementar fallback se evento não existir
6. ⏳ Adicionar aviso de disconnect
7. ⏳ Testar performance em corrida

## 🔍 CÓDIGO PARA DESCOBRIR EVENTOS

```csharp
// Adicionar no AttachOverlayToVoCore para debug
var bitmapType = bitmapDisplay.GetType();
SimHub.Logging.Current.Info($"BitmapDisplay type: {bitmapType.FullName}");

// Listar TODOS os eventos
var events = bitmapType.GetEvents();
foreach (var evt in events)
{
    SimHub.Logging.Current.Info($"  Event: {evt.Name} ({evt.EventHandlerType})");
}

// Listar TODOS os métodos públicos
var methods = bitmapType.GetMethods();
foreach (var method in methods)
{
    if (method.Name.Contains("Render") || method.Name.Contains("Draw") || method.Name.Contains("Paint"))
    {
        SimHub.Logging.Current.Info($"  Method: {method.Name}");
    }
}
```
