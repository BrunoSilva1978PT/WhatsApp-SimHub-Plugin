# VoCoreExplorer v3 - Documentação Completa

## 🎯 OBJETIVO PRINCIPAL

Descobrir **TODAS as formas possíveis** de fazer overlay no VoCore **SEM destruir** a configuração do utilizador.

---

## ❓ O PROBLEMA QUE ESTAMOS A RESOLVER

**Situação atual:**
```
User tem configurado:
├─ Dashboard Principal: DDU com RPM, Speed, etc
└─ Overlay Dashboard: Information Overlay (pit stops, combustível)
   ├─ CurrentOverlayDashboard = user's overlay
   └─ UseOverlayDashboard = true
```

**O que NÃO podemos fazer:**
```csharp
// ❌ MAU - Substitui overlay do user
_vocoreSettings.CurrentOverlayDashboard = whatsappOverlay;
_vocoreSettings.UseOverlayDashboard = true;
// → User perde o seu overlay de informação de corrida!
```

**O que precisamos:**
- ✅ Mostrar notificações WhatsApp **POR CIMA** do DDU
- ✅ **SEM substituir** o overlay do utilizador
- ✅ **SEM corromper** o ecrã do VoCore
- ✅ Performance óptima (30 FPS+)

---

## 🔬 O QUE O EXPLORER V3 INVESTIGA

### **1. RENDERING METHODS (5 categorias)**

#### **CATEGORY 1: SEND/DRAW METHODS**
Procura por todos os métodos `SendBitmap()` e variantes:
```csharp
SendBitmap(byte[] data, int width, int height, bool flip)
SendBitmap(byte[] data, int width, int height, bool flip, RenderMode mode)  // ⚡ Possível!
SendBitmapRegion(byte[] data, int x, int y, int width, int height)        // ⚡ Ideal!
DrawBitmap(Bitmap bmp, Point location, BlendMode mode)                     // ⚡ Perfeito!
```

**O que analisamos:**
- ✅ Número de parâmetros
- ✅ Tipos de parâmetros (bool flags, enums)
- ✅ Se existe parâmetro `BlendMode`, `RenderMode`, `OverlayMode`
- ✅ Se existe parâmetro para coordenadas (x, y) → indica partial draw

#### **CATEGORY 2: PARTIAL/REGION METHODS**
```csharp
SendPartialBitmap(byte[] data, Rectangle region)          // ⚡ BINGO!
UpdateRegion(byte[] data, int x, int y, int w, int h)     // ⚡ BINGO!
DrawRect(byte[] data, Rectangle bounds)                    // ⚡ Possível
```

#### **CATEGORY 3: BLEND/ALPHA/COMPOSITE METHODS**
```csharp
BlendBitmap(byte[] data, float alpha)                      // ⚡ Transparência!
CompositeBitmap(byte[] overlay, byte[] base)               // ⚡ Mistura 2 bitmaps!
SetOverlayAlpha(float alpha)                               // ⚡ Controlo de opacidade
```

#### **CATEGORY 4: BUFFER/FRAME METHODS**
```csharp
GetCurrentFrame() -> Bitmap                                // ⚡ Captura frame atual!
GetFrameBuffer() -> byte[]                                 // ⚡ Acesso ao buffer!
CaptureScreen() -> Bitmap                                  // ⚡ Screenshot do VoCore
```

Se existir algum destes, podemos:
```csharp
// Capturar frame atual (com DDU + overlay user)
Bitmap currentFrame = vocoreDevice.GetCurrentFrame();

// Desenhar WhatsApp por cima
using (Graphics g = Graphics.FromImage(currentFrame))
{
    DrawWhatsAppNotification(g, message);
}

// Enviar frame composto
vocoreDevice.SendBitmap(currentFrame);
```

#### **CATEGORY 5: OTHER RELEVANT METHODS**
Qualquer método que contenha: `render`, `paint`, `display`, `show`, `image`, etc.

---

### **2. OVERLAY DASHBOARD SYSTEM**

#### **CurrentOverlayDashboard (DashboardSelection)**
Explora **em profundidade** o objeto `DashboardSelection`:

```csharp
// Propriedades procuradas:
DashboardPath        // Caminho do ficheiro .simhubdash
DashboardId          // ID interno
DashboardName        // Nome do dashboard
Content              // Conteúdo (pode ser JSON/XML)
IsLoaded             // Se está carregado
CanStack             // ⚡ SE pode empilhar múltiplos overlays!
```

