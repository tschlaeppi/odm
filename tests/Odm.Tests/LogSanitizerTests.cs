using Odm.SharedKernel.Text;

namespace Odm.Tests;

public class LogSanitizerTests
{
    [Fact]
    public void Clean_removes_line_breaks_and_control_characters()
    {
        Assert.Equal("annaFAKE LOG", LogSanitizer.Clean("anna\r\nFAKE LOG"));
        Assert.Equal("ab", LogSanitizer.Clean("a\u0007b"));
        Assert.Equal("", LogSanitizer.Clean(null));
        Assert.Equal(129, LogSanitizer.Clean(new string('x', 500)).Length);
    }
}
