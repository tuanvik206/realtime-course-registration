$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $repositoryRoot '.env'
$examplePath = Join-Path $repositoryRoot '.env.example'

if (Test-Path -LiteralPath $envPath) {
    throw '.env already exists. It was left unchanged.'
}

if (-not (Test-Path -LiteralPath $examplePath)) {
    throw '.env.example is missing.'
}

function New-Secret {
    param([int] $ByteCount = 32)

    $bytes = New-Object 'byte[]' $ByteCount
    $generator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$values = [ordered]@{
    POSTGRES_ADMIN_PASSWORD = New-Secret
    IDENTITY_DB_PASSWORD = New-Secret
    CAPACITY_DB_PASSWORD = New-Secret
    AUDIT_DB_PASSWORD = New-Secret
    RABBITMQ_USER = 'course_app'
    RABBITMQ_PASSWORD = New-Secret
    JWT_SIGNING_KEY = New-Secret
    JWT_ISSUER = 'course-registration'
    JWT_AUDIENCE = 'course-web'
    AUDIT_INTERNAL_API_KEY = New-Secret
    DEMO_SEED_ENABLED = 'true'
    DEMO_ADMIN_PASSWORD = New-Secret
    DEMO_STUDENT_PASSWORD = New-Secret
    WEB_PORT = '3000'
    RABBITMQ_MANAGEMENT_PORT = '15672'
}

$lines = foreach ($entry in $values.GetEnumerator()) {
    '{0}={1}' -f $entry.Key, $entry.Value
}

[System.IO.File]::WriteAllLines($envPath, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Host 'Created .env with random local secrets. The file is ignored by Git; do not share it.'
