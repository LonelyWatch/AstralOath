using System.Net;
using System.Net.Mail;

namespace AstralOath.Server.Entry;

internal sealed class RegistrationMail(IConfiguration configuration, IHostEnvironment environment)
{
    private readonly IConfigurationSection settings = configuration.GetSection("Entry:Smtp");

    public void EnsureConfigured() => _ = ReadSettings();

    public async Task Send(string recipient, string code, CancellationToken cancellation)
    {
        var (host, port, from, tls, username, password) = ReadSettings();
        using var smtp = new SmtpClient(host, port) { EnableSsl = tls };
        if (!string.IsNullOrEmpty(username)) smtp.Credentials = new NetworkCredential(username, password);
        using var message = new MailMessage(from, recipient, "AstralOath verification code",
            $"Your AstralOath verification code is {code}. It expires in 10 minutes. If you did not request it, ignore this email.");
        await smtp.SendMailAsync(message, cancellation);
    }

    private (string Host, int Port, string From, bool Tls, string? Username, string? Password) ReadSettings()
    {
        string? host = settings["Host"]?.Trim();
        string? from = EntryEndpoints.Email(settings["From"]);
        int port = settings.GetValue("Port", 587);
        bool tls = settings.GetValue("UseStartTls", true);
        string? username = settings["Username"];
        string? password = settings["Password"];
        bool local = IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
        if (string.IsNullOrWhiteSpace(host) || from is null || port is < 1 or > 65535 ||
            (!tls && (!environment.IsDevelopment() || !local)) ||
            string.IsNullOrEmpty(username) != string.IsNullOrEmpty(password))
            throw new EntryProblem(503, "smtp_unavailable");
        return (host, port, from, tls, username, password);
    }
}
