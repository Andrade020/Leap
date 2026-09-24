# Leap

Navegação estilo NeoVim para o Windows: um atalho global escurece a tela e mostra
seus diretórios favoritos como cartões com teclas — no espírito do **which-key.nvim**
(painel de acordes de tecla) e do **harpoon** (slots fixos para os lugares que você
mais visita).

## Uso

1. Rode `Iniciar-Leap.cmd` (o app fica no ícone da bandeja do sistema).
   Para abrir junto com o Windows: bandeja → "Iniciar com o Windows".
2. Pressione **Alt+Espaço** em qualquer lugar do Windows.
3. Na tela escura:
   - **tecla do cartão** (ou clique) → abre o diretório no Explorer
   - **Ctrl + tecla** → abre no terminal (Windows Terminal)
   - **Alt + tecla** → abre no editor (VS Code)
   - **Shift + tecla** → copia o caminho
   - **Backspace** → volta um nível · **Esc** → fecha
4. Cartões roxos são grupos (submenus): a tecla entra no grupo, estilo which-key.
5. **`+`** fixa a pasta da janela do Explorer que estava em primeiro plano num
   slot livre (estilo harpoon): navegue até a pasta no Explorer, `Alt+Espaço`, `+`.

## Navegador embutido

Apertar a tecla de uma pasta agora **navega dentro do Leap** (em vez de abrir o
Explorer): digite para filtrar — a busca é **recursiva** (acha arquivos em
subpastas, mostrando o caminho completo) —, `Enter` entra na subpasta ou abre o
arquivo, `Backspace` sobe um nível. Com a caixa vazia, a primeira linha
(`⏎ abrir esta pasta no Windows Explorer`) abre a pasta atual no Explorer.
`Ctrl+Enter`/`Alt+Enter`/`Shift+Enter` continuam valendo no item selecionado, e
`Ctrl+P` fixa a pasta atual como novo atalho. Para voltar ao comportamento
antigo (tecla abre o Explorer direto), ponha `"folderAction": "open"` no config.

## Adicionando atalhos

- **Sem editar nada**: `+` fixa a pasta do Explorer em primeiro plano; `Ctrl+P`
  fixa a pasta atual do navegador embutido; `m`+letra cria uma marca; e
  **`x`+tecla remove** um atalho ou marca do painel.
- **Pelo config** (bandeja → "Editar configuração"): adicione um objeto na
  seção desejada. Exemplos:

```json
{ "key": "m", "label": "Meu Projeto", "path": "C:/repos/meu-projeto", "action": "editor" }
{ "key": "s", "label": "Spotify", "path": "spotify:" }
{ "key": "y", "label": "YouTube", "path": "https://youtube.com" }
```

`action: "editor"` = tecla abre direto no editor (nvim para pastas). Sem
`action`, pasta navega no Leap e arquivo/app/URL abre no padrão do sistema.

## Pastas virtuais (grupos)

Um item com `children` é uma "pasta que só existe no Leap": agrupa itens de
qualquer lugar do disco num painel próprio. Ex.: `r` → "Repos principais" reúne
repositórios espalhados; a tecla do filho abre/navega normalmente. Grupos podem
conter grupos.

```json
{ "key": "r", "label": "Repos principais", "children": [
  { "key": "1", "label": "Explorer", "path": "C:/.../Repositorios/Explorer" },
  { "key": "2", "label": "Outro", "path": "D:/outro-lugar/projeto" }
] }
```

## Modo comando ( : ) — aliases, macros e prompt livre

`:` abre a paleta de comandos: filtra fuzzy, `Enter` executa num Windows
Terminal (que fica aberto mostrando o resultado).

- **Contexto**: apertando `:` dentro do navegador de pastas, os comandos rodam
  **naquela pasta** (ex.: entrou num repo → `gs` → git status dele). Comandos
  sem `dir` no config são contextuais.
- **Linha livre**: se o que você digitar não for um alias, a última opção é
  `⚡ executar: <o que você digitou>` — `:code .` abre o VS Code na pasta,
  `:git init` inicia um repo, qualquer PowerShell vale.

Defina aliases no config, campo `commands`:

```json
{ "name": "gs", "label": "git status — MeuRepo", "run": "git status", "dir": "C:/repos/meu" }
{ "name": "build", "label": "compilar o Leap", "run": "dotnet build", "dir": "C:/.../Explorer", "keepOpen": true }
```

`keepOpen: false` roda em segundo plano, sem janela. Vêm de fábrica: `gs`,
`gl`, `gp` (git status/log/pull do primeiro repo git encontrado) e `ip`.

## Ajuda ( ? )

`?` no painel abre a referência completa de todas as teclas e modos do Leap.

## Truques de bruxo

- **`b` — trocar de janela (buffers)**: lista todas as janelas abertas com busca
  fuzzy; digite 2–3 letras do título ou do programa e `Enter` foca a janela.
  Substitui o alt-tab.
- **`m` + letra — marcar (vim marks)**: com uma janela do Explorer em primeiro
  plano, marca aquela pasta com a letra escolhida. Ficam salvas no config
  (campo `marks`).
- **`'` + letra — pular para a marca**: `'` abre o painel de marcas estilo
  which-key; a letra abre a pasta (modificadores Ctrl/Alt/Shift funcionam).
