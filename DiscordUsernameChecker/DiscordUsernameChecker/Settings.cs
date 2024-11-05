public class Settings
{
    /* SAVED CONFIG VALUES */
    public SharpConfig.Configuration config;
    public int Threads;
    public bool Debug;
    /* END SAVED CONFIG VALUES */

    public Settings(string file)
    {
        SharpConfig.Configuration config = SharpConfig.Configuration.LoadFromFile(file);
        Threads = config["AppSettings"]["Threads"].IntValue;
        Debug = config["AppSettings"]["Debug"].BoolValue;
    }
}