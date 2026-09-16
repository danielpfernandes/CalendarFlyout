# CalendarFlyout — WPF / .NET 8

*[Read in English](README.en.md)*

Utilitário de bandeja para Windows 11 que mostra, num painel compacto perto do ícone da bandeja, a agenda do Google Calendar de hoje aos próximos 3 dias — somente leitura, das agendas que você escolher.

## Recursos

- Roda oculto na bandeja; não abre janela nem aparece na barra de tarefas.
- Login com sua conta Google via OAuth (o app nunca vê nem grava sua senha).
- Em **Configurações**, escolha quais agendas da sua conta aparecem no painel.
- Atualiza sozinho a cada minuto enquanto o painel está aberto.
- Início automático opcional com o Windows (por usuário, sem precisar de administrador).
- Acompanha o tema claro/escuro e o alto contraste do Windows.

## Pré-requisitos

- Windows 11.
- **.NET 8 SDK**, para compilar — ou o **.NET Desktop Runtime 8**, para rodar um build já publicado sem o SDK.

## 1. Configurar o acesso ao Google Calendar

1. Crie ou escolha um projeto no [Google Cloud Console](https://console.cloud.google.com/) e ative a **Google Calendar API**.
2. Configure o consentimento OAuth (Google Auth Platform → Branding, Audience, Data Access). Em modo de teste, adicione sua conta em **Test users**.
3. Em Credenciais, crie um **OAuth client ID** do tipo **Desktop app** e baixe o JSON.
4. Salve o arquivo como `client_secret.json` ao lado de `CalendarFlyout.csproj` (ou do executável, numa build publicada). Ele não é versionado nem distribuído com o projeto.

O app usa somente `client_id`/`client_secret` desse arquivo e solicita os escopos:

```text
https://www.googleapis.com/auth/calendar.events.readonly
https://www.googleapis.com/auth/calendar.calendarlist.readonly
```

Em projetos OAuth no modo **Testing** do Google, o refresh token costuma expirar em ~7 dias; quando isso acontecer, use **Esquecer login** e conecte novamente.

## 2. Compilar e executar

No PowerShell, dentro da pasta do projeto:

```powershell
dotnet restore .\CalendarFlyout.csproj
dotnet build .\CalendarFlyout.csproj -c Release
Start-Process .\bin\Release\net8.0-windows\CalendarFlyout.exe
```

Para distribuir sem exigir o .NET Desktop Runtime instalado, publique uma versão self-contained:

```powershell
dotnet publish .\CalendarFlyout.csproj -c Release -r win-x64 --self-contained true -o .\publish
```

Troque `win-x64` por `win-arm64` para Windows ARM64. Distribua a pasta inteira; `client_secret.json` precisa ficar ao lado do executável.

### Release automatizada

O workflow `.github/workflows/release.yml` publica automaticamente uma build self-contained `win-x64` e anexa o zip a uma GitHub Release. Dispara ao enviar uma tag `vX.Y.Z` (`git tag v1.0.0 && git push origin v1.0.0`) ou manualmente pela aba **Actions** do repositório.

## 3. Usar

1. Ao abrir, o app fica totalmente oculto — só o ícone aparece na bandeja.
2. Clique esquerdo no ícone (ou **Abrir agenda** no menu de clique direito) abre o painel.
3. Na primeira vez, clique **Conectar Google**, autorize no navegador e reabra o painel pela bandeja.
4. Em **Configurações**, escolha quais agendas exibir e, se quiser, ative **Iniciar com o Windows**.
5. **Atualizar** busca os eventos de novo; o painel também atualiza sozinho a cada minuto enquanto está aberto.
6. **Esquecer login** remove os tokens salvos localmente (não revoga o acesso no Google — para isso, use as [conexões da Conta Google](https://myaccount.google.com/connections)).
7. Clique fora, Esc ou o botão de fechar oculta o painel sem encerrar o app. **Sair**, no menu da bandeja, encerra o processo.

## Segurança e privacidade

Os tokens OAuth ficam criptografados em disco com o Windows DPAPI, vinculados ao seu usuário Windows, em `%LOCALAPPDATA%\CalendarFlyout\Tokens`. O app não registra tokens nem conteúdo de eventos em log, e não envia dados a nenhum servidor além da API do Google Calendar.

## Referências oficiais

- [OAuth na biblioteca .NET do Google](https://developers.google.com/api-client-library/dotnet/guide/aaa_oauth)
- [OAuth para aplicativos desktop, loopback e expiração](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google Calendar: Events.list](https://developers.google.com/workspace/calendar/api/v3/reference/events/list)
