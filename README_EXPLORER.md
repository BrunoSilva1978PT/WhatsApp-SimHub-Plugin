╔══════════════════════════════════════════════════════════════════════╗
║      WhatsApp SimHub Plugin - VoCore Settings Explorer v2.0          ║
║                    VERSÃO COM EXPLORAÇÃO PROFUNDA                    ║
║                         27 Janeiro 2025                              ║
╚══════════════════════════════════════════════════════════════════════╝

🔥 O QUE HÁ DE NOVO
═══════════════════════════════════════════════════════════════════════

Esta versão MANTÉM todo o código funcional do plugin WhatsApp e ADICIONA:

✓ Exploração PROFUNDA do VoCore Settings
✓ Exploração recursiva até 2 níveis de profundidade
✓ Detecção de propriedades relevantes (marcadas com ★★★)
✓ Detecção de métodos relevantes para overlay
✓ Exploração de campos (fields) privados
✓ Busca específica por métodos conhecidos (ShowInformationOverlay, etc.)
✓ Logs formatados e organizados

🎯 O QUE FAZ
═══════════════════════════════════════════════════════════════════════

Quando o plugin arranca e se conecta ao VoCore, ele:

1. ✅ Funciona NORMALMENTE (todas as funcionalidades WhatsApp)
2. ✅ EXPLORA automaticamente o VoCore Settings
3. ✅ Gera logs DETALHADOS com tudo o que encontra
4. ✅ Marca com ★★★ tudo o que é relevante para overlay
5. ✅ Procura métodos específicos conhecidos


📋 ESTRUTURA DA EXPLORAÇÃO
═══════════════════════════════════════════════════════════════════════

FASE 1: EXPLORING SETTINGS PROPERTY
  → Todas as propriedades de Settings
  → Objetos complexos relevantes (até 2 níveis)
  → Métodos relevantes de Settings

FASE 2: EXPLORING OWNER DEVICE
  → Todas as propriedades de OwnerDevice
  → Objetos complexos relevantes
  → Métodos relevantes de OwnerDevice

FASE 3: EXPLORING BITMAP DEVICE
  → Todas as propriedades do BitmapDisplayDevice
  → Campos privados relevantes

FASE 4: SEARCHING FOR OVERLAY METHODS
  → ShowInformationOverlay
  → ShowTextOverlay, ShowOverlay
  → DisplayMessage, DisplayInfo
  → AddOverlay, SetOverlayText
  → DrawTextOverlay


🔍 PALAVRAS-CHAVE PROCURADAS
═══════════════════════════════════════════════════════════════════════

Propriedades/Métodos/Campos que contenham:
✓ overlay      - Sistema de sobreposição
✓ info         - Informação
✓ message      - Mensagem
✓ notification - Notificação
✓ popup        - Popup
✓ display      - Display/Mostrar
✓ show         - Mostrar
✓ text         - Texto
✓ label        - Label
✓ layer        - Camada
✓ draw         - Desenhar
✓ render       - Renderizar


⚡ INSTALAÇÃO E USO
═══════════════════════════════════════════════════════════════════════

1. COMPILAR (se necessário):
   - Abra WhatsAppSimHubPlugin.sln no Visual Studio
   - Build → Build Solution
   
2. INSTALAR:
   - Copie WhatsAppSimHubPlugin.dll para pasta do SimHub
   - Copie pasta 'scripts' também
   
3. CONFIGURAR:
   - Abra SimHub
   - Settings → Plugins → WhatsApp
   - Configure o VoCore DDU desejado
   - Salve as configurações
   
4. VER LOGS:
   - Os logs estão em: %AppData%/SimHub/WhatsAppPlugin/logs/messages.log
   - Procure por:
     * "VOCORE SETTINGS - EXPLORAÇÃO PROFUNDA"
     * "★★★" (marca itens relevantes)
     
5. ANALISAR:
   - Copie TODOS os logs da exploração
   - Analise propriedades/métodos marcados com ★★★
   - Identifique a API correta para overlay


📊 O QUE ESPERAR NOS LOGS
═══════════════════════════════════════════════════════════════════════

EXEMPLO DE OUTPUT:

╔══════════════════════════════════════════════════════════════════════╗
║         VOCORE SETTINGS - EXPLORAÇÃO PROFUNDA v2.0                   ║
╚══════════════════════════════════════════════════════════════════════╝
Bitmap Device Type: SimHub.Plugins.OutputPlugins.GraphicalDashboard...

═══ 1. EXPLORING SETTINGS PROPERTY ═══
Settings Type: SimHub.Plugins.OutputPlugins.GraphicalDashboard.DDUScreenSettings
[Settings] has 47 properties:
  Width (Int32) = 800
  Height (Int32) = 480
  ★★★ ShowInformationOverlay (Boolean) = True
  ★★★ OverlayPosition (String) = "Top"
  ★★★ OverlayDuration (Int32) = 5000
  ...

═══ 4. SEARCHING FOR OVERLAY METHODS ═══
★★★ FOUND: ShowInformationOverlay(String text, Int32 duration) → Void
  [Settings] ★★★ SetOverlayText(String text) → Void
  ...


