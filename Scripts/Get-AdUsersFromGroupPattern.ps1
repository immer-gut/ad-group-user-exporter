param(
    [string]$GroupPattern,

    [string]$SearchBase,

    [string]$Server,

    [switch]$OnlyEnabled,

    [string]$CompareUserA,

    [string]$CompareUserB
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Import-Module ActiveDirectory -ErrorAction Stop

function ConvertTo-LdapFilterValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray()) {
        switch ($character) {
            '(' { [void]$builder.Append('\28') }
            ')' { [void]$builder.Append('\29') }
            '\' { [void]$builder.Append('\5c') }
            ([char]0) { [void]$builder.Append('\00') }
            default { [void]$builder.Append($character) }
        }
    }

    $builder.ToString()
}

function ConvertTo-LdapLiteralFilterValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    $builder = [System.Text.StringBuilder]::new()
    foreach ($character in $Value.ToCharArray()) {
        switch ($character) {
            '(' { [void]$builder.Append('\28') }
            ')' { [void]$builder.Append('\29') }
            '\' { [void]$builder.Append('\5c') }
            '*' { [void]$builder.Append('\2a') }
            ([char]0) { [void]$builder.Append('\00') }
            default { [void]$builder.Append($character) }
        }
    }

    $builder.ToString()
}

function New-AdCommandParameters {
    param(
        [string]$Identity,
        [string[]]$Properties
    )

    $parameters = @{}
    if ($Identity) {
        $parameters.Identity = $Identity
    }
    if ($Properties) {
        $parameters.Properties = $Properties
    }
    if ($Server) {
        $parameters.Server = $Server
    }

    $parameters
}

function Resolve-GroupMembers {
    param(
        [Parameter(Mandatory = $true)]$Group,
        [Parameter(Mandatory = $true)][string[]]$Path,
        [Parameter(Mandatory = $true)][hashtable]$VisitedGroups,
        [System.Collections.Generic.List[object]]$Results,
        [hashtable]$SeenUsers
    )

    if ($VisitedGroups.ContainsKey($Group.DistinguishedName)) {
        return
    }

    $VisitedGroups[$Group.DistinguishedName] = $true
    $memberParameters = New-AdCommandParameters -Identity $Group.DistinguishedName

    foreach ($member in Get-ADGroupMember @memberParameters) {
        if ($member.objectClass -eq 'group') {
            $nestedGroupParameters = New-AdCommandParameters -Identity $member.DistinguishedName -Properties @('DistinguishedName', 'Name')
            $nestedGroup = Get-ADGroup @nestedGroupParameters
            Resolve-GroupMembers -Group $nestedGroup -Path ($Path + $nestedGroup.Name) -VisitedGroups $VisitedGroups -Results $Results -SeenUsers $SeenUsers
            continue
        }

        if ($member.objectClass -ne 'user') {
            continue
        }

        $userParameters = New-AdCommandParameters -Identity $member.DistinguishedName -Properties @(
            'SamAccountName',
            'DisplayName',
            'Mail',
            'Enabled',
            'Department',
            'Title',
            'DistinguishedName'
        )
        $user = Get-ADUser @userParameters

        if ($OnlyEnabled -and -not $user.Enabled) {
            continue
        }

        $key = '{0}|{1}' -f $Group.DistinguishedName, $user.DistinguishedName
        if ($SeenUsers.ContainsKey($key)) {
            continue
        }
        $SeenUsers[$key] = $true

        $Results.Add([pscustomobject]@{
            GroupName = $Path[0]
            GroupPath = ($Path -join ' > ')
            SamAccountName = [string]$user.SamAccountName
            DisplayName = [string]$user.DisplayName
            Mail = [string]$user.Mail
            Enabled = [bool]$user.Enabled
            Department = [string]$user.Department
            Title = [string]$user.Title
            DistinguishedName = [string]$user.DistinguishedName
        })
    }
}

function Get-ComparisonUser {
    param([Parameter(Mandatory = $true)][string]$Identity)

    $userParameters = New-AdCommandParameters -Identity $Identity -Properties @(
        'DistinguishedName',
        'SamAccountName',
        'Name',
        'PrimaryGroupID'
    )
    Get-ADUser @userParameters
}

