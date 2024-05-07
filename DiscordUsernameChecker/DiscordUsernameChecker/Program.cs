using Discord;
using Discord.Rest;
using HtmlAgilityPack;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
internal static class Program
{
    static Program()
    {
        Console.Title = $"Discord Username Checker V1.6 / https://github.com/TheVisual";

        appSettings = new("config.ini");
    }
    static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private static ConcurrentQueue<string> usernameQueue = new ConcurrentQueue<string>();
    private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public static Settings? appSettings;
    internal static string[] Proxies = File.ReadAllLines("proxies.txt");
    internal static int ProxyIndex = 0;
    private static readonly object claimLock = new object();
    private static readonly object ValidLock = new object();
    private static readonly object DebugLock = new object();
    private static string UserAgent = "";
    private static string date = DateTime.Now.ToString("MM-dd-yyyy");
    public class CheckJson
    {
        public bool taken { get; set; }
        public double retry_after { get; set; }
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

    public static async Task PostToDiscordWebhook(string usernameClaimed, int statusCode, CancellationToken cancellationToken)
    {
        // Ensure the DiscordWebhookClient is properly configured with your webhook URL
        var webhookId = ulong.Parse(appSettings.WebHookId); // Your webhook ID
        var webhookToken = appSettings.WebHookToken; // Your webhook token
        var webhookClient = new Discord.Webhook.DiscordWebhookClient(webhookId, webhookToken);

        // Create an embed with Discord.Net
        var embed = new EmbedBuilder()
            .WithTitle("DiscordUsernameChecker V1.6")
            .WithDescription("@everyone\nClaimed a username!")
            .WithColor(new Discord.Color(5814783)) // Discord.Net uses a specific Color structure.
            .AddField("USERNAME", usernameClaimed, true)
            .AddField("STATUS", statusCode.ToString(), true)
            .AddField("SOURCE", "Webhook Notification")
            .WithFooter(footer => footer.Text = "DiscordUsernameChecker V1.6")
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

    public static async Task<bool> TryClaimUsername(string username, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var claimJson = new ClaimJson { username = username, password = appSettings.Password };

        using (var claimClient = new HttpClient(new HttpClientHandler { Proxy = GetProxy(), UseProxy = true }))
        {
            claimClient.DefaultRequestHeaders.Clear();
            claimClient.DefaultRequestHeaders.Add("Accept", "application/json");
            claimClient.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
            claimClient.DefaultRequestHeaders.Add("Authorization", appSettings.Token);
            claimClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0");
            claimClient.DefaultRequestHeaders.Add("X-Super-Properties", "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiQ2hyb21lIiwiZGV2aWNlIjoiIiwic3lzdGVtX2xvY2FsZSI6ImVuLVVTIiwiYnJvd3Nlcl91c2VyX2FnZW50IjoiTW96aWxsYS81LjAgKFdpbmRvd3MgTlQgMTAuMDsgV2luNjQ7IHg2NCkgQXBwbGVXZWJLaXQvNTM3LjM2IChLSFRNTCwgbGlrZSBHZWNrbykgQ2hyb21lLzEyNC4wLjAuMCBTYWZhcmkvNTM3LjM2IEVkZy8xMjQuMC4wLjAiLCJicm93c2VyX3ZlcnNpb24iOiIxMjQuMC4wLjAiLCJvc192ZXJzaW9uIjoiMTAiLCJyZWZlcnJlciI6Imh0dHBzOi8vd3d3LmJpbmcuY29tLyIsInJlZmVycmluZ19kb21haW4iOiJ3d3cuYmluZy5jb20iLCJzZWFyY2hfZW5naW5lIjoiYmluZyIsInJlZmVycmVyX2N1cnJlbnQiOiIiLCJyZWZlcnJpbmdfZG9tYWluX2N1cnJlbnQiOiIiLCJyZWxlYXNlX2NoYW5uZWwiOiJzdGFibGUiLCJjbGllbnRfYnVpbGRfbnVtYmVyIjoyOTA5OTgsImNsaWVudF9ldmVudF9zb3VyY2UiOm51bGwsImRlc2lnbl9pZCI6MH0=");

            var content = new StringContent(JsonSerializer.Serialize(claimJson), Encoding.UTF8, "application/json");
            var response = await claimClient.PatchAsync("https://discord.com/api/v9/users/@me", content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                AnsiConsole.Markup($"[green]Username Claimed: {username}[/]\n");
                if (appSettings.UseWebhook)
                {
                    await PostToDiscordWebhook(username, (int)response.StatusCode, cancellationToken);
                }
                return true;
            }
            else
            {
                AnsiConsole.Markup($"[red]Failed to claim username {username}. Status Code: {response.StatusCode}[/]\n");
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