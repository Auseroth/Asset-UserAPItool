using System.Net;
using System.Net.Mail;
using System.Text;
using LdapCloudSync.Core.Interfaces;
using LdapCloudSync.Core.Models;
using Serilog;

namespace LdapCloudSync.Core.Providers;

public sealed class EmailCloudClient : ICloudClient
{
    private readonly CloudTargetConfig _target;
    private readonly ILogger _log;

    public EmailCloudClient(CloudTargetConfig target, HttpClient? httpClient = null, ILogger? logger = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _log = logger ?? Log.Logger;
    }

    public Task<(bool Success, string Message)> TestConnectionAsync()
    {
        var validation = ValidateSettings();
        if (!validation.Success)
            return Task.FromResult(validation);

        return Task.FromResult((true, "Email provider settings look valid."));
    }

    public Task<(IReadOnlyList<string> Fields, string RawResponse)> DiscoverFieldsAsync(string category)
    {
        IReadOnlyList<string> fields =
        [
            "first_name",
            "last_name",
            "phone_number",
            "asset_tag",
            "serial_number",
            "check_in_note",
            "maintenance_trigger"
        ];

        return Task.FromResult((fields, "Email provider uses mapped/source fields as message body."));
    }

    public async Task<SyncResult> PushRecordsAsync(string category, IReadOnlyList<Dictionary<string, object>> records)
    {
        var result = new SyncResult();
        var validation = ValidateSettings();
        if (!validation.Success)
        {
            result.Failed = records.Count == 0 ? 1 : records.Count;
            result.Errors.Add(validation.Message);
            return result;
        }

        foreach (var record in records)
        {
            try
            {
                using var message = BuildMessage(record);
                using var smtp = BuildSmtpClient();
                await smtp.SendMailAsync(message);
                result.Created++;
                result.LastSuccessResponseBody = $"Email sent to {_target.Connection.EmailToAddress}";
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Email send failed for target {TargetName}", _target.Name);
                result.Failed++;
                result.Errors.Add(ex.Message);
            }
        }

        return result;
    }

    private (bool Success, string Message) ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_target.Connection.SmtpHost))
            return (false, "SMTP host is required for Email provider.");

        if (_target.Connection.SmtpPort <= 0)
            return (false, "SMTP port must be greater than 0 for Email provider.");

        if (string.IsNullOrWhiteSpace(_target.Connection.EmailFromAddress))
            return (false, "From email address is required for Email provider.");

        if (string.IsNullOrWhiteSpace(_target.Connection.EmailToAddress))
            return (false, "Recipient email address is required for Email provider.");

        return (true, "OK");
    }

    private MailMessage BuildMessage(Dictionary<string, object> record)
    {
        var subjectPrefix = string.IsNullOrWhiteSpace(_target.Connection.EmailSubjectPrefix)
            ? "Check-in Request"
            : _target.Connection.EmailSubjectPrefix.Trim();

        var customSubject = TryGetString(record, "email_subject");
        var subject = string.IsNullOrWhiteSpace(customSubject)
            ? $"{subjectPrefix} - {DateTime.Now:yyyy-MM-dd HH:mm}"
            : $"{subjectPrefix} - {customSubject}";

        var body = BuildBody(record);

        var message = new MailMessage(_target.Connection.EmailFromAddress.Trim(), _target.Connection.EmailToAddress.Trim(), subject, body)
        {
            IsBodyHtml = false
        };

        return message;
    }

    private string BuildBody(Dictionary<string, object> record)
    {
        var customBody = TryGetString(record, "email_body");
        if (!string.IsNullOrWhiteSpace(customBody))
            return customBody;

        var builder = new StringBuilder();
        builder.AppendLine($"Target: {_target.Name}");
        builder.AppendLine($"Submitted: {DateTime.Now:O}");
        builder.AppendLine();

        foreach (var (key, value) in record)
        {
            if (key.StartsWith("email_", StringComparison.OrdinalIgnoreCase))
                continue;

            builder.AppendLine($"{key}: {value}");
        }

        return builder.ToString();
    }

    private static string TryGetString(Dictionary<string, object> record, string key)
    {
        if (!record.TryGetValue(key, out var value) || value is null)
            return string.Empty;

        return value.ToString()?.Trim() ?? string.Empty;
    }

    private SmtpClient BuildSmtpClient()
    {
        var client = new SmtpClient(_target.Connection.SmtpHost.Trim(), _target.Connection.SmtpPort)
        {
            EnableSsl = _target.Connection.SmtpUseSsl
        };

        if (!string.IsNullOrWhiteSpace(_target.Connection.SmtpUsername))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(
                _target.Connection.SmtpUsername,
                _target.Connection.SmtpPassword);
        }
        else
        {
            client.UseDefaultCredentials = true;
        }

        return client;
    }

    public void Dispose()
    {
    }
}
