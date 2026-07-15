namespace Sample.Lib;

/// <summary>Tiny math helpers for the fixture.</summary>
public static class MathHelpers
{
    /// <summary>Doubles a number.</summary>
    public static int Double(int value) => Triple(value) - value;

    public static int Triple(int value) => value * 3;
}
