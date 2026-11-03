using Portal.Core.Ssh;

namespace Portal.Tests.Ssh;

public class SshConfigTests
{
    private const string Home = @"C:\Users\tester";

    [Fact]
    public void Load_of_a_missing_file_is_an_empty_config()
    {
        using var temp = new TempDirectory();

        var config = SshConfig.Load(temp.Combine("config"), temp.Path);

        var settings = config.Resolve("web");
        Assert.Empty(config.HostAliases);
        Assert.Equal("web", settings.HostName);
        Assert.Null(settings.User);
        Assert.Empty(settings.IdentityFiles);
    }

    [Fact]
    public void Load_reads_the_file()
    {
        using var temp = new TempDirectory();
        var path = temp.Write("config", "Host web\n  HostName 10.0.0.5\n");

        var config = SshConfig.Load(path, temp.Path);

        Assert.Equal("10.0.0.5", config.Resolve("web").HostName);
    }

    [Fact]
    public void Resolves_the_basic_keywords_for_a_host_block()
    {
        var config = SshConfig.Parse(
            """
            Host web
                HostName web.example.org
                User deploy
                Port 2222
                IdentityFile ~/.ssh/id_web
            """,
            Home);

        var settings = config.Resolve("web");

        Assert.Equal("web", settings.Alias);
        Assert.Equal("web.example.org", settings.HostName);
        Assert.Equal("deploy", settings.User);
        Assert.Equal(2222, settings.Port);
        Assert.Equal([Home + "/.ssh/id_web"], settings.IdentityFiles);
    }

    [Fact]
    public void An_unmatched_host_keeps_its_own_name_and_no_settings()
    {
        var config = SshConfig.Parse("Host web\n  User deploy\n", Home);

        var settings = config.Resolve("db");

        Assert.Equal("db", settings.HostName);
        Assert.Null(settings.User);
        Assert.Null(settings.Port);
        Assert.Empty(settings.IdentityFiles);
    }

    [Fact]
    public void Keywords_ignore_case_and_accept_equals_signs()
    {
        var config = SshConfig.Parse(
            """
            HOST=web
              hostname=web.example.org
              USER = deploy
              port= 2200
            """,
            Home);

        var settings = config.Resolve("web");

        Assert.Equal("web.example.org", settings.HostName);
        Assert.Equal("deploy", settings.User);
        Assert.Equal(2200, settings.Port);
    }

    [Fact]
    public void Quoted_values_keep_their_spaces()
    {
        var config = SshConfig.Parse(
            """
            Host web
              IdentityFile "C:\Keys\my key"
              User "deploy"
            """,
            Home);

        var settings = config.Resolve("web");

        Assert.Equal([@"C:\Keys\my key"], settings.IdentityFiles);
        Assert.Equal("deploy", settings.User);
    }

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var config = SshConfig.Parse(
            """
            # a comment line

            Host web # trailing comment
              # indented comment
              User deploy   # who we log in as
            """,
            Home);

