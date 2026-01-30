# 📋 CONTEXT - Estado Atual do Projeto

**Última atualização:** 2026-01-30 20:05  
**Sessão:** Claude Code (Zed) - Sonnet 4.5

---

## 🎯 O QUE É ESTE PROJETO

Plugin para SimHub que mostra notificações de WhatsApp no VoCore durante corridas de sim racing.

**Funcionalidades principais:**
- Conexão WhatsApp via whatsapp-web.js (Node.js)
- Sistema de fila com prioridades (VIP, Urgente, Normal)
- Overlay condicional no VoCore
- Respostas rápidas via botões do volante
- Dashboard merge automático (pendente)

---

## 📍 ESTADO ATUAL

### ✅ Concluído
- [x] Estrutura base do plugin C# (.NET Framework 4.8)
- [x] Integração com whatsapp-web.js via WebSocket
- [x] Sistema de fila de mensagens (MessageQueue.cs)
- [x] UI de configuração (SettingsControl.xaml)
- [x] Git inicializado e primeiro commit feito
- [x] Repositório GitHub criado: https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin
- [x] Build script automatizado (build-and-deploy.bat)
- [x] Compilação Release funcional

### 🔄 Em Progresso
- [ ] Dashboard merge V2.0 (Wrapper com 2 Layers) - documentação lida, não implementado

### ⏳ Pendente (TODO)
- [ ] Implementar auto-merge de dashboards ao iniciar plugin
- [ ] Overlay renderer com hook no VoCore
- [ ] Aviso "Disconnected" no VoCore
- [ ] Cores dinâmicas do SimHub (remover hard-coded)
- [ ] Auto-save nas configurações
- [ ] Default Position = "Top"

---

## 🛠️ STACK TECNOLÓGICO

- **Plugin:** C# .NET Framework 4.8
- **WhatsApp:** whatsapp-web.js (Node.js) via WebSocket
- **UI:** WPF (XAML)
- **Build:** MSBuild (Visual Studio 2022 Professional)
- **Git:** main branch, GitHub

---

## 📂 ESTRUTURA DO PROJETO

```
whatsapp-plugin/
├── WhatsAppPlugin.cs           (106KB - classe principal)
├── Core/
│   ├── WebSocketManager.cs    (gestão Node.js)
│   ├── MessageQueue.cs         (fila de mensagens)
│   ├── DashboardGenerator.cs
│   ├── DependencyManager.cs
│   └── OverlayRenderer.cs
├── Models/
│   ├── Contact.cs
│   ├── PluginSettings.cs
│   └── QueuedMessage.cs
├── UI/
│   ├── SettingsControl.xaml
│   └── SetupControl.xaml
├── Resources/
│   └── whatsapp-server.js      (Node.js WebSocket server)
├── build-and-deploy.bat        (compila + copia para SimHub)
└── DASHBOARD_MERGE_DOCUMENTATION.md
```

---

## 🔑 DECISÕES TÉCNICAS IMPORTANTES

### 1. WhatsApp Web Backend
**Decisão:** whatsapp-web.js (mantido)  
**Alternativa explorada:** Baileys (mais leve, sem browser)  
**Razão:** whatsapp-web.js já integrado, funcional  
**Nota:** Baileys é válido para projetos futuros

### 2. Dashboard Merge
**Decisão:** V2.0 - Wrapper com 2 Layers  
**Alternativa rejeitada:** V1.0 - Merge direto nos Items  
**Razão:** 
- Zero conflitos com Widgets
- Ordem de renderização garantida
- Mais simples de manter
- Performance melhor

### 3. Build & Deploy
**Decisão:** Batch file automatizado  
**Funcionalidade:**
- Rebuild completo (não incremental)
- Fecha SimHub se estiver aberto
- Copia DLL para `C:\Program Files (x86)\SimHub\`

---

## 🚀 PRÓXIMOS PASSOS

### Imediato (próxima sessão)
1. Implementar `DashboardMerger.cs` com técnica V2.0
2. Testar merge automático ao iniciar plugin

### Curto Prazo
1. Overlay renderer no VoCore
2. Sistema de avisos (disconnected, etc)
3. Melhorias UI (cores dinâmicas, auto-save)

### Médio Prazo
1. Testes com utilizadores
2. Documentação de instalação
3. Release v1.0 no GitHub

---

## 📝 NOTAS PARA PRÓXIMA SESSÃO

### O que estava a fazer
- Acabei de criar repositório Git e build script
- Li documentação de Dashboard Merge (V2.0)
- Compilação funcional

### Se houver problemas
- Build: usar `build-and-deploy.bat`
- Git: branch `main`, origin configurado
- SimHub: `C:\Program Files (x86)\SimHub\`

### Ficheiros importantes para ler
- `DASHBOARD_MERGE_DOCUMENTATION.md` - técnica de merge
- `TODO_IMPLEMENTATION.md` - tarefas pendentes (pode estar desatualizado)
- `README.md` - visão geral do projeto

---

## 🔗 LINKS ÚTEIS

- Repositório: https://github.com/BrunoSilva1978PT/WhatsApp-SimHub-Plugin
- whatsapp-web.js: https://github.com/pedroslopez/whatsapp-web.js
- Baileys (alternativa): https://github.com/WhiskeySockets/Baileys

---

**Instruções para Claude:** 
Lê este ficheiro no início de cada sessão para recuperar contexto.
Atualiza este ficheiro sempre que houver progresso significativo.
