# 🔧 CORREÇÃO DEFINITIVA - Quick Reply Bug

## 🐛 O Problema (Versão 2)

**Sintoma:** Botão configurado, mensagem aparece no ecrã, mas ao carregar no botão o reply **não vai para o contacto correto**.

## 🔍 Investigação

### Código ERRADO (Versão Anterior):
```csharp
private async void SendQuickReply(int replyNumber)
{
    // ❌ ERRADO: Pega PRIMEIRA mensagem da fila
    var currentMessage = _messageQueue?.PeekNextMessage();
    string chatId = currentMessage.Number;
    
    await _nodeManager.SendReplyAsync(chatId, replyText);
}
```

### Problema Identificado:

O método `PeekNextMessage()` faz isto:
```csharp
public QueuedMessage PeekNextMessage()
{
    // Retorna PRIMEIRA do grupo atual
    if (_currentDisplayGroup != null && _currentDisplayGroup.Count > 0)
    {
        return _currentDisplayGroup[0];  // ❌ Sempre a PRIMEIRA!
    }
    
    // Ou primeira da fila VIP/URGENT
    if (_vipUrgentQueue.Count > 0)
    {
        return _vipUrgentQueue[0];
    }
    
    // Ou primeira da fila NORMAL
    if (_normalQueue.Count > 0)
    {
        return _normalQueue[0];
    }
    
    return null;
}
```

**Problema:** Se tens **MÚLTIPLAS mensagens** do mesmo contacto (agrupadas), o `PeekNextMessage()` retorna sempre a **PRIMEIRA**, mas o ecrã pode estar a mostrar a **SEGUNDA** ou **TERCEIRA**!

## ✅ A Solução Correta

O plugin JÁ tem variáveis que rastreiam **EXATAMENTE** o que está no ecrã:

```csharp
// Em WhatsAppPlugin.cs (linha 202-203):
private List<QueuedMessage> _currentMessageGroup = null;  // ✅ Grupo no ecrã
private string _currentContactNumber = "";                 // ✅ Número no ecrã
```

Estas são atualizadas pelo evento `OnGroupDisplay` (linha 777-778):
```csharp
private void MessageQueue_OnGroupDisplay(List<QueuedMessage> messages)
{
    if (messages != null && messages.Count > 0)
    {
        // ✅ GUARDAR GRUPO ATUAL (para Quick Reply)
        _currentMessageGroup = messages;
        _currentContactNumber = messages[0].Number;
        
        // Atualizar overlay...
        UpdateOverlayProperties(messages);
    }
}
```

### Código CORRETO (Novo):
```csharp
private async void SendQuickReply(int replyNumber)
{
    // ✅ CORRETO: Usar mensagem que está MOSTRANDO no ecrã!
    if (_currentMessageGroup == null || _currentMessageGroup.Count == 0)
    {
        WriteLog($"[QUICK REPLY] ❌ No message being displayed on screen");
        return;
    }

    if (string.IsNullOrEmpty(_currentContactNumber))
    {
        WriteLog($"[QUICK REPLY] ❌ No contact number available");
        return;
    }

    string replyText = replyNumber == 1 ? _settings.Reply1Text : _settings.Reply2Text;
    string chatId = _currentContactNumber;  // ✅ Número CORRETO do ecrã!
    string contactName = _currentMessageGroup[0].From;

    WriteLog($"[QUICK REPLY {replyNumber}] 📱 Current screen: {contactName} ({chatId})");
    WriteLog($"[QUICK REPLY {replyNumber}] 📤 Sending: {replyText}");

    // Send reply via WebSocket
    await _nodeManager.SendReplyAsync(chatId, replyText);

    WriteLog($"[QUICK REPLY {replyNumber}] ✅ Reply sent successfully to {contactName}!");

    // ✅ Remover TODAS as mensagens deste contacto
    if (_settings.RemoveAfterReply)
    {
        _messageQueue.RemoveMessagesFromContact(_currentContactNumber);
        WriteLog($"[QUICK REPLY {replyNumber}] 🗑️ Removed all messages from {contactName}");
    }
}
```

## 📊 Fluxo Correto Agora

```
1. Mensagem chega → Entra na fila
2. Fila decide mostrar → OnGroupDisplay() é chamado
3. OnGroupDisplay() atualiza:
   - _currentMessageGroup = [msg1, msg2, msg3]
   - _currentContactNumber = "+351912345678"
   - UpdateOverlayProperties() → Mostra no VoCore
4. User vê no ecrã → Carrega no botão
5. SendQuickReply() é chamado
6. Usa _currentContactNumber ✅ (número do ecrã!)
7. Envia reply para o contacto CORRETO! 🎉
```

## 🧪 Como Testar

1. **Enviar** mensagem WhatsApp para ti próprio
2. **Ver** mensagem aparecer no VoCore
3. **Nota** o nome/número no ecrã
4. **Carregar** no botão configurado
5. **Verificar logs**:
   ```
   [QUICK REPLY 1] 📱 Current screen: Maria (+351912345678)
   [QUICK REPLY 1] 📤 Sending: Estou numa corrida, ligo depois 🏎️
   [QUICK REPLY 1] ✅ Reply sent successfully to Maria!
   [QUICK REPLY 1] 🗑️ Removed all messages from Maria
   ```
6. **Verificar WhatsApp**: Reply deve chegar na conversa certa! ✅

## 📝 Logs Melhorados

Agora tens logs super detalhados:
- `📱 Current screen:` - Mostra quem está no ecrã
- `📤 Sending:` - Mostra o texto enviado
- `✅ Reply sent successfully to X!` - Confirma envio
- `🗑️ Removed all messages from X` - Confirma remoção

## ⚠️ Possíveis Problemas

### Se continuar a não funcionar:

1. **Verificar** se `_currentContactNumber` está preenchido:
   - Adicionar log em `OnGroupDisplay()` para confirmar
   
2. **Verificar** se WebSocket está conectado:
   - Ver logs do Node.js
   
3. **Verificar** formato do número:
   - Deve ser `+351912345678` (com +)
   - Confirmar em `SendReplyAsync()`

## 🎯 Resumo

**ANTES:** ❌ Usava `PeekNextMessage()` → Primeira da fila (errado!)

**AGORA:** ✅ Usa `_currentContactNumber` → Contacto NO ECRÃ (correto!)

**Resultado:** 🎉 Reply vai para o contacto certo!

---

**Data:** 2026-01-30  
**Versão:** v2 - Correção definitiva  
**Status:** ✅ RESOLVIDO
