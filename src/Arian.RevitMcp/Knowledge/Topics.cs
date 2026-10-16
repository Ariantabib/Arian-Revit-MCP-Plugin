namespace Arian.RevitMcp.Knowledge;

public sealed class Topic
{
    public string Title, Description, Text;
}

public static partial class Topics
{
    public static readonly Dictionary<string, Topic> All = new(StringComparer.OrdinalIgnoreCase);

    static void Add(string key, string title, string desc, string text) =>
        All[key] = new Topic { Title = title, Description = desc, Text = text };

    static Topics()
    {
        Register();
    }

    static partial void RegisterMore();
    static void Register() { RegisterMore(); }
}
