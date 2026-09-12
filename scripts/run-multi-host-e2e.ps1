[CmdletBinding()]
param(
    [switch]$LeaveKafkaRunning,
    [string]$NodeExecutable = 'node'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N')
$topic = "notifications-e2e-$runId"
$userId = "user-$runId"
$firstBaseUrl = 'http://127.0.0.1:5087'
$secondBaseUrl = 'http://127.0.0.1:5088'
$producerBaseUrl = 'http://127.0.0.1:5097'
$firstWebSocketUrl = "ws://127.0.0.1:5087/ws/notifications?userId=$userId"
$secondWebSocketUrl = "ws://127.0.0.1:5088/ws/notifications?userId=$userId"
$runDirectory = Join-Path $repositoryRoot ".e2e\multi-$runId"
$processes = [System.Collections.Generic.List[System.Diagnostics.Process]]::new()
$kafkaStarted = $false
$passed = $false

function Start-BackgroundProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$Output,
        [string]$ErrorOutput
    )

    if ($PSVersionTable.PSEdition -eq 'Desktop' -or $IsWindows) {
        return Start-Process `
            -FilePath $FilePath `
            -WindowStyle Hidden `
            -PassThru `
            -ArgumentList $ArgumentList `
            -RedirectStandardOutput $Output `
            -RedirectStandardError $ErrorOutput
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.WorkingDirectory = (Get-Location).Path
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $outputStream = [System.IO.File]::Open(
        $Output,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::Read)
    $errorStream = [System.IO.File]::Open(
        $ErrorOutput,
        [System.IO.FileMode]::Create,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::Read)
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "Could not start $FilePath."
        }
        $redirectTasks = @(
            $process.StandardOutput.BaseStream.CopyToAsync($outputStream),
            $process.StandardError.BaseStream.CopyToAsync($errorStream)
        )
        $process | Add-Member -NotePropertyName RedirectTasks -NotePropertyValue $redirectTasks
        $process | Add-Member -NotePropertyName RedirectStreams -NotePropertyValue @($outputStream, $errorStream)
        $process | Add-Member -NotePropertyName RedirectsCompleted -NotePropertyValue $false
        return $process
    }
    catch {
        $outputStream.Dispose()
        $errorStream.Dispose()
        $process.Dispose()
        throw
    }
}

function Complete-BackgroundProcessRedirects {
    param([System.Diagnostics.Process]$Process)

    if (-not $Process.PSObject.Properties['RedirectTasks'] -or $Process.RedirectsCompleted) {
        return
    }

    try {
        foreach ($redirectTask in $Process.RedirectTasks) {
            $redirectTask.GetAwaiter().GetResult()
        }
    }
    finally {
        foreach ($redirectStream in $Process.RedirectStreams) {
            $redirectStream.Dispose()
        }
        $Process.RedirectsCompleted = $true
    }
}

function Start-DotNetApplication {
    param(
        [string]$Name,
        [string]$Project,
        [string]$Url,
        [string[]]$ApplicationArguments
    )

    $output = Join-Path $runDirectory "$Name.out.log"
    $errorOutput = Join-Path $runDirectory "$Name.err.log"
    $arguments = @(
        'run',
        '--project', $Project,
        '--configuration', 'Release',
        '--no-build',
        '--',
        '--urls', $Url
    ) + $ApplicationArguments
    $process = Start-BackgroundProcess `
        -FilePath 'dotnet' `
        -ArgumentList $arguments `
        -Output $output `
        -ErrorOutput $errorOutput
    $processes.Add($process)
    return $process
}

function Start-Host {
    param(
        [string]$Name,
        [string]$Url,
        [string]$InstanceId
    )

    return Start-DotNetApplication `
        -Name $Name `
        -Project 'samples/WebSocketNotifications.Host' `
        -Url $Url `
        -ApplicationArguments @(
            "--KafkaAdapter:ApplicationName=websocket-notifications-e2e",
            "--KafkaAdapter:InstanceId=$InstanceId",
            "--KafkaConsumerWorkers:Consumers:0:Topics:0=$topic"
        )
}

