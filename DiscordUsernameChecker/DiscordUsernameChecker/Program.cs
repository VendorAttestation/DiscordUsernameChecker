using Discord;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
internal static class Program
{
    static Program()
    {
        Console.Title = $"Discord Username Checker V1.7 / https://github.com/TheVisual";

        appSettings = new("config.ini");
    }
    static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private static ConcurrentQueue<string> usernameQueue = new ConcurrentQueue<string>();
    private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public static Settings? appSettings;
    internal static string[] Proxies = File.ReadAllLines("proxies.txt");
	internal static string[] CaptchaProxies = File.ReadAllLines("captcha_proxies.txt");
	internal static int ProxyIndex = 0;
	internal static int CaptchaProxyIndex = 0;
	private static readonly object ValidLock = new object();
    private static readonly object DebugLock = new object();
    private static string date = DateTime.Now.ToString("MM-dd-yyyy");
    public class CheckJson
    {
        public bool taken { get; set; }
        public double retry_after { get; set; }
    }
	public class Data
	{
		public string sitekey { get; set; }
		public string url { get; set; }
		public string proxy { get; set; }
		public string rqdata { get; set; }
	}

	public class createTask
	{
		public string task_type { get; set; }
		public string api_key { get; set; }
		public Data data { get; set; }
	}

	public class createTaskResponse
	{
		public bool error { get; set; }
		public string task_id { get; set; }
	}

	public class getTaskData
	{
		public string task_id { get; set; }
		public string api_key { get; set; }
	}

	public class getTaskDataResponse
	{
		public bool error { get; set; }
		public getTaskDataTask task { get; set; }
	}

	public class getTaskDataTask
	{
		public string captcha_key { get; set; }
		public bool refunded { get; set; }
		public string state { get; set; }
	}

	public class CaptchaStuff
	{
		public List<string> captcha_key { get; set; }
		public string captcha_sitekey { get; set; }
		public string captcha_service { get; set; }
		public string captcha_rqdata { get; set; }
		public string captcha_rqtoken { get; set; }
	}

	static WebProxy GetProxy()
    {
        try
        {
            string proxyAddress;

            lock (Proxies)
            {
                Random random = new Random();

                if (ProxyIndex >= Proxies.Length)
                {
                    ProxyIndex = 0;
                }

                proxyAddress = Proxies[ProxyIndex++];
            }

            // Create a WebProxy object from the proxy address
            if (proxyAddress.Split(':').Length == 4)
            {
                var proxyHost = proxyAddress.Split(':')[0];
                int proxyPort = Int32.Parse(proxyAddress.Split(':')[1]);
                var username = proxyAddress.Split(':')[2];
                var password = proxyAddress.Split(':')[3];
                ICredentials credentials = new NetworkCredential(username, password);
                var proxyUri = new Uri($"http://{proxyHost}:{proxyPort}");
                return new WebProxy(proxyUri, false, null, credentials);
            }
            else if (proxyAddress.Split(':').Length == 2)
            {
                var proxyHost = proxyAddress.Split(':')[0];
                int proxyPort = Int32.Parse(proxyAddress.Split(':')[1]);
                return new WebProxy(proxyHost, proxyPort);
            }
            else
            {
                throw new ArgumentException("Invalid proxy format.");
            }
        }
        catch (Exception)
        {
            throw new ArgumentException("Invalid proxy.");
        }
    }

	static string GetCaptchaProxy()
	{
		try
		{
			string proxyAddress;

			lock (CaptchaProxies)
			{
				Random random = new Random();

				if (CaptchaProxyIndex >= CaptchaProxies.Length)
				{
					CaptchaProxyIndex = 0;
				}

				proxyAddress = CaptchaProxies[CaptchaProxyIndex++];
			}
			return proxyAddress;
		}
		catch (Exception)
		{
			throw new ArgumentException("Invalid proxy.");
		}
	}

	static async Task LoadUsernamesAsync(string filePath)
    {
        var usernames = await File.ReadAllLinesAsync(filePath);
        foreach (var username in usernames.Distinct())
        {
            usernameQueue.Enqueue(username);
        }
    }

    static async Task Main()
    {
        await LoadUsernamesAsync("usernames.txt");
        var tasks = new Task[appSettings.Threads];
        for (int i = 0; i < appSettings.Threads; i++)
        {
            tasks[i] = Task.Run(() => ConsumeUsernames(cancellationTokenSource.Token));
        }

        // Wait for all tasks to complete
        await Task.WhenAll(tasks);

        AnsiConsole.Write(new Markup($"[yellow]Checker Completed List\n[/]"));
        Console.ReadLine();
    }
    public class PostJson
    {
        public string username { get; set; }
    }
    public class ClaimJson
    {
        public string username { get; set; }
        public string password { get; set; }
    }

