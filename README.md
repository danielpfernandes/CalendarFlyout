# CalendarFlyout — WPF / .NET 8

Utilitário de bandeja para Windows 11. Abre uma agenda compacta no canto inferior direito do monitor sob o ponteiro, respeitando a área útil e a escala DPI. Consulta a **agenda principal** da conta Google em modo somente leitura.

## Estrutura

```text
CalendarFlyout/
├── CalendarFlyout.csproj
├── App.xaml
├── App.xaml.cs                    # Inicialização oculta, NotifyIcon e encerramento
├── MainWindow.xaml               # Interface WPF
├── MainWindow.xaml.cs            # Flyout, light dismiss e operações assíncronas
├── GlobalUsings.cs
├── app.manifest                  # Per-monitor DPI v2
├── Assets/calendar.ico           # Ícone personalizado incluído
├── Native/WindowEffects.cs       # DWM, Acrylic, cantos, posição e ativação
├── Services/
│   ├── CalendarClient.cs         # OAuth, refresh automático e API paginada
│   ├── EncryptedDataStore.cs     # IDataStore com Windows DPAPI
│   ├── AgendaProjection.cs       # Intervalos, fuso, all-day e agrupamento
│   └── ThemeService.cs           # Tema do Windows e alto contraste
├── Tests/
│   ├── CalendarFlyout.Tests.csproj
│   └── Program.cs                # Verificações executáveis sem framework extra
├── .gitignore
└── README.md
```

## 1. Preparar o Google Cloud

