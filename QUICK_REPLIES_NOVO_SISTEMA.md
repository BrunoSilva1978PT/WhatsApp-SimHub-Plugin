# 🎮 Quick Replies - Novo Sistema de Botões

## 📋 O Que Mudou?

### ❌ Sistema Antigo (INCORRETO)
- Plugin tentava ler estado dos botões manualmente com `GetPropertyValue("InputStatus.{buttonName}")`
- Configuração de botões era feita nos Settings do plugin
- Não funcionava porque os botões não estavam registados como INPUTS

### ✅ Sistema Novo (CORRETO)
- Plugin regista BUTTON INPUTS como **Sources** (aparecem na coluna Source do Mapping Picker)
- Plugin regista ACTIONS como **Targets** (aparecem na coluna Target do Mapping Picker)  
- **Utilizador mapeia** os botões para as actions no UI do SimHub (Control and Events)
- SimHub chama as Actions automaticamente quando os botões são primidos

## 🔧 Como Funciona Agora

### 1️⃣ Button Inputs Registados (Sources)
```csharp
this.AddButtonInput("Reply1Button", "WhatsApp", "Send Reply 1");
this.AddButtonInput("Reply2Button", "WhatsApp", "Send Reply 2");
this.AddButtonInput("DismissButton", "WhatsApp", "Dismiss Message");
```

Estes aparecem na coluna **Source** do Mapping Picker sob o plugin "WhatsApp":
- `WhatsApp` > `Reply1Button` - Send Reply 1
- `WhatsApp` > `Reply2Button` - Send Reply 2  
- `WhatsApp` > `DismissButton` - Dismiss Message

### 2️⃣ Actions Registadas (Targets)
```csharp
this.AddAction("WhatsApp.SendReply1", (a, b) => { SendQuickReply(1); });
this.AddAction("WhatsApp.SendReply2", (a, b) => { SendQuickReply(2); });
this.AddAction("WhatsApp.DismissMessage", (a, b) => { DismissCurrentMessage(); });
```

Estas aparecem na coluna **Target** do Mapping Picker:
- `WhatsAppPlugin` > `WhatsApp.SendReply1`
- `WhatsAppPlugin` > `WhatsApp.SendReply2`
- `WhatsAppPlugin` > `WhatsApp.DismissMessage`

### 3️⃣ Mapeamento pelo Utilizador

O utilizador vai a **Controls and Events** no SimHub e mapeia:

**Source → Target:**
```
SCREEN1_BUTTON1 (ShortPress) → WhatsApp.SendReply1
SCREEN1_BUTTON1 (LongPress)  → WhatsApp.SendReply2
SCREEN1_BUTTON2 (ShortPress) → WhatsApp.DismissMessage
```

Ou usando teclas físicas:
```
KeyboardReaderPlugin.Alt+Shift+1 → WhatsApp.SendReply1
KeyboardReaderPlugin.Alt+Shift+2 → WhatsApp.SendReply2
```

## 📝 Código Removido

### Do WhatsAppPlugin.cs:
```csharp
// ❌ REMOVIDO - Variáveis de estado
private bool _reply1ButtonWasPressed = false;
private bool _reply2ButtonWasPressed = false;

// ❌ REMOVIDO - Lógica manual no DataUpdate
public void DataUpdate(PluginManager pluginManager, ref GameData data)
{
    // Código que tentava ler InputStatus.{buttonName} manualmente
    // Isto estava ERRADO e foi completamente removido
}
```

### Do PluginSettings.cs:
```csharp
// ❌ REMOVIDO - Configurações de botões
public string Reply1Button { get; set; } = "";
public string Reply1Behavior { get; set; } = "Press";
public string Reply2Button { get; set; } = "";  
public string Reply2Behavior { get; set; } = "LongPress";
public bool Reply2SameButton { get; set; } = true;
```

