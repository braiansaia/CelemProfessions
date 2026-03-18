# CelemTemplate

Template base para criar plugins C# de V Rising usando ScarletCore.

Este projeto existe para ser duplicado e servir de ponto de partida para outros plugins. Ele ja deixa pronto o bootstrap padrao de `BasePlugin`, o registro de comandos e os pontos opcionais mais comuns que aparecem na maioria dos plugins.

## Objetivo

Usar este repositório como base para novos plugins, evitando recomeçar do zero e mantendo o mesmo padrao estrutural entre projetos.

## Estrutura esperada

- `Plugin.cs`
  Ponto de entrada do plugin.
- `Commands/`
  Interface de comandos.
- `Service/`
  Regra de negocio.
- `Patches/`
  Captura de eventos do jogo. Deve continuar fino.
- `Events/`
  Contratos entre sistemas.
- `Models/`
  Modelos persistidos ou compartilhados.

As pastas podem ficar vazias no template. Adicione somente o que o plugin realmente precisar.

## O que o template ja faz

O arquivo `Plugin.cs` ja deixa pronto:

- inicializacao do plugin com `BasePlugin`
- `Instance`
- `LogInstance`
- `CommandHandler.RegisterAll()`
- `CommandHandler.UnregisterAssembly()`

## O que esta comentado no `Plugin.cs`

Algumas partes nao sao necessarias em todos os plugins, mas sao comuns o suficiente para ficarem preparadas no template.

### Harmony

Descomente quando o plugin tiver classes em `Patches/` ou qualquer patch manual.

```csharp
// static Harmony _harmony;
// public static Harmony Harmony => _harmony;
```

E no `Load()`:

```csharp
// _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
// _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
```

E no `Unload()`:

```csharp
// _harmony?.UnpatchSelf();
```

### Settings e Database

Descomente quando o plugin precisar persistir configuracoes ou dados com ScarletCore.

```csharp
// public static Settings Settings { get; private set; }
// public static Database Database { get; private set; }
```

E no `Load()`:

```csharp
// Settings = new Settings(MyPluginInfo.PLUGIN_GUID, Instance);
// Database = new Database(MyPluginInfo.PLUGIN_GUID);
```

## Como usar este template

1. Duplique este projeto para uma nova pasta.
2. Renomeie a solution, o `.csproj`, o namespace e o `manifest.json` para o nome do novo plugin.
3. Ajuste `MyPluginInfo` gerado pelo build a partir dos metadados do projeto.
4. Adicione apenas os arquivos necessarios para o plugin novo.
5. Se precisar de eventos do jogo, use `Patches/` finos e mova a logica para `Service/`.
6. Se ScarletCore ja tiver uma funcionalidade equivalente, reutilize em vez de duplicar codigo.

## Padrao recomendado

- `Commands` chama `Service`
- `Patches` captura e encaminha
- `Service` concentra regra de negocio
- `Events` expoe contratos entre sistemas
- `Models` guarda estruturas compartilhadas ou persistidas

## O que evitar

- logica pesada dentro de patch
- helpers sem uso real
- duplicar utilitarios que podem ficar no ScarletCore
- polling desnecessario
- scans repetidos por tick sem necessidade
- criar arquitetura nova se o padrao do projeto ja resolve

## Build

```bash
dotnet build CelemTemplate.sln -c Release
```

## Validacao minima apos criar um plugin novo

- compila sem erro
- registra comandos corretamente
- inicializa apenas o que realmente usa
- nao deixa codigo legado comentado sem necessidade
- nao cria dependencia paralela ao ScarletCore

## Autor

- SirSaia
