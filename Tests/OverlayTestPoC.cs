using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace WhatsAppSimHubPlugin.Tests
{
    /// <summary>
    /// PoC - Proof of Concept para testar métodos de overlay
    /// 
    /// OBJETIVO:
    /// 1. Testar SetOverlay() com diferentes formatos
    /// 2. Testar SendBitmap() com partial draws
    /// 3. Validar qual solução funciona melhor
    /// </summary>
    public class OverlayTestPoC
    {
        private readonly object _vocoreManager;  // RemoteVOCOREManager
        private readonly object _vocoreSettings;  // VOCORESettings
        private readonly Action<string> _log;
        
        public OverlayTestPoC(object vocoreManager, object vocoreSettings, Action<string> log)
        {
            _vocoreManager = vocoreManager ?? throw new ArgumentNullException(nameof(vocoreManager));
            _vocoreSettings = vocoreSettings ?? throw new ArgumentNullException(nameof(vocoreSettings));
            _log = log ?? ((msg) => Console.WriteLine(msg));
        }
        
        /// <summary>
        /// Executa TODOS os testes em sequência
        /// </summary>
        public async Task RunAllTests()
        {
            _log("╔═══════════════════════════════════════════════════════════════════╗");
            _log("║                  OVERLAY POC - COMEÇANDO TESTES                   ║");
            _log("╚═══════════════════════════════════════════════════════════════════╝");
            _log("");
            
            // Teste 1: SetOverlay com Base64
            await TestSetOverlayBase64();
            await Task.Delay(3000); // Esperar 3s entre testes
            
            // Teste 2: SetOverlay com Path
            await TestSetOverlayPath();
            await Task.Delay(3000);
            
            // Teste 3: SendBitmap com Partial Draws
            await TestSendBitmapPartialDraws();
            await Task.Delay(3000);
            
            // Teste 4: Clear Overlay
            await TestClearOverlay();
            
            _log("");
            _log("╔═══════════════════════════════════════════════════════════════════╗");
            _log("║                     TESTES CONCLUÍDOS                             ║");
            _log("╚═══════════════════════════════════════════════════════════════════╝");
        }
        
        /// <summary>
        /// TESTE 1: SetOverlay() com Base64
        /// </summary>
        private async Task TestSetOverlayBase64()
        {
            _log("─── TESTE 1: SetOverlay() com Base64 ───");
            _log("");
            
            try
            {
                // 1. Criar bitmap de teste
                Bitmap overlayBitmap = CreateTestOverlayBitmap("TESTE BASE64", isVip: true, isUrgent: false);
                
                // 2. Converter para Base64
                string base64 = BitmapToBase64(overlayBitmap);
                _log($"✓ Bitmap convertido para Base64 ({base64.Length} chars)");
                
                // 3. Chamar SetOverlay via reflection
                var method = _vocoreManager.GetType().GetMethod("SetOverlay");
                if (method == null)
                {
                    _log("❌ Método SetOverlay não encontrado!");
                    return;
                }
                
                _log("✓ Método SetOverlay encontrado");
                _log("→ Chamando SetOverlay(base64)...");
                
                method.Invoke(_vocoreManager, new object[] { base64 });
                
                _log("✅ SetOverlay(base64) executado SEM ERROS!");
                _log("   Verifique o VoCore: deve aparecer 'TESTE BASE64' com estrela");
                _log("");
                
                // Esperar 5s e limpar
                _log("   Aguardando 5 segundos...");
                await Task.Delay(5000);
                
                method.Invoke(_vocoreManager, new object[] { null });
                _log("✓ Overlay limpo com SetOverlay(null)");
            }
            catch (Exception ex)
            {
                _log($"❌ ERRO: {ex.Message}");
                _log($"   Stack: {ex.StackTrace}");
            }
            
            _log("");
        }
        
        /// <summary>
        /// TESTE 2: SetOverlay() com Path
        /// </summary>
        private async Task TestSetOverlayPath()
        {
            _log("─── TESTE 2: SetOverlay() com Path ───");
            _log("");
            
            try
            {
                // 1. Criar bitmap de teste
                Bitmap overlayBitmap = CreateTestOverlayBitmap("TESTE PATH", isVip: false, isUrgent: true);
                
                // 2. Salvar em ficheiro temporário
                string tempPath = Path.Combine(Path.GetTempPath(), "whatsapp_overlay_test.png");
                overlayBitmap.Save(tempPath, ImageFormat.Png);
                _log($"✓ Bitmap salvo em: {tempPath}");
                
                // 3. Chamar SetOverlay via reflection
                var method = _vocoreManager.GetType().GetMethod("SetOverlay");
                if (method == null)
                {
                    _log("❌ Método SetOverlay não encontrado!");
                    return;
                }
                
                _log("→ Chamando SetOverlay(path)...");
                method.Invoke(_vocoreManager, new object[] { tempPath });
                
                _log("✅ SetOverlay(path) executado SEM ERROS!");
                _log("   Verifique o VoCore: deve aparecer 'TESTE PATH' com alerta");
                _log("");
                
                // Esperar 5s e limpar
                _log("   Aguardando 5 segundos...");
                await Task.Delay(5000);
                
                method.Invoke(_vocoreManager, new object[] { null });
                _log("✓ Overlay limpo");
                
                // Limpar ficheiro
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                    _log("✓ Ficheiro temporário removido");
                }
            }
            catch (Exception ex)
            {
                _log($"❌ ERRO: {ex.Message}");
                _log($"   Stack: {ex.StackTrace}");
            }
            
            _log("");
        }
        
        /// <summary>
        /// TESTE 3: SendBitmap() com Partial Draws
        /// </summary>
        private async Task TestSendBitmapPartialDraws()
        {
            _log("─── TESTE 3: SendBitmap() com Partial Draws ───");
            _log("");
            
            try
            {
                // 1. Obter dimensões do display
                var widthProp = _vocoreSettings.GetType().GetProperty("DisplayWidth");
                var heightProp = _vocoreSettings.GetType().GetProperty("DisplayHeight");
                
                int width = widthProp != null ? (int)widthProp.GetValue(_vocoreSettings) : 800;
                int height = heightProp != null ? (int)heightProp.GetValue(_vocoreSettings) : 480;
                
                _log($"✓ Dimensões do VoCore: {width}x{height}");
                
                // 2. Criar bitmap COMPLETO com overlay no topo
                Bitmap fullFrame = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(fullFrame))
                {
                    // Fundo preto (transparente para VoCore)
                    g.Clear(Color.Black);
                    
                    // Desenhar notificação WhatsApp no topo
                    DrawWhatsAppNotification(g, "TESTE SENDBITMAP", "Via SendBitmap + Partial Draws", true, true);
                }
                
                _log("✓ Frame completo criado com overlay");
                
                // 3. Converter para bytes
                byte[] bitmapBytes = BitmapToBytes(fullFrame);
                _log($"✓ Bitmap convertido para bytes ({bitmapBytes.Length} bytes)");
                
                // 4. Chamar SendBitmap via reflection
                var method = _vocoreManager.GetType().GetMethod("SendBitmap");
                if (method == null)
                {
                    _log("❌ Método SendBitmap não encontrado!");
                    return;
                }
                
                _log("→ Chamando SendBitmap(bytes, width, height, false)...");
                method.Invoke(_vocoreManager, new object[] { bitmapBytes, width, height, false });
                
                _log("✅ SendBitmap() executado SEM ERROS!");
                _log("   Verifique o VoCore: deve aparecer notificação no topo");
                _log("   O DDU deve CONTINUAR VISÍVEL por baixo! ✨");
                _log("");
                
                // Esperar 5s e limpar
                _log("   Aguardando 5 segundos...");
                await Task.Delay(5000);
                
                // Enviar frame todo preto para limpar
                fullFrame = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(fullFrame))
                {
                    g.Clear(Color.Black);
                }
                bitmapBytes = BitmapToBytes(fullFrame);
                method.Invoke(_vocoreManager, new object[] { bitmapBytes, width, height, false });
                
                _log("✓ Overlay limpo (frame preto enviado)");
            }
            catch (Exception ex)
            {
                _log($"❌ ERRO: {ex.Message}");
                _log($"   Stack: {ex.StackTrace}");
            }
            
            _log("");
        }
        
        /// <summary>
        /// TESTE 4: Clear Overlay
        /// </summary>
        private async Task TestClearOverlay()
        {
            _log("─── TESTE 4: Clear Overlay ───");
            _log("");
            
            try
            {
                var method = _vocoreManager.GetType().GetMethod("SetOverlay");
                if (method != null)
                {
                    _log("→ Chamando SetOverlay(null)...");
                    method.Invoke(_vocoreManager, new object[] { null });
                    _log("✅ SetOverlay(null) executado");
                }
                else
                {
                    _log("⚠️  SetOverlay não disponível, usando SendBitmap com frame preto");
                    
                    var widthProp = _vocoreSettings.GetType().GetProperty("DisplayWidth");
                    var heightProp = _vocoreSettings.GetType().GetProperty("DisplayHeight");
                    int width = widthProp != null ? (int)widthProp.GetValue(_vocoreSettings) : 800;
                    int height = heightProp != null ? (int)heightProp.GetValue(_vocoreSettings) : 480;
                    
                    Bitmap blackFrame = new Bitmap(width, height);
                    using (Graphics g = Graphics.FromImage(blackFrame))
                    {
                        g.Clear(Color.Black);
                    }
                    
                    byte[] bytes = BitmapToBytes(blackFrame);
                    var sendMethod = _vocoreManager.GetType().GetMethod("SendBitmap");
                    sendMethod?.Invoke(_vocoreManager, new object[] { bytes, width, height, false });
                    
                    _log("✅ Frame preto enviado");
                }
            }
            catch (Exception ex)
            {
                _log($"❌ ERRO: {ex.Message}");
            }
            
            _log("");
        }
        
        // ═══════════════════════════════════════════════════════════════════
        //  FUNÇÕES AUXILIARES
        // ═══════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Cria bitmap de teste com notificação WhatsApp
        /// </summary>
        private Bitmap CreateTestOverlayBitmap(string title, bool isVip, bool isUrgent)
        {
            // Criar bitmap 800x120 (notificação no topo)
            Bitmap bmp = new Bitmap(800, 120);
            
            using (Graphics g = Graphics.FromImage(bmp))
            {
                DrawWhatsAppNotification(g, title, "Esta é uma mensagem de teste", isVip, isUrgent);
            }
            
            return bmp;
        }
        
        /// <summary>
        /// Desenha notificação WhatsApp estilo SimHub
        /// </summary>
        private void DrawWhatsAppNotification(Graphics g, string sender, string message, bool isVip, bool isUrgent)
        {
            // Background semi-transparente
            var boxRect = new Rectangle(20, 20, 760, 100);
            using (var brush = new SolidBrush(Color.FromArgb(220, 40, 40, 40)))
            {
                g.FillRectangle(brush, boxRect);
            }
            
            // Border
            using (var pen = new Pen(Color.FromArgb(100, 0, 122, 255), 2))
            {
                g.DrawRectangle(pen, boxRect);
            }
            
            // Icon/Badge
            int iconX = 40;
            if (isVip)
            {
                DrawStar(g, iconX, 35);
                iconX += 40;
            }
            if (isUrgent)
            {
                DrawAlert(g, iconX, 35);
            }
            
            // Sender
            using (var font = new Font("Segoe UI", 16, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                g.DrawString(sender, font, brush, 110, 30);
            }
            
            // Message
            using (var font = new Font("Segoe UI", 12))
            using (var brush = new SolidBrush(Color.FromArgb(220, 220, 220)))
            {
                g.DrawString(message, font, brush, 110, 60);
            }
        }
        
        /// <summary>
        /// Desenha estrela VIP
        /// </summary>
        private void DrawStar(Graphics g, int x, int y)
        {
            using (var brush = new SolidBrush(Color.Gold))
            {
                g.FillEllipse(brush, x, y, 30, 30);
            }
            using (var font = new Font("Segoe UI", 16, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.Black))
            {
                g.DrawString("★", font, brush, x + 5, y + 2);
            }
        }
        
        /// <summary>
        /// Desenha alerta URGENTE
        /// </summary>
        private void DrawAlert(Graphics g, int x, int y)
        {
            using (var brush = new SolidBrush(Color.OrangeRed))
            {
                g.FillEllipse(brush, x, y, 30, 30);
            }
            using (var font = new Font("Segoe UI", 16, FontStyle.Bold))
            using (var brush = new SolidBrush(Color.White))
            {
                g.DrawString("!", font, brush, x + 10, y + 2);
            }
        }
        
        /// <summary>
        /// Converte Bitmap para Base64
        /// </summary>
        private string BitmapToBase64(Bitmap bmp)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                byte[] bytes = ms.ToArray();
                return Convert.ToBase64String(bytes);
            }
        }
        
        /// <summary>
        /// Converte Bitmap para array de bytes (RGB565 ou RGB888)
        /// </summary>
        private byte[] BitmapToBytes(Bitmap bmp)
        {
            // Converter para RGB565 (formato VoCore)
            // 2 bytes por pixel
            
            BitmapData bmpData = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb
            );
            
            int stride = bmpData.Stride;
            int bytes = Math.Abs(stride) * bmp.Height;
            
            byte[] rgbValues = new byte[bytes];
            Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);
            bmp.UnlockBits(bmpData);
            
            // Converter RGB888 → RGB565
            byte[] rgb565 = new byte[bmp.Width * bmp.Height * 2];
            int pos = 0;
            
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    int offset = y * stride + x * 3;
                    
                    byte b = rgbValues[offset];
                    byte g = rgbValues[offset + 1];
                    byte r = rgbValues[offset + 2];
                    
                    // RGB888 → RGB565
                    ushort rgb565Pixel = (ushort)(
                        ((r >> 3) << 11) |
                        ((g >> 2) << 5) |
                        (b >> 3)
                    );
                    
                    // Little endian
                    rgb565[pos++] = (byte)(rgb565Pixel & 0xFF);
                    rgb565[pos++] = (byte)((rgb565Pixel >> 8) & 0xFF);
                }
            }
            
            return rgb565;
        }
    }
}
