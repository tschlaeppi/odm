namespace Odm.Tests;

public class LogSanitizerTests
{
    [Fact]
    public void Clean_removes_line_breaks_and_control_characters()
    {
        // LogSanitizer is internal to the Identity module; reached through reflection to keep the API closed.
        var type = typeof(Odm.Modules.Identity.IdentityModule).Assembly.GetType("Odm.Modules.Identity.Infrastructure.LogSanitizer")!;
        var clean = type.GetMethod("Clean")!;

        Assert.Equal("annaFAKE LOG", clean.Invoke(null, ["anna\r\nFAKE LOG"]));
        Assert.Equal("ab", clean.Invoke(null, ["a\u0007b"]));
        Assert.Equal("", clean.Invoke(null, [null]));
        Assert.Equal(129, ((string)clean.Invoke(null, [new string('x', 500)])!).Length);
    }
}
