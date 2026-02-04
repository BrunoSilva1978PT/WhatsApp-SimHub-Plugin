using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// WhatsApp message queue system for SimHub
    /// </summary>
    public class MessageQueue
    {
        #region Fields

        private readonly List<QueuedMessage> _vipUrgentQueue = new List<QueuedMessage>();
        private readonly List<QueuedMessage> _normalQueue = new List<QueuedMessage>();

        private readonly Timer _displayTimer;
        private readonly Timer _reminderTimer;

        private List<QueuedMessage> _currentDisplayGroup;
        private string _currentDisplayContact;

        private DateTime _lastVipUrgentDisplay = DateTime.MinValue;
        private bool _isDisplayingVipUrgent = false;

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

            _displayTimer = new Timer();
            _displayTimer.Elapsed += DisplayTimer_Elapsed;

            _reminderTimer = new Timer();
            _reminderTimer.Interval = TimeSpan.FromMinutes(1).TotalMilliseconds;
            _reminderTimer.Elapsed += ReminderTimer_Elapsed;
            _reminderTimer.Start();
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

                CheckForTimerReset(message);
                ProcessQueue();
            }
            catch (Exception ex)
            {
                _logger($"[QUEUE] Error: {ex.Message}");
            }
        }

        private void AddToVipUrgentQueue(QueuedMessage message)
        {
            _vipUrgentQueue.Add(message);
        }

        private void AddToNormalQueue(QueuedMessage message)
        {
            _normalQueue.Add(message);
        }

        #endregion

        #region Timer Reset Logic

        private void CheckForTimerReset(QueuedMessage newMessage)
        {
            if (_currentDisplayGroup != null &&
                _currentDisplayContact == newMessage.Number)
            {
                var queue = (newMessage.IsVip || newMessage.IsUrgent) ? _vipUrgentQueue : _normalQueue;
                var contactMessagesCount = queue.Count(m => m.Number == newMessage.Number);

                if (contactMessagesCount <= _settings.MaxGroupSize)
                {
                    // Still fits in group - full refresh
                    _displayTimer.Stop();
                    RefreshCurrentDisplay();
                    StartDisplayTimer();
                }
                else
                {
                    // Already reached limit - only update display to show +X
                    OnGroupDisplay?.Invoke(_currentDisplayGroup);
                }
            }
        }

        private void RefreshCurrentDisplay()
        {
            var messages = GetMessagesForContact(_currentDisplayContact, _isDisplayingVipUrgent);

            if (messages.Count > 0)
            {
                _currentDisplayGroup = messages;
                OnGroupDisplay?.Invoke(messages);
            }
        }

        #endregion

        #region Process Queue

        private void ProcessQueue()
        {
            try
            {
                if (_currentDisplayGroup != null)
                    return;

                if (ShouldShowVipUrgent())
                {
                    ShowNextVipUrgentContact();
                    return;
                }

                ShowNextNormalContact();
            }
            catch (Exception ex)
            {
                _logger($"[QUEUE] ProcessQueue error: {ex.Message}");
            }
        }

        private bool ShouldShowVipUrgent()
        {
            if (_vipUrgentQueue.Count == 0)
                return false;

            var unshownMessages = _vipUrgentQueue.Where(m => !m.WasDisplayed).ToList();
            if (unshownMessages.Count > 0)
                return true;

            if (_settings.RemoveAfterFirstDisplay)
                return false;

            var timeSinceLastDisplay = DateTime.Now - _lastVipUrgentDisplay;

            if (timeSinceLastDisplay.TotalMilliseconds >= _settings.ReminderInterval)
            {
                foreach (var msg in _vipUrgentQueue)
                {
                    msg.WasDisplayed = false;
                }
                return true;
            }

            return false;
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

            var nextContact = _vipUrgentQueue
                .OrderByDescending(m => m.IsUrgent)
                .ThenBy(m => m.Timestamp)
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

            var messages = queue
                .Where(m => m.Number == contactNumber)
                .OrderByDescending(m => m.Timestamp)
                .Take(_settings.MaxGroupSize)
                .OrderBy(m => m.Timestamp)
                .ToList();

            return messages;
        }

        private void DisplayMessages(List<QueuedMessage> messages, bool isVipUrgent)
        {
            if (messages == null || messages.Count == 0)
                return;

            _currentDisplayGroup = messages;
            _currentDisplayContact = messages[0].Number;
            _isDisplayingVipUrgent = isVipUrgent;

            OnGroupDisplay?.Invoke(messages);
            StartDisplayTimer();
        }

        private void StartDisplayTimer()
        {
            try
            {
                _displayTimer.Stop();

                int durationMs = _isDisplayingVipUrgent ?
                    _settings.UrgentDuration :
                    _settings.NormalDuration;

                _displayTimer.Interval = durationMs;
                _displayTimer.Start();
            }
            catch (Exception ex)
            {
                _logger($"[QUEUE] Timer error: {ex.Message}");
            }
        }

        #endregion

        #region Timer Events

        private void DisplayTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _displayTimer.Stop();

                if (_currentDisplayGroup == null)
                    return;

                RemoveDisplayedMessages();

                _currentDisplayGroup = null;
                _currentDisplayContact = null;

                OnMessageRemoved?.Invoke();
                ProcessQueue();
            }
            catch (Exception ex)
            {
                _logger($"[QUEUE] Timer elapsed error: {ex.Message}");
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
                    if (_settings.RemoveAfterFirstDisplay)
                    {
                        _vipUrgentQueue.Remove(message);
                    }
                    else
                    {
                        message.WasDisplayed = true;
                        message.LastDisplayed = DateTime.Now;
                        message.DisplayCount++;
                    }
                }
                else
                {
                    _normalQueue.Remove(message);
                }
            }
        }

        private void ReminderTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_vipUrgentQueue.Count > 0 && _currentDisplayGroup == null)
            {
                var allDisplayed = _vipUrgentQueue.All(m => m.WasDisplayed);

                if (allDisplayed && !_settings.RemoveAfterFirstDisplay)
                {
                    var timeSinceLastDisplay = DateTime.Now - _lastVipUrgentDisplay;

                    if (timeSinceLastDisplay.TotalMilliseconds >= _settings.ReminderInterval)
                    {
                        ProcessQueue();
                    }
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
                OnMessageRemoved?.Invoke();
            }
        }

        public void ClearVipUrgentMessages()
        {
            _vipUrgentQueue.Clear();

            if (_currentDisplayGroup != null && _isDisplayingVipUrgent)
            {
                _displayTimer.Stop();
                _currentDisplayGroup = null;
                _currentDisplayContact = null;
                OnMessageRemoved?.Invoke();
            }
        }

        public void RemoveMessagesFromContact(string contactNumber)
        {
            var removed = _vipUrgentQueue.RemoveAll(m => m.Number == contactNumber);
            removed += _normalQueue.RemoveAll(m => m.Number == contactNumber);

            if (_currentDisplayContact == contactNumber)
            {
                _displayTimer.Stop();
                _currentDisplayGroup = null;
                _currentDisplayContact = null;
            }

            if (removed > 0)
            {
                OnMessageRemoved?.Invoke();
            }
        }

        public int GetQueueSize()
        {
            return _vipUrgentQueue.Count + _normalQueue.Count;
        }

        public List<QueuedMessage> GetAllMessages()
        {
            return _vipUrgentQueue.Concat(_normalQueue).ToList();
        }

        /// <summary>
        /// Get total message count for a contact across both queues
        /// </summary>
        public int GetContactMessageCount(string contactNumber)
        {
            int vipCount = _vipUrgentQueue.Count(m => m.Number == contactNumber);
            int normalCount = _normalQueue.Count(m => m.Number == contactNumber);
            return vipCount + normalCount;
        }

        public void PauseQueue()
        {
            _displayTimer.Stop();
            _reminderTimer.Stop();
        }

        public void ResumeQueue()
        {
            _reminderTimer.Start();
            ProcessQueue();
        }

        public void ClearQueue()
        {
            _vipUrgentQueue.Clear();
            _normalQueue.Clear();
            _currentDisplayGroup = null;
            _currentDisplayContact = null;

            _displayTimer.Stop();
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
        }

        #endregion
    }
}
