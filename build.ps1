param([switch]$Probe)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Push-Location $root
try {
    $csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
    if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found' }
    $fw = Split-Path $csc
    New-Item -ItemType Directory -Force (Join-Path $root 'dist') | Out-Null

    $common = @(
        'src\Hid\Native.cs',
        'src\Hid\Hid.cs',
        'src\Devices\DeviceReading.cs',
        'src\Devices\LogitechG304.cs',
        'src\Devices\VgnKeyboard.cs'
    )

    if ($Probe) {
        $sources = $common + 'tools\Probe.cs'
        $out = Join-Path $env:TEMP 'kmb-probe.exe'
        $cscArgs = @('/nologo', '/target:exe', '/codepage:65001', '/optimize+', "/out:$out") + $sources
    } else {
        $sources = $common + @(
            'src\Services\Settings.cs',
            'src\Services\AutoStart.cs',
            'src\Services\BatteryMonitor.cs',
            'src\Services\HistoryLog.cs',
            'src\Services\Statistics.cs',
            'src\UI\Palette.cs',
            'src\UI\BatteryBar.cs',
            'src\UI\DeviceCard.cs',
            'src\UI\TrayIcon.cs',
            'src\UI\MainWindow.cs',
            'src\UI\StatsWindow.cs',
            'src\Program.cs'
        )
        $refs = @(
            "$fw\WPF\PresentationFramework.dll",
            "$fw\WPF\PresentationCore.dll",
            "$fw\WPF\WindowsBase.dll",
            "$fw\System.Xaml.dll",
            "$fw\System.Windows.Forms.dll",
            "$fw\System.Drawing.dll"
        ) | ForEach-Object { "/r:$_" }
        $out = Join-Path $root 'dist\KeyMouseBattery.exe'
        $cscArgs = @('/nologo', '/target:winexe', '/codepage:65001', '/optimize+', '/platform:anycpu',
            "/out:$out", '/win32manifest:src\app.manifest', '/win32icon:src\app.ico') + $refs + $sources
    }

    & $csc @cscArgs
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
    Write-Host "OK -> $out"
}
finally {
    Pop-Location
}
