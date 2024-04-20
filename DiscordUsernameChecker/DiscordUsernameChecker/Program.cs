﻿using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class Program
{
    public class CheckJson
    {
        public bool taken { get; set; }
        public double retry_after { get; set; }
    }

    private static ConcurrentQueue<string> usernameQueue = new ConcurrentQueue<string>();
    private static CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
    public static Settings? appSettings;
    internal static string[] Proxies = File.ReadAllLines("proxies.txt");
    internal static int ProxyIndex = 0;
    internal static int TokenIndex = 0;
    private static object ValidLock = new object();
    private static object DebugLock = new object();
    private static string date = DateTime.Now.ToString("MM-dd-yyyy");
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
        foreach (var username in usernames)
        {
            usernameQueue.Enqueue(username);
        }
    }

    static async Task Main(string[] args)
    {
        Console.Title = $"Discord Username Checker V1.2 / https://github.com/TheVisual";

        appSettings = new("config.ini");

        if (appSettings.Threads > 500 || appSettings.Threads < 1)
        {
            AnsiConsole.Write(new Markup($"[red]You may only use between 1-500 threads.[/]"));
        }
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
    static async Task ConsumeUsernames(CancellationToken cancellationToken)
    {
        while (!usernameQueue.IsEmpty && !cancellationToken.IsCancellationRequested)
        {
            if (usernameQueue.TryDequeue(out var username))
            {
                try
                {
                    string url = "https://discord.com/api/v9/unique-username/username-attempt-unauthed";
                    using (var httpClient = new HttpClient(new HttpClientHandler { Proxy = GetProxy(), UseProxy = true }))
                    {
                        try
                        {
                        Retry:
                            httpClient.DefaultRequestHeaders.Clear();
                            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                            httpClient.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
                            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/123.0.0.0 Safari/537.36");
                            httpClient.DefaultRequestHeaders.Add("X-Super-Properties", "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiQ2hyb21lIiwiZGV2aWNlIjoiIiwic3lzdGVtX2xvY2FsZSI6ImRlLURFIiwiYnJvd3Nlcl91c2VyX2FnZW50IjoiTW96aWxsYS81LjAgKFdpbmRvd3MgTlQgMTAuMDsgV2luNjQ7IHg2NCkgQXBwbGVXZWJLaXQvNTM3LjM2IChLSFRNTCwgbGlrZSBHZWNrbykgQ2hyb21lLzEyMy4wLjAuMCBTYWZhcmkvNTM3LjM2IiwiYnJvd3Nlcl92ZXJzaW9uIjoiMTIzLjAuMC4wIiwib3NfdmVyc2lvbiI6IjEwIiwicmVmZXJyZXIiOiJodHRwczovL2Rpc2NvcmQuY29tLyIsInJlZmVycmluZ19kb21haW4iOiJkaXNjb3JkLmNvbSIsInJlZmVycmVyX2N1cnJlbnQiOiJodHRwczovL2Rpc2NvcmQuY29tLyIsInJlZmVycmluZ19kb21haW5fY3VycmVudCI6ImRpc2NvcmQuY29tIiwicmVsZWFzZV9jaGFubmVsIjoic3RhYmxlIiwiY2xpZW50X2J1aWxkX251bWJlciI6Mjg0NDIyLCJjbGllbnRfZXZlbnRfc291cmNlIjpudWxsfQ==");

                            PostJson postJson = new PostJson
                            {
                                username = username
                            };
                            StringContent content = new StringContent(JsonSerializer.Serialize(postJson), Encoding.UTF8, "application/json");


                            HttpResponseMessage response = await httpClient.PostAsync(url, content);

                            string responseBody = await response.Content.ReadAsStringAsync();
                            CheckJson jsonObject = JsonSerializer.Deserialize<CheckJson>(responseBody);

                            if (jsonObject.taken)
                            {
                                AnsiConsole.Markup($"[red]Username Taken: {username}[/]\n");
                            }
                            else if (response.StatusCode == HttpStatusCode.TooManyRequests)
                            {
                                if (appSettings.Debug)
                                {
                                    AnsiConsole.Markup($"[yellow]Ratelimited Retrying After {jsonObject.retry_after} seconds...[/]\n");
                                }
                                await Task.Delay(TimeSpan.FromSeconds(jsonObject.retry_after));
                                goto Retry;
                            }
                            if (!jsonObject.taken)
                            {
                                AnsiConsole.Markup($"[green]Username Available: {username}[/]\n");
                                lock (ValidLock)
                                {
                                    File.AppendAllText($"ValidUsernames-{date}.txt", $"{username}\n");
                                }
                            }
                        }
                        catch (HttpRequestException e)
                        {
                            if (appSettings.Debug)
                            {
                                lock (DebugLock)
                                {
                                    File.AppendAllText($"DebugLogs-{date}.txt", e.Message);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    if (appSettings.Debug)
                    {
                        lock (DebugLock)
                        {
                            File.AppendAllText($"DebugLogs-{date}.txt", e.Message);
                        }
                    }
                }
            }
        }
    }
}