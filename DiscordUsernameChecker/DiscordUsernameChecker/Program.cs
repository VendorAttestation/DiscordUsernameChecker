using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
internal static class Program
{
    static Program()
    {
        Console.Title = $"Discord Username Checker V1.8 / https://github.com/VendorAttestation";

        appSettings = new("config.ini");
    }
    static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private static ConcurrentQueue<string> usernameQueue = new ConcurrentQueue<string>();
    private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public static Settings? appSettings;
    internal static string[] Proxies = File.ReadAllLines("proxies.txt");
	internal static int ProxyIndex = 0;
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
						CheckClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0");
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