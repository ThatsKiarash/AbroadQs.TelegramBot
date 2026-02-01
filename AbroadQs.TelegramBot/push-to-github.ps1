# Create GitHub repo and push. Run: .\push-to-github.ps1
# If GITHUB_TOKEN is not set, browser opens to create token; paste token when asked.

$repoName = "AbroadQs.TelegramBot"
$token = $env:GITHUB_TOKEN

if (-not $token) {
    Write-Host "GITHUB_TOKEN not set. Opening token page..." -ForegroundColor Yellow
    Start-Process 'https://github.com/settings/tokens/new?description=AbroadQs-push&scopes=repo'
    Write-Host ""
    $token = Read-Host "Paste your GitHub token here"
    if (-not $token) { Write-Host "Cancelled."; exit 1 }
}

$headers = @{
    "Authorization" = "Bearer $token"
    "Accept"        = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

$user = Invoke-RestMethod -Uri "https://api.github.com/user" -Headers $headers -Method Get
$username = $user.login
Write-Host "User: $username" -ForegroundColor Cyan

$body = @{ name = $repoName; description = "Telegram bot Webhook GetUpdates dashboard tunnel"; private = $false } | ConvertTo-Json
try {
    Invoke-RestMethod -Uri "https://api.github.com/user/repos" -Headers $headers -Method Post -Body $body -ContentType "application/json" | Out-Null
    Write-Host "Repo created." -ForegroundColor Green
} catch {
    if ($_.Exception.Response.StatusCode -eq 422) { Write-Host "Repo already exists." -ForegroundColor Yellow }
    else { throw }
}

$gitDir = $PSScriptRoot
Push-Location $gitDir
git remote remove origin 2>$null
$pushUrl = "https://${token}@github.com/${username}/${repoName}.git"
git remote add origin $pushUrl
git push -u origin main
$ok = ($LASTEXITCODE -eq 0)
git remote set-url origin "https://github.com/${username}/${repoName}.git"
Pop-Location

if ($ok) {
    Write-Host ""
    Write-Host "Done: https://github.com/$username/$repoName" -ForegroundColor Green
} else {
    Write-Host "Push failed." -ForegroundColor Red
    exit 1
}
