namespace Odm.Tests;

internal static class TestData
{
    /// <summary>Write expected lists as an array; collection expressions are ambiguous with Assert.Equal.</summary>
    public static string[] Arr(params string[] items) => items;
}
