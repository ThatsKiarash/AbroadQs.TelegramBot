# اجرای Tunnel Client در پس‌زمینه و سپس اجرای Bot Host (برای توسعه لوکال با تانل)
$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$slnDir = Split-Path -Parent (Split-Path -Parent $scriptDir)
Set-Location $slnDir

$tunnelProc = $null
try {
    $tunnelProc = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "src/AbroadQs.TunnelClient", "--", "--url", "https://webhook.abroadqs.com", "--local", "5252" -PassThru -WindowStyle Normal
    Write-Host "Tunnel Client started (PID $($tunnelProc.Id)). Waiting 3s before starting Host..."
    Start-Sleep -Seconds 3
    Push-Location "src/AbroadQs.Bot.Host.Webhook"
    $env:ASPNETCORE_URLS = "http://127.0.0.1:5252"
    dotnet run
}
finally {
    if ($tunnelProc -and -not $tunnelProc.HasExited) {
        Write-Host "Stopping Tunnel Client..."
        Stop-Process -Id $tunnelProc.Id -Force -ErrorAction SilentlyContinue
    }
}
