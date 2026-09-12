[CmdletBinding()]
param(
    [string]$PackageDirectory = 'artifacts/packages/current'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resolvedPackageDirectory = (Resolve-Path -LiteralPath (Join-Path $repositoryRoot $PackageDirectory)).Path
$packages = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter '*.nupkg' -File |
    Where-Object { $_.Name -notlike '*.snupkg' })
$symbolPackages = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter '*.snupkg' -File)
if ($packages.Count -ne 1 -or $symbolPackages.Count -ne 1) {
    throw "Expected one .nupkg and one .snupkg in $resolvedPackageDirectory."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageArchive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
$symbolArchive = [System.IO.Compression.ZipFile]::OpenRead($symbolPackages[0].FullName)
try {
    $entryNames = @($packageArchive.Entries.FullName)
    $requiredEntries = @(
        'WebSocketNotifications.nuspec',
        'README.md',
        'lib/net10.0/WebSocketNotifications.dll',
        'lib/net10.0/WebSocketNotifications.xml'
    )
    foreach ($requiredEntry in $requiredEntries) {
        if ($requiredEntry -notin $entryNames) {
            throw "The package is missing $requiredEntry."
        }
    }

    $unexpectedLibraryEntries = @($entryNames | Where-Object {
        $_ -like 'lib/*' -and $_ -notlike 'lib/net10.0/*'
    })
    if ($unexpectedLibraryEntries.Count -gt 0) {
        throw "The package contains unsupported target assets: $($unexpectedLibraryEntries -join ', ')."
    }

    $nuspecEntry = $packageArchive.GetEntry('WebSocketNotifications.nuspec')
    $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
    try {
        [xml]$nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $namespace = [System.Xml.XmlNamespaceManager]::new($nuspec.NameTable)
    $namespace.AddNamespace('n', $nuspec.DocumentElement.NamespaceURI)
    $license = $nuspec.SelectSingleNode('/n:package/n:metadata/n:license', $namespace)
    if ($license.type -ne 'expression' -or $license.InnerText -ne 'MIT') {
        throw 'The package license must be the MIT SPDX expression.'
    }
    if ($nuspec.SelectSingleNode('/n:package/n:metadata/n:projectUrl', $namespace).InnerText -ne
        'https://github.com/sameeranand2711/WebSocketNotifications') {
        throw 'The package project URL is missing or incorrect.'
    }
    $repository = $nuspec.SelectSingleNode('/n:package/n:metadata/n:repository', $namespace)
    if ($repository.type -ne 'git' -or $repository.url -ne
        'https://github.com/sameeranand2711/WebSocketNotifications') {
        throw 'The package repository metadata is missing or incorrect.'
    }
    $dependencies = @($nuspec.SelectNodes('/n:package/n:metadata/n:dependencies/n:group/n:dependency', $namespace))
    if ($dependencies.Count -ne 0) {
        throw "The core package unexpectedly contains package dependencies: $($dependencies.id -join ', ')."
    }

    $symbolEntries = @($symbolArchive.Entries.FullName)
    if ('lib/net10.0/WebSocketNotifications.pdb' -notin $symbolEntries) {
        throw 'The symbol package is missing the portable PDB.'
    }
    $pdbEntry = $symbolArchive.GetEntry('lib/net10.0/WebSocketNotifications.pdb')
    $pdbStream = $pdbEntry.Open()
    $pdbBuffer = [System.IO.MemoryStream]::new()
    try {
        $pdbStream.CopyTo($pdbBuffer)
        $pdbText = [Text.Encoding]::UTF8.GetString($pdbBuffer.ToArray())
    }
    finally {
        $pdbBuffer.Dispose()
        $pdbStream.Dispose()
    }
    if ($pdbText -notlike '*https://raw.githubusercontent.com/sameeranand2711/WebSocketNotifications/*') {
        throw 'The portable PDB does not contain the expected GitHub Source Link mapping.'
    }

    Write-Host "PACKAGE INSPECTION PASSED: $($packages[0].Name) and $($symbolPackages[0].Name)"
}
finally {
    $packageArchive.Dispose()
    $symbolArchive.Dispose()
}
