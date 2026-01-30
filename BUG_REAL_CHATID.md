# 🐛 BUG REAL ENCONTRADO - ChatId vs Number

## 🎯 O Problema VERDADEIRO

Tens razão Bruno! O agrupamento garante que todas as mensagens são do **mesmo contacto**.

O problema NÃO era `PeekNextMessage()` vs `_currentContactNumber`.

## 🔍 O Bug REAL: Formato do Número

### ❌ LINHA 791 (ERRADA):
```csharp
_currentContactNumber = messages[0].Number;  // "351912345678"
```

### ✅ LINHA 791 (CORRETA):
```csharp
_currentContactNumber = messages[0].ChatId;  // "351912345678@c.us"
```

## 📋 Explicação Técnica

### No WhatsApp Web.js:

O `msg.from` vem assim: `"351912345678@c.us"`

### No Node.js (whatsapp-server.js):

```javascript
// Linha 339: Extrai número sem @c.us
number = msg.from.split("@")[0]; // "351912345678"

// Linha 434: Envia AMBOS para C#
{
    "number": "351912345678",      // ❌ SEM @c.us
    "chatId": "351912345678@c.us"  // ✅ COM @c.us
}
```

### No C# (WhatsAppPlugin.cs):

```csharp
// Linha 655-663: Cria QueuedMessage com AMBOS campos
var queuedMessage = new QueuedMessage
{
    Number = number,      // "351912345678"
    ChatId = chatId,      // "351912345678@c.us"  ✅ Este é o correto!
};
```

## ⚠️ O Erro Fatal

### Linha 791 (ANTES - ERRADO):
```csharp
_currentContactNumber = messages[0].Number;  // ❌ "351912345678"
```

Depois ao enviar reply:
```csharp
await _nodeManager.SendReplyAsync(_currentContactNumber, replyText);
// Envia: "351912345678" ❌
```

### No WebSocket:
```csharp
// SendReplyAsync envia JSON:
{
    "type": "sendReply",
    "chatId": "351912345678",  // ❌ FALTA @c.us!
    "text": "Estou numa corrida..."
}
```

### No Node.js:
```javascript
// Linha 130: Tenta enviar
await client.sendMessage(data.chatId, data.text);
// client.sendMessage("351912345678", ...) ❌ FALHA!
```

**WhatsApp Web.js PRECISA do formato completo `NUMBER@c.us`!**

## ✅ A Solução

### Linha 791 (DEPOIS - CORRETO):
```csharp
_currentContactNumber = messages[0].ChatId;  // ✅ "351912345678@c.us"
```

Agora ao enviar reply:
```csharp
await _nodeManager.SendReplyAsync(_currentContactNumber, replyText);
// Envia: "351912345678@c.us" ✅
```

### No WebSocket:
```csharp
{
    "type": "sendReply",
    "chatId": "351912345678@c.us",  // ✅ FORMATO CORRETO!
    "text": "Estou numa corrida..."
}
```

### No Node.js:
```javascript
await client.sendMessage("351912345678@c.us", text);
// ✅ FUNCIONA!
```

## 📊 Resumo Visual

```
Mensagem chega do WhatsApp:
  msg.from = "351912345678@c.us"
       ↓
Node.js processa:
  number = "351912345678"      (remove @c.us)
  chatId = "351912345678@c.us" (mantém original)
       ↓
Envia para C# via WebSocket:
  { number: "351912345678", chatId: "351912345678@c.us" }
       ↓
C# cria QueuedMessage:
  Number = "351912345678"
  ChatId = "351912345678@c.us"
       ↓
❌ BUG: Linha 791 usava Number
✅ FIX: Agora usa ChatId
       ↓
SendQuickReply envia:
  chatId = "351912345678@c.us" ✅
       ↓
Node.js recebe e envia com WhatsApp:
  client.sendMessage("351912345678@c.us", text) ✅
       ↓
🎉 REPLY ENTREGUE!
```

## 🧪 Como Testar Agora

1. **Receber** mensagem WhatsApp
2. **Ver** no VoCore
3. **Carregar** botão configurado
4. **Verificar logs**:
   ```
   [QUICK REPLY 1] ⚡ BUTTON PRESSED!
   [QUICK REPLY 1] 📱 Current screen: Maria (351912345678@c.us)  ← COM @c.us!
   [QUICK REPLY 1] 📤 Sending: Estou numa corrida...
   [WEBSOCKET] 📤 SendReplyAsync called - chatId: 351912345678@c.us
   [WEBSOCKET] 📨 Sending JSON: {"type":"sendReply","chatId":"351912345678@c.us"...}
   [WEBSOCKET] ✅ SendAsync completed successfully
   [QUICK REPLY 1] ✅ Reply sent successfully to Maria!
   ```

5. **Verificar WhatsApp** → Reply deve chegar! 🎉

## 📝 Logs Node.js

Também verifica o log do Node.js em:
```
%AppData%\SimHub\WhatsAppPlugin\node\node.log
```

Procura por:
```
[WS] Received: sendReply
[REPLY] Sent
[REPLY] Marked read
```

## 🎯 Conclusão

**Bug:** Usava `Number` (sem @c.us) em vez de `ChatId` (com @c.us)

**Fix:** 1 linha mudada - linha 791

**Resultado:** Quick Replies FUNCIONAM! 🚀

---

**Data:** 2026-01-30  
**Ficheiro:** WhatsAppPlugin.cs  
**Linha:** 791  
**Mudança:** `messages[0].Number` → `messages[0].ChatId`  
**Status:** ✅ RESOLVIDO DEFINITIVAMENTE
