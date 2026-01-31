using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Sistema de filas de mensagens WhatsApp para SimHub
    /// - VIP/URGENT Queue: Sem limite global, limite por contacto, prioridade URGENT > VIP
    /// - NORMAL Queue: Limite global, remove após 1ª exibição
    /// - Timer reset: Máximo 3x quando chega nova msg do mesmo contacto
    /// - Agrupamento: MaxMessagesPerContact mais recentes, ordenadas antiga→recente
    /// Updated: 2026-01-29 - Sistema completo refatorado
    /// </summary>
    public class MessageQueue
    {
        #region Fields

        // 🔥 DUAS FILAS SEPARADAS
        private readonly List<QueuedMessage> _vipUrgentQueue = new List<QueuedMessage>();
        private readonly List<QueuedMessage> _normalQueue = new List<QueuedMessage>();

        // Timers
        private readonly Timer _displayTimer;    // Timer para duração de exibição
        private readonly Timer _reminderTimer;   // Timer para repeat VIP/URGENT

        // Estado atual
        private List<QueuedMessage> _currentDisplayGroup;  // Mensagens sendo mostradas
        private string _currentDisplayContact;             // Contacto sendo mostrado

        private DateTime _lastVipUrgentDisplay = DateTime.MinValue;  // Última vez que mostrou VIP
        private bool _isDisplayingVipUrgent = false;       // Flag se está mostrando VIP/URGENT

        private readonly PluginSettings _settings;
        private readonly Action<string> _logger;

        #endregion

        #region Events

        public event Action<List<QueuedMessage>> OnGroupDisplay;
        public event Action OnMessageRemoved;

        #endregion

        #region Constructor

        public MessageQueue(PluginSettings settings, Action<string> logger)
        {
            _settings = settings;
            _logger = logger;

            _logger("[QUEUE] ═══════════════════════════════════════");
            _logger("[QUEUE] Creating MessageQueue instance...");

            // Display Timer: Controla duração da exibição
            _logger("[QUEUE] Creating Display Timer...");
            _displayTimer = new Timer();
            _logger($"[QUEUE] Display Timer created: {_displayTimer != null}");
            _logger($"[QUEUE] Display Timer type: {_displayTimer.GetType().FullName}");

            _logger("[QUEUE] Subscribing to DisplayTimer.Elapsed event...");
            _displayTimer.Elapsed += DisplayTimer_Elapsed;
            _logger("[QUEUE] ✅ Display Timer event subscribed");

            // Reminder Timer: Controla repeat de VIP/URGENT
            _logger("[QUEUE] Creating Reminder Timer...");
            _reminderTimer = new Timer();
            _reminderTimer.Interval = TimeSpan.FromMinutes(1).TotalMilliseconds; // Check cada 1 min
            _logger($"[QUEUE] Reminder Timer created: {_reminderTimer != null}");

            _logger("[QUEUE] Subscribing to ReminderTimer.Elapsed event...");
            _reminderTimer.Elapsed += ReminderTimer_Elapsed;
            _logger("[QUEUE] ✅ Reminder Timer event subscribed");

            _logger("[QUEUE] Starting Reminder Timer...");
            _reminderTimer.Start();
            _logger($"[QUEUE] Reminder Timer started - Enabled = {_reminderTimer.Enabled}");

            _logger("[QUEUE] ✅ Initialized with dual-queue system");
            _logger("[QUEUE] ═══════════════════════════════════════");
        }

        #endregion

        #region Add Message

        public void AddMessage(QueuedMessage message)
        {
            try
            {
                if (message.IsVip || message.IsUrgent)
                {
                    AddToVipUrgentQueue(message);
                }
                else
                {
                    AddToNormalQueue(message);
                }

                // Se chegar nova mensagem do contacto que está sendo exibido
                CheckForTimerReset(message);

                // Processar fila
                ProcessQueue();
            }
            catch (Exception ex)
            {
                _logger($"[QUEUE ERROR] AddMessage: {ex.Message}");
            }
        }

        private void AddToVipUrgentQueue(QueuedMessage message)
        {
            // Adicionar mensagem à queue (sem limite - as extras ficam para mostrar depois)
            _vipUrgentQueue.Add(message);

            var priority = message.IsUrgent ? "URGENT" : "VIP";
            var contactCount = _vipUrgentQueue.Count(m => m.Number == message.Number);
            _logger($"[QUEUE] Added {priority} message from {message.From} " +
                   $"(contact has {contactCount} messages, VIP/URGENT queue: {_vipUrgentQueue.Count})");
        }

        private void AddToNormalQueue(QueuedMessage message)
        {
            // Adicionar mensagem à queue (sem limite - as extras ficam para mostrar depois)
            _normalQueue.Add(message);

            var contactCount = _normalQueue.Count(m => m.Number == message.Number);
            _logger($"[QUEUE] Added NORMAL message from {message.From} " +
                   $"(contact has {contactCount} messages, Normal queue: {_normalQueue.Count})");
        }

        #endregion

        #region Timer Reset Logic

        private void CheckForTimerReset(QueuedMessage newMessage)
        {
            // Se estamos mostrando algo E a nova mensagem é do mesmo contacto
            if (_currentDisplayGroup != null &&
                _currentDisplayContact == newMessage.Number)
            {
                // Contar mensagens do contacto na queue
                var queue = (newMessage.IsVip || newMessage.IsUrgent) ? _vipUrgentQueue : _normalQueue;
                var contactMessagesCount = queue.Count(m => m.Number == newMessage.Number);

                // Só faz reset se ainda não atingiu o limite por contacto
                // Se já atingiu → mensagem fica na queue para mostrar na próxima vez
                if (contactMessagesCount <= _settings.MaxGroupSize)
                {
                    // ✅ RESET TIMER!
                    _displayTimer.Stop();

                    _logger($"[TIMER] Reset for {newMessage.From} ({contactMessagesCount}/{_settings.MaxGroupSize} messages)");

                    // Atualizar display com nova mensagem
                    RefreshCurrentDisplay();

                    // Reiniciar timer
                    StartDisplayTimer();
                }
                else
                {
                    _logger($"[TIMER] No reset for {newMessage.From} - limit reached ({_settings.MaxGroupSize}), message queued for next display");
                }
            }
        }

        private void RefreshCurrentDisplay()
        {
            // Buscar mensagens atualizadas do contacto
            var messages = GetMessagesForContact(_currentDisplayContact, _isDisplayingVipUrgent);

            if (messages.Count > 0)
            {
                _currentDisplayGroup = messages;
                OnGroupDisplay?.Invoke(messages);
                _logger($"[DISPLAY] Refreshed display for {messages[0].From} ({messages.Count} messages)");
            }
        }

        #endregion

        #region Process Queue

        private void ProcessQueue()
        {
            try
            {
                _logger($"[PROCESS-QUEUE] ▶ ProcessQueue called at {DateTime.Now:HH:mm:ss.fff}");
                _logger($"[PROCESS-QUEUE] VIP/URGENT queue: {_vipUrgentQueue.Count}, NORMAL queue: {_normalQueue.Count}");
                _logger($"[PROCESS-QUEUE] Current display group: {(_currentDisplayGroup != null ? $"{_currentDisplayGroup.Count} messages" : "null")}");

                // Se já está mostrando algo, não interromper
                if (_currentDisplayGroup != null)
                {
                    _logger($"[PROCESS-QUEUE] ⏸ Already displaying messages - skipping");
                    return;
                }

                // 1. Primeiro: Processar VIP/URGENT
                _logger($"[PROCESS-QUEUE] Checking ShouldShowVipUrgent()...");
                bool shouldShowVip = ShouldShowVipUrgent();
                _logger($"[PROCESS-QUEUE] ShouldShowVipUrgent = {shouldShowVip}");

                if (shouldShowVip)
                {
                    _logger($"[PROCESS-QUEUE] → Calling ShowNextVipUrgentContact()");
                    ShowNextVipUrgentContact();
                    return;
                }

                // 2. Durante espera: Mostrar NORMAL
                _logger($"[PROCESS-QUEUE] → Calling ShowNextNormalContact()");
                ShowNextNormalContact();
                _logger($"[PROCESS-QUEUE] ✅ ProcessQueue completed");
            }
            catch (Exception ex)
            {
                _logger($"[PROCESS-QUEUE] ❌ ERROR: {ex.Message}");
                _logger($"[PROCESS-QUEUE] Stack: {ex.StackTrace}");
            }
        }

        private bool ShouldShowVipUrgent()
        {
            // Se não há mensagens VIP/URGENT
            if (_vipUrgentQueue.Count == 0)
                return false;

            // ✅ Se há mensagens NÃO MOSTRADAS, mostrar imediatamente
            var unshownMessages = _vipUrgentQueue.Where(m => !m.WasDisplayed).ToList();
            if (unshownMessages.Count > 0)
            {
                _logger($"[QUEUE] {unshownMessages.Count} unshown VIP/URGENT messages - showing immediately");
                return true;
            }

            // Se RemoveAfterFirstDisplay está ativo, não repetir (já foram mostradas e removidas)
            if (_settings.RemoveAfterFirstDisplay)
                return false;

            // ✅ RemoveAfterFirstDisplay = false → SEMPRE REPETIR
            // Resetar WasDisplayed para mostrar novamente
            foreach (var msg in _vipUrgentQueue)
            {
                msg.WasDisplayed = false;
            }
            _logger($"[QUEUE] Resetting VIP/URGENT messages for repeat (RemoveAfterFirstDisplay=false)");
            return true;
        }

        #endregion

        #region Show Messages

        private void ShowNextVipUrgentContact()
        {
            if (_vipUrgentQueue.Count == 0)
            {
                ShowNextNormalContact();
                return;
            }

            // ✅ PRIORIDADE: URGENT primeiro, por timestamp
            var nextContact = _vipUrgentQueue
                .OrderByDescending(m => m.IsUrgent)  // URGENT primeiro
                .ThenBy(m => m.Timestamp)            // Mais antigo primeiro
                .Select(m => m.Number)
                .Distinct()
                .FirstOrDefault();

            if (nextContact != null)
            {
                var messages = GetMessagesForContact(nextContact, isVipUrgent: true);
                DisplayMessages(messages, isVipUrgent: true);

                _lastVipUrgentDisplay = DateTime.Now;
            }
        }

        private void ShowNextNormalContact()
        {
            if (_normalQueue.Count == 0)
                return;

            // Pegar próximo contacto (mais antigo)
            var nextContact = _normalQueue
                .OrderBy(m => m.Timestamp)
                .Select(m => m.Number)
                .Distinct()
                .FirstOrDefault();

            if (nextContact != null)
            {
                var messages = GetMessagesForContact(nextContact, isVipUrgent: false);
                DisplayMessages(messages, isVipUrgent: false);
            }
        }

        private List<QueuedMessage> GetMessagesForContact(string contactNumber, bool isVipUrgent)
        {
            var queue = isVipUrgent ? _vipUrgentQueue : _normalQueue;

            // ✅ Buscar mensagens do contacto
            // ✅ Pegar as MaxGroupSize MAIS RECENTES
            // ✅ Reordenar: Antiga → Recente (estilo WhatsApp)
            var messages = queue
                .Where(m => m.Number == contactNumber)
                .OrderByDescending(m => m.Timestamp)         // Mais recentes
                .Take(_settings.MaxGroupSize)                // Limitar
                .OrderBy(m => m.Timestamp)                   // Reordenar: antiga→recente
                .ToList();

            return messages;
        }

        private void DisplayMessages(List<QueuedMessage> messages, bool isVipUrgent)
        {
            _logger($"[DISPLAY] ═══════════════════════════════════════");
            _logger($"[DISPLAY] ▶▶▶ DisplayMessages CALLED ◀◀◀");
            _logger($"[DISPLAY] Messages count: {messages?.Count ?? 0}");

            if (messages == null || messages.Count == 0)
            {
                _logger($"[DISPLAY] ⚠️ No messages to display - returning");
                return;
            }

            _logger($"[DISPLAY] Setting _currentDisplayGroup...");
            _currentDisplayGroup = messages;
            _currentDisplayContact = messages[0].Number;
            _isDisplayingVipUrgent = isVipUrgent;
            _logger($"[DISPLAY] ✅ State set: contact={_currentDisplayContact}, isVipUrgent={isVipUrgent}");

            // Determinar duração (settings já estão em MS)
            int durationMs = isVipUrgent ? _settings.UrgentDuration : _settings.NormalDuration;
            _logger($"[DISPLAY] Duration: {durationMs}ms ({durationMs / 1000.0}s) (VIP/URGENT={isVipUrgent})");

            // Evento para overlay exibir
            _logger($"[DISPLAY] Invoking OnGroupDisplay event...");
            OnGroupDisplay?.Invoke(messages);
            _logger($"[DISPLAY] ✅ OnGroupDisplay invoked");

            var type = isVipUrgent ? "VIP/URGENT" : "NORMAL";
            _logger($"[DISPLAY] Showing {messages.Count} {type} messages from {messages[0].From} for {durationMs / 1000.0}s");

            // Iniciar timer
            _logger($"[DISPLAY] Calling StartDisplayTimer()...");
            StartDisplayTimer();
            _logger($"[DISPLAY] ✅ StartDisplayTimer() returned");
            _logger($"[DISPLAY] ═══════════════════════════════════════");
        }

        private void StartDisplayTimer()
        {
            try
            {
                _logger($"[DISPLAY-TIMER] ═══════════════════════════════════════");
                _logger($"[DISPLAY-TIMER] ▶▶▶ StartDisplayTimer CALLED ◀◀◀");
                _logger($"[DISPLAY-TIMER] Thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
                _logger($"[DISPLAY-TIMER] Time: {DateTime.Now:HH:mm:ss.fff}");

                _logger($"[DISPLAY-TIMER] 1. Checking if _displayTimer exists...");
                if (_displayTimer == null)
                {
                    _logger($"[DISPLAY-TIMER] ❌ FATAL: _displayTimer is NULL!");
                    return;
                }
                _logger($"[DISPLAY-TIMER] ✅ _displayTimer exists");

                _logger($"[DISPLAY-TIMER] 2. Stopping existing timer...");
                _displayTimer.Stop();
                _logger($"[DISPLAY-TIMER] ✅ Timer stopped - Enabled = {_displayTimer.Enabled}");

                _logger($"[DISPLAY-TIMER] 3. Calculating duration...");
                int durationMs = _isDisplayingVipUrgent ?
                    _settings.UrgentDuration :
                    _settings.NormalDuration;
                _logger($"[DISPLAY-TIMER] Duration = {durationMs}ms ({durationMs / 1000.0}s) (_isDisplayingVipUrgent = {_isDisplayingVipUrgent})");
                _logger($"[DISPLAY-TIMER] UrgentDuration setting = {_settings.UrgentDuration}ms ({_settings.UrgentDuration / 1000.0}s)");
                _logger($"[DISPLAY-TIMER] NormalDuration setting = {_settings.NormalDuration}ms ({_settings.NormalDuration / 1000.0}s)");

                _logger($"[DISPLAY-TIMER] 4. Setting interval...");
                // ✅ CORREÇÃO: Settings já estão em MS, não multiplicar por 1000!
                _displayTimer.Interval = durationMs;
                _logger($"[DISPLAY-TIMER] ✅ Interval set - Timer.Interval = {_displayTimer.Interval}ms ({_displayTimer.Interval / 1000.0}s)");

                _logger($"[DISPLAY-TIMER] 5. Checking timer properties...");
                _logger($"[DISPLAY-TIMER] Timer.AutoReset = {_displayTimer.AutoReset}");
                _logger($"[DISPLAY-TIMER] Timer.Enabled = {_displayTimer.Enabled}");

                _logger($"[DISPLAY-TIMER] 6. STARTING TIMER...");
                _displayTimer.Start();
                _logger($"[DISPLAY-TIMER] ✅✅✅ Timer.Start() CALLED ✅✅✅");

                _logger($"[DISPLAY-TIMER] 7. Verifying timer started...");
                _logger($"[DISPLAY-TIMER] Timer.Enabled after Start() = {_displayTimer.Enabled}");

                if (!_displayTimer.Enabled)
                {
                    _logger($"[DISPLAY-TIMER] ❌❌❌ TIMER DID NOT START! Enabled = False ❌❌❌");
                }
                else
                {
                    _logger($"[DISPLAY-TIMER] ✅ Timer successfully started!");
                    _logger($"[DISPLAY-TIMER] Timer will fire at: {DateTime.Now.AddMilliseconds(durationMs):HH:mm:ss.fff}");
                    _logger($"[DISPLAY-TIMER] Expecting elapsed event in {durationMs / 1000.0} seconds");
                }

                _logger($"[DISPLAY-TIMER] ═══════════════════════════════════════");
            }
            catch (Exception ex)
            {
                _logger($"[DISPLAY-TIMER] ❌❌❌ EXCEPTION in StartDisplayTimer! ❌❌❌");
                _logger($"[DISPLAY-TIMER] Exception: {ex.GetType().Name}");
                _logger($"[DISPLAY-TIMER] Message: {ex.Message}");
                _logger($"[DISPLAY-TIMER] Stack: {ex.StackTrace}");
            }
        }

        #endregion

        #region Timer Events

        private void DisplayTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _logger($"[DISPLAY-TIMER] ▶ Timer ELAPSED triggered at {DateTime.Now:HH:mm:ss.fff}");

                _displayTimer.Stop();
                _logger($"[DISPLAY-TIMER] Timer STOPPED");

                if (_currentDisplayGroup == null)
                {
                    _logger($"[DISPLAY-TIMER] ⚠ No current display group - exiting");
                    return;
                }

                _logger($"[DISPLAY] Timer elapsed for {_currentDisplayGroup[0].From} ({_currentDisplayGroup.Count} messages)");

                // Remover mensagens da fila
                _logger($"[DISPLAY-TIMER] Calling RemoveDisplayedMessages()...");
                RemoveDisplayedMessages();

                // Limpar estado
                _currentDisplayGroup = null;
                _currentDisplayContact = null;
                _logger($"[DISPLAY-TIMER] State cleared");

                // Notificar que mensagem foi removida
                _logger($"[DISPLAY-TIMER] Invoking OnMessageRemoved event...");
                OnMessageRemoved?.Invoke();
                _logger($"[DISPLAY-TIMER] OnMessageRemoved invoked");

                // Processar próxima
                _logger($"[DISPLAY-TIMER] Calling ProcessQueue()...");
                ProcessQueue();
                _logger($"[DISPLAY-TIMER] ✅ DisplayTimer_Elapsed completed");
            }
            catch (Exception ex)
            {
                _logger($"[DISPLAY-TIMER] ❌ ERROR: {ex.Message}");
                _logger($"[DISPLAY-TIMER] Stack: {ex.StackTrace}");
            }
        }

        private void RemoveDisplayedMessages()
        {
            if (_currentDisplayGroup == null)
                return;

            foreach (var message in _currentDisplayGroup)
            {
                if (_isDisplayingVipUrgent)
                {
                    // ✅ VIP/URGENT: Depende de RemoveAfterFirstDisplay
                    if (_settings.RemoveAfterFirstDisplay)
                    {
                        _vipUrgentQueue.Remove(message);
                        _logger($"[QUEUE] Removed VIP/URGENT message from {message.From} (RemoveAfterFirstDisplay=true)");
                    }
                    else
                    {
                        // Mantém no queue para repetir, mas marca como mostrada
                        message.WasDisplayed = true;
                        message.LastDisplayed = DateTime.Now;
                        message.DisplayCount++;
                        _logger($"[QUEUE] Kept VIP/URGENT message from {message.From} for repeat (displayed {message.DisplayCount}x)");
                    }
                }
                else
                {
                    // ✅ NORMAL: Sempre remove após exibição
                    _normalQueue.Remove(message);
                    _logger($"[QUEUE] Removed NORMAL message from {message.From}");
                }
            }
        }

        private void ReminderTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            // ✅ Verificar se há mensagens VIP/URGENT para repetir
            if (_vipUrgentQueue.Count > 0 && _currentDisplayGroup == null)
            {
                var allDisplayed = _vipUrgentQueue.All(m => m.WasDisplayed);

                // Se todas foram mostradas E RemoveAfterFirstDisplay = false → reprocessar
                if (allDisplayed && !_settings.RemoveAfterFirstDisplay)
                {
                    _logger($"[REMINDER] All VIP/URGENT shown, reprocessing queue (RemoveAfterFirstDisplay=false)");
                    ProcessQueue();
                }
            }
        }

        #endregion

        #region Public Methods

        public void TriggerProcessQueue()
        {
            ProcessQueue();
        }

        public void RemoveMessage(string messageId)
        {
            var message = _vipUrgentQueue.FirstOrDefault(m => m.Id == messageId) ??
                         _normalQueue.FirstOrDefault(m => m.Id == messageId);

            if (message != null)
            {
                _vipUrgentQueue.Remove(message);
                _normalQueue.Remove(message);

                _logger($"[QUEUE] Manually removed message {messageId}");
                OnMessageRemoved?.Invoke();
            }
        }

        /// <summary>
        /// Limpa todas as mensagens VIP/URGENT da queue
        /// </summary>
        public void ClearVipUrgentMessages()
        {
            int count = _vipUrgentQueue.Count;
            _vipUrgentQueue.Clear();
            _logger($"[QUEUE] Cleared {count} VIP/URGENT messages");

            // Se estava a mostrar VIP/URGENT, limpar
            if (_currentDisplayGroup != null && _isDisplayingVipUrgent)
            {
                _displayTimer.Stop();
                _currentDisplayGroup = null;
                _currentDisplayContact = null;
                OnMessageRemoved?.Invoke();
                _logger($"[QUEUE] Stopped displaying VIP/URGENT messages");
            }
        }

        public void RemoveMessagesFromContact(string contactNumber)
        {
            var removed = _vipUrgentQueue.RemoveAll(m => m.Number == contactNumber);
            removed += _normalQueue.RemoveAll(m => m.Number == contactNumber);

            if (removed > 0)
            {
                _logger($"[QUEUE] Removed {removed} messages from {contactNumber}");
                OnMessageRemoved?.Invoke();
            }
        }

        public int GetQueueSize()
        {
            return _vipUrgentQueue.Count + _normalQueue.Count;
        }

        public int GetVipUrgentQueueSize()
        {
            return _vipUrgentQueue.Count;
        }

        public int GetNormalQueueSize()
        {
            return _normalQueue.Count;
        }

        public List<QueuedMessage> GetAllMessages()
        {
            return _vipUrgentQueue.Concat(_normalQueue).ToList();
        }

        /// <summary>
        /// 🎮 Retorna a próxima mensagem sem remover da fila
        /// Usado para Quick Replies - detectar botão primido
        /// </summary>
        public QueuedMessage PeekNextMessage()
        {
            // Se estamos mostrando um grupo agora, retornar primeira mensagem do grupo
            if (_currentDisplayGroup != null && _currentDisplayGroup.Count > 0)
            {
                return _currentDisplayGroup[0];
            }

            // Senão, retornar próxima da fila VIP/URGENT
            if (_vipUrgentQueue.Count > 0)
            {
                return _vipUrgentQueue[0];
            }

            // Senão, retornar próxima da fila NORMAL
            if (_normalQueue.Count > 0)
            {
                return _normalQueue[0];
            }

            // Sem mensagens
            return null;
        }

        public void PauseQueue()
        {
            _displayTimer.Stop();
            _reminderTimer.Stop();
            _logger("[QUEUE] Paused");
        }

        public void ResumeQueue()
        {
            _reminderTimer.Start();
            ProcessQueue();
            _logger("[QUEUE] Resumed");
        }

        public void ClearQueue()
        {
            _vipUrgentQueue.Clear();
            _normalQueue.Clear();
            _currentDisplayGroup = null;
            _currentDisplayContact = null;

            _displayTimer.Stop();

            _logger("[QUEUE] Cleared all messages");
            OnMessageRemoved?.Invoke();
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            _displayTimer?.Stop();
            _displayTimer?.Dispose();
            _reminderTimer?.Stop();
            _reminderTimer?.Dispose();

            _logger("[QUEUE] Disposed");
        }

        #endregion
    }
}