**O ponto crítico:** Se descobrirmos que `CanStack = true`, podemos ter **MÚLTIPLOS overlays**:
```
┌─────────────────────────────┐
│   Dashboard Principal       │
│                             │
│   ├─ Overlay 1 (User)       │  ← Information overlay do user
│   └─ Overlay 2 (WhatsApp)   │  ← Nosso overlay WhatsApp
└─────────────────────────────┘
```

#### **DashPlaylistManager**
Pode ter métodos para:
```csharp
AddOverlay(Dashboard overlay)            // ⚡ Adiciona overlay sem substituir
RemoveOverlay(Dashboard overlay)         // Remove overlay específico
GetActiveOverlays() -> List<Dashboard>   // Lista todos os overlays ativos
```

#### **Métodos de manipulação**
```csharp
LoadOverlayDashboard(string path)                    // Carregar de ficheiro
SetOverlayDashboard(DashboardSelection dashboard)    // Definir programaticamente
SwitchOverlay(DashboardSelection overlay, bool add)  // ⚡ Adicionar vs Substituir
```

---

### **3. PRIVATE FIELDS**

Explora campos privados que podem ter managers escondidos:

```csharp
_renderManager      // ⚡ Manager de rendering
_overlayRenderer    // ⚡ Renderer de overlays!
_compositor         // ⚡ Compositor de layers
_layerManager       // ⚡ Gestão de camadas
_frameBuffer        // ⚡ Acesso direto ao buffer
```

**Exemplo do que procuramos:**
```csharp
// Se encontrarmos algo como:
private IOverlayRenderer _overlayRenderer;

// Podemos aceder via reflection:
var field = type.GetField("_overlayRenderer", BindingFlags.NonPublic | BindingFlags.Instance);
var overlayRenderer = field.GetValue(vocoreDevice);

// E descobrir métodos:
overlayRenderer.DrawOverlay(bitmap, x, y, alpha);  // ⚡ BINGO!
```

---

### **4. EVENTS (Rendering Hooks)**

Procura eventos que podemos **hookar** para injetar overlay:

```csharp
BeforeRender    // Executado ANTES de renderizar frame
AfterRender     // Executado DEPOIS de renderizar frame  ⚡ IDEAL!
OnPaint         // Evento de pintura
OnUpdate        // Evento de atualização
```

**Como usaríamos:**
```csharp
// Subscrever evento
vocoreDevice.AfterRender += (sender, args) => 
{
    // O frame está renderizado, agora injetamos WhatsApp
    var graphics = args.Graphics; // Se passar Graphics
    DrawWhatsAppNotification(graphics, message);
};
```

---

## 📊 FORMATO DOS LOGS

### **Métodos com parâmetros detalhados:**
```
★★★ SendBitmap(Byte[] data, Int32 width, Int32 height, Boolean flip) -> Void
    📌 DETAILED PARAMS:
       - data (System.Byte[])
       - width (System.Int32)
       - height (System.Int32)
       - flip (System.Boolean)
         ⚡ BOOLEAN FLAG - Possible blend/overlay mode!
```

### **Enums com valores possíveis:**
```
★★★ DrawBitmap(Bitmap bmp, Point location, RenderMode mode) -> Void
    📌 DETAILED PARAMS:
       - mode (SimHub.Plugins.RenderMode)
         ⚡ ENUM - Possible rendering modes:
            * Normal
            * Blend        ← ⚡ ISTO É O QUE QUEREMOS!
            * Overlay      ← ⚡ OU ISTO!
            * Add
            * Multiply
```

### **Campos privados descobertos:**
```
★★★ PRIVATE FIELD: _overlayRenderer (IOverlayRenderer)
    Type: SimHub.Plugins.Rendering.OverlayRenderer
    Value: SimHub.Plugins.Rendering.OverlayRenderer
    → Exploring this field in depth:
      DrawOverlay(Bitmap bmp, Int32 x, Int32 y, Single alpha) -> Void  ⚡ JACKPOT!
```

---

## 🎯 CENÁRIOS POSSÍVEIS