function Wait-ForHttpOk {
    param(
        [string]$Url,
        [string]$Description
    )

    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    throw "$Description did not become ready at $Url. See logs in $runDirectory."
}

function Stop-Application {
    param([System.Diagnostics.Process]$Process)

    if ($Process -and -not $Process.HasExited) {
        Stop-Process -Id $Process.Id
        [void]$Process.WaitForExit(10000)
    }
    if ($Process) {
        Complete-BackgroundProcessRedirects $Process
    }
}

function Start-Probe {
    param(
        [string]$Name,
        [string[]]$WebSocketUrls,
        [string]$ExpectedMarker,
        [int]$ExpectedCount,
        [string]$ForbiddenMarker = ''
    )

    $output = Join-Path $runDirectory "$Name.out.log"
    $errorOutput = Join-Path $runDirectory "$Name.err.log"
    $arguments = @(
        'scripts/multi-host-e2e-client.mjs',
        ($WebSocketUrls -join ','),
        $ExpectedMarker,
        $ExpectedCount
    )
    if ($ForbiddenMarker) {
        $arguments += $ForbiddenMarker
    }

    $process = Start-BackgroundProcess `
        -FilePath $NodeExecutable `
        -ArgumentList $arguments `
        -Output $output `
        -ErrorOutput $errorOutput
    $processes.Add($process)

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) {
            throw "Client probe $Name exited before connecting. See $errorOutput."
        }

        if ((Test-Path $output) -and (Get-Content -Raw $output) -match '"type":"ready"') {
            return $process
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Client probe $Name did not connect. See $errorOutput and $output."
}

function Wait-ForProbeSuccess {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Name
    )

    if (-not $Process.WaitForExit(95000)) {
        throw "Client probe $Name timed out. See logs in $runDirectory."
    }

    $Process.WaitForExit()
    Complete-BackgroundProcessRedirects $Process
    $probeOutput = Get-Content -Raw (Join-Path $runDirectory "$Name.out.log")
    if ($probeOutput -notmatch '"type":"result"') {
        $probeError = Get-Content -Raw (Join-Path $runDirectory "$Name.err.log")
        throw "Client probe $Name failed. $probeError See logs in $runDirectory."
    }

    $probeOutput | Write-Host
}

function Publish-Notification {
    param([string]$Marker)

    $requestBody = @{
        users = @($userId)
        payload = @{ marker = $Marker }
        key = "user:$userId"
    } | ConvertTo-Json -Depth 4
    return Invoke-RestMethod `
        -Method Post `
        -Uri "$producerBaseUrl/api/notifications/users" `
        -ContentType 'application/json' `
        -Body $requestBody
}

New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null