🎨 O QUE FAZER COM OS RESULTADOS
═══════════════════════════════════════════════════════════════════════

Depois de recolher os logs:

1. IDENTIFICAR A API:
   Procure por:
   - Propriedades booleanas que activam/desactivam overlay
   - Métodos que mostram texto/mensagens
   - Propriedades que guardam o texto do overlay
   
2. CRIAR VERSÃO DE TESTE:
   Teste o método/propriedade descoberto:
   ```csharp
   // Exemplo se encontrarmos ShowInformationOverlay
   vocoreSettings.ShowInformationOverlay("Teste WhatsApp", 5000);
   ```
   
3. IMPLEMENTAR NO OVERLAYRENDERER:
   Actualizar OverlayRenderer.cs para usar a API descoberta
   
4. TESTAR:
   Usar botão "Test Overlay" na UI
   
5. REFINAR:
   Ajustar posicionamento, duração, animações


🚀 FUNCIONALIDADES EXISTENTES
═══════════════════════════════════════════════════════════════════════

O plugin MANTÉM todas as funcionalidades anteriores:

✅ Integração WhatsApp via Node.js (whatsapp-web.js)
✅ Gestão de contactos VIP
✅ Keywords urgentes
✅ Fila de mensagens com priorização
✅ Agrupamento de mensagens
✅ Quick Replies configuráveis
✅ DeviceManager (detecção de VoCores)
✅ OverlayRenderer (renderização de overlay)
✅ Interface WPF completa (5 tabs)
✅ Propriedades expostas ao SimHub
✅ Ações registadas (SendReply1/2, DismissMessage)


🐛 TROUBLESHOOTING
═══════════════════════════════════════════════════════════════════════

Não vejo logs de exploração?
→ Verifique se o VoCore está configurado em Settings
→ Verifique se o device está online
→ Reinicie o SimHub

Logs não têm ★★★?
→ Pode significar que não há API de overlay exposta
→ Ou está com nome diferente do esperado
→ Analise TODOS os métodos mesmo sem ★★★

Plugin não carrega?
→ Verifique se todas as DLLs de referência existem
→ Confirme .NET Framework 4.8
→ Veja Plugin Logs em SimHub


📂 ESTRUTURA DE FICHEIROS
═══════════════════════════════════════════════════════════════════════

WhatsAppSimHubPlugin/
├── Core/
│   ├── DeviceManager.cs          - Detecção de VoCores
│   ├── MessageQueue.cs            - Gestão da fila de mensagens
│   ├── NodeJsManager.cs           - Gestão do processo Node.js
│   └── OverlayRenderer.cs         - Renderização do overlay
├── Models/
│   ├── Contact.cs                 - Modelo de contacto
│   ├── PluginSettings.cs          - Configurações do plugin
│   └── QueuedMessage.cs           - Modelo de mensagem na fila
├── UI/
│   ├── SettingsControl.xaml       - Interface WPF
│   └── SettingsControl.xaml.cs    - Code-behind
├── scripts/
│   └── whatsapp-client.js         - Cliente Node.js WhatsApp
├── WhatsAppPlugin.cs              - CLASSE PRINCIPAL (COM EXPLORAÇÃO)
└── WhatsAppSimHubPlugin.csproj    - Ficheiro de projeto


⚙️ ALTERAÇÕES NO CÓDIGO
═══════════════════════════════════════════════════════════════════════

FICHEIRO: WhatsAppPlugin.cs

ADICIONADO:
• ExploreVoCoreDeep()            - Método principal de exploração
• ExploreObject()                - Exploração recursiva de objetos
• IsRelevantProperty()           - Filtro de propriedades relevantes
• IsRelevantMethod()             - Filtro de métodos relevantes
• FormatValue()                  - Formatação de valores para log
• SearchOverlayMethods()         - Busca de métodos conhecidos
• SearchMethodsInObject()        - Busca em objetos específicos

MODIFICADO:
• AttachToVoCore()               - Chama ExploreVoCoreDeep()


📧 PRÓXIMOS PASSOS
═══════════════════════════════════════════════════════════════════════

1. ✅ Compilar e instalar esta versão
2. ✅ Executar SimHub com VoCore configurado
3. ✅ Recolher logs completos da exploração
4. ✅ Identificar API de overlay nos logs
5. ⏳ Criar versão de teste com API descoberta
6. ⏳ Implementar no OverlayRenderer
7. ⏳ Testar com mensagens WhatsApp reais
8. ⏳ Refinar e otimizar


💡 DICAS IMPORTANTES
═══════════════════════════════════════════════════════════════════════

• A exploração acontece AUTOMATICAMENTE ao conectar ao VoCore
• Não precisa fazer nada especial, só configurar o device
• Os logs ficam em %AppData%/SimHub/WhatsAppPlugin/logs/
• Propriedades ★★★ são as mais promissoras
• Mesmo propriedades SEM ★★★ podem ser úteis
• Analise os tipos (Type) das propriedades
• Métodos com "Show" ou "Display" são candidatos principais


═══════════════════════════════════════════════════════════════════════
                    BOA EXPLORAÇÃO! 🚀🔍
           Vamos descobrir como fazer overlay no VoCore!
═══════════════════════════════════════════════════════════════════════
