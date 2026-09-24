namespace CircleToSearch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => CompositionRoot.Run(args);
}
