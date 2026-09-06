[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = [Guid]::NewGuid().ToString('N')
$userId = "e2e-$runId"
$baseUrl = 'http://127.0.0.1:5087'
$producerBaseUrl = 'http://127.0.0.1:5097'
$webSocketUrl = "ws://127.0.0.1:5087/ws/notifications?userId=$userId"
$e2eDirectory = Join-Path $repositoryRoot '.e2e'
$hostOutput = Join-Path $e2eDirectory 'host.out.log'
$hostError = Join-Path $e2eDirectory 'host.err.log'
$producerOutput = Join-Path $e2eDirectory 'producer.out.log'
$producerError = Join-Path $e2eDirectory 'producer.err.log'
$clientOutput = Join-Path $e2eDirectory 'client.out.log'
$clientError = Join-Path $e2eDirectory 'client.err.log'

New-Item -ItemType Directory -Force -Path $e2eDirectory | Out-Null

Push-Location $repositoryRoot
try {
    docker compose up -d --wait --wait-timeout 120
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose could not start the repository Kafka service."
    }

    docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 `
        --create `
        --if-not-exists `
        --topic notifications `
        --partitions 3 `
        --replication-factor 1
    if ($LASTEXITCODE -ne 0) {
        throw "The notifications Kafka topic could not be created."
    }

    $hostProcess = Start-Process -FilePath 'dotnet' -WindowStyle Hidden -PassThru `
        -ArgumentList @(
            'run',
            '--project', 'samples/WebSocketNotifications.Host',
            '--configuration', 'Release',
            '--no-build',
            '--urls', $baseUrl
        ) `
        -RedirectStandardOutput $hostOutput `
        -RedirectStandardError $hostError

    $ready = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $baseUrl -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $ready) {
        throw "The sample host did not become ready. See $hostError and $hostOutput."
    }

    $producerProcess = Start-Process -FilePath 'dotnet' -WindowStyle Hidden -PassThru `
        -ArgumentList @(
            'run',
            '--project', 'samples/NotificationProducer',
            '--configuration', 'Release',
            '--no-build',
            '--urls', $producerBaseUrl
        ) `
        -RedirectStandardOutput $producerOutput `
        -RedirectStandardError $producerError

    $producerReady = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $producerBaseUrl -UseBasicParsing -TimeoutSec 1
            if ($response.StatusCode -eq 200) {
                $producerReady = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $producerReady) {
        throw "The producer API did not become ready. See $producerError and $producerOutput."
    }

    $clientProcess = Start-Process -FilePath 'node' -WindowStyle Hidden -PassThru `
        -ArgumentList @('scripts/live-e2e-client.mjs', $webSocketUrl, $runId) `
        -RedirectStandardOutput $clientOutput `
        -RedirectStandardError $clientError

    Start-Sleep -Seconds 1
    $requestBody = @{
        targets = @($userId)
        payload = @{ text = 'Hello from Kafka' }
        key = "user:$userId"
    } | ConvertTo-Json -Depth 4
    $publishResult = Invoke-RestMethod `
        -Method Post `
        -Uri "$producerBaseUrl/api/notifications/users" `
        -ContentType 'application/json' `
        -Body $requestBody

    if (-not $clientProcess.WaitForExit(30000)) {
        throw "The Next.js WebSocket client timed out. See $clientError and $clientOutput."
    }

    $clientResult = Get-Content -Raw $clientOutput
    if ($clientResult -notmatch [Regex]::Escape("`"runId`":`"$runId`"") -or
        $clientResult -notmatch [Regex]::Escape("`"messageId`":`"$($publishResult.messageId)`"") -or
        $clientResult -notmatch '"type":"notification"') {
        throw "The Next.js WebSocket client did not record the expected notification. See $clientError and $clientOutput."
    }

    Write-Host $clientResult.Trim()
    Write-Host "LIVE E2E PASSED: producer -> Kafka -> host -> WebSocket -> Next.js client ($runId)"
}
finally {
    if ($clientProcess -and -not $clientProcess.HasExited) {
        Stop-Process -Id $clientProcess.Id
    }

    if ($hostProcess -and -not $hostProcess.HasExited) {
        Stop-Process -Id $hostProcess.Id
    }

    if ($producerProcess -and -not $producerProcess.HasExited) {
        Stop-Process -Id $producerProcess.Id
    }

    Pop-Location
}
