using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

var telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
var telegramUserId = Environment.GetEnvironmentVariable("TELEGRAM_USER_ID");
var webhookSecret = Environment.GetEnvironmentVariable("TELEGRAM_WEBHOOK_SECRET");

if (string.IsNullOrWhiteSpace(telegramToken) ||
	!long.TryParse(telegramUserId, out var allowedUserId) ||
	string.IsNullOrWhiteSpace(webhookSecret))
{
	Console.WriteLine(
		"Configure TELEGRAM_BOT_TOKEN, TELEGRAM_USER_ID e TELEGRAM_WEBHOOK_SECRET.");
	return;
}

var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
var githubOwner = Environment.GetEnvironmentVariable("GITHUB_OWNER");
var githubRepo = Environment.GetEnvironmentVariable("GITHUB_REPO");
var githubCommitEmail = Environment.GetEnvironmentVariable("GITHUB_COMMIT_EMAIL");

if (string.IsNullOrWhiteSpace(githubToken) ||
	string.IsNullOrWhiteSpace(githubOwner) ||
	string.IsNullOrWhiteSpace(githubRepo) ||
	string.IsNullOrWhiteSpace(githubCommitEmail))
{
	Console.WriteLine(
		"Configure GITHUB_TOKEN, GITHUB_OWNER, GITHUB_REPO e GITHUB_COMMIT_EMAIL.");
	return;
}

using var github = new HttpClient
{
	BaseAddress = new Uri("https://api.github.com/")
};

github.DefaultRequestHeaders.UserAgent.ParseAdd("CommitJournal/1.0");
github.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
github.DefaultRequestHeaders.Authorization =
	new AuthenticationHeaderValue("Bearer", githubToken);
github.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");

var bot = new TelegramBotClient(telegramToken);
var expectedSecret = Encoding.UTF8.GetBytes(webhookSecret);
var repositoryPath = $"repos/{githubOwner}/{githubRepo}";

const string command = "/commit";
const string secretHeader = "X-Telegram-Bot-Api-Secret-Token";

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

var app = builder.Build();
app.MapPost("/telegram", HandleTelegramAsync);
app.Run();

async Task<IResult> HandleTelegramAsync(
	HttpRequest request,
	CancellationToken cancellationToken)
{
	if (!request.Headers.TryGetValue(secretHeader, out var receivedSecret) ||
		!CryptographicOperations.FixedTimeEquals(
			Encoding.UTF8.GetBytes(receivedSecret.ToString()), expectedSecret))
	{
		return Results.Unauthorized();
	}

	Update? update;

	try
	{
		update = await JsonSerializer.DeserializeAsync<Update>(
			request.Body, JsonBotAPI.Options, cancellationToken);
	}
	catch (JsonException)
	{
		return Results.BadRequest();
	}

	if (update is null)
	{
		return Results.BadRequest();
	}

	var message = update.Message;

	if (message is null ||
		message.Chat.Type != ChatType.Private ||
		message.From?.Id != allowedUserId)
	{
		return Results.Ok();
	}

	var text = message.Text;

	if (text is null ||
		(text != command && !text.StartsWith(command + " ", StringComparison.Ordinal)))
	{
		return Results.Ok();
	}

	var note = text.Substring(command.Length).Trim();

	var reply = note.Length == 0
		? "Envie uma nota depois de /commit."
		: await CreateCommitReplyAsync(update.Id, note, cancellationToken);

	await bot.SendMessage(message.Chat.Id, reply, cancellationToken: cancellationToken);
	return Results.Ok();
}

async Task<string> CreateCommitReplyAsync(
	int updateId,
	string note,
	CancellationToken cancellationToken)
{
	var filePath = $"notes/telegram-{updateId}.md";
	var endpoint = $"{repositoryPath}/contents/{filePath}";

	var payload = new
	{
		message = note,
		content = Convert.ToBase64String(Encoding.UTF8.GetBytes(note + "\n")),
		author = new
		{
			name = githubOwner,
			email = githubCommitEmail
		}
	};

	try
	{
		using var response = await github.PutAsJsonAsync(
			endpoint, payload, cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);

		if (response.IsSuccessStatusCode)
		{
			using var json = JsonDocument.Parse(body);
			var commitUrl = json.RootElement.GetProperty("commit")
				.GetProperty("html_url").GetString();

			return $"Commit criado!\n{commitUrl}";
		}

		if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
		{
			var encodedPath = Uri.EscapeDataString(filePath);
			var commits = await github.GetFromJsonAsync<JsonElement>(
				$"{repositoryPath}/commits?path={encodedPath}&per_page=1",
				cancellationToken);

			if (commits.GetArrayLength() > 0)
			{
				var commitUrl = commits[0].GetProperty("html_url").GetString();
				return $"Commit já registrado!\n{commitUrl}";
			}
		}

		Console.Error.WriteLine($"GitHub HTTP {(int)response.StatusCode}\n{body}");
		return $"O GitHub retornou HTTP {(int)response.StatusCode}. Confira os logs.";
	}
	catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
	{
		Console.Error.WriteLine(error.Message);
		return "Não consegui confirmar o commit. Confira o repositório antes de tentar de novo.";
	}
}