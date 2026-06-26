# AD Group User Exporter

Kleine Windows-App zum Auslesen von AD-Benutzern aus Gruppen, deren Name einem Muster entspricht. Die GUI ist in C# / WPF gebaut, die AD-Abfrage laeuft ueber ein separates PowerShell-Script. Ja, zwei Welten, aber diesmal ausnahmsweise mit Absicht.

## Funktionen

- Gruppenmuster frei setzen, z. B. `abc*_1a*`
- Gruppenmuster-Verlauf speichern und Eintraege entfernen
- Hell-/Dunkel-Theme umschalten und merken
- optional `SearchBase` und Domain Controller angeben
- rekursive Aufloesung verschachtelter Gruppen
- Ergebnis in einer Tabelle anzeigen
- sichtbares Ergebnis live filtern
- sichtbare `GroupName`-Werte in die Zwischenablage kopieren
- gefiltertes Ergebnis als CSV exportieren
- optional nur aktive Benutzer ausgeben
- zwei Benutzer aus dem geladenen Ergebnis vergleichen und gemeinsame bzw. unterschiedliche Gruppen anzeigen

## Voraussetzungen

- Windows
- .NET 9 SDK zum Bauen
- PowerShell 5.1
- ActiveDirectory PowerShell-Modul, z. B. RSAT Active Directory Tools
- Leserechte auf die relevanten AD-Gruppen und Benutzer

RSAT Active Directory Tools installieren:

```powershell
Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0
```

## Download

Die fertige Windows-Version liegt nicht im Git-Repository, sondern als GitHub Release Asset:

```text
https://github.com/immer-gut/ad-group-user-exporter/releases/latest
```

Direkter Download der aktuellen ZIP:

```text
https://github.com/immer-gut/ad-group-user-exporter/releases/download/v0.2.1/AdGroupUserExporter-win-x64.zip
```

## Start in Visual Studio

1. Repository klonen.
2. `AdGroupUserExporter.csproj` in Visual Studio oeffnen.
3. Projekt starten.

## Start per CLI

```powershell
dotnet run --project .\AdGroupUserExporter.csproj
```

## Bedienung

1. Gruppenmuster eintragen oder aus dem Verlauf waehlen, z. B. `abc*_1a*`.
2. Optional `SearchBase` eintragen, z. B. `OU=Groups,DC=example,DC=local`.
3. Optional Domain Controller eintragen, z. B. `dc01.example.local`.
4. Optional Theme auf `Hell` oder `Dunkel` setzen.
5. `Suchen` klicken.
6. Ergebnis ueber das Filterfeld einschraenken.
7. Sichtbare `GroupName`-Werte kopieren oder das sichtbare Ergebnis als CSV exportieren.
8. Fuer einen Gruppenvergleich zuerst das Gruppenmuster laden, dann zwei Benutzer aus den Auswahllisten `User 1` und `User 2` waehlen oder per Teiltext suchen und `User vergleichen` klicken.

Der Gruppenmuster-Verlauf wird unter `%AppData%\AdGroupUserExporter\group-pattern-history.json` gespeichert. Der Button `Eintrag entfernen` entfernt das aktuell ausgewaehlte oder eingetragene Muster aus dem Verlauf. Ja, auch Verlaufslisten brauchen irgendwann eine Muellabfuhr.

Die Theme-Auswahl wird unter `%AppData%\AdGroupUserExporter\settings.json` gespeichert.

## PowerShell-Script

Die AD-Logik liegt in:

```text
Scripts/Get-AdUsersFromGroupPattern.ps1
```

Die App ruft das Script auf und liest JSON aus `stdout`. Fehler werden ueber `stderr` an die GUI gemeldet.

Direkter Script-Test:

```powershell
.\Scripts\Get-AdUsersFromGroupPattern.ps1 -GroupPattern "abc*_1a*" -OnlyEnabled
```

Mit SearchBase:

```powershell
.\Scripts\Get-AdUsersFromGroupPattern.ps1 `
  -GroupPattern "abc*_1a*" `
  -SearchBase "OU=Groups,DC=example,DC=local" `
  -OnlyEnabled
```

## Build

```powershell
dotnet build
```

Release-Build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-x64
```

## Ergebnis-Spalten

- `GroupName`
- `GroupPath`
- `SamAccountName`
- `DisplayName`
- `Mail`
- `Enabled`
- `Department`
- `Title`
- `DistinguishedName`

## Vergleichs-Spalten

- `Status`
- `GroupName`
- `UserA`
- `UserB`
- `GroupPathA`
- `GroupPathB`

## Hinweise

Der Export bezieht sich immer auf das aktuell gefilterte, sichtbare Ergebnis. Genau so sollte es sein, auch wenn manche Tools offenbar der Meinung sind, ein Filter sei nur dekorative Kunst.
