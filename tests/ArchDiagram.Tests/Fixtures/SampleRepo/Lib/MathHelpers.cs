namespace Sample.Lib;

// Example connection string in a comment (must NOT be detected as a real database):
// "Server=commentbox;Database=commentcatalog;User Id=x;Password=y;"

/// <summary>Tiny math helpers for the fixture.</summary>
public static class MathHelpers
{
    /// <summary>Doubles a number.</summary>
    public static int Double(int value) => Triple(value) - value;

    public static int Triple(int value) => value * 3;
}
