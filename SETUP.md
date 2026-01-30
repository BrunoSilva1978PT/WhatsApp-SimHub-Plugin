# Setup do Projeto WhatsApp SimHub Plugin

## 🔧 Requisitos

- Visual Studio 2019 ou superior
- .NET Framework 4.8 SDK
- SimHub instalado

## 📝 Passo a Passo

### 1. Clonar/Extrair o Projeto

```bash
git clone https://github.com/seu-usuario/WhatsAppSimHubPlugin.git
cd WhatsAppSimHubPlugin
```

Ou extrair o ZIP para uma pasta.

### 2. Configurar Caminho do SimHub

Tens 3 opções:

#### Opção A: Variável de Ambiente (RECOMENDADO)

1. Abrir "Environment Variables" no Windows
2. Adicionar variável do sistema:
   - Nome: `SimHubPath`
   - Valor: `C:\Program Files (x86)\SimHub` (ou onde tens instalado)

#### Opção B: Editar .csproj

Abrir `WhatsAppSimHubPlugin.csproj` e alterar esta linha:

```xml
<SimHubPath Condition="'$(SimHubPath)' == ''">C:\Program Files (x86)\SimHub</SimHubPath>
```

Para o caminho correto do teu SimHub.

#### Opção C: Passar na Linha de Comando

```bash
msbuild WhatsAppSimHubPlugin.csproj /p:SimHubPath="D:\MeuSimHub"
```

### 3. Verificar DLLs do SimHub

O projeto precisa destas DLLs do SimHub:

- ✅ `SimHub.Plugins.dll` - Interface do plugin
- ✅ `Newtonsoft.Json.dll` - JSON serialization

**Verificar se existem:**

```
C:\Program Files (x86)\SimHub\SimHub.Plugins.dll
C:\Program Files (x86)\SimHub\Newtonsoft.Json.dll
```

Se não existirem, o SimHub pode estar instalado noutro local. Procura por:
- `C:\SimHub\`
- `D:\Program Files\SimHub\`
- Onde instalaste o SimHub

### 4. Abrir no Visual Studio

1. Abrir `WhatsAppSimHubPlugin.sln`
2. Wait for NuGet packages to restore (QRCoder, System.Drawing.Common)
3. Se aparecerem erros de referências:
   - Botão direito no projeto → Properties
   - Reference Paths → Adicionar caminho do SimHub
   - Ou: Botão direito em References → Add Reference → Browse → Selecionar DLLs

### 5. Build

```
Build → Build Solution (F6)
```

**Se tudo correr bem:**
```
========== Build: 1 succeeded, 0 failed ==========
```

**Output:**
```
bin\Release\net48\WhatsAppSimHubPlugin.dll
```

## ❌ Troubleshooting

### Erro: "Não foi possível encontrar SimHub.Plugins.dll"

**Solução:**
1. Confirmar caminho do SimHub
2. Editar .csproj com caminho correto
3. Ou adicionar manualmente: References → Add Reference → Browse

### Erro: "Não foi possível encontrar Newtonsoft.Json"

**Solução:**
- O SimHub inclui esta DLL
- Verificar se está na mesma pasta que SimHub.Plugins.dll
- Se não, fazer download via NuGet

### Erro: "icon.png not found"

**Solução:**
- O ícone deve estar em `Resources\icon.png`
- Se não existir, criar uma imagem 64x64 PNG qualquer
- Ou comentar a linha do ícone no código

### Erro: QRCoder não restaura

**Solução:**
```bash
# Na pasta do projeto
nuget restore
# ou
dotnet restore
```

### Erro: "Target framework not installed"

**Solução:**
- Instalar .NET Framework 4.8 Developer Pack
- Download: https://dotnet.microsoft.com/download/dotnet-framework/net48

## 📦 Estrutura Esperada

```
WhatsAppSimHubPlugin/
├── WhatsAppSimHubPlugin.csproj  ← Referências aqui
├── WhatsAppPlugin.cs
├── Resources/
│   └── icon.png                  ← Tem que existir
├── Models/
│   ├── Contact.cs
│   ├── PluginSettings.cs
│   └── QueuedMessage.cs
├── Core/
│   ├── MessageQueue.cs
│   └── NodeJsManager.cs
└── scripts/
    └── whatsapp-client.js
```

## ✅ Verificação Final

Antes de compilar, verificar:

- [ ] SimHub instalado
- [ ] Variável SimHubPath configurada OU .csproj editado
- [ ] DLLs existem no caminho configurado
- [ ] icon.png existe em Resources/
- [ ] Visual Studio aberto com projeto carregado
- [ ] NuGet packages restaurados

## 🚀 Após Compilar

1. Copiar `bin\Release\net48\WhatsAppSimHubPlugin.dll` para pasta do SimHub
2. Copiar pasta `scripts\` para `%AppData%\SimHub\WhatsAppPlugin\`
3. Reiniciar SimHub
4. Settings → Plugins → Encontrar "WhatsApp Plugin"

## 💡 Dicas

- Se mudares o caminho do SimHub, faz Rebuild (não só Build)
- Em caso de dúvida, usa caminho absoluto no .csproj
- Podes compilar em Debug primeiro para testar
- Logs do SimHub: Settings → Plugins → Plugin Logs

## 📞 Problemas?

Se continuarem erros:
1. Copiar TODAS as mensagens de erro
2. Verificar caminho do SimHub
3. Confirmar que DLLs existem
4. Tentar Build em modo Debug primeiro
