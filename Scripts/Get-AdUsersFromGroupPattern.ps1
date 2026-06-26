param(
    [Parameter(Mandatory = $true)]
    [string]$GroupPattern,

    [string]$SearchBase,

    [string]$Server,

    [switch]$OnlyEnabled
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
