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
        Threads = config["App"]["Threads"].IntValue;
        Debug = config["App"]["Debug"].BoolValue;
    }
}