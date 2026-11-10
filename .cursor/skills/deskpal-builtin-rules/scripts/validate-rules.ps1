# Validates builtin-rules.json. Exit code 1 when any problem is found.
param([string]$Path = (Join-Path $PSScriptRoot '..\..\..\..\src\DeskPal.Server\Features\Rules\builtin-rules.json'))

$json = [System.IO.File]::ReadAllText((Resolve-Path $Path), [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$categories = 'Work','Meeting','Communication','Video','Music','Shopping','Social','News','Game','Reading','Other'
$kinds = 'Process','Domain','TitleKeyword'
$browsers = 'chrome','msedge','firefox','brave','opera','vivaldi','360se','360chrome','qqbrowser','sogouexplorer','2345explorer'
$problems = New-Object System.Collections.Generic.List[string]
$seen = @{}

if (-not ($json.version -is [int] -or $json.version -is [long]) -or $json.version -lt 1) { $problems.Add("version must be a positive integer") }

foreach ($r in $json.rules) {
    $label = "[$($r.kind)] $($r.pattern)"
    if ($kinds -notcontains $r.kind) { $problems.Add("${label}: invalid kind") }
    if ($categories -notcontains $r.category) { $problems.Add("${label}: category '$($r.category)' is not an ActivityCategory member") }
    if ([string]::IsNullOrWhiteSpace($r.name)) { $problems.Add("${label}: missing name") }
    if ($r.pattern -cne $r.pattern.Trim().ToLowerInvariant() -and $r.kind -ne 'TitleKeyword') { $problems.Add("${label}: must be lower case without surrounding spaces") }
    switch ($r.kind) {
        'Process' {
            if ($r.pattern -like '*.exe') { $problems.Add("${label}: drop the .exe suffix") }
            if ($browsers -contains $r.pattern) { $problems.Add("${label}: browsers are classified by domain, not by process") }
        }
        'Domain' {
            if ($r.pattern -match '^(https?://|www\.)' -or $r.pattern -match '[/:]') { $problems.Add("${label}: host only, no scheme, www., path or port") }
        }
    }
    $key = "$($r.kind)|$($r.pattern.Trim().ToLowerInvariant())"
    if ($seen.ContainsKey($key)) { $problems.Add("${label}: duplicate") } else { $seen[$key] = $true }
}

if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host $_ }
    Write-Host "$($problems.Count) problem(s)"
    exit 1
}
Write-Host "OK: version $($json.version), $($json.rules.Count) rules"