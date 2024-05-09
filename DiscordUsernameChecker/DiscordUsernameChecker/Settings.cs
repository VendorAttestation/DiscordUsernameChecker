public class Settings
{
    /* SAVED CONFIG VALUES */
    public SharpConfig.Configuration config;
    public int Threads;
    public bool Debug;
    public bool AutoClaim;
    public bool UseWebhook;
    public string Token;
    public string Password;
    public string WebHookId;
    public string WebHookToken;
    public string HCoptchaKey;
    /* END SAVED CONFIG VALUES */

    public Settings(string file)
    {
        SharpConfig.Configuration config = SharpConfig.Configuration.LoadFromFile(file);
        Threads = config["AppSettings"]["Threads"].IntValue;
        Debug = config["AppSettings"]["Debug"].BoolValue;
        HCoptchaKey = config["AppSettings"]["HCoptchaKey"].StringValue;
        AutoClaim = config["AppSettings"]["AutoClaim"].BoolValue;
        UseWebhook = config["AppSettings"]["UseWebhook"].BoolValue;
        Token = config["AutoClaimSettings"]["Token"].StringValue;
        Password = config["AutoClaimSettings"]["Password"].StringValue;
        WebHookId = config["AutoClaimSettings"]["WebHookId"].StringValue;
        WebHookToken = config["AutoClaimSettings"]["WebHookToken"].StringValue;
    }
}