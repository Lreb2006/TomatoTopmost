param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
try {
    $expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\TomatoTopmost')).TrimEnd('\')
    $actual = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    if ($actual -ne $expected) { throw 'Uninstall must run from the installed TomatoTopmost directory.' }
    if ((Get-Item -LiteralPath $actual).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to uninstall through a directory link.' }
    if ($DryRun) { Write-Output "Validated uninstall directory: $actual"; exit 0 }
    Add-Type -AssemblyName System.Windows.Forms
    $answer = [Windows.Forms.MessageBox]::Show('卸载番茄置顶助手？不会删除番茄ToDo或待办数据。','卸载确认','YesNo','Question')
    if ($answer -ne 'Yes') { exit 0 }
    $exe = Join-Path $actual 'TomatoTopmost.exe'
    if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -ArgumentList '--stop' -Wait }
    Start-Sleep -Milliseconds 700
    $running = @(Get-Process TomatoTopmost -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
    if ($running.Count -gt 0) { throw 'Please exit TomatoTopmost from the tray and retry uninstalling.' }
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $startup = Get-ItemPropertyValue -Path $run -Name TomatoTopmost -ErrorAction SilentlyContinue
    if ($startup -eq ('"' + $exe + '" --background') -or $startup -eq ('"' + $exe + '"')) { Remove-ItemProperty -Path $run -Name TomatoTopmost }
    if (Test-Path -LiteralPath 'HKCU:\Software\TomatoTopmost') { Remove-Item -LiteralPath 'HKCU:\Software\TomatoTopmost' -Recurse }
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'TomatoTopmost'
    foreach ($name in @('TomatoTopmost.lnk','Uninstall.lnk')) {
        $path = Join-Path $menu $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    if ((Test-Path -LiteralPath $menu) -and @(Get-ChildItem -LiteralPath $menu -Force).Count -eq 0) { Remove-Item -LiteralPath $menu }
    Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TomatoTopmost' -Recurse -ErrorAction SilentlyContinue
    # Delete only our known files. Leave any user-added files intact.
    foreach ($name in @('TomatoTopmost.exe','README.md','LICENSE','NOTICE','Uninstall.ps1')) {
        $path = Join-Path $actual $name
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    if (@(Get-ChildItem -LiteralPath $actual -Force).Count -eq 0) { Remove-Item -LiteralPath $actual }
    [Windows.Forms.MessageBox]::Show('已卸载番茄置顶助手。','卸载完成') | Out-Null
} catch { Write-Error $_; exit 1 }
