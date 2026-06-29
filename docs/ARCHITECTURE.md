# Architecture

## Ueberblick

AD Group User Exporter besteht aus drei einfachen Teilen:

1. WPF-Oberflaeche
2. PowerShell-Script fuer AD-Abfragen
3. Datenmodelle fuer Anzeige, Filter, Vergleich und Export

```text
WPF GUI
  |
  | startet powershell.exe
  v
Scripts/Get-AdUsersFromGroupPattern.ps1
  |
  | nutzt ActiveDirectory-Modul
  v
Active Directory

PowerShell stdout JSON
  |
  v
WPF GUI DataGrid / Filter / Vergleich / CSV
```

## WPF-App

Die zentrale Datei ist `MainWindow.xaml.cs`.

Aufgaben:

- Eingaben aus der UI lesen
- PowerShell-Prozess starten
- JSON deserialisieren
- Ergebnisse in `ObservableCollection<AdUserResult>` laden
- sichtbare Ergebnisliste ueber `ICollectionView` filtern
- User-Auswahllisten aus geladenen Ergebnissen erzeugen
- zwei Benutzer aus der geladenen Liste vergleichen
- Kopieren und CSV-Export ausfuehren
- Theme und Verlauf speichern

Die Oberflaeche liegt in `MainWindow.xaml`.

## PowerShell-Script

`Scripts/Get-AdUsersFromGroupPattern.ps1` enthaelt die AD-Logik.

Aufgaben:

- ActiveDirectory-Modul laden
- Gruppen per LDAP-Filter suchen
- optional `SearchBase` und `Server` verwenden
- Gruppenmitglieder rekursiv aufloesen
- Benutzerattribute lesen
- optional nur aktive Benutzer ausgeben
- Ergebnis als JSON nach `stdout` schreiben

Fehler sollen ueber den Prozess-Exitcode und `stderr` zur WPF-App gelangen.

## Datenmodelle

`AdUserResult` beschreibt eine Ergebniszeile aus der AD-Abfrage.

Wichtige Felder:

- `GroupName`
- `GroupPath`
- `SamAccountName`
- `DisplayName`
- `Mail`
- `Enabled`
- `Department`
- `Title`
- `DistinguishedName`

`GroupComparisonResult` beschreibt eine Vergleichszeile.

Wichtige Felder:

- `Status`
- `GroupName`
- `UserA`
- `UserB`
- `GroupPathA`
- `GroupPathB`

## Vergleichslogik

Der Vergleich laeuft bewusst in der WPF-App und nicht im PowerShell-Script.

Grund:

- Die Benutzer sollen nur innerhalb der bereits geladenen Gruppenliste verglichen werden.
- Ein erneuter AD-Scan wuerde ein anderes Ergebnis liefern koennen und waere fuer diesen Workflow verwirrend.
- Die App kann die bereits vorhandenen `AdUserResult`-Zeilen direkt gruppieren und vergleichen.

Die Dropdowns fuer `User 1` und `User 2` werden aus den geladenen `AdUserResult`-Zeilen aufgebaut.

## Release-Modell

Das Repository enthaelt Quellcode und Dokumentation.

Fertige Windows-ZIPs werden als GitHub Release Assets veroeffentlicht:

```text
https://github.com/immer-gut/ad-group-user-exporter/releases/latest
```

Die ZIP-Datei wird lokal aus dem Publish-Output gebaut und nicht im Git-Repository versioniert.