- **`;` — repetir**: refaz a última ação (mesmo destino, mesmo modificador).

## Modo busca (telescope/fzf)

Pressione **`/`** no painel para entrar no modo busca:

- Digite qualquer trecho do nome — o match é **fuzzy**: `rep exp` acha `Repositorios\Explorer`.
- **↑/↓** ou **Ctrl+J / Ctrl+K** navegam nos resultados.
- **Enter** abre; **Ctrl+Enter** terminal, **Alt+Enter** editor, **Shift+Enter** copia.
- **Esc** (ou Backspace com a caixa vazia) volta ao painel de acordes.

Com a **caixa vazia**, o painel mostra os lugares que você mais usa
(frecência, estilo zoxide: frequência + recência).

Fontes de resultados, combinadas e ranqueadas (uso frequente dá bônus):

1. **Seus favoritos** do config (sempre com prioridade);
2. **Índice do PC inteiro**: arquivos e pastas de todos os drives fixos,
   construído em background e salvo comprimido em `%APPDATA%\Leap\index.txt.gz`
   — na próxima inicialização a busca já nasce pronta. Controles no config:
   `indexRoots` (vazio = todos os drives), `indexMaxDepth`, `indexMaxEntries`,
   `indexRefreshHours` e `indexExclude` (pastas de sistema e lixo de build são
   ignoradas por padrão). Bandeja → "Reconstruir índice de busca" força refresh;
3. **Everything (voidtools)** — opcional: se o `es.exe` estiver instalado e o
   Everything rodando, também entra na mistura.
   Instale com: `winget install voidtools.Everything voidtools.Everything.Cli`.
   Nenhuma configuração é necessária — o Leap detecta sozinho.

## Layout em zonas

A raiz do painel é organizada em **seções** de cima para baixo (config → `sections`):
**Pastas** no alto, **Código — direto no editor** (tecla única já abre no VS Code,
sem modificador: `"action": "editor"`), **Apps** (exe, alias ou URL no `path`)
e, embaixo, a **escola de vim**: uma leva nova de vim motions a cada abertura
do painel — clique numa dica para abrir a referência completa em português.
As teclas são globais entre as seções; submenus continuam em painel centralizado.

## Editores

Dois editores, cada um no seu papel (config → `editor` / `projectEditor`):

- **Arquivos → Leap Edit** (`"editor": "leap"`): editor nativo embutido
  (AvalonEdit) com coloração para Python, HTML, JS, C#, XML etc. Como o Leap já
  está residente, abre em milissegundos. `Ctrl+S` salva, `Esc` fecha.
- **Pastas/projetos → Neovim** no Windows Terminal (instalado em
  `%LOCALAPPDATA%\Programs\nvim`, config inicial amigável em
  `%LOCALAPPDATA%\nvim\init.lua`). É o modo treino: os motions que o painel
  ensina são usados aqui. Para trocar por outro editor, ajuste
  `projectEditor`/`projectEditorArgs`.

## Configuração

Arquivo: `%APPDATA%\Leap\config.json` (menu da bandeja → "Editar configuração").
É recarregado toda vez que o painel abre — salve e teste na hora, sem reiniciar.

```json
{
  "hotkey": "Alt+Space",
  "terminal": "wt.exe",
  "terminalArgs": "-d \"{0}\"",
  "editor": "code",
  "editorArgs": "\"{0}\"",
  "items": [
    { "key": "1", "label": "Repositórios", "path": "C:\\Users\\voce\\Desktop\\Repositorios" },
    {
      "key": "p", "label": "Projetos",
      "children": [
        { "key": "e", "label": "Explorer", "path": "C:\\...\\Repositorios\\Explorer" }
      ]
    }
  ]
}
```

Notas:

- `path` também aceita arquivos, executáveis e URLs (`https://...`) — abre no app padrão.
- Em JSON, barras invertidas precisam ser duplicadas (`C:\\pasta`) — ou use `/`.
- `hotkey` aceita combinações como `Ctrl+Alt+Space`, `Win+K`, `F13`.

## Compilar

```
dotnet build -c Release
```

(SDK .NET 10 instalado em `%LOCALAPPDATA%\Microsoft\dotnet`; use `Iniciar-Leap.cmd`
para rodar, pois ele define `DOTNET_ROOT`.)

## Compilar / publicar

```
dotnet build -c Release                                        # build de desenvolvimento
dotnet publish -c Release -r win-x64 --self-contained -o bin\publish   # build final
```

O `Iniciar-Leap.cmd` usa o build de `bin\publish` (self-contained — não depende
do .NET instalado; é esse que o "Iniciar com o Windows" aponta).

## Roadmap (ideias da comunidade Vim)

- ~~Busca fuzzy (telescope/fzf)~~ — feito (`/` no painel).
- ~~Frecência (zoxide)~~ — feito (caixa de busca vazia mostra os mais usados;
  uso frequente dá bônus no ranking).
- ~~"Arpoar" a pasta do Explorer atual~~ — feito (tecla `+`).
- ~~Iniciar com o Windows~~ — feito (menu da bandeja).
- ~~Índice de PC inteiro persistido~~ — feito (`index.txt.gz`).
- Ícone próprio, animação de fade-in, suporte multi-monitor, realce dos
  caracteres casados nos resultados da busca.
