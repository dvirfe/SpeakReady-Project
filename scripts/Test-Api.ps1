#requires -Version 7.0
param([string]$BaseUrl = 'http://localhost:5214')
$ErrorActionPreference = 'Stop'
$passed = 0
$createdId = $null
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$username = 'check_' + $suffix
$email = $username + '@speakready.example'
$password = 'Demo-' + [Guid]::NewGuid().ToString('N') + '!'

function Check-Request([string]$Method, [string]$Path, [int]$Expected, $Body = $null) {
    $params = @{ Uri = $BaseUrl.TrimEnd('/') + $Path; Method = $Method; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) {
        $params.ContentType = 'application/json; charset=utf-8'
        $params.Body = [System.Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 5))
    }
    $response = Invoke-WebRequest @params
    if ([int]$response.StatusCode -ne $Expected) {
        throw "$Method $Path expected $Expected but received $($response.StatusCode)."
    }
    $script:passed++
    Write-Host "PASS $Method $Path => $Expected"
    if ($response.Content) { return ($response.Content | ConvertFrom-Json) }
}

try {
    $null = Check-Request GET '/health' 200
    $all = @(Check-Request GET '/api/users' 200)
    if ($all.Count -lt 10) { throw 'Expected at least 10 seeded users. Configure Seed:DemoPassword before the first run.' }
    if ($all[0].PSObject.Properties.Name -contains 'passwordHash') { throw 'Password hash was exposed in JSON.' }
    $null = Check-Request GET '/api/users/count-by-role' 200
    $found = Check-Request GET '/api/users/username-exists?username=admin' 200
    if (-not $found) { throw 'Seeded admin was not found.' }
    $injection = [uri]::EscapeDataString("notreal' OR 1=1 --")
    $found = Check-Request GET ("/api/users/username-exists?username=$injection") 200
    if ($found) { throw 'SQL-like input unexpectedly matched a user.' }

    $body = @{ username=$username; fullName='API test user'; email=$email; password=$password }
    $created = Check-Request POST '/api/users' 201 $body
    $createdId = $created.id
    if ($created.role -ne 'Student') { throw 'New account must default to Student.' }
    $null = Check-Request GET "/api/users/$createdId" 200
    $null = Check-Request POST '/api/users' 409 $body
    $body.username = 'other_' + $suffix
    $null = Check-Request POST '/api/users' 409 $body
    $null = Check-Request POST '/api/users' 400 @{ username='x'; fullName=''; email='bad'; password='x' }
    $null = Check-Request POST '/api/users/login' 200 @{username=$username; password=$password}
    $null = Check-Request POST '/api/users/login' 200 @{username=$email; password=$password}
    $null = Check-Request POST '/api/users/login' 401 @{username=$username; password=$password.ToLowerInvariant()}
    $null = Check-Request PUT "/api/users/$createdId/role" 204 @{role='Teacher'}
    $user = Check-Request GET "/api/users/$createdId" 200
    if ($user.role -ne 'Teacher' -or $user.teacherRequestStatus -ne 'Approved') { throw 'Teacher role/status mismatch.' }
    $null = Check-Request PUT "/api/users/$createdId/role" 400 @{role=99}
    $null = Check-Request PUT "/api/users/$createdId/email" 409 @{email='admin@speakready.example'}
    $newEmail = 'updated_' + $email
    $null = Check-Request PUT "/api/users/$createdId/email" 204 @{email=$newEmail}
    $null = Check-Request POST '/api/users/login' 200 @{username=$newEmail; password=$password}
    $newPassword = 'New-' + $password
    $null = Check-Request PUT "/api/users/$createdId/password" 204 @{password=$newPassword}
    $null = Check-Request POST '/api/users/login' 401 @{username=$username; password=$password}
    $null = Check-Request POST '/api/users/login' 200 @{username=$username; password=$newPassword}
    $null = Check-Request PUT "/api/users/$createdId/active" 204 @{isActive=$false}
    $null = Check-Request POST '/api/users/login' 401 @{username=$username; password=$newPassword}
    $user = Check-Request GET "/api/users/$createdId" 200
    if ($user.isActive) { throw 'User is still active.' }
    $null = Check-Request PUT "/api/users/$createdId/active" 204 @{isActive=$true}
    $null = Check-Request POST '/api/users/login' 200 @{username=$username; password=$newPassword}
    $null = Check-Request GET '/api/users/2147483647' 404
    $null = Check-Request PUT '/api/users/2147483647/email' 404 @{email='missing@speakready.example'}
    Write-Host "$passed HTTP checks passed. Test user: $username (ID $createdId)."
}
finally {
    # משביתים רק את משתמש הבדיקה שנוצר כאן. לא מוחקים ולא משנים משתמשים קיימים.
    if ($null -ne $createdId) {
        $null = Check-Request PUT "/api/users/$createdId/active" 204 @{isActive=$false}
    }
}
