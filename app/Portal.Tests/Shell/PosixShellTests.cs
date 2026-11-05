using Portal.Core.Shell;

namespace Portal.Tests.Shell;

public class PosixShellTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("", "''")]
    [InlineData("my file.txt", "'my file.txt'")]
    [InlineData("it's", @"'it'\''s'")]
    [InlineData("''", @"''\'''\'''")]
    [InlineData("$HOME", "'$HOME'")]
    [InlineData("`whoami`", "'`whoami`'")]
    [InlineData("$(rm -rf /)", "'$(rm -rf /)'")]
    [InlineData("line1\nline2", "'line1\nline2'")]
    [InlineData("-rf", "'-rf'")]
    [InlineData("back\\slash \"double\"", "'back\\slash \"double\"'")]
    public void Quote_wraps_the_argument_so_the_shell_reads_it_literally(string argument, string expected)
    {
        Assert.Equal(expected, PosixShell.Quote(argument));
    }

    [Fact]
    public void Join_quotes_each_argument_and_separates_with_spaces()
    {
        var command = PosixShell.Join(["mv", "--", "/srv/it's here", "/srv/$dest"]);

        Assert.Equal(@"'mv' '--' '/srv/it'\''s here' '/srv/$dest'", command);
    }

    [Fact]
    public void Join_of_nothing_is_empty()
    {
        Assert.Equal(string.Empty, PosixShell.Join([]));
    }
}
