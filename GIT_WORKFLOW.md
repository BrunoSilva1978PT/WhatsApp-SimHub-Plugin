# 🔄 GIT WORKFLOW - Guia de Sobrevivência

## 🎯 REGRA DE OURO

**NUNCA trabalhar direto na `main`!**

---

## 📋 WORKFLOW DIÁRIO

### 1. Começar nova funcionalidade

```bash
# Ir para main e atualizar
git checkout main
git pull origin main

# Criar branch para a funcionalidade
git checkout -b feature/nome-da-feature

# Exemplos:
git checkout -b feature/dashboard-merge
git checkout -b feature/overlay-renderer
git checkout -b fix/connection-bug
```

### 2. Trabalhar e fazer commits frequentes

```bash
# Ver o que mudou
git status

# Adicionar ficheiros
git add .

# Commit (mensagem clara!)
git commit -m "Add DashboardMerger class with V2.0 logic"

# Ou usar o script seguro
./git-safe-commit.bat
```

### 3. Quando a funcionalidade estiver pronta

```bash
# Push para GitHub
git push origin feature/dashboard-merge

# Merge na main (só quando TUDO funciona!)
git checkout main
git merge feature/dashboard-merge
git push origin main

# Apagar branch antiga (opcional)
git branch -d feature/dashboard-merge
```

---

## 🚨 SITUAÇÕES DE EMERGÊNCIA

### "Fiz asneira! Quero voltar atrás!"

#### Opção 1: Desfazer último commit (mantém alterações)
```bash
git reset --soft HEAD~1
# Ficheiros continuam alterados, commit é desfeito
```

#### Opção 2: Desfazer último commit (perde TUDO!)
```bash
git reset --hard HEAD~1
# ⚠️ CUIDADO: Perde alterações permanentemente!
```

#### Opção 3: Voltar para commit específico
```bash
# Ver histórico
git log --oneline

# Voltar para commit abc1234
git reset --hard abc1234
```

### "Mudei ficheiros mas quero descartar tudo!"

```bash
# Descartar TODAS alterações não commitadas
git reset --hard HEAD

# Descartar ficheiro específico
git checkout -- WhatsAppPlugin.cs
```

### "Commitei na main por engano!"

```bash
# Mover commit para nova branch
git branch feature/acidental    # Cria branch com o commit
git reset --hard HEAD~1          # Remove da main
git checkout feature/acidental   # Vai para a branch nova
```

---

## 🔍 COMANDOS ÚTEIS

### Ver estado atual
```bash
git status              # Ficheiros alterados
git log --oneline       # Histórico de commits
git log --oneline -10   # Últimos 10 commits
git diff                # Ver alterações não commitadas
```

### Ver branches
```bash
git branch              # Listar branches locais
git branch -a           # Listar todas (incluindo remotas)
git checkout main       # Mudar para main
```

### Comparar versões
```bash
# Diferenças entre branches
git diff main feature/dashboard-merge

# Diferenças num ficheiro específico
git diff WhatsAppPlugin.cs
```

---

## 💾 CRIAR PONTOS DE SALVAMENTO

### Antes de mudanças arriscadas

```bash
# Criar backup do estado atual
git checkout -b backup/before-risky-change

# Voltar ao trabalho
git checkout feature/dashboard-merge

# Se correr mal, podes voltar:
git checkout backup/before-risky-change
```

### Tags para versões importantes

```bash
# Marcar versão funcional
git tag v1.0-working
git push origin v1.0-working

# Voltar para essa versão
git checkout v1.0-working
```

---

## 📊 ESTRATÉGIA DE BRANCHES

```
main (produção - sempre funcional)
  │
  ├── feature/dashboard-merge
  ├── feature/overlay-renderer  
  ├── fix/connection-bug
  └── backup/working-state-2026-01-30
```

**Regras:**
- `main` - Só código que funciona 100%
- `feature/*` - Novas funcionalidades
- `fix/*` - Correções de bugs
- `backup/*` - Estados de salvamento

---

## ✅ CHECKLIST ANTES DE MERGE NA MAIN

- [ ] Código compila sem erros
- [ ] Funcionalidade testada e funcional
- [ ] Commits com mensagens claras
- [ ] Build script funciona (`build-and-deploy.bat`)
- [ ] `CONTEXT.md` atualizado

---

## 🤖 USAR COM CLAUDE

### Pedir commits ao Claude
```
"Faz commit das alterações com mensagem clara"
```

Claude vai:
1. Ver ficheiros alterados
2. Criar mensagem descritiva
3. Fazer commit

### Pedir para voltar atrás
```
"Volta ao commit anterior, mantendo alterações"
"Descarta todas as alterações não commitadas"
```

---

## 📝 MENSAGENS DE COMMIT (Boas Práticas)

### ✅ BOM
```
Add DashboardMerger class with V2.0 wrapper logic
Fix overlay rendering order issue
Update CONTEXT.md with current progress
Refactor WebSocketManager connection handling
```

### ❌ MAU
```
changes
fix
update
asdfasdf
```

---

## 🔗 COMANDOS RÁPIDOS

```bash
# Status rápido
git status -s

# Commit rápido (tudo)
git add . && git commit -m "message"

# Ver último commit
git show

# Desfazer último commit (soft)
git reset --soft HEAD~1

# Ver diferenças visuais
git diff --color

# Histórico visual
git log --graph --oneline --all
```

---

**Criado:** 2026-01-30  
**Para:** WhatsApp SimHub Plugin  
**Autor:** Claude + Bruno
