using System.Net;
using System.Net.Mail;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string SECRET_TOKEN = "AbroadQs_Email_Relay_2026_Secure";
const string FROM_EMAIL = "info@abroadqs.com";
const string FROM_NAME = "AbroadQs";

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

        // Try sending via local SMTP first (Plesk mail server)
        try
        {
            using var message = new MailMessage();
            message.From = new MailAddress(FROM_EMAIL, FROM_NAME);
            message.To.Add(to);
            message.Subject = subject;
            message.Body = htmlBody;
            message.IsBodyHtml = true;

            // Try localhost SMTP (Plesk's built-in mail)
            using var smtp = new SmtpClient("localhost", 25)
            {
                EnableSsl = false,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 10000
            };
            await smtp.SendMailAsync(message);
            return Results.Json(new { ok = true, method = "localhost_smtp" });
        }
        catch (Exception ex1)
        {
            // Try authenticated SMTP on the same server
            try
            {
                using var message = new MailMessage();
                message.From = new MailAddress(FROM_EMAIL, FROM_NAME);
                message.To.Add(to);
                message.Subject = subject;
                message.Body = htmlBody;
                message.IsBodyHtml = true;

                using var smtp = new SmtpClient("localhost", 587)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(FROM_EMAIL, "Kia135724!"),
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    Timeout = 10000
                };
                await smtp.SendMailAsync(message);
                return Results.Json(new { ok = true, method = "localhost_smtp_auth" });
            }
            catch (Exception ex2)
            {
                // Try direct connection on port 465
                try
                {
                    using var message = new MailMessage();
                    message.From = new MailAddress(FROM_EMAIL, FROM_NAME);
                    message.To.Add(to);
                    message.Subject = subject;
                    message.Body = htmlBody;
                    message.IsBodyHtml = true;

                    using var smtp = new SmtpClient("localhost", 465)
                    {
                        EnableSsl = true,
                        Credentials = new NetworkCredential(FROM_EMAIL, "Kia135724!"),
                        DeliveryMethod = SmtpDeliveryMethod.Network,
                        Timeout = 10000
                    };
                    await smtp.SendMailAsync(message);
                    return Results.Json(new { ok = true, method = "localhost_smtp_ssl" });
                }
                catch (Exception ex3)
                {
                    return Results.Json(new { ok = false, error = $"All SMTP attempts failed. 1:{ex1.Message} 2:{ex2.Message} 3:{ex3.Message}" }, statusCode: 500);
                }
            }
        }
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message }, statusCode: 500);
    }
});

app.Run();
