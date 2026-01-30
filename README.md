# WhatsApp SimHub Plugin

Plugin para SimHub que mostra notificações de WhatsApp durante corridas de sim racing, com sistema de fila inteligente, prioridades e respostas rápidas via botões do volante.

## 🎯 Características

- ✅ **Conexão WhatsApp** via QR Code
- ✅ **Sistema de Fila Inteligente** com priorização (Urgente > VIP > Normal)
- ✅ **Agrupamento de Mensagens** do mesmo contacto
- ✅ **Overlay Transparente** no VoCore
- ✅ **Respostas Rápidas** via botões do volante
- ✅ **Notificações com Badges** (⭐ VIP, 🚨 Urgente)
- ✅ **Re-notificações** para mensagens importantes

## 📋 Requisitos

- SimHub instalado
- .NET Framework 4.8
- Node.js (será instalado automaticamente na primeira execução)

## 🚀 Instalação

### Opção 1: Build do Código Fonte

1. Clone o repositório:
```bash
git clone https://github.com/seu-usuario/WhatsAppSimHubPlugin.git
```

2. Abra o projeto no Visual Studio 2019 ou superior

3. Configure a variável de ambiente `SimHubPath` apontando para a pasta de instalação do SimHub

4. Build do projeto (Release)

5. Copie `WhatsAppSimHubPlugin.dll` para a pasta raiz do SimHub

### Opção 2: Download do Release

1. Baixe o último release da página de releases
2. Extraia o ficheiro ZIP
3. Copie `WhatsAppSimHubPlugin.dll` para a pasta raiz do SimHub
4. Copie a pasta `scripts` para `%AppData%/SimHub/WhatsAppPlugin/`

## ⚙️ Configuração

### Primeira Execução

1. Inicie o SimHub
2. Vá para Settings > Plugins
3. Encontre "WhatsApp Plugin" na lista
4. Aguarde ~2 minutos enquanto Node.js é baixado automaticamente
5. Após instalação, será mostrado um QR Code
6. Abra WhatsApp no telemóvel > Settings > Linked Devices
7. Escaneie o QR Code mostrado no SimHub

### Configurar Contactos

1. Vá para a tab "Contacts"
2. Adicione contactos permitidos (Nome + Número no formato internacional)
3. Marque ⭐ para contactos VIP (mensagens ficam na fila até serem lidas)
4. Salve as alterações

### Configurar Keywords Urgentes

1. Vá para a tab "Keywords"
2. Adicione palavras-chave que tornam mensagens urgentes (ex: "urgente", "emergência")
3. Mensagens com estas palavras são priorizadas e mostradas por mais tempo

### Configurar Respostas Rápidas

1. Vá para a tab "Quick Replies"
2. Configure até 2 respostas rápidas
3. Associe cada resposta a um botão do volante (ex: Botão 5)
4. Configure o comportamento (Press, LongPress, Double)

### Configurar Overlay

1. Vá para a tab "Display"
2. Escolha o dispositivo alvo (VoCore)
3. Escolha posição (Top, Center, Bottom)
4. Configure durações e comportamentos

## 🎮 Utilização

Durante uma corrida:

1. Quando receber mensagem, ela aparecerá no overlay
2. Mensagens normais: mostram 1x por 5 segundos
3. Mensagens VIP: repetem a cada 5 minutos até serem lidas
4. Mensagens urgentes: mostram por 10 segundos
5. Para responder, pressione o botão configurado no volante
6. A resposta pré-configurada será enviada automaticamente

## 📊 Propriedades Expostas

O plugin expõe as seguintes propriedades para uso em dashboards:

- `[WhatsApp.ConnectionStatus]` - "Connected" / "Disconnected" / "Error"
- `[WhatsApp.ConnectedNumber]` - Número conectado
- `[WhatsApp.HasMessage]` - true/false
- `[WhatsApp.CurrentSender]` - Nome do remetente
- `[WhatsApp.CurrentMessage]` - Texto da mensagem
- `[WhatsApp.MessageTime]` - Hora da mensagem
- `[WhatsApp.IsVip]` - true/false
- `[WhatsApp.IsUrgent]` - true/false
- `[WhatsApp.QueueCount]` - Número de mensagens na fila

## 🔧 Troubleshooting

### Plugin não carrega
- Verifique se todos os ficheiros DLL estão na pasta do SimHub
- Verifique logs em SimHub → Settings → Plugins → Plugin Logs

### Node.js não inicia
- Verifique se a pasta `%AppData%/SimHub/WhatsAppPlugin/node` existe
- Reinstale manualmente o Node.js se necessário

### WhatsApp não conecta
- Certifique-se que escaneou o QR Code
- Verifique conexão à internet
- Tente desconectar outros dispositivos vinculados

### Mensagens não aparecem
- Verifique se o contacto está na lista de permitidos
- Verifique se o overlay está configurado corretamente
- Verifique logs do plugin

## 📝 Estrutura de Ficheiros

```
WhatsAppSimHubPlugin/
├── WhatsAppSimHubPlugin.dll          # Plugin principal
├── Models/                            # Classes de dados
│   ├── Contact.cs
│   ├── QueuedMessage.cs
│   └── PluginSettings.cs
├── Core/                              # Lógica principal
│   ├── MessageQueue.cs                # Gestão de fila
│   └── NodeJsManager.cs               # Gestão Node.js
├── scripts/                           # Scripts Node.js
│   └── whatsapp-client.js
└── config/                            # Configurações (criado em runtime)
    ├── settings.json
    ├── contacts.json
    └── keywords.json
```

## 🤝 Contribuir

Contribuições são bem-vindas! Por favor:

1. Fork o projeto
2. Crie uma branch para sua feature (`git checkout -b feature/AmazingFeature`)
3. Commit suas mudanças (`git commit -m 'Add some AmazingFeature'`)
4. Push para a branch (`git push origin feature/AmazingFeature`)
5. Abra um Pull Request

## 📄 Licença

Este projeto está sob licença MIT. Veja o ficheiro LICENSE para mais detalhes.

## 👨‍💻 Autor

Desenvolvido pela comunidade SimHub

## 🙏 Agradecimentos

- Equipa SimHub pelo excelente simulador
- Biblioteca whatsapp-web.js
- Comunidade de sim racing
