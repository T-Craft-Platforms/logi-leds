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
$maxCommitsPerCategory = 5
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
    FEATURES = [System.Collections.Generic.List[object]]::new()
    FIXES = [System.Collections.Generic.List[object]]::new()
    OTHER_CHANGES = [System.Collections.Generic.List[object]]::new()
}

$commitOrder = 0
foreach ($commit in $commits) {
    $parts = $commit -split "`t", 3
    if ($parts.Count -ne 3) {
        continue
    }

    $subject = $parts[0].Trim()
    $shortSha = $parts[1].Trim()
    $sha = $parts[2].Trim()
    $line = "- [$subject](https://github.com/$Repository/commit/$sha) ($shortSha)"

    $numstat = @(git diff-tree --no-commit-id --numstat -r --root $sha)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to calculate changed lines for commit '$sha'."
    }

    $changedLines = 0
    foreach ($fileStat in $numstat) {
        $statParts = $fileStat -split "`t", 3
        if ($statParts.Count -eq 3) {
            $added = 0
            $deleted = 0
            if ([int]::TryParse($statParts[0], [ref] $added)) { $changedLines += $added }
            if ([int]::TryParse($statParts[1], [ref] $deleted)) { $changedLines += $deleted }
        }
    }

    $entry = [pscustomobject]@{
        Line = $line
        ChangedLines = $changedLines
        CommitOrder = $commitOrder
    }
    $commitOrder++

    if ($subject -match '^(feat|feature)(\([^)]*\))?!?:\s*') {
        $groups.FEATURES.Add($entry)
    }
    elseif ($subject -match '^(fix|bugfix)(\([^)]*\))?!?:\s*') {
        $groups.FIXES.Add($entry)
    }
    else {
        $groups.OTHER_CHANGES.Add($entry)
    }
}

foreach ($name in @('FEATURES', 'FIXES', 'OTHER_CHANGES')) {
    $sortedEntries = @($groups[$name] | Sort-Object -Property @{ Expression = 'ChangedLines'; Descending = $true }, @{ Expression = 'CommitOrder'; Descending = $false })
    $selectedEntries = @($sortedEntries | Select-Object -First $maxCommitsPerCategory)
    $groups[$name] = [System.Collections.Generic.List[string]]::new()

    if ($selectedEntries.Count -eq 0) {
        $groups[$name].Add('- No changes in this category.')
    }
    else {
        foreach ($entry in $selectedEntries) {
            $groups[$name].Add($entry.Line)
        }

        $omittedCount = $sortedEntries.Count - $selectedEntries.Count
        if ($omittedCount -gt 0) {
            $groups[$name].Add("- $omittedCount additional commit(s) omitted (limit: $maxCommitsPerCategory).")
        }
    }
}

$replacements = @{
    VERSION = $Tag
    RELEASE_DATE = [DateTime]::UtcNow.ToString('yyyy-MM-dd')
    FEATURES = $groups.FEATURES -join "`n"
    FIXES = $groups.FIXES -join "`n"
    OTHER_CHANGES = $groups.OTHER_CHANGES -join "`n"
    COMPARE_LINK = "[View the full comparison]($compareUrl)"
}

foreach ($placeholder in $replacements.Keys) {
    $template = $template.Replace("{{$placeholder}}", [string] $replacements[$placeholder])
}

Set-Content -LiteralPath $OutputPath -Value $template -Encoding utf8
