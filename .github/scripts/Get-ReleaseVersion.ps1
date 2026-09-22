[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Commit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tags = @(git tag --list)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not read release tags.'
}

$commitTags = @(git tag --points-at $Commit)
if ($LASTEXITCODE -ne 0) {
    throw "Could not read tags for commit $Commit."
}

$versions = @(
    $tags |
        Where-Object { $_ -cmatch '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' } |
        Sort-Object { [version]$_.Substring(1) } -Descending
)

$existingTag = $versions |
    Where-Object { $commitTags -ccontains $_ } |
    Select-Object -First 1

if ($existingTag) {
    $tag = $existingTag
}
elseif ($versions.Count -eq 0) {
    $tag = 'v1.0.0'
}
else {
    $latest = [version]$versions[0].Substring(1)
    $tag = "v$($latest.Major).$($latest.Minor).$($latest.Build + 1)"
}

[pscustomobject]@{
    Tag = $tag
    Version = $tag.Substring(1)
    TagExists = [bool]$existingTag
}