1. Crie ou escolha um projeto no [Google Cloud Console](https://console.cloud.google.com/).
2. Ative a **Google Calendar API** nesse projeto.
3. Configure o Google Auth Platform: Branding, Audience e Data Access. Se o aplicativo estiver em teste, adicione a conta que vai usar em **Test users**.
4. Em Clients/Credenciais, crie um **OAuth client ID** com tipo **Desktop app / Aplicativo para computador**.
5. Baixe o JSON e salve como `client_secret.json` na pasta que contém `CalendarFlyout.csproj`.

O código valida a seção `installed` desse arquivo e usa somente `client_id` e `client_secret`. O cliente OAuth usa o navegador padrão e um callback loopback local gerenciado pelo SDK. Não use credenciais Web ou conta de serviço.

Escopos solicitados:

```text
https://www.googleapis.com/auth/calendar.events.readonly
https://www.googleapis.com/auth/calendar.calendarlist.readonly
```

A credencial real não acompanha este projeto. O arquivo é ignorado pelo Git e copiado para a saída na compilação/publicação quando está presente. É possível compilar sem ele; o painel informa como configurá-lo ao conectar.

## 2. Compilar e executar

Requisitos: Windows 11 e SDK .NET 8 ou superior; Visual Studio com desenvolvimento desktop .NET também pode abrir diretamente o `.csproj`.

No PowerShell, dentro da pasta do projeto:

```powershell
dotnet restore .\CalendarFlyout.csproj
dotnet build .\CalendarFlyout.csproj -c Release
Start-Process .\bin\Release\net8.0-windows\CalendarFlyout.exe
```

Para execução dependente de framework, instale o **.NET Desktop Runtime 8**. Para distribuir sem exigir esse runtime, publique para a arquitetura desejada:

```powershell
dotnet publish .\CalendarFlyout.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

Para Windows ARM64, troque `win-x64` por `win-arm64`. Distribua toda a pasta publicada; `client_secret.json` deve ficar ao lado do executável. Não há instalação de serviço, agendamento ou execução automática no login do Windows.

## 3. Usar

- A execução começa totalmente oculta, apenas com ícone na bandeja. O Windows pode colocá-lo no menu de ícones ocultos; fixe-o na área de notificação se desejar.
- Clique esquerdo no ícone: abre o painel. Também há **Abrir agenda** no menu de clique direito.
- No primeiro uso, clique **Conectar Google** e autorize no navegador. Reabra o painel pela bandeja após concluir. O OAuth continua em segundo plano quando o painel perde foco.
- Clique fora, pressione Esc ou use o botão de fechar: a janela se oculta e o processo continua ativo.
- **Atualizar** busca os eventos novamente. A abertura do painel também atualiza, assim como o temporizador de um minuto enquanto ele está visível.
- **Esquecer login** remove os tokens locais e os eventos exibidos. Não revoga o consentimento no Google nem encerra a sessão do navegador. Para revogar, use as [conexões da Conta Google](https://myaccount.google.com/connections).
- Clique direito na bandeja → **Sair** encerra o processo, remove o ícone e libera os recursos.

O painel não usa WebView, não abre uma janela convencional na inicialização e não aparece na barra de tarefas ou no Alt+Tab. Abrir uma segunda instância na mesma sessão do Windows encerra a nova instância.

## Datas e atualização

- Janela temporal: da meia-noite local de hoje até a meia-noite local quatro dias depois, com fim exclusivo.
- Recorrências são expandidas por `SingleEvents`; todas as páginas são lidas.
- Horários com offset são convertidos para o fuso local do Windows. O início e o fim reais são apresentados; eventos que atravessam a meia-noite aparecem em cada dia abrangido, com as datas nos horários.
- Eventos de dia inteiro usam as datas civis originais e o fim exclusivo da API. Não recebem uma conversão artificial de fuso.
- A agenda lista os quatro dias, inclusive dias sem eventos. Eventos cancelados são omitidos.
- Em falhas transitórias, o último resultado pode continuar visível com uma mensagem de falha; não há cache de eventos em disco.

## Segurança e OAuth

O `EncryptedDataStore` implementa `IDataStore`. Tanto o access token quanto o refresh token são serializados e criptografados com `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)` **antes** de gravar em disco:

```text
%LOCALAPPDATA%\CalendarFlyout\Tokens\<hash>.bin
```

As gravações usam um temporário já criptografado e substituição no mesmo diretório. A biblioteca oficial renova access tokens usando o refresh token e persiste o resultado pelo mesmo armazenamento. DPAPI vincula a proteção à identidade Windows; programas executando como esse mesmo usuário ainda podem acessar seus dados. O aplicativo não registra tokens nem conteúdos de eventos em logs.

Nenhum aplicativo pode garantir login permanente: tokens podem ser revogados ou expirar. Em projetos OAuth externos em modo **Testing**, refresh tokens para este escopo normalmente expiram em sete dias. Para distribuição, configure a publicação/verificação que se aplique ao projeto. Quando a autorização for recusada, use **Esquecer login** e conecte novamente.

## Aparência e comportamento nativo

`ShowInTaskbar=False`, `WindowStyle=None` e `WS_EX_TOOLWINDOW` mantêm a janela como utilitário. O evento WPF `Deactivated` chama `Hide()`, inclusive durante o OAuth. Não há polling global do mouse nem ganchos de teclado.

O DWM arredonda os cantos. `DWMWA_SYSTEMBACKDROP_TYPE=DWMSBT_TRANSIENTWINDOW` solicita **Desktop Acrylic** em Windows 11 22H2 / build 22621 ou posterior. A janela mantém `AllowsTransparency=False` e estende o frame para permitir composição nativa. O conteúdo tem uma camada translúcida para legibilidade. Em builds anteriores há fundo sólido; o Windows também pode reduzir o efeito conforme acessibilidade, transparência ou desempenho.

O tema acompanha `AppsUseLightTheme` e mudanças de preferências do Windows. Alto contraste usa cores de sistema e desliga o backdrop. Não altera preferências do Windows.

## Validação

Validação realizada na entrega: compilação Release com **zero erros e zero avisos**; as **11 verificações de agenda/datas passaram**. A execução dos testes DPAPI foi bloqueada pelo contexto restrito do ambiente de validação, que retornou erro de perfil Windows não carregado. Esses testes estão incluídos para execução em uma sessão Windows normal. Login real, aparência e interações da bandeja precisam da validação manual abaixo; não foram declarados como testados.

Execute as verificações de datas e armazenamento em Windows:

```powershell
dotnet run --project .\Tests\CalendarFlyout.Tests.csproj -c Release
```

O programa testa os quatro dias, fim exclusivo all-day, meia-noite, fuso, horário de verão, evento cancelado, dia vazio, título ausente e persistência/atualização/exclusão DPAPI. Ele usa somente tokens fictícios em uma pasta temporária.

Validação manual da integração, que exige sua própria credencial e conta:

1. Iniciar e confirmar ausência de janela e botão na barra de tarefas.
2. Abrir pela bandeja; clicar fora, pressionar Esc e fechar com Alt+F4; o processo deve permanecer ativo.
3. Conectar no navegador, reabrir e conferir agenda; reiniciar o aplicativo e confirmar reaproveitamento do login.
4. Criar no Google eventos all-day, recorrentes e atravessando meia-noite; comparar os quatro dias.
5. Alterar tema claro/escuro; conferir outro monitor com escala diferente e a posição acima da barra.
6. Desconectar a rede, atualizar e conferir a mensagem; testar Esquecer login e Sair.

## Referências oficiais

- [OAuth na biblioteca .NET do Google](https://developers.google.com/api-client-library/dotnet/guide/aaa_oauth)
- [OAuth para aplicativos desktop, loopback e expiração](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google Calendar: Events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list)
- [Pacote Google.Apis.Calendar.v3](https://www.nuget.org/packages/Google.Apis.Calendar.v3/1.75.0.4206)
- [DWM_SYSTEMBACKDROP_TYPE e requisitos](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)
