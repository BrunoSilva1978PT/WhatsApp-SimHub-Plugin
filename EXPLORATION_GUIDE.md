# WhatsApp SimHub Plugin - VoCore Settings Explorer

## 🆕 O QUE MUDOU

Adicionei **exploração avançada** do VoCore Settings para descobrir a API interna de overlay/information display do SimHub.

### Novo ficheiro criado:
- **`Core/VoCoreExplorer.cs`** - Classe especializada em exploração profunda

### Modificações:
- **`WhatsAppPlugin.cs`** - Usa agora o VoCoreExplorer no método `AttachToVoCore()`

---

## 🎯 OBJETIVO

Descobrir como o SimHub faz o **Information Overlay** internamente, para implementar notificações WhatsApp que apareçam SOBRE o DDU sem substituir o ecrã todo (problema atual com `SendBitmap()`).

---

## 🔍 O QUE A EXPLORAÇÃO FAZ

O `VoCoreExplorer` explora em profundidade:

### 1. **Settings do VoCore**
   - Todas as propriedades públicas e privadas
   - Foco especial em `DDUScreenSettings`
   - Métodos públicos e privados
   - Campos (fields)

### 2. **DDUScreenSettings (Foco Principal)**
   - **Propriedades** - Todas, marcando as relevantes com ★★★
   - **Métodos** - Procura por métodos de overlay/display/draw
   - **Campos** - Incluindo privados (via Reflection)
   - **Objetos complexos** - Explora até 3 níveis de profundidade

### 3. **OwnerDevice**
   - Propriedades relacionadas com overlay/settings
   - Métodos de renderização

### 4. **BitmapDevice**
   - Métodos disponíveis no device do VoCore

---

## 📋 PALAVRAS-CHAVE PROCURADAS

A exploração marca com **★★★ RELEVANT** tudo que contenha:
- `overlay`
- `info` / `information`
- `message`
- `notification`
- `popup`
- `display` / `show`
- `text` / `label`
- `layer`
- `draw` / `render` / `paint`

---

## 🚀 COMO USAR

### 1. Compilar
```bash
# No Windows, na pasta do projeto:
msbuild WhatsAppSimHubPlugin.sln /p:Configuration=Release
```

Ou abra `WhatsAppSimHubPlugin.sln` no Visual Studio e compile.

### 2. Instalar
Copie `bin\Release\WhatsAppSimHubPlugin.dll` para a pasta do SimHub.

### 3. Configurar VoCore no Plugin
1. Abra SimHub
2. Vá a Settings → Plugins → WhatsApp Plugin
3. Na tab "DDU/Overlay":
   - Clique em "🔄 Refresh Devices"
   - Selecione o VoCore DDU 1 (800x480)
   - Clique "Save Settings"

### 4. Ver os Logs
**A exploração acontece automaticamente quando o plugin conecta ao VoCore!**

Para ver os resultados:
1. Vá à pasta do plugin: `%APPDATA%\SimHub\WhatsAppPlugin\logs\`
2. Abra o ficheiro `messages.log`
3. Procure por:
   - `★★★ RELEVANT` - Propriedades/métodos importantes
   - `DDU SCREEN SETTINGS` - Secção principal
   - `VOCORE SETTINGS EXPLORATION` - Início da exploração

---

## 📊 FORMATO DOS LOGS

### Exemplo de propriedade relevante:
```
★★★ RELEVANT: OverlayManager (OverlayManager) = SimHub.Plugins.OverlayManager
  OverlayManager.ShowText (Boolean) = False
  OverlayManager.TextPosition (String) = Top
  OverlayManager.TextDuration (Int32) = 5000
```

### Exemplo de método relevante:
```
★★★ RELEVANT METHOD: ShowTextOverlay(String text, Int32 x, Int32 y, Int32 duration) -> Void
```

### Exemplo de campo relevante:
```
★★★ RELEVANT FIELD: _informationOverlay (InformationOverlayManager) = ...
```

---

## 🔬 O QUE PROCURAR NOS LOGS

### ✅ BONS SINAIS:
1. **Propriedades com "Overlay":**
   - `OverlayManager`, `InformationOverlay`, `TextOverlay`
   
2. **Métodos que mostram texto:**
   - `ShowTextOverlay()`, `DisplayMessage()`, `AddOverlayText()`
   
3. **Propriedades de layers:**
   - `Layers`, `OverlayLayers`, `TextLayer`
   
4. **Objetos complexos relevantes:**
   - Se uma propriedade retorna um objeto manager/controller

### 🎯 EXEMPLOS DO QUE PODE EXISTIR:

**Hipótese 1 - Propriedade Directa:**
```csharp
DDUScreenSettings.OverlayText = "Mensagem WhatsApp";
DDUScreenSettings.ShowOverlay = true;
```

**Hipótese 2 - Método de Overlay:**
```csharp
DDUScreenSettings.ShowTextOverlay("Mensagem", x, y, duration);
```

**Hipótese 3 - Manager/Controller:**
```csharp
var overlayManager = DDUScreenSettings.OverlayManager;
overlayManager.AddText("Mensagem", position, style);
```

---

## 🐛 TROUBLESHOOTING

### Não vejo os logs
- Verifique se o VoCore está selecionado nas settings do plugin
- Verifique o caminho: `%APPDATA%\SimHub\WhatsAppPlugin\logs\messages.log`
- Reinicie o SimHub depois de instalar o plugin

### Exploração não acontece
- A exploração só acontece quando o plugin conecta ao VoCore
- Certifique-se que o VoCore está:
  - Ligado
  - Configurado no SimHub
  - Selecionado nas settings do plugin WhatsApp

### Muitos logs, difícil de ler
- Use CTRL+F para procurar "★★★"
- Foque primeiro na secção "DDU SCREEN SETTINGS"
- Ignore linhas sem ★★★ inicialmente

---

## 📝 PRÓXIMOS PASSOS

Após recolher e analisar os logs:

1. **Identificar a API correcta** de overlay descoberta
2. **Criar versão de teste** que usa essa API
3. **Validar** que funciona sem corromper o ecrã
4. **Implementar** no OverlayRenderer.cs
5. **Testar** com mensagens WhatsApp reais
6. **Fase 4 completa** ✅

---

## 📧 SUPORTE

Se encontrar erros ou precisar de ajuda:
1. Copie TODO o conteúdo de `messages.log`
2. Especialmente as secções com ★★★
3. Envie para análise

---

**Data:** 27 Janeiro 2025  
**Versão:** 1.0 - VoCore Settings Explorer
