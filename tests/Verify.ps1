$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($file in Get-ChildItem -LiteralPath $root -Filter *.ps1 -Recurse) {
    $tokens=$null; $errors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$tokens,[ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
}
$package = Join-Path $root 'dist\TomatoTopmost-1.0.0'
$exe = Join-Path $package 'TomatoTopmost.exe'
foreach ($attempt in 1..3) {
    $p = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Native window test failed on attempt $attempt" }
}
$p = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + (Join-Path $package 'Install.ps1') + '"'),'-DryRun') -Wait -PassThru -WindowStyle Hidden
if ($p.ExitCode -ne 0) { throw 'Installer package validation failed' }
Write-Output 'PASS: PowerShell syntax, three native window tests, installer dry run.'