function Get-UserGroups {
    param([Parameter(Mandatory = $true)]$User)

    $escapedDn = ConvertTo-LdapLiteralFilterValue -Value $User.DistinguishedName
    $groupParameters = @{
        LDAPFilter = "(member:1.2.840.113556.1.4.1941:=$escapedDn)"
        Properties = @('DistinguishedName', 'Name')
    }
    if ($SearchBase) {
        $groupParameters.SearchBase = $SearchBase
    }
    if ($Server) {
        $groupParameters.Server = $Server
    }

    $groups = @(Get-ADGroup @groupParameters)

    if ($User.PrimaryGroupID) {
        try {
            $primaryGroupParameters = @{
                LDAPFilter = "(primaryGroupToken=$($User.PrimaryGroupID))"
                Properties = @('DistinguishedName', 'Name')
            }
            if ($SearchBase) {
                $primaryGroupParameters.SearchBase = $SearchBase
            }
            if ($Server) {
                $primaryGroupParameters.Server = $Server
            }

            $groups += @(Get-ADGroup @primaryGroupParameters)
        } catch { }
    }

    $byDn = @{}
    foreach ($group in $groups) {
        if (-not $byDn.ContainsKey($group.DistinguishedName)) {
            $byDn[$group.DistinguishedName] = $group
        }
    }

    $byDn.Values | Sort-Object Name
}

function Compare-UserGroups {
    param(
        [Parameter(Mandatory = $true)][string]$UserAIdentity,
        [Parameter(Mandatory = $true)][string]$UserBIdentity
    )

    $userA = Get-ComparisonUser -Identity $UserAIdentity
    $userB = Get-ComparisonUser -Identity $UserBIdentity
    $userALabel = if ($userA.SamAccountName) { [string]$userA.SamAccountName } else { [string]$UserAIdentity }
    $userBLabel = if ($userB.SamAccountName) { [string]$userB.SamAccountName } else { [string]$UserBIdentity }

    $groupsA = Get-UserGroups -User $userA
    $groupsB = Get-UserGroups -User $userB
    $mapA = @{}
    $mapB = @{}
    $allDns = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($group in $groupsA) {
        $mapA[$group.DistinguishedName] = $group
        [void]$allDns.Add($group.DistinguishedName)
    }
    foreach ($group in $groupsB) {
        $mapB[$group.DistinguishedName] = $group
        [void]$allDns.Add($group.DistinguishedName)
    }

    foreach ($dn in $allDns) {
        $inA = $mapA.ContainsKey($dn)
        $inB = $mapB.ContainsKey($dn)
        $group = if ($inA) { $mapA[$dn] } else { $mapB[$dn] }
        $status = if ($inA -and $inB) { 'Beide' } elseif ($inA) { "Nur $userALabel" } else { "Nur $userBLabel" }

        [pscustomobject]@{
            Status = $status
            GroupName = [string]$group.Name
            UserA = if ($inA) { 'Ja' } else { 'Nein' }
            UserB = if ($inB) { 'Ja' } else { 'Nein' }
            DistinguishedName = [string]$group.DistinguishedName
        }
    }
}

if ($CompareUserA -or $CompareUserB) {
    if ([string]::IsNullOrWhiteSpace($CompareUserA) -or [string]::IsNullOrWhiteSpace($CompareUserB)) {
        throw 'Fuer den User-Vergleich muessen -CompareUserA und -CompareUserB angegeben werden.'
    }

    $comparisonResults = @(Compare-UserGroups -UserAIdentity $CompareUserA -UserBIdentity $CompareUserB | Sort-Object Status, GroupName)
    if ($comparisonResults.Count -eq 0) {
        '[]'
    } else {
        ConvertTo-Json -InputObject $comparisonResults -Depth 6
    }
    return
}

if ([string]::IsNullOrWhiteSpace($GroupPattern)) {
    throw 'Bitte -GroupPattern angeben.'
}

$ldapPattern = ConvertTo-LdapFilterValue -Value $GroupPattern
$groupParameters = @{
    LDAPFilter = "(name=$ldapPattern)"
    Properties = @('DistinguishedName', 'Name')
}
if ($SearchBase) {
    $groupParameters.SearchBase = $SearchBase
}
if ($Server) {
    $groupParameters.Server = $Server
}

$groups = Get-ADGroup @groupParameters | Sort-Object Name
$results = [System.Collections.Generic.List[object]]::new()
$seenUsers = @{}

foreach ($group in $groups) {
    Resolve-GroupMembers -Group $group -Path @($group.Name) -VisitedGroups @{} -Results $results -SeenUsers $seenUsers
}

if ($results.Count -eq 0) {
    '[]'
} else {
    ConvertTo-Json -InputObject $results -Depth 6
}
