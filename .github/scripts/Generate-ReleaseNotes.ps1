param(
    [Parameter(Mandatory = $true)]
    [string] $Tag,

    [Parameter(Mandatory = $true)]
    [string] $Repository,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'

if ($Tag -notmatch '^v(?<version>\d+\.\d+\.\d+)$') {
    throw "Release tags must use the vX.Y.Z format; received '$Tag'."
}

$version = $Matches.version
$templatePath = Join-Path $PSScriptRoot '..\RELEASE_NOTES_TEMPLATE.md'
$template = Get-Content -LiteralPath $templatePath -Raw
$tags = @(git tag --merged $Tag --list 'v[0-9]*' --sort=-version:refname)
if ($LASTEXITCODE -ne 0) {
    throw 'Failed to list Git tags.'
}

$previousTag = $null
foreach ($candidate in $tags) {
    if ($candidate -match '^v(?<candidateVersion>\d+\.\d+\.\d+)$' -and
        [version] $Matches.candidateVersion -lt [version] $version) {
        $previousTag = $candidate
        break
    }
}

if ($previousTag) {
    $commits = @(git log --no-merges --reverse "${previousTag}..${Tag}" --format='%s%x09%h%x09%H')
    $compareUrl = "https://github.com/$Repository/compare/$previousTag...$Tag"
}
else {
    $commits = @(git log --no-merges --reverse $Tag --format='%s%x09%h%x09%H')
    $compareUrl = "https://github.com/$Repository/commits/$Tag"
}

if ($LASTEXITCODE -ne 0) {
    throw "Failed to collect commits for '$Tag'."
}

$groups = @{
    HIGHLIGHTS = [System.Collections.Generic.List[string]]::new()
    IMPROVEMENTS = [System.Collections.Generic.List[string]]::new()
    FIXES = [System.Collections.Generic.List[string]]::new()
    OTHER_CHANGES = [System.Collections.Generic.List[string]]::new()
}

foreach ($commit in $commits) {
    $parts = $commit -split "`t", 3
    if ($parts.Count -ne 3) {
        continue
    }

    $subject = $parts[0].Trim()
    $shortSha = $parts[1].Trim()
    $sha = $parts[2].Trim()
    $line = "- [$subject](https://github.com/$Repository/commit/$sha) ($shortSha)"

    if ($subject -match '^(feat|feature)(\([^)]*\))?!?:\s*') {
        $groups.HIGHLIGHTS.Add($line)
    }
    elseif ($subject -match '^(fix|bugfix)(\([^)]*\))?!?:\s*') {
        $groups.FIXES.Add($line)
    }
    elseif ($subject -match '^(perf|refactor|improve|improvement)(\([^)]*\))?!?:\s*') {
        $groups.IMPROVEMENTS.Add($line)
    }
    else {
        $groups.OTHER_CHANGES.Add($line)
    }
}

foreach ($name in @('HIGHLIGHTS', 'IMPROVEMENTS', 'FIXES', 'OTHER_CHANGES')) {
    if ($groups[$name].Count -eq 0) {
        $groups[$name].Add('- No changes in this category.')
    }
}

$replacements = @{
    VERSION = $Tag
    RELEASE_DATE = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
    HIGHLIGHTS = $groups.HIGHLIGHTS -join "`n"
    IMPROVEMENTS = $groups.IMPROVEMENTS -join "`n"
    FIXES = $groups.FIXES -join "`n"
    OTHER_CHANGES = $groups.OTHER_CHANGES -join "`n"
    COMPARE_LINK = "[View the full comparison]($compareUrl)"
}

foreach ($placeholder in $replacements.Keys) {
    $template = $template.Replace("{{$placeholder}}", [string] $replacements[$placeholder])
}

Set-Content -LiteralPath $OutputPath -Value $template -Encoding utf8
