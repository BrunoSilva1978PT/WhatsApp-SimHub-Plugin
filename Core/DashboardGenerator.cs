using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WhatsAppSimHubPlugin.Core
{
    /// <summary>
    /// Gera ficheiros .simhubdash dinamicamente para usar como overlay
    /// </summary>
    public class DashboardGenerator
    {
        private readonly string _dashboardsPath;

        public DashboardGenerator()
        {
            // Pasta onde SimHub guarda dashboards
            _dashboardsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimHub", "DashTemplates"
            );

            Directory.CreateDirectory(_dashboardsPath);
        }

        /// <summary>
        /// Cria dashboard overlay com mensagem WhatsApp
        /// </summary>
        public string CreateWhatsAppOverlay(string sender, string message, bool isVip, bool isUrgent)
        {
            string dashboardName = "WhatsAppOverlay.simhubdash";
            string fullPath = Path.Combine(_dashboardsPath, dashboardName);

            try
            {
                // Criar estrutura do dashboard
                var dashboard = new JObject
                {
                    ["Width"] = 800,
                    ["Height"] = 480,
                    ["Background"] = "Transparent", // CHAVE: Fundo transparente!
                    
                    ["Elements"] = new JArray
                    {
                        // Caixa de fundo semi-transparente
                        new JObject
                        {
                            ["Type"] = "Rectangle",
                            ["X"] = 20,
                            ["Y"] = 20,
                            ["Width"] = 760,
                            ["Height"] = 120,
                            ["Fill"] = "#CC282828", // Cinza escuro 80% opaco
                            ["Stroke"] = isUrgent ? "#FFFF0000" : (isVip ? "#FFFFD700" : "#FF007ACC"), // Vermelho/Dourado/Azul
                            ["StrokeThickness"] = 3,
                            ["RadiusX"] = 10,
                            ["RadiusY"] = 10
                        },
                        
                        // Badge VIP/Urgente (se aplicável)
                        new JObject
                        {
                            ["Type"] = "TextBlock",
                            ["X"] = 700,
                            ["Y"] = 30,
                            ["Text"] = isUrgent ? "🚨" : (isVip ? "⭐" : ""),
                            ["FontSize"] = 24,
                            ["Foreground"] = "#FFFFFFFF",
                            ["Visibility"] = (isUrgent || isVip) ? "Visible" : "Collapsed"
                        },
                        
                        // Nome do remetente
                        new JObject
                        {
                            ["Type"] = "TextBlock",
                            ["X"] = 40,
                            ["Y"] = 35,
                            ["Width"] = 640,
                            ["Text"] = sender,
                            ["FontSize"] = 18,
                            ["FontWeight"] = "Bold",
                            ["Foreground"] = "#FFFFFFFF"
                        },
                        
                        // Mensagem
                        new JObject
                        {
                            ["Type"] = "TextBlock",
                            ["X"] = 40,
                            ["Y"] = 65,
                            ["Width"] = 700,
                            ["Height"] = 65,
                            ["Text"] = message,
                            ["FontSize"] = 14,
                            ["Foreground"] = "#FFFFFFFF",
                            ["TextWrapping"] = "Wrap"
                        }
                    }
                };

                // Guardar ficheiro
                File.WriteAllText(fullPath, dashboard.ToString(Formatting.Indented));
                
                return dashboardName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateWhatsAppOverlay failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Remove o dashboard overlay
        /// </summary>
        public void RemoveWhatsAppOverlay()
        {
            string fullPath = Path.Combine(_dashboardsPath, "WhatsAppOverlay.simhubdash");
            
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveWhatsAppOverlay failed: {ex.Message}");
            }
        }
    }
}
