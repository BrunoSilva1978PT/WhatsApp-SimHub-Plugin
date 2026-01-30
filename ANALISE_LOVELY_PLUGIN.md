# 🔍 Análise do Lovely Plugin - Sistema de Botões Nativo

## 📋 Descobertas Importantes

### 1. Namespace do SimHub
```xml
xmlns:styles="clr-namespace:SimHub.Plugins.Styles;assembly=SimHub.Plugins"
```

O Lovely Plugin importa estilos e controles do **SimHub.Plugins.Styles**!

### 2. Propriedades das Actions
```
ActionName="LovelyPlugin.TrueDarkModeStateToggle"
FriendlyName="True Dark Mode Trigger"
```

Cada action/botão configurável tem:
- **ActionName**: Nome único da action (usado internamente)
- **FriendlyName**: Nome amigável mostrado ao user

### 3. Métodos Disponíveis
```csharp
IModifySimHub.AddAction           // Registar action
IModifySimHub.AddInputMapping     // Criar mapeamento automaticamente!
IModifySimHub.Manager              // Acesso ao PluginManager
```

## 🎯 Como Deve Funcionar

### Passo 1: Registar Action (no C#)
```csharp
// WhatsAppPlugin.cs
private void RegisterActions()
{
    // Registar action com FriendlyName
    this.AddAction("WhatsApp.SendReply1", (a, b) =>
    {
        SendQuickReply(1);
    });
    
    // Definir FriendlyName (se possível)
    // ???
}
```

### Passo 2: UI no XAML
```xml
<!-- Hipótese: Deve haver um controle do SimHub.Plugins.Styles -->
<StackPanel>
    <TextBlock Text="Reply 1 Button:"/>
    
    <!-- CONTROLE NATIVO DO SIMHUB (nome exato desconhecido) -->
    <!-- Possibilidades:
         - <styles:ActionInput ActionName="WhatsApp.SendReply1" />
         - <styles:InputMapper ActionName="WhatsApp.SendReply1" />
         - <styles:ControlBinding ActionName="WhatsApp.SendReply1" />
    -->
    
    <!-- Este controle automaticamente: -->
    <!-- 1. Mostra "Click to configure" -->
    <!-- 2. Abre ControlPicker ao clicar -->
    <!-- 3. Mostra botão configurado -->
    <!-- 4. Mostra Change/Clear/Add no hover -->
    <!-- 5. CRIA mapeamento no Control and Events -->
</StackPanel>
```

### Passo 3: Criar Mapeamento (Opcional?)
```csharp
// Talvez seja necessário criar mapeamento programaticamente?
// Usando AddInputMapping???
```

## ❓ O Que Ainda Não Sei

1. **Nome exato** do controle WPF que faz isto
2. Como **ligar** o FriendlyName à action
3. Se preciso chamar **AddInputMapping** manualmente

## 💡 Próximos Passos

### Opção A: Experimentar
Tentar diferentes nomes de controles:
- `<styles:ActionInput>`
- `<styles:InputMapper>`  
- `<styles:ControlBinding>`

### Opção B: Ver SimHub.Plugins.dll
Descompilar `SimHub.Plugins.dll` (que o Bruno deve ter) para ver que controles existem em `SimHub.Plugins.Styles`

### Opção C: Procurar na Internet
Ver se há documentação ou exemplos de plugins do SimHub open-source

## 🎯 Teoria de Funcionamento

```
1. Plugin regista Action: "WhatsApp.SendReply1"
   ↓
2. XAML usa controle nativo: <styles:??? ActionName="WhatsApp.SendReply1" />
   ↓
3. User clica "Click to configure"
   ↓
4. SimHub abre ControlPicker nativo (SEM popup!)
   ↓
5. User escolhe botão (ex: SCREEN1_BUTTON1)
   ↓
6. SimHub AUTOMATICAMENTE:
   - Cria mapeamento no Control and Events
   - Mostra botão na UI
   - Adiciona Change/Clear/Add buttons
   ↓
7. Durante corrida:
   - Botão primido → SimHub chama Action → Reply enviado
```

## 📝 Conclusão

O sistema usa um **controle WPF nativo do SimHub** que:
- ❌ NÃO precisas programar ControlPicker manualmente
- ❌ NÃO precisas mostrar popups
- ✅ Tudo é gerido automaticamente pelo controle
- ✅ Só precisas especificar ActionName

**PROBLEMA:** Não sei o nome exato do controle! 😅

Preciso de:
- `SimHub.Plugins.dll` para descompilar, OU
- Exemplo de XAML de outro plugin
