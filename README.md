# Commit Journal

Bot pessoal em C# que transforma notas enviadas pelo Telegram em arquivos Markdown e commits no GitHub. O bot roda na AWS e responde no chat com o link do commit.

## Como usar

Envie este comando no chat privado com o bot.

```text
/commit Integrei o Telegram com a API do GitHub
```

O bot grava a nota em `notes/telegram-<id-da-atualização>.md` no repositório configurado e usa o texto como mensagem do commit. Se você enviar apenas `/commit`, ele pede que você escreva uma nota.

## Arquitetura

1. O **Telegram** envia a atualização para `POST /telegram` no **Amazon API Gateway**.
2. A **AWS Lambda** executa a API em **ASP.NET Core Minimal API (.NET 10)**. O código verifica o segredo do webhook e aceita comandos apenas no chat privado da conta autorizada.
3. A aplicação usa a **API REST do GitHub** para criar o arquivo e o commit na branch padrão do repositório de notas. Em seguida, devolve o link pelo Telegram.

O repositório do bot pode ser público. As notas podem ficar em um repositório privado separado.

## Pré-requisitos

- SDK do .NET 10.
- Um bot criado no [BotFather](https://t.me/BotFather) e o seu ID numérico do Telegram.
- Um repositório pessoal para as notas, inicializado com um README ou outro commit.
- Um token *fine-grained* do GitHub com **Contents → Read and write**, limitado ao repositório de notas.
- Conta AWS com acesso a Lambda e API Gateway para publicar o bot.

Para obter seu ID no Telegram, envie uma mensagem ao bot em um chat privado e consulte `getUpdates` antes de registrar o webhook. O valor está em `message.from.id`.

## Variáveis de ambiente

Configure estas variáveis na sessão do terminal para executar localmente. Para o deploy, use os mesmos nomes em **Configuração → Variáveis de ambiente** na Lambda. Mantenha os valores reais dos tokens e do segredo fora do repositório.

| Variável | Valor |
| --- | --- |
| `TELEGRAM_BOT_TOKEN` | Token do BotFather |
| `TELEGRAM_USER_ID` | ID numérico da conta do Telegram autorizada |
| `TELEGRAM_WEBHOOK_SECRET` | Segredo aleatório compartilhado com o webhook do Telegram |
| `GITHUB_TOKEN` | Token do GitHub com acesso de leitura e escrita aos conteúdos do repositório de notas |
| `GITHUB_OWNER` | Nome de usuário do GitHub que é dono do repositório |
| `GITHUB_REPO` | Nome do repositório de notas, por exemplo `commit-journal-notes` |
| `GITHUB_COMMIT_EMAIL` | E-mail associado à conta do GitHub ou o endereço `noreply` fornecido pelo GitHub |

No PowerShell, use `Read-Host` para preencher cada variável. Este comando configura o token do Telegram.

```powershell
$env:TELEGRAM_BOT_TOKEN = Read-Host "Token do BotFather"
```

Gere o segredo do webhook com este comando no PowerShell.

```powershell
& { $bytes = [byte[]]::new(32); $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create(); try { $rng.GetBytes($bytes); $env:TELEGRAM_WEBHOOK_SECRET = [BitConverter]::ToString($bytes).Replace('-', '') } finally { $rng.Dispose() } }
```

## Execução local

Com as variáveis configuradas, execute o projeto a partir da raiz do repositório.

```powershell
dotnet run -- --urls http://localhost:5080
```

Em outro terminal, confirme que a rota rejeita requisições sem o segredo. O resultado esperado é `401 Unauthorized`.

```powershell
curl.exe -i -X POST http://localhost:5080/telegram
```

## Publicação na AWS

1. Instale `Amazon.Lambda.Tools` com `dotnet tool install --global Amazon.Lambda.Tools` e gere o ZIP na raiz do projeto.

   ```powershell
   dotnet lambda package --configuration Release --framework net10.0 --function-architecture x86_64 --output-package "$env:TEMP\commit-journal-lambda.zip"
   ```

2. Crie a função Lambda com runtime **.NET 10**, arquitetura **x86_64** e manipulador `commit-journal`. Envie o ZIP, configure as sete variáveis de ambiente e ajuste o tempo limite para 25 segundos.
3. No API Gateway, crie uma **API HTTP** na região da Lambda. Configure **`POST /telegram`** com integração Lambda no formato de payload **2.0**. Use o estágio `$default` com implantação automática.
4. No PowerShell com as variáveis do projeto, defina `$apiUrl` com a URL de invocação da API. Cole a URL base quando o comando pedir.

   ```powershell
   $apiUrl = (Read-Host "URL de invocação do API Gateway").TrimEnd('/')
   ```

5. Registre o webhook. Use no Telegram o mesmo segredo que você configurou na Lambda.

   ```powershell
   Invoke-RestMethod -Method Post -Uri "https://api.telegram.org/bot$($env:TELEGRAM_BOT_TOKEN)/setWebhook" -ContentType "application/json" -Body (@{ url = "$apiUrl/telegram"; secret_token = $env:TELEGRAM_WEBHOOK_SECRET; allowed_updates = @("message"); max_connections = 1 } | ConvertTo-Json -Compress)
   ```

6. Com a aplicação local parada, envie um `/commit` com texto no Telegram e abra o link devolvido pelo bot para conferir o arquivo e o autor do commit.

## Contribuições no GitHub

O GitHub atribui o commit pelo e-mail do autor. Use o e-mail associado à sua conta e confira se o repositório de destino é próprio, fora de um fork. Se as notas estiverem em um repositório privado e você quiser exibir a contagem no perfil, ative **Contribution settings → Private contributions** no GitHub.