### ✅ O Que Ficou no PluginSettings.cs:
```csharp
// ✅ MANTIDO - Apenas os textos das respostas
public string Reply1Text { get; set; } = "Estou numa corrida, ligo depois 🏎️";
public string Reply2Text { get; set; } = "Se for urgente liga sfr 📞";

// ✅ MANTIDO - Opções de comportamento
public bool RemoveAfterReply { get; set; } = true;
public bool ShowConfirmation { get; set; } = true;
public bool EnableCooldown { get; set; } = true;
```

## 🎯 Lógica das Actions

Quando um botão é primido (depois de mapeado), o SimHub chama a Action:

```csharp
private async void SendQuickReply(int replyNumber)
{
    // ⚠️ Só funciona se houver mensagem no ecrã
    if (_currentMessageGroup == null || _currentMessageGroup.Count == 0)
    {
        WriteLog("[QUICK REPLY] ⚠️ No message on screen - reply ignored");
        return;
    }
    
    // Enviar reply via WebSocket
    string replyText = replyNumber == 1 ? _settings.Reply1Text : _settings.Reply2Text;
    await _nodeManager.SendReplyAsync(_currentContactNumber, replyText);
    
    // Remover mensagens se configurado
    if (_settings.RemoveAfterReply)
    {
        _messageQueue.RemoveMessagesFromContact(_currentContactNumber);
    }
}
```

**Importante:** 
- Os botões **só funcionam quando há mensagem no VoCore**
- Se não houver mensagem, o quick reply é ignorado (com log)
- Envia reply para o contacto que está no ecrã (`_currentContactNumber`)

## 📱 Próximos Passos

### 1. UI Settings - Remover configuração de botões
O UI ainda tem componentes para configurar botões manualmente. Precisa de:
- Remover campos de seleção de botões (Reply1Button, Reply2Button)
- Remover combo boxes de Behavior (Press/LongPress)
- Manter apenas os campos de texto (Reply1Text, Reply2Text)
- Adicionar texto informativo: **"Configure os botões em Controls and Events"**

### 2. Testar o Sistema
1. Compilar o plugin
2. Iniciar SimHub
3. Verificar se os botões aparecem em Source ("WhatsApp" > "Reply1Button", etc.)
4. Verificar se as actions aparecem em Target ("WhatsAppPlugin" > "WhatsApp.SendReply1", etc.)
5. Mapear um botão (ex: SCREEN1_BUTTON1 → WhatsApp.SendReply1)
6. Com mensagem no VoCore, carregar no botão
7. Verificar logs para ver se a action foi chamada e o reply enviado

### 3. Documentação para Utilizador
Criar um guia no README explicando:
- Como abrir Control and Events
- Como mapear botões do volante para quick replies
- Exemplos de mapeamento (ShortPress, LongPress)

## 🎮 Exemplo de Mapeamento Ideal

Para um volante com botões físicos:
```
SCREEN1_BUTTON7 (ShortPress) → WhatsApp.SendReply1 (Reply rápida)
SCREEN1_BUTTON7 (LongPress)  → WhatsApp.SendReply2 (Reply alternativa)
SCREEN1_BUTTON8 (ShortPress) → WhatsApp.DismissMessage (Descartar)
```

## ✅ Benefícios do Novo Sistema

1. **Padrão SimHub:** Usa o sistema de Control Mapper nativo
2. **Flexibilidade:** Utilizador escolhe qualquer botão (físico ou virtual)
3. **Press Types:** Suporta Press, LongPress, DoublePress, etc.
4. **Reliability:** SimHub gerencia o mapeamento, não o plugin
5. **Sem Bugs:** Não há lógica manual de detecção de botões para dar erro

## 🔍 Debug

Para verificar se as actions estão sendo chamadas:
```
Logs: /mnt/user-data/uploads/.../logs/messages.log
Procurar por: "[ACTION] WhatsApp.SendReply1 triggered!"
```

Se a action não for chamada:
1. Verificar se o botão está mapeado em Control and Events
2. Verificar se o mapeamento está ativo (não desabilitado)
3. Verificar se há mensagem no VoCore (requisito para funcionar)
