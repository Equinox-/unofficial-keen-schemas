namespace SchemaBuilder
{
    public enum Game
    {
        ME,
        SE,
    }

    public class Configuration
    {
        public string Name;

        public Game Game;

        public string GameBranch;
    }
}