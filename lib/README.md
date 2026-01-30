# ⚠️ PASTA DEPRECADA

Esta pasta **não é mais necessária**!

## ✅ Nova Abordagem

O plugin agora referencia DLLs diretamente da instalação do SimHub.

**Vantagens:**
- ✅ Sem conflitos de versão
- ✅ Updates automáticos
- ✅ Não precisa copiar DLLs manualmente

## 🔧 Como Compilar

Ver `BUILD.md` na raiz do projeto para instruções completas.

**Resumo:**
1. Abrir projeto no Visual Studio
2. Build → Rebuild Solution
3. Pronto!

O Visual Studio vai buscar as DLLs diretamente de:
```
C:\Program Files (x86)\SimHub\
```

## 📝 Nota Técnica

O `.csproj` agora usa:
```xml
<SimHubPath>C:\Program Files (x86)\SimHub\</SimHubPath>
<Reference Include="SimHub.Plugins">
  <HintPath>$(SimHubPath)SimHub.Plugins.dll</HintPath>
</Reference>
```

Se o SimHub estiver noutra pasta, basta editar `<SimHubPath>` no `.csproj`.

