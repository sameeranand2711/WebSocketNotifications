[CmdletBinding()]
param(
    [string]$PackageDirectory = 'artifacts/packages/current'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedPackageDirectory = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot $PackageDirectory)).Path
$packages = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter '*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.snupkg' })
if ($packages.Count -ne 1) {
    throw "Expected exactly one .nupkg in $resolvedPackageDirectory; found $($packages.Count)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
    if ($nuspecEntries.Count -ne 1) {
        throw "Expected exactly one .nuspec in $($packages[0].Name)."
    }
    $nuspecEntry = $nuspecEntries[0]
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try {
        [xml]$nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
    $packageVersion = $nuspec.package.metadata.version
}
finally {
    $archive.Dispose()
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "WebSocketNotifications-package-smoke-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $templateDirectory = Join-Path $PSScriptRoot 'package-smoke'
    Copy-Item -LiteralPath (Join-Path $templateDirectory 'PackageSmokeTest.csproj') -Destination $temporaryRoot
    Copy-Item -LiteralPath (Join-Path $templateDirectory 'Program.cs') -Destination $temporaryRoot
    $project = Join-Path $temporaryRoot 'PackageSmokeTest.csproj'
    $packageCache = Join-Path $temporaryRoot 'packages'
    $versionProperty = "-p:WebSocketNotificationsVersion=$packageVersion"

    & dotnet restore $project $versionProperty --source $resolvedPackageDirectory --packages $packageCache
    if ($LASTEXITCODE -ne 0) {
        throw 'The packed-package smoke-test restore failed.'
    }

    & dotnet run --project $project --configuration Release --no-restore $versionProperty
    if ($LASTEXITCODE -ne 0) {
        throw 'The packed-package smoke test failed.'
    }
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a smoke-test path outside the system temporary directory: $resolvedTemporaryRoot"
    }

    Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
}