### **CENÁRIO A: Partial Draws existem**
```csharp
// ✅ Enviar apenas a região da notificação
vocoreDevice.SendPartialBitmap(whatsappBitmap, x: 0, y: 0, w: 800, h: 120);
```
**Vantagem:** Eficiente, não toca no resto do ecrã.

### **CENÁRIO B: Blend Mode existe**
```csharp
// ✅ Enviar com modo blend/overlay
vocoreDevice.SendBitmap(whatsappBitmap, mode: RenderMode.Overlay);
```
**Vantagem:** Renderiza por cima automaticamente.

### **CENÁRIO C: GetCurrentFrame existe**
```csharp
// ✅ Capturar, modificar, enviar
Bitmap frame = vocoreDevice.GetCurrentFrame();
using (Graphics g = Graphics.FromImage(frame))
{
    DrawWhatsAppNotification(g, message);
}
vocoreDevice.SendBitmap(frame);
```
**Vantagem:** Controlo total, não destruímos nada.

### **CENÁRIO D: Events existem**
```csharp
// ✅ Hook no evento de rendering
vocoreDevice.AfterRender += (s, e) =>
{
    e.Graphics.DrawString(message, font, brush, x, y);
};
```
**Vantagem:** Mais elegante, não invasivo.

### **CENÁRIO E: Multiple Overlays possível**
```csharp
// ✅ Adicionar overlay sem substituir
dashboardManager.AddOverlay(whatsappOverlay);
// User overlay continua ativo!
```
**Vantagem:** Zero impacto no user.

### **CENÁRIO F: Nada funciona (fallback)**
```csharp
// ⚠️ Guardar, substituir, restaurar
_originalOverlay = settings.CurrentOverlayDashboard;
_originalUseOverlay = settings.UseOverlayDashboard;

settings.CurrentOverlayDashboard = whatsappOverlay;
settings.UseOverlayDashboard = true;

Task.Delay(5000).ContinueWith(_ => {
    settings.CurrentOverlayDashboard = _originalOverlay;
    settings.UseOverlayDashboard = _originalUseOverlay;
});
```
**Desvantagem:** User perde overlay temporariamente (5s).

---

## 🚀 COMO USAR OS RESULTADOS

### **1. Ler os logs**
```bash
%APPDATA%\SimHub\WhatsAppPlugin\logs\messages.log
```

### **2. Procurar por:**
- ⚡ Métodos com "Partial", "Region", "Blend", "Overlay"
- ⚡ Parâmetros booleanos ou enums em `SendBitmap`
- ⚡ Métodos `GetCurrentFrame`, `CaptureScreen`
- ⚡ Campos privados `_overlayRenderer`, `_compositor`
- ⚡ Eventos `AfterRender`, `BeforeRender`

### **3. Testar a API descoberta**
```csharp
// Exemplo: Se descobrimos SendBitmap com blend
var sendMethod = device.GetType().GetMethod("SendBitmap");
var parameters = sendMethod.GetParameters();

// Testar com modo blend
if (parameters.Length == 5 && parameters[4].ParameterType.IsEnum)
{
    // Existe um modo enum! Testar valores
    sendMethod.Invoke(device, new object[] { 
        bitmapBytes, 
        800, 
        480, 
        false, 
        Enum.Parse(parameters[4].ParameterType, "Overlay")  // ⚡
    });
}
```

---

## 📈 PROBABILIDADES DE SUCESSO

| Cenário | Probabilidade | Impacto no User |
|---------|---------------|-----------------|
| Partial Draws | 70% | ✅ Zero |
| Blend Mode | 60% | ✅ Zero |
| GetCurrentFrame | 50% | ✅ Zero |
| Rendering Events | 40% | ✅ Zero |
| Multiple Overlays | 30% | ✅ Zero |
| Fallback (Save/Restore) | 100% | ⚠️ Perda temporária |

---

## 🔮 PRÓXIMOS PASSOS APÓS LOGS

1. **Analisar os logs** → Identificar melhor cenário
2. **Criar PoC** → Testar API descoberta
3. **Validar** → Confirmar que não corrompe ecrã
4. **Implementar** → Integrar no OverlayRenderer.cs
5. **Documentar** → Atualizar README com solução final

---

**Versão:** 3.0  
**Data:** 27 Janeiro 2025  
**Autor:** Bruno + Claude  
**Status:** Pronto para exploração completa! 🚀
