param([switch]$DryRun)
$ErrorActionPreference = 'Stop'
try {
    $destination = Join-Path $env:LOCALAPPDATA 'Programs\TomatoTopmost'
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'TomatoTopmost'
    foreach ($file in @('TomatoTopmost.exe', 'Uninstall.ps1', 'README.md', 'LICENSE', 'NOTICE')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $file))) { throw "Missing package file: $file" }
    }
    if ($DryRun) { Write-Output "Validated package. Install destination: $destination"; exit 0 }
    $exe = Join-Path $destination 'TomatoTopmost.exe'
    if (Test-Path -LiteralPath $exe) {
        Start-Process -FilePath $exe -ArgumentList '--stop' -Wait
        Start-Sleep -Milliseconds 700
    }
    New-Item -ItemType Directory -Path $destination,$menu -Force | Out-Null
    foreach ($file in @('TomatoTopmost.exe', 'Uninstall.ps1', 'README.md', 'LICENSE', 'NOTICE')) {
        $source = Join-Path $PSScriptRoot $file
        $target = Join-Path $destination $file
        if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($target)) { Copy-Item -LiteralPath $source -Destination $target -Force }
    }
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path $menu 'TomatoTopmost.lnk'))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $destination
    $shortcut.Save()
    $shortcut = $shell.CreateShortcut((Join-Path $menu 'Uninstall.lnk'))
    $shortcut.TargetPath = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
    $shortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $destination 'Uninstall.ps1') + '"'
    $shortcut.Save()
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TomatoTopmost'
    New-Item -Path $key -Force | Out-Null
    $values = @{
        DisplayName='TomatoTopmost - 番茄置顶助手'; DisplayVersion='1.0.0';
        InstallLocation=$destination; DisplayIcon=$exe;
        UninstallString='"' + "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" + '" -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $destination 'Uninstall.ps1') + '"'
    }
    foreach ($name in $values.Keys) { New-ItemProperty -Path $key -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null }
    New-ItemProperty -Path $key -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $key -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    Start-Process -FilePath $exe
    Write-Host '安装完成。请在任务栏托盘中找到番茄置顶助手。开机启动默认关闭。'
} catch { Write-Error $_; exit 1 }
