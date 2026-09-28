$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework C# compiler was not found.' }
$dist = Join-Path $PSScriptRoot 'dist'
$package = Join-Path $dist 'TomatoTopmost-1.0.0'
New-Item -ItemType Directory -Path $package -Force | Out-Null
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ("/win32icon:" + (Join-Path $PSScriptRoot "assets\app.ico")) ("/resource:" + (Join-Path $PSScriptRoot "assets\app.ico") + ",TomatoTopmost.app.ico") /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ("/out:" + (Join-Path $package 'TomatoTopmost.exe')) (Join-Path $PSScriptRoot 'src\Program.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
foreach ($name in @('Install.cmd','Install.ps1','Uninstall.ps1')) {
    $source = Join-Path $PSScriptRoot "scripts\$name"
    $target = Join-Path $package $name
    if ($name.EndsWith('.ps1')) {
        [IO.File]::WriteAllText($target, [IO.File]::ReadAllText($source), [Text.UTF8Encoding]::new($true))
    } else { Copy-Item -LiteralPath $source -Destination $target -Force }
}
foreach ($name in @('README.md','LICENSE','NOTICE')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $package -Force }
$testLog = Join-Path $dist 'self-test-error.txt'
$test = Start-Process -FilePath (Join-Path $package 'TomatoTopmost.exe') -ArgumentList '--self-test' -Wait -PassThru -RedirectStandardError $testLog
if ($test.ExitCode -ne 0) { throw ('Native window self-test failed. ' + (Get-Content -Raw -LiteralPath $testLog)) }
Compress-Archive -Path $package -DestinationPath (Join-Path $dist 'TomatoTopmost-1.0.0-windows.zip') -Force
Get-FileHash -LiteralPath (Join-Path $dist 'TomatoTopmost-1.0.0-windows.zip') -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLower() + '  TomatoTopmost-1.0.0-windows.zip' } | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding Ascii
Write-Output 'Build and native window self-test passed.'
