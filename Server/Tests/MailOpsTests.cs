using AstralOath.Server.Entry;
using AstralOath.Server.Mail;
using Xunit;

public sealed class MailOpsTests
{
    private const string A = "00112233445566778899aabbccddeeff";
    private const string B = "11112233445566778899aabbccddeeff";

    [Theory]
    [InlineData("preview", "--realm", "undying-memory", "--deny-file", "deny.txt", "--template", "mail.json")]
    [InlineData("preview", "--realm", "undying-memory", "--all-in-realm", "--to", A, "--template", "mail.json")]
    [InlineData("preview", "--realm", "undying-memory", "--allow-file", "allow.txt", "--to", A, "--template", "mail.json")]
    [InlineData("preview", "--realm", "undying-memory", "--to", "", "--template", "mail.json")]
    [InlineData("preview", "--realm", "undying-memory", "--to", "not-a-player", "--template", "mail.json")]
    [InlineData("preview", "--realm", "undying-memory", "--all-in-realm", "--template", "mail.json", "--typo", "yes")]
    [InlineData("confirm", "--preview-id", A, "--all-in-realm")]
    [InlineData("status", "--batch-id", "../bad")]
    public void RefusesAmbiguousOrUnscopedCommands(params string[] arguments)
    {
        var error = Assert.Throws<EntryProblem>(() => MailCli.Parse(arguments));
        Assert.Equal("invalid_mail_cli_arguments", error.Code);
    }

    [Fact]
    public void ExplicitPlayersKeepDuplicatesForPreviewStatistics()
    {
        var options = MailCli.Parse(["mail", "preview", "--realm", "undying-memory", "--to", A + "," + B + "," + A,
            "--template", "mail.json", "--deny-file", "deny.txt"]);
        Assert.Equal("preview", options.Action);
        Assert.Equal("ids", options.Scope);
        Assert.Equal(new[] { A, B, A }, options.PlayerIds);
        Assert.Equal("deny.txt", options.DenyFile);
    }

    [Theory]
    [InlineData("confirm", "--preview-id")]
    [InlineData("status", "--batch-id")]
    [InlineData("retry", "--batch-id")]
    public void ExistingPreviewAndBatchAreAddressedByStableIds(string action, string key)
    {
        var options = MailCli.Parse([action, key, A.ToUpperInvariant()]);
        Assert.Equal(action, options.Action);
        Assert.Equal(A, action == "confirm" ? options.PreviewId : options.BatchId);
    }

    [Fact]
    public void ScopeMustBeExplicitEvenWhenRealmIsKnown()
    {
        Assert.Equal("allInRealm", MailCli.Parse(["preview", "--realm", "undying-memory", "--all-in-realm", "--template", "mail.json"]).Scope);
        Assert.Equal("whitelist", MailCli.Parse(["preview", "--realm", "undying-memory", "--allow-file", "list.txt", "--template", "mail.json"]).Scope);
    }

    [Fact]
    public async Task MissingListFailsBeforeConnectingAndDoesNotRevealItsName()
    {
        var error = await Assert.ThrowsAsync<EntryProblem>(() => MailCli.ReadIds("/missing/sensitive-whitelist.txt", CancellationToken.None));
        Assert.Equal("mail_list_unreadable", error.Code);
        Assert.DoesNotContain("sensitive", error.ToString());
    }

    [Fact]
    public async Task Utf8ListIsStrictAndDoesNotTreatEmptyAsWholeRealm()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(repository.FullName, "Tools/ProjectEnvironment.ps1")))
            repository = repository.Parent ?? throw new InvalidOperationException("Repository root not found.");
        var folder = Path.Combine(repository.FullName, ".artifacts/mail-system-review/server", "ops-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "players.txt");
        try
        {
            await File.WriteAllTextAsync(file, "\uFEFF" + A.ToUpperInvariant() + "\r\n" + B + "\n" + A + "\n");
            Assert.Equal(new[] { A, B, A }, await MailCli.ReadIds(file, CancellationToken.None));
            await File.WriteAllTextAsync(file, " \n\r\n");
            Assert.Equal("mail_list_empty", (await Assert.ThrowsAsync<EntryProblem>(() => MailCli.ReadIds(file, CancellationToken.None))).Code);
            await File.WriteAllBytesAsync(file, [0xff, 0xff]);
            Assert.Equal("mail_list_unreadable", (await Assert.ThrowsAsync<EntryProblem>(() => MailCli.ReadIds(file, CancellationToken.None))).Code);
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
