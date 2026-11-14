param(
    [Parameter(Mandatory = $true)][string]$InstallerPath,
    [Parameter(Mandatory = $true)][string]$PublishDirectory
)

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $env:TEMP ('WallhavenUpgradeTest-' + [guid]::NewGuid().ToString('N'))
$installDirectory = Join-Path $testRoot 'installed'
$otherDirectory = Join-Path $testRoot 'unrelated'
New-Item -ItemType Directory -Path $installDirectory, $otherDirectory -Force | Out-Null
$sourcePath = Join-Path $testRoot 'LockFile.cs'
$helperPath = Join-Path $testRoot 'LockFile.exe'
@'
using System;
using System.IO;
using System.Threading;
internal static class LockFile
{
    private static void Main(string[] args)
    {
        using (var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            File.WriteAllText(args[1], "ready");
            Thread.Sleep(Timeout.Infinite);
        }
    }
}
'@ | Set-Content -LiteralPath $sourcePath -Encoding utf8
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe "/out:$helperPath" $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Unable to compile the file-lock test process.' }

function Start-FileLock([string]$Directory, [string]$ReadyName) {
    $exe = Join-Path $Directory 'WallhavenService.exe'
    $lockedFile = Join-Path $Directory 'clrjit.dll'
    $readyFile = Join-Path $Directory $ReadyName
    Copy-Item -LiteralPath $helperPath -Destination $exe -Force
    if (-not (Test-Path -LiteralPath $lockedFile)) {
        Set-Content -LiteralPath $lockedFile -Value 'test file to replace'
    }
    $process = Start-Process -FilePath $exe -ArgumentList @(
        ('"' + $lockedFile + '"'), ('"' + $readyFile + '"')
    ) -WindowStyle Hidden -PassThru
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if (Test-Path -LiteralPath $readyFile) { return $process }
        if ($process.HasExited) { throw 'File-lock helper exited unexpectedly.' }
        Start-Sleep -Milliseconds 100
    }
    throw 'File-lock helper did not become ready.'
}

$unrelated = $null
$installed = $null
try {
    $unrelated = Start-FileLock $otherDirectory 'unrelated.ready'
    foreach ($round in 1..2) {
        # Round two reinstalls exactly the same package while files are locked.
        $installed = Start-FileLock $installDirectory "installed-$round.ready"
        $log = Join-Path $testRoot "setup-$round.log"
        $setup = Start-Process -FilePath $InstallerPath -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
            '/NOCLOSEAPPLICATIONS', '/NOICONS', '/TASKS=',
            ('/DIR="' + $installDirectory + '"'), ('/LOG="' + $log + '"')
        ) -WindowStyle Hidden -PassThru
        if (-not $setup.WaitForExit(120000)) {
            $setup.Kill()
            throw "Upgrade round $round timed out. Log: $log"
        }
        if ($setup.ExitCode -ne 0) { throw "Upgrade round $round failed: $($setup.ExitCode). Log: $log" }
        if (-not $installed.WaitForExit(1000)) { throw 'The installed file-lock process is still running.' }
        if ($unrelated.HasExited) { throw 'The installer stopped an unrelated same-name process.' }
        foreach ($name in 'clrjit.dll', 'WallhavenService.exe', 'WallhavenService.dll') {
            $expected = (Get-FileHash -LiteralPath (Join-Path $PublishDirectory $name)).Hash
            $actual = (Get-FileHash -LiteralPath (Join-Path $installDirectory $name)).Hash
            if ($actual -ne $expected) { throw "Incorrect installed file: $name" }
        }
        if (-not (Select-String -LiteralPath $log -Pattern 'Stopping installed application PID' -Quiet)) {
            throw 'The installer did not log its independent shutdown check.'
        }
        Write-Output "Upgrade round $round passed with Restart Manager shutdown disabled; unrelated process survived."
    }
}
finally {
    foreach ($process in @($installed, $unrelated)) {
        if ($process -and -not $process.HasExited) { $process.Kill(); $process.WaitForExit() }
    }
}
