using System.Net;
using System.Net.Mail;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string SECRET_TOKEN = "AbroadQs_Email_Relay_2026_Secure";
const string FROM_EMAIL = "info@abroadqs.com";
const string FROM_NAME = "AbroadQs";
const string SMTP_PASSWORD = "Kia135724!";

// Health check
app.MapGet("/", () => Results.Json(new { ok = true, service = "EmailRelay" }));

// Email sending endpoint
app.MapPost("/", async (HttpContext ctx) =>
{
    try
    {
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
        
        var token = body.TryGetProperty("token", out var t) ? t.GetString() : "";
        var to = body.TryGetProperty("to", out var toEl) ? toEl.GetString() : "";
        var subject = body.TryGetProperty("subject", out var s) ? s.GetString() : "";
        var htmlBody = body.TryGetProperty("body", out var b) ? b.GetString() : "";

        if (token != SECRET_TOKEN)
            return Results.Json(new { ok = false, error = "Unauthorized" }, statusCode: 403);

        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(htmlBody))
            return Results.Json(new { ok = false, error = "Missing to, subject, or body" }, statusCode: 400);

        var errors = new List<string>();

        // Attempt 1: localhost:25 WITH authentication
        try
        {
            using var msg = CreateMessage(to, subject, htmlBody);
            using var smtp = new SmtpClient("localhost", 25)
            {
                Credentials = new NetworkCredential(FROM_EMAIL, SMTP_PASSWORD),
                EnableSsl = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 10000
            };
            await smtp.SendMailAsync(msg);
            return Results.Json(new { ok = true, method = "localhost_25_auth" });
        }
        catch (Exception ex) { errors.Add($"25auth:{ex.Message}"); }

        // Attempt 2: localhost:587 WITH authentication + STARTTLS
        try
        {
            using var msg = CreateMessage(to, subject, htmlBody);
            using var smtp = new SmtpClient("localhost", 587)
            {
                Credentials = new NetworkCredential(FROM_EMAIL, SMTP_PASSWORD),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 10000
            };
            await smtp.SendMailAsync(msg);
            return Results.Json(new { ok = true, method = "localhost_587_auth" });
        }
        catch (Exception ex) { errors.Add($"587auth:{ex.Message}"); }

        // Attempt 3: server hostname with auth (in case localhost doesn't work)
        try
        {
            using var msg = CreateMessage(to, subject, htmlBody);
            using var smtp = new SmtpClient("windows5.centraldnserver.com", 25)
            {
                Credentials = new NetworkCredential(FROM_EMAIL, SMTP_PASSWORD),
                EnableSsl = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 10000
            };
            await smtp.SendMailAsync(msg);
            return Results.Json(new { ok = true, method = "hostname_25_auth" });
        }
        catch (Exception ex) { errors.Add($"host25:{ex.Message}"); }

        // Attempt 4: server hostname:465 with SSL  
        try
        {
            using var msg = CreateMessage(to, subject, htmlBody);
            using var smtp = new SmtpClient("windows5.centraldnserver.com", 465)
            {
                Credentials = new NetworkCredential(FROM_EMAIL, SMTP_PASSWORD),
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 10000
            };
            await smtp.SendMailAsync(msg);
            return Results.Json(new { ok = true, method = "hostname_465_ssl" });
        }
        catch (Exception ex) { errors.Add($"host465:{ex.Message}"); }

        return Results.Json(new { ok = false, error = string.Join(" | ", errors) }, statusCode: 500);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
    }
});

app.Run();

static MailMessage CreateMessage(string to, string subject, string htmlBody)
{
    var msg = new MailMessage();
    msg.From = new MailAddress(FROM_EMAIL, FROM_NAME);
    msg.To.Add(to);
    msg.Subject = subject;
    msg.Body = htmlBody;
    msg.IsBodyHtml = true;
    return msg;
}
