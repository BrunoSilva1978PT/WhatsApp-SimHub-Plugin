using System;
using System.Linq;
using WhatsAppSimHubPlugin.Models;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Renderiza mensagens WhatsApp como overlay usando Information Overlay do SimHub
    /// </summary>
    public class OverlayRenderer : IDisposable
    {
        private readonly PluginSettings _settings;
        private readonly DashboardGenerator _dashboardGenerator;
        private object _vocoreDevice;
        private object _vocoreSettings;

        public OverlayRenderer(PluginSettings settings)
        {
            _settings = settings;
            _dashboardGenerator = new DashboardGenerator();
        }

        /// <summary>
        /// Conecta o renderer ao device VoCore
        /// </summary>
        public bool AttachToDevice(object device)
        {
            try
            {
                _vocoreDevice = device;
                
                // Obter Settings do device
                var settingsProp = device.GetType().GetProperty("Settings");
                if (settingsProp != null)
                {
                    _vocoreSettings = settingsProp.GetValue(device);
                }
                
                return _vocoreSettings != null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OverlayRenderer.AttachToDevice failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// CÓDIGO FINAL CORRIGIDO!
        /// Usa DASHBOARD CORRETO: WhatsAppPlugin (não AudiConcept!)
        /// SÓ muda settings se necessário (se já estiver correto, não mexe!)
        /// </summary>
        public bool ShowMessage(QueuedMessage message, Action<string> log = null)
        {
            if (_vocoreSettings == null)
            {
                log?.Invoke("❌ VoCore settings not available");
                return false;
            }

            try
            {
                // 🎯 DASHBOARD CORRETO DO WHATSAPP PLUGIN!
                const string CORRECT_DASHBOARD = "WhatsAppPlugin";
                
                var settingsType = _vocoreSettings.GetType();
                
                // ============================================
                // VERIFICAR ESTADO ATUAL
                // ============================================
                
                var overlayDashboardProp = settingsType.GetProperty("CurrentOverlayDashboard");
                if (overlayDashboardProp == null)
                {
                    log?.Invoke("❌ CurrentOverlayDashboard property not found");
                    return false;
                }

                var overlayDashboard = overlayDashboardProp.GetValue(_vocoreSettings);
                if (overlayDashboard == null)
                {
                    log?.Invoke("❌ CurrentOverlayDashboard is null");
                    return false;
                }

                var overlayType = overlayDashboard.GetType();
                var dashboardProp = overlayType.GetProperty("Dashboard");
                var useOverlayProp = settingsType.GetProperty("UseOverlayDashboard");
                
                if (dashboardProp == null || useOverlayProp == null)
                {
                    log?.Invoke("❌ Required properties not found");
                    return false;
                }
                
                // Estado atual
                var currentDashboard = dashboardProp.GetValue(overlayDashboard) as string;
                var currentOverlayEnabled = (bool)(useOverlayProp.GetValue(_vocoreSettings) ?? false);
                
                bool needsChange = false;
                
                // ============================================
                // PASSO 1: VERIFICAR/MUDAR DASHBOARD
                // ============================================
                
                if (currentDashboard != CORRECT_DASHBOARD)
                {
                    log?.Invoke($"🔄 Dashboard incorreto: '{currentDashboard}' → '{CORRECT_DASHBOARD}'");
                    
                    var trySetMethod = overlayType.GetMethod("TrySet");
                    if (trySetMethod != null)
                    {
                        trySetMethod.Invoke(overlayDashboard, new object[] { CORRECT_DASHBOARD });
                        log?.Invoke($"   ✅ Dashboard changed to {CORRECT_DASHBOARD}");
                    }
                    else
                    {
                        dashboardProp.SetValue(overlayDashboard, CORRECT_DASHBOARD);
                        log?.Invoke($"   ✅ Dashboard set to {CORRECT_DASHBOARD}");
                    }
                    needsChange = true;
                }
                else
                {
                    // ✅ Dashboard já está correto, não mexer!
                    log?.Invoke($"✅ Dashboard already correct: {CORRECT_DASHBOARD}");
                }

                // ============================================
                // PASSO 2: VERIFICAR/ATIVAR OVERLAY
                // ============================================
                
                if (!currentOverlayEnabled)
                {
                    log?.Invoke("🔄 UseOverlayDashboard: false → true");
                    useOverlayProp.SetValue(_vocoreSettings, true);
                    log?.Invoke("   ✅ Overlay activated");
                    needsChange = true;
                }
                else
                {
                    // ✅ Overlay já está ativo, não mexer!
                    log?.Invoke("✅ Overlay already active");
                }
                
                // ============================================
                // VERIFICAÇÃO FINAL
                // ============================================
                
                if (needsChange)
                {
                    log?.Invoke("");
                    log?.Invoke("📊 Final state:");
                    log?.Invoke($"   Dashboard:           {dashboardProp.GetValue(overlayDashboard)}");
                    log?.Invoke($"   UseOverlayDashboard: {useOverlayProp.GetValue(_vocoreSettings)}");
                    log?.Invoke("   🎉 Settings updated!");
                }
                else
                {
                    log?.Invoke("   ✨ Settings already correct - nothing changed");
                }

                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"❌ ShowMessage failed: {ex.Message}");
                log?.Invoke($"   Stack: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Remove overlay (desativa)
        /// </summary>
        public bool ClearOverlay(Action<string> log = null)
        {
            if (_vocoreSettings == null) return false;

            try
            {
                // Desativar UseOverlayDashboard
                var useOverlayProp = _vocoreSettings.GetType().GetProperty("UseOverlayDashboard");
                if (useOverlayProp != null)
                {
                    log?.Invoke("🔄 Setting UseOverlayDashboard = false...");
                    useOverlayProp.SetValue(_vocoreSettings, false);
                    log?.Invoke("✅ Overlay cleared");
                }

                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"❌ ClearOverlay failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Alias para ClearOverlay() (compatibilidade)
        /// </summary>
        public void Clear()
        {
            ClearOverlay(null);
        }

        /// <summary>
        /// Define mensagem de sistema (aviso de disconnect, etc)
        /// </summary>
        public void SetSystemMessage(string message)
        {
            // Criar QueuedMessage temporária com mensagem de sistema
            var systemMsg = new QueuedMessage
            {
                From = "⚠️ WhatsApp Plugin",
                Body = message,
                Timestamp = DateTime.Now,
                IsVip = false,
                IsUrgent = true
            };
            
            ShowMessage(systemMsg, null);
        }

        /// <summary>
        /// Define mensagem atual (para compatibilidade)
        /// </summary>
        public void SetMessage(QueuedMessage message)
        {
            // Mostrar imediatamente
            ShowMessage(message, null);
        }

        public void Dispose()
        {
            _dashboardGenerator?.RemoveWhatsAppOverlay();
        }
    }
}
