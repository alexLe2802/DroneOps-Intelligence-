using System.Net;
using System.Net.Mail;
using DroneOps.Application.Interfaces.Auth;
using Microsoft.Extensions.Configuration;

namespace DroneOps.Application.Services.Auth;

public class EmailService : IEmailService
{
    private readonly IConfiguration _config;

    public EmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string toEmail, string subject, string htmlMessage)
    {
        var host = _config["SmtpSettings:Host"] ?? "smtp.gmail.com";
        var port = int.Parse(_config["SmtpSettings:Port"] ?? "587");
        var senderEmail = _config["SmtpSettings:SenderEmail"];
        var senderName = _config["SmtpSettings:SenderName"] ?? "DroneOps System";
        var appPassword = _config["SmtpSettings:AppPassword"];

        using var client = new SmtpClient(host, port)
        {
            Port = port,
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(senderEmail, appPassword)
        };

        var mail = new MailMessage
        {
            From = new MailAddress(senderEmail!, senderName),
            Subject = subject,
            Body = htmlMessage,
            IsBodyHtml = true
        };

        mail.To.Add(toEmail);
        await client.SendMailAsync(mail);
    }
}