    public static async Task PostToDiscordWebhook(string usernameClaimed, int statusCode)
    {
        // Ensure the DiscordWebhookClient is properly configured with your webhook URL
        var webhookId = ulong.Parse(appSettings.WebHookId); // Your webhook ID
        var webhookToken = appSettings.WebHookToken; // Your webhook token
        var webhookClient = new Discord.Webhook.DiscordWebhookClient(webhookId, webhookToken);

        // Create an embed with Discord.Net
        var embed = new EmbedBuilder()
            .WithTitle("DiscordUsernameChecker V1.7")
            .WithDescription("@everyone\nClaimed a username!")
            .WithColor(new Discord.Color(5814783)) // Discord.Net uses a specific Color structure.
            .AddField("USERNAME", usernameClaimed, true)
            .AddField("STATUS", statusCode.ToString(), true)
            .AddField("SOURCE", "Webhook Notification")
            .WithFooter(footer => footer.Text = "DiscordUsernameChecker V1.7")
            .Build();

        try
        {
            // Send the webhook message with the embed
            await webhookClient.SendMessageAsync("@everyone", false, new[] { embed }, "DiscordUsernameChecker V1.6");
        }
        catch (Exception ex)
        {
            if (appSettings.Debug)
            {
                lock (DebugLock)
                {
                    File.AppendAllText($"DebugLogs-{DateTime.Now:yyyyMMdd}.txt", $"{ex.Message}\n");
                }
            }
        }
    }
	public static async Task<string> TryClaimNoCaptcha(string username, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
			return "";

		using (var claimClient = new HttpClient(new HttpClientHandler { Proxy = GetProxy(), UseProxy = true }))
		{
			claimClient.DefaultRequestHeaders.Clear();
			claimClient.DefaultRequestHeaders.Add("Accept", "application/json");
			claimClient.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
			claimClient.DefaultRequestHeaders.Add("Authorization", appSettings.Token);
			claimClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9146 Chrome/120.0.6099.291 Electron/28.2.10 Safari/537.36");
			claimClient.DefaultRequestHeaders.Add("X-Super-Properties", "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiRGlzY29yZCBDbGllbnQiLCJyZWxlYXNlX2NoYW5uZWwiOiJzdGFibGUiLCJjbGllbnRfdmVyc2lvbiI6IjEuMC45MTQ2Iiwib3NfdmVyc2lvbiI6IjEwLjAuMjI2MzEiLCJvc19hcmNoIjoieDY0IiwiYXBwX2FyY2giOiJ4NjQiLCJzeXN0ZW1fbG9jYWxlIjoiZW4tVVMiLCJicm93c2VyX3VzZXJfYWdlbnQiOiJNb3ppbGxhLzUuMCAoV2luZG93cyBOVCAxMC4wOyBXaW42NDsgeDY0KSBBcHBsZVdlYktpdC81MzcuMzYgKEtIVE1MLCBsaWtlIEdlY2tvKSBkaXNjb3JkLzEuMC45MTQ2IENocm9tZS8xMjAuMC42MDk5LjI5MSBFbGVjdHJvbi8yOC4yLjEwIFNhZmFyaS81MzcuMzYiLCJicm93c2VyX3ZlcnNpb24iOiIyOC4yLjEwIiwiY2xpZW50X2J1aWxkX251bWJlciI6MjkxNTA3LCJuYXRpdmVfYnVpbGRfbnVtYmVyIjo0NzU0MywiY2xpZW50X2V2ZW50X3NvdXJjZSI6bnVsbCwiZGVzaWduX2lkIjowfQ==");
			var claimJson = new ClaimJson { username = username, password = appSettings.Password };
			var content = new StringContent(JsonSerializer.Serialize(claimJson), Encoding.UTF8, "application/json");
			HttpResponseMessage response = await claimClient.PatchAsync("https://discord.com/api/v9/users/@me", content, cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				var data = JsonSerializer.Deserialize<CaptchaStuff>(await response.Content.ReadAsStringAsync());
				return data.captcha_rqdata + ":" + data.captcha_sitekey;
			}
			else
			{
				AnsiConsole.Markup($"[green]Failed to claim username {username}. Status Code: {response.StatusCode}[/]\n");
				if (appSettings.UseWebhook)
				{
					await PostToDiscordWebhook(username, (int)response.StatusCode);
				}
				return "";
			}
		}
	}
	public static async Task<bool> TryClaimUsername(string username, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
			return false;

		using (var captchaClient = new HttpClient())
		{
			captchaClient.DefaultRequestHeaders.Clear();
			captchaClient.DefaultRequestHeaders.Add("Accept", "application/json");
			captchaClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0");

			string captchaProxy = GetCaptchaProxy();
			var rq_data = await TryClaimNoCaptcha(username, cancellationToken);
			if (string.IsNullOrEmpty(rq_data))
			{
				return true;
			}
			HttpResponseMessage responseCaptcha = await captchaClient.PostAsync($"https://api.hcoptcha.com/api/createTask", new StringContent(JsonSerializer.Serialize(new createTask
			{
				task_type = "hcaptchaEnterprise",
				api_key = appSettings.HCoptchaKey,
				data = new Data
				{
					proxy = GetCaptchaProxy(),
					sitekey = rq_data.Split(':')[1],
					url = "https://discord.com/api/v9/users/@me",
					rqdata = rq_data.Split(':')[0]
				},
			}), Encoding.UTF8, "application/json"));

			if (!responseCaptcha.IsSuccessStatusCode)
			{
				AnsiConsole.Markup("[red]Initial captcha request failed.[/]\n");
				return false;
			}
			int maxAttempts = 5;
			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				if (cancellationToken.IsCancellationRequested)
					return false;

				createTaskResponse responseCaptchaContent = JsonSerializer.Deserialize<createTaskResponse>(await responseCaptcha.Content.ReadAsStringAsync());
				Console.WriteLine(await responseCaptcha.Content.ReadAsStringAsync());
				string captchaId = responseCaptchaContent.task_id;
				HttpResponseMessage responseCaptchaSolve = await captchaClient.PostAsync(
					"https://api.hcoptcha.com/api/getTaskData",
					new StringContent(
						JsonSerializer.Serialize(new getTaskData
						{
							api_key = appSettings.HCoptchaKey,
							task_id = captchaId
						}),
						Encoding.UTF8,
						"application/json"
					)
				);
				var responseCaptchaSolveContent = JsonSerializer.Deserialize<getTaskDataResponse>(await responseCaptchaSolve.Content.ReadAsStringAsync());

				while (responseCaptchaSolveContent.task.state == "processing")
                {

                    AnsiConsole.Markup("[yellow]" + responseCaptchaSolveContent + "[/]\n");
                    await Task.Delay(100); // Wait before retrying
                    responseCaptchaSolve = await captchaClient.PostAsync(
					    "https://api.hcoptcha.com/api/getTaskData",
					    new StringContent(
							JsonSerializer.Serialize(new getTaskData
						    {
							    api_key = appSettings.HCoptchaKey,
							    task_id = captchaId
						    }),
						    Encoding.UTF8,
						    "application/json"
					    )
				    );
					responseCaptchaSolveContent = JsonSerializer.Deserialize<getTaskDataResponse>(await responseCaptchaSolve.Content.ReadAsStringAsync());
					if (responseCaptchaSolveContent.task.state == "error")
                    {
						break;
                    }
					captchaId = responseCaptchaContent.task_id;
				}
				if (responseCaptchaSolveContent.task.state.Equals("completed"))
				{           
					return await ClaimUsernameWithCaptcha(username, responseCaptchaSolveContent.task.captcha_key, cancellationToken);
				}
				else
				{
					AnsiConsole.Markup($"[yellow]Captcha attempt {attempt} failed. Response: {responseCaptchaSolveContent}[/]\n");
					captchaProxy = GetCaptchaProxy();
					rq_data = await TryClaimNoCaptcha(username, cancellationToken);
					if (string.IsNullOrEmpty(rq_data))
					{
						return true;
					}
					responseCaptcha = await captchaClient.PostAsync($"https://api.hcoptcha.com/api/createTask", new StringContent(JsonSerializer.Serialize(new createTask
					{
						task_type = "hcaptchaEnterprise",
						api_key = appSettings.HCoptchaKey,
						data = new Data
						{
							proxy = GetCaptchaProxy(),
							sitekey = rq_data.Split(':')[1],
							url = "https://discord.com/api/v9/users/@me",
							rqdata = rq_data.Split(':')[0]
						}
					}), Encoding.UTF8, "application/json"));
				}
			}

			AnsiConsole.Markup("[red]All captcha attempts failed.[/]\n");
			return false;
		}
	}

	private static async Task<bool> ClaimUsernameWithCaptcha(string username, string captchaSolve, CancellationToken cancellationToken)
	{
		using (var claimClient = new HttpClient(new HttpClientHandler { Proxy = GetProxy(), UseProxy = true }))
		{
			claimClient.DefaultRequestHeaders.Clear();
			claimClient.DefaultRequestHeaders.Add("Accept", "application/json");
			claimClient.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
			claimClient.DefaultRequestHeaders.Add("X-Captcha-Key", captchaSolve);
			claimClient.DefaultRequestHeaders.Add("Authorization", appSettings.Token);
			claimClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) discord/1.0.9146 Chrome/120.0.6099.291 Electron/28.2.10 Safari/537.36");
			claimClient.DefaultRequestHeaders.Add("X-Super-Properties", "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiRGlzY29yZCBDbGllbnQiLCJyZWxlYXNlX2NoYW5uZWwiOiJzdGFibGUiLCJjbGllbnRfdmVyc2lvbiI6IjEuMC45MTQ2Iiwib3NfdmVyc2lvbiI6IjEwLjAuMjI2MzEiLCJvc19hcmNoIjoieDY0IiwiYXBwX2FyY2giOiJ4NjQiLCJzeXN0ZW1fbG9jYWxlIjoiZW4tVVMiLCJicm93c2VyX3VzZXJfYWdlbnQiOiJNb3ppbGxhLzUuMCAoV2luZG93cyBOVCAxMC4wOyBXaW42NDsgeDY0KSBBcHBsZVdlYktpdC81MzcuMzYgKEtIVE1MLCBsaWtlIEdlY2tvKSBkaXNjb3JkLzEuMC45MTQ2IENocm9tZS8xMjAuMC42MDk5LjI5MSBFbGVjdHJvbi8yOC4yLjEwIFNhZmFyaS81MzcuMzYiLCJicm93c2VyX3ZlcnNpb24iOiIyOC4yLjEwIiwiY2xpZW50X2J1aWxkX251bWJlciI6MjkxNTA3LCJuYXRpdmVfYnVpbGRfbnVtYmVyIjo0NzU0MywiY2xpZW50X2V2ZW50X3NvdXJjZSI6bnVsbCwiZGVzaWduX2lkIjowfQ==");
			var claimJson = new ClaimJson { username = username, password = appSettings.Password };
			var content = new StringContent(JsonSerializer.Serialize(claimJson), Encoding.UTF8, "application/json");
			HttpResponseMessage response = await claimClient.PatchAsync("https://discord.com/api/v9/users/@me", content, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				AnsiConsole.Markup($"[green]Username Claimed: {username}[/]\n");
				return true;
			}
			else
			{
				AnsiConsole.Markup($"[red]Failed to claim username {username}. Status Code: {response.StatusCode}, {captchaSolve}[/]\n");
				return false;
			}
		}
	}


	static async Task ConsumeUsernames(CancellationToken cancellationToken)
    {
        while (!usernameQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
        {
            if (usernameQueue.TryDequeue(out var username))
            {
                try
                {
                    using (var CheckClient = new HttpClient(new HttpClientHandler { Proxy = GetProxy(), UseProxy = true }))
                    {
                        HttpResponseMessage response = await CheckClient.PostAsync("https://discord.com/api/v9/unique-username/username-attempt-unauthed", new StringContent(JsonSerializer.Serialize(new PostJson { username = username }), Encoding.UTF8, "application/json"));
                        CheckClient.DefaultRequestHeaders.Clear();
                        CheckClient.DefaultRequestHeaders.Add("Accept", "application/json");
                        CheckClient.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
						CheckClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0");
                        string responseBody = await response.Content.ReadAsStringAsync();
                        CheckJson jsonObject = JsonSerializer.Deserialize<CheckJson>(responseBody);

                        if (jsonObject.taken)
                        {
                            AnsiConsole.Markup($"[red]Username Taken: {username}[/]\n");
                        }
                        else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            usernameQueue.Enqueue(username);
                        }
                        else if (!jsonObject.taken && appSettings.AutoClaim)
                        {
                            await semaphore.WaitAsync(cancellationToken);
                            try
                            {
                                if (await TryClaimUsername(username, cancellationToken))
                                {
                                    cancellationTokenSource.Cancel();
                                }
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }
                        else if (!jsonObject.taken)
                        {
                            AnsiConsole.Markup($"[green]Username Available: {username}[/]\n");
                            lock (ValidLock)
                            {
                                File.AppendAllText($"ValidUsernames-{date}.txt", $"{username}\n");
                            }
                        }
                    }
                }
                catch (HttpRequestException e)
                {
                    usernameQueue.Enqueue(username);
                    if (appSettings.Debug)
                    {
                        lock (DebugLock)
                        {
                            File.AppendAllText($"DebugLogs-{date}.txt", $"{e.Message.ToString()}\n");
                        }
                    }
                }
                catch (Exception e)
                {
                    usernameQueue.Enqueue(username);
                    if (appSettings.Debug)
                    {
                        lock (DebugLock)
                        {
                            File.AppendAllText($"DebugLogs-{date}.txt", $"{e.Message.ToString()}\n");
                        }
                    }
                }
            }
        }
    }
}