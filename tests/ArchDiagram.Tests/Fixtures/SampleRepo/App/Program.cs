// Sample console app used by ArchDiagram tests.
using Sample.Lib;

namespace Sample.App;

public static class Program
{
    public static void Main()
    {
        var ordersDb = "Server=db1;Database=orders;User Id=app;Password=secret;";
        var doubled = MathHelpers.Double(21);
        System.Console.WriteLine(doubled + ordersDb.Length);
    }
}