        Assert.Equal(["web"], config.HostAliases);
        Assert.Equal("deploy", config.Resolve("web").User);
    }

    [Fact]
    public void The_first_value_of_a_keyword_wins()
    {
        var config = SshConfig.Parse(
            """
            Host web
              User first
              Port 2201
            Host web
              User second
              Port 2202
            Host *
              User fallback
              HostName %h.example.org
            """,
            Home);

        var web = config.Resolve("web");
        var other = config.Resolve("db");

        Assert.Equal("first", web.User);
        Assert.Equal(2201, web.Port);
        Assert.Equal("web.example.org", web.HostName);
        Assert.Equal("fallback", other.User);
        Assert.Equal("db.example.org", other.HostName);
    }

    [Fact]
    public void Settings_before_the_first_host_apply_to_every_host()
    {
        var config = SshConfig.Parse("User everyone\nHost web\n  User deploy\n", Home);

        Assert.Equal("everyone", config.Resolve("web").User);
    }

    [Fact]
    public void Identity_files_accumulate_across_blocks_without_duplicates()
    {
        var config = SshConfig.Parse(
            """
            Host web
              IdentityFile ~/.ssh/id_web
            Host *
              IdentityFile ~/.ssh/id_ed25519
              IdentityFile ~/.ssh/id_web
            """,
            Home);

        Assert.Equal([Home + "/.ssh/id_web", Home + "/.ssh/id_ed25519"], config.Resolve("web").IdentityFiles);
    }

    [Fact]
    public void Identity_files_expand_home_and_host_tokens()
    {
        var config = SshConfig.Parse(
            """
            Host web
              HostName web.example.org
              IdentityFile %d/.ssh/%h_key
              IdentityFile ~
              IdentityFile C:/keys/100%%_%x
            """,
            Home);

        Assert.Equal(
            [Home + "/.ssh/web.example.org_key", Home, "C:/keys/100%_%x"],
            config.Resolve("web").IdentityFiles);
    }

    [Theory]
    [InlineData("web1", true)]
    [InlineData("WEB2", true)]
    [InlineData("web", false)]
    [InlineData("web10", false)]
    [InlineData("db.example.org", true)]
    [InlineData("example.org", false)]
    public void Host_patterns_support_star_and_question_mark(string host, bool matches)
    {
        var config = SshConfig.Parse("Host web? *.example.org\n  User matched\n", Home);

        Assert.Equal(matches ? "matched" : null, config.Resolve(host).User);
    }

    [Fact]
    public void A_negated_pattern_excludes_a_host_the_wildcard_would_match()
    {
        var config = SshConfig.Parse("Host * !bastion\n  User inner\n", Home);

        Assert.Equal("inner", config.Resolve("web").User);
        Assert.Null(config.Resolve("bastion").User);
    }

    [Fact]
    public void Host_aliases_list_concrete_names_once_in_file_order()
    {
        var config = SshConfig.Parse(
            """
            Host web db
            Host *.example.org web? !bastion
            Host cache web
            Host *
            """,
            Home);

        Assert.Equal(["web", "db", "cache"], config.HostAliases);
    }

    [Fact]
    public void Match_blocks_are_skipped_until_the_next_host()
    {
        var config = SshConfig.Parse(
            """
            Match host web
              User from-match
            Host web
              Port 2222
            """,
            Home);

        var settings = config.Resolve("web");

        Assert.Null(settings.User);
        Assert.Equal(2222, settings.Port);
    }

    [Fact]
    public void Invalid_ports_are_ignored()
    {
        var config = SshConfig.Parse("Host web\n  Port ssh\n  Port 70000\n  Port 22\n", Home);

        Assert.Equal(22, config.Resolve("web").Port);
    }

    [Fact]
    public void An_unterminated_quote_drops_only_that_line()
    {
        var config = SshConfig.Parse("Host web\n  User \"broken\n  Port 2222\n", Home);

        var settings = config.Resolve("web");

        Assert.Null(settings.User);
        Assert.Equal(2222, settings.Port);
    }

    [Fact]
    public void Include_resolves_relative_paths_against_the_ssh_folder()
    {
        using var temp = new TempDirectory();
        temp.Write(Path.Combine(".ssh", "extra.conf"), "Host db\n  HostName db.internal\n");

        var config = SshConfig.Parse("Include extra.conf\nHost web\n  User deploy\n", temp.Path);

        Assert.Equal(["db", "web"], config.HostAliases);
        Assert.Equal("db.internal", config.Resolve("db").HostName);
        Assert.Equal("deploy", config.Resolve("web").User);
    }

    [Fact]
    public void Include_accepts_absolute_paths_and_home_paths()
    {
        using var temp = new TempDirectory();
        var absolute = temp.Write(Path.Combine("elsewhere", "a.conf"), "Host a\n  User alpha\n");
        temp.Write(Path.Combine("other", "b.conf"), "Host b\n  User beta\n");

        var config = SshConfig.Parse($"Include \"{absolute}\" ~/other/b.conf\n", temp.Path);

        Assert.Equal("alpha", config.Resolve("a").User);
        Assert.Equal("beta", config.Resolve("b").User);
    }

    [Fact]
    public void Include_expands_globs_in_sorted_order()
    {
        using var temp = new TempDirectory();
        temp.Write(Path.Combine(".ssh", "config.d", "20-late.conf"), "Host *\n  User late\nHost second\n");
        temp.Write(Path.Combine(".ssh", "config.d", "10-early.conf"), "Host *\n  User early\nHost first\n");
        temp.Write(Path.Combine(".ssh", "config.d", "notes.txt"), "Host ignored\n");

        var config = SshConfig.Parse("Include config.d/*.conf\n", temp.Path);

        Assert.Equal(["first", "second"], config.HostAliases);
        Assert.Equal("early", config.Resolve("anything").User);
    }

    [Fact]
    public void Include_expands_globs_in_folder_names()
    {
        using var temp = new TempDirectory();
        temp.Write(Path.Combine(".ssh", "teams", "ops", "hosts"), "Host ops-box\n");
        temp.Write(Path.Combine(".ssh", "teams", "dev", "hosts"), "Host dev-box\n");

        var config = SshConfig.Parse("Include teams/*/hosts\n", temp.Path);

        Assert.Equal(["dev-box", "ops-box"], config.HostAliases);
    }

    [Fact]
    public void Include_inside_a_host_block_applies_only_to_that_host()
    {
        using var temp = new TempDirectory();
        temp.Write(Path.Combine(".ssh", "web.conf"), "User deploy\n");

        var config = SshConfig.Parse("Host web\n  Include web.conf\nHost db\n  Port 5432\n", temp.Path);

        Assert.Equal("deploy", config.Resolve("web").User);
        Assert.Null(config.Resolve("db").User);
        Assert.Equal(5432, config.Resolve("db").Port);
    }

    [Fact]
    public void A_host_block_in_an_included_file_does_not_leak_into_the_including_file()
    {
        using var temp = new TempDirectory();
        temp.Write(Path.Combine(".ssh", "inc.conf"), "Host other\n  User other-user\n");

        var config = SshConfig.Parse("Include inc.conf\nUser global\n", temp.Path);

        Assert.Equal("global", config.Resolve("web").User);
        Assert.Equal("other-user", config.Resolve("other").User);
    }

    [Fact]
    public void Missing_includes_and_include_loops_are_harmless()
    {
        using var temp = new TempDirectory();
        temp.Write(Path.Combine(".ssh", "loop.conf"), "Include loop.conf\nHost looped\n");

        var config = SshConfig.Parse("Include nowhere.conf loop.conf\nHost web\n", temp.Path);

        Assert.Equal(["looped", "web"], config.HostAliases);
    }
}
