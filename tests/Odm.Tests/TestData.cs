namespace Odm.Tests;

internal static class TestData
{
    /// <summary>Erwartete Listen als Array schreiben; Sammlungsausdrücke sind bei Assert.Equal mehrdeutig.</summary>
    public static string[] Arr(params string[] items) => items;
}
