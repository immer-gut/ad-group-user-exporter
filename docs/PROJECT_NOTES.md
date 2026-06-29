# Project Notes

## Ziel

AD Group User Exporter ist eine kleine Windows-Desktop-App zum Auslesen von Active-Directory-Benutzern aus Gruppen, deren Gruppenname zu einem Suchmuster passt.

Das Tool soll typische Admin-Auswertungen beschleunigen:

- passende AD-Gruppen per Namensmuster finden
- Benutzer rekursiv aus verschachtelten Gruppen aufloesen
- Ergebnisse filtern, kopieren und als CSV exportieren
- zwei Benutzer innerhalb des geladenen Ergebnisses vergleichen
- Unterschiede und Gemeinsamkeiten bezogen auf die geladenen Gruppen sichtbar machen

Die App ist bewusst auf Windows- und AD-Umgebungen ausgerichtet. Ja, das ist spezialisiert. Genau deshalb ist es nuetzlich.

## Entscheidungen

- **WPF fuer die Oberflaeche:** Die App ist ein Windows-Tool und nutzt WPF fuer eine einfache, lokale GUI.
- **PowerShell fuer AD-Zugriff:** Die AD-Abfrage liegt in `Scripts/Get-AdUsersFromGroupPattern.ps1`, weil das ActiveDirectory-Modul in vielen Windows-Admin-Umgebungen ohnehin vorhanden ist.
- **JSON als Prozessschnittstelle:** Die App startet PowerShell, liest JSON aus `stdout` und meldet Fehler aus `stderr`.
- **Vergleich aus geladenen Daten:** Der User-Vergleich fragt AD nicht erneut ab. Er vergleicht nur die Benutzer und Gruppen aus der aktuell geladenen Ergebnisliste.
- **Release-Artefakte nicht im Git-Repo:** Die fertige ZIP-Datei wird als GitHub Release Asset veroeffentlicht, nicht als versionierte Datei im Repository.
- **Lokale Einstellungen:** Verlauf und Theme werden unter `%AppData%\AdGroupUserExporter\` gespeichert.
- **Anonymisierte Beispiele:** README und Dokumentation verwenden generische Muster, Benutzer, Domains und Servernamen.

## Grenzen

- Es ist kein vollstaendiges AD-Reporting-System.
- Es gibt keine serverseitige Datenhaltung und keine zentrale Konfiguration.
- Die App setzt Windows, PowerShell 5.1 und das ActiveDirectory-Modul voraus.
- Die Performance haengt von AD-Groesse, Gruppenverschachtelung, Netzwerk und Berechtigungen ab.
- Der Vergleich betrachtet nur die aktuell geladene Ergebnismenge, nicht alle Gruppen eines Benutzers im gesamten AD.
- Der Export bezieht sich auf die aktuell sichtbare, gefilterte Ansicht.
- Es gibt derzeit keine automatisierten Tests gegen eine echte AD-Testumgebung.

## Roadmap

Kurzfristig:

- bessere Fehlermeldungen fuer nicht eindeutige oder nicht gefundene Benutzer
- optionaler Button zum Leeren der User-Auswahlfelder
- klarere Statusanzeige fuer Anzahl Gruppen, Benutzer und Vergleichsergebnisse

Mittelfristig:

- Abbrechen laufender AD-Abfragen
- Timeout fuer PowerShell-Prozess
- stabilere Behandlung sehr grosser Gruppen
- optionale Anzeige direkter und verschachtelter Mitgliedschaft
- Export des Vergleichs mit sprechenderem Dateinamen

Langfristig:

- automatisierter Release-Workflow ueber GitHub Actions
- signierte Builds
- Tests fuer CSV-Export, Vergleichslogik und JSON-Verarbeitung
- optionale Konfigurationsdatei fuer Standard-SearchBase und bevorzugten Domain Controller

## Besonderheiten

- Das Gruppenmuster nutzt AD-/LDAP-Filterlogik, nicht die Filterbox der Tabelle.
- Die Filterbox in der App filtert nur bereits geladene Daten.
- `User 1` und `User 2` werden aus der geladenen Ergebnisliste befuellt.
- Teiltexte in den User-Dropdowns sind erlaubt; mehrdeutige Treffer muessen eindeutiger eingegrenzt werden.
- Der User-Vergleich zeigt Gruppen, in denen einer oder beide Benutzer in der geladenen Liste vorkommen.
- Die Spalten `GroupPathA` und `GroupPathB` zeigen, ueber welchen Gruppenpfad ein Benutzer gefunden wurde.
- Fertige Windows-ZIPs liegen unter GitHub Releases, z. B. `releases/latest`.

## Datenschutz und Anonymisierung

In Dokumentation und Beispielen duerfen keine privaten Werte stehen:

- keine lokalen Benutzerpfade
- keine internen IP-Adressen
- keine echten Domain-Namen
- keine echten Benutzerkonten
- keine Tokens, Passwoerter oder Secrets
- keine personenbezogenen Werte aus produktiven AD-Umgebungen

Beispiele sollen generisch bleiben, z. B.:

- `OU=Groups,DC=example,DC=local`
- `dc01.example.local`
- `abc*_1a*`
- `user1`, `user2`

