# Installs the kscript CLI from the latest GitHub release.
#
#   irm https://raw.githubusercontent.com/kylepantall/kscript/master/install.ps1 | iex
#
# Options (environment variables, set before running):
#   $env:KSCRIPT_VERSION = 'v0.1.0'   install a specific release instead of the latest
#   $env:KSCRIPT_INSTALL = 'C:\tools\kscript'   install folder (default: %LOCALAPPDATA%\Programs\kscript)
#   $env:KSCRIPT_NO_MODIFY_PATH = '1' don't add the folder to your user PATH

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is far slower with the progress bar on Windows PowerShell 5.1.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

& {
    $repo = if ($env:KSCRIPT_REPO) { $env:KSCRIPT_REPO } else { 'kylepantall/kscript' }
    $version = if ($env:KSCRIPT_VERSION) { $env:KSCRIPT_VERSION } else { 'latest' }
    $installDir = if ($env:KSCRIPT_INSTALL) { $env:KSCRIPT_INSTALL } else { Join-Path $env:LOCALAPPDATA 'Programs\kscript' }

    if ($env:OS -ne 'Windows_NT') {
        throw "install.ps1 is for Windows. On Linux or macOS run: curl -fsSL https://raw.githubusercontent.com/$repo/master/install.sh | sh"
    }

    # Only an x64 build is published; Windows on Arm runs it under emulation.
    $asset = 'kscript-win-x64.zip'
    if ($version -eq 'latest') {
        $url = "https://github.com/$repo/releases/latest/download/$asset"
    } else {
        if (-not $version.StartsWith('v')) { $version = "v$version" }
        $url = "https://github.com/$repo/releases/download/$version/$asset"
    }

    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("kscript-install-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        Write-Host "Downloading $asset ($version)..."
        $zip = Join-Path $tmp $asset
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        $pkg = Join-Path $tmp 'pkg'
        Expand-Archive -Path $zip -DestinationPath $pkg -Force
        $new = Join-Path $pkg 'kscript.exe'
        if (-not (Test-Path $new)) { throw "$asset has no kscript.exe inside" }

        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
        $exe = Join-Path $installDir 'kscript.exe'
        if (Test-Path $exe) {
            # A running kscript.exe can't be overwritten but can be renamed; it is deleted on the next start.
            $old = "$exe.old"
            Remove-Item $old -Force -ErrorAction SilentlyContinue
            if (Test-Path $old) { $old = "$exe.$([Guid]::NewGuid().ToString('N')).old" }
            Move-Item $exe $old -Force
        }
        Move-Item $new $exe -Force
        Unblock-File $exe -ErrorAction SilentlyContinue
        # The desktop UI's native libraries ship beside kscript.exe.
        Get-ChildItem $pkg -File | ForEach-Object {
            $dest = Join-Path $installDir $_.Name
            Move-Item $_.FullName $dest -Force
            Unblock-File $dest -ErrorAction SilentlyContinue
        }
    } finally {
        Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }

    $installed = & $exe --version
    if ($LASTEXITCODE -ne 0) { throw "Installed $exe but it didn't run" }
    Write-Host "Installed kscript $installed to $exe"

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $parts = @($userPath -split ';' | Where-Object { $_ })
    if ($parts -notcontains $installDir) {
        if ($env:KSCRIPT_NO_MODIFY_PATH -eq '1') {
            Write-Host "Add $installDir to your PATH to run kscript from anywhere."
            return
        }
        [Environment]::SetEnvironmentVariable('Path', (($parts + $installDir) -join ';'), 'User')
        Write-Host "Added $installDir to your user PATH. Open a new terminal to use it."
    }
    if (($env:Path -split ';') -notcontains $installDir) { $env:Path = "$env:Path;$installDir" }
    Write-Host "Run: kscript --help"
}