Push-Location $repositoryRoot
try {
    dotnet build WebSocketNotifications.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Release build failed.'
    }

    docker compose up -d --wait --wait-timeout 120
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker Compose could not start Kafka.'
    }
    $kafkaStarted = $true

    docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --create `
        --topic $topic `
        --partitions 3 `
        --replication-factor 1
    if ($LASTEXITCODE -ne 0) {
        throw "Kafka topic $topic could not be created."
    }

    $producer = Start-DotNetApplication `
        -Name 'producer' `
        -Project 'samples/NotificationProducer' `
        -Url $producerBaseUrl `
        -ApplicationArguments @("--NotificationProducer:Topic=$topic")
    Wait-ForHttpOk -Url $producerBaseUrl -Description 'Notification producer'

    $sharedInstanceId = "shared-$runId"
    $firstHost = Start-Host -Name 'shared-host-1' -Url $firstBaseUrl -InstanceId $sharedInstanceId
    $secondHost = Start-Host -Name 'shared-host-2' -Url $secondBaseUrl -InstanceId $sharedInstanceId
    Wait-ForHttpOk -Url "$firstBaseUrl/health/ready" -Description 'Shared-group host 1 source'
    Wait-ForHttpOk -Url "$secondBaseUrl/health/ready" -Description 'Shared-group host 2 source'
    $sharedMarker = "shared-$runId"
    $sharedProbe = Start-Probe `
        -Name 'shared-probe' `
        -WebSocketUrls @($firstWebSocketUrl, $secondWebSocketUrl) `
        -ExpectedMarker $sharedMarker `
        -ExpectedCount 1
    Publish-Notification -Marker $sharedMarker | Out-Null
    Wait-ForProbeSuccess -Process $sharedProbe -Name 'shared-probe'
    Stop-Application $firstHost
    Stop-Application $secondHost

    $firstHost = Start-Host -Name 'independent-host-1' -Url $firstBaseUrl -InstanceId "first-$runId"
    $secondHost = Start-Host -Name 'independent-host-2' -Url $secondBaseUrl -InstanceId "second-$runId"
    Wait-ForHttpOk -Url "$firstBaseUrl/health/ready" -Description 'Independent host 1 source'
    Wait-ForHttpOk -Url "$secondBaseUrl/health/ready" -Description 'Independent host 2 source'
    $fanOutMarker = "fanout-$runId"
    $fanOutProbe = Start-Probe `
        -Name 'fanout-probe' `
        -WebSocketUrls @($firstWebSocketUrl, $secondWebSocketUrl) `
        -ExpectedMarker $fanOutMarker `
        -ExpectedCount 2
    Publish-Notification -Marker $fanOutMarker | Out-Null
    Wait-ForProbeSuccess -Process $fanOutProbe -Name 'fanout-probe'

    Stop-Application $firstHost
    $continuityMarker = "continuity-$runId"
    $continuityProbe = Start-Probe `
        -Name 'continuity-probe' `
        -WebSocketUrls @($secondWebSocketUrl) `
        -ExpectedMarker $continuityMarker `
        -ExpectedCount 1
    Publish-Notification -Marker $continuityMarker | Out-Null
    Wait-ForProbeSuccess -Process $continuityProbe -Name 'continuity-probe'
    Stop-Application $secondHost

    $offlineMarker = "offline-$runId"
    Publish-Notification -Marker $offlineMarker | Out-Null
    docker compose stop kafka
    if ($LASTEXITCODE -ne 0) {
        throw 'Kafka could not be stopped for the restart/no-replay phase.'
    }
    $restartedHost = Start-Host `
        -Name 'restarted-host' `
        -Url $firstBaseUrl `
        -InstanceId "restarted-$runId"
    Wait-ForHttpOk -Url $firstBaseUrl -Description 'Restarted host liveness'
    $liveMarker = "live-$runId"
    $restartProbe = Start-Probe `
        -Name 'restart-probe' `
        -WebSocketUrls @($firstWebSocketUrl) `
        -ExpectedMarker $liveMarker `
        -ExpectedCount 1 `
        -ForbiddenMarker $offlineMarker
    docker compose up -d --wait --wait-timeout 120
    if ($LASTEXITCODE -ne 0) {
        throw 'Kafka could not restart for the restart/no-replay phase.'
    }
    Wait-ForHttpOk -Url "$firstBaseUrl/health/ready" -Description 'Restarted host source'
    Publish-Notification -Marker $liveMarker | Out-Null
    Wait-ForProbeSuccess -Process $restartProbe -Name 'restart-probe'

    $passed = $true
    Write-Host "MULTI-HOST E2E PASSED: shared-group negative control, independent fan-out, host continuity, and no-replay restart ($runId)"
}
finally {
    foreach ($process in $processes) {
        Stop-Application $process
    }

    if ($kafkaStarted) {
        docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh `
            --bootstrap-server localhost:9092 `
            --delete `
            --topic $topic 2>$null
        if (-not $LeaveKafkaRunning) {
            docker compose down
        }
    }

    Pop-Location

    if ($passed) {
        $resolvedRunDirectory = [System.IO.Path]::GetFullPath($runDirectory)
        $resolvedE2eRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot '.e2e'))
        if ($resolvedRunDirectory.StartsWith($resolvedE2eRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedRunDirectory -Recurse -Force
        }
    }
}
