# Open Device Manager (ODM)

Freie Alternative zu Microsoft Configuration Manager / Intune.
Anforderungen: siehe [REQUIREMENTS.md](REQUIREMENTS.md).

**Stand: Etappe 4b + 5 (Repo-Pakete, Secrets, OS-Updates)** – zusätzlich zu Etappe 4a, 3, 2 und 1 (modularer Monolith, lokale Anmeldung per JWT, Web-UI mit Dark Mode und DE/EN):

- Geräteliste mit Namenssuche und Gruppenfilter
- Zuordnung von Hauptbenutzer, Organisationseinheit und Kostenstelle (Rolle Operator/Admin)
- Dynamische Gruppen aus Regeln (Feld, Operator, Wert), kombinierbar mit "alle" oder "mindestens eine":
  - Gerätename: ist / beginnt mit / endet mit / enthält
  - OS-Familie, OS-Name, OS-Version
  - Hauptbenutzer, Organisationseinheit, Kostenstelle
- Live-Vorschau beim Bearbeiten einer Gruppe
- **Agent** (C#, Windows und Linux): Registrierung per Einmal-Token, Heartbeat, Auftragsabruf, Online-Status in der Geräteliste
- **Aufträge:** Infrastruktur samt Testauftrag "ping" (Ergebnis erscheint im Aufträge-Dialog eines Geräts)
- **Repo-Pakete:** Winget, Chocolatey, APT, RPM, Flatpak und Snap; Version fest oder automatisch vom Paketmanager
  aktualisiert; Zusatzargumente und anderes je Paketmanager (z. B. Winget-Bereich, Flatpak-Remote, Snap-Kanal)
- **Secrets:** verschlüsselt gespeicherte Werte (AES-256-GCM), in Zusatzargumenten (auch für PSADT) per `{{secret:NAME}}` nutzbar
- **OS-Updates:** Richtlinien (nur Sicherheitsupdates oder alle; Neustart nie oder bei Bedarf), wiederkehrende
  Wartungsfenster mit Zeitzone, Zuweisung an Geräte und Gruppen, automatische Planung, Lauf-Protokoll, manueller Lauf
- **Softwareverteilung (PSADT):**
  - Pakete mit Versionen: ZIP-Upload (Fortschrittsanzeige), Ablage auf S3 (z. B. RustFS), Prüfsumme (SHA-256)
  - Zuweisung an Gerät, Benutzer (Hauptbenutzer) oder Gruppe; Version "immer die neueste" (automatisch) oder fest (manuell)
  - Abgleich Soll/Ist im Hintergrund: neue Gruppenmitglieder und neue Versionen werden automatisch erfasst
  - Installation als Agent-Auftrag (still, als SYSTEM), Status je Gerät, automatische Wiederholung (3 Versuche) und manuelles "Erneut versuchen"

## Architektur

```
src/
  Odm.SharedKernel/            gemeinsame Basis: IModule, Entity, DB-Konfiguration
  Odm.Host/                    ASP.NET-Core-Host: verdrahtet alle Module, liefert das Frontend aus
  Modules/
    Identity/                  lokale Benutzer, Passwort-Hashing (PBKDF2), JWT, Auth-Provider-Abstraktion
    Devices/                   Geräte, Zuordnung und dynamische Gruppen
    Agents/                    Registrierungs-Tokens, Agent-Zugangsdaten, Aufträge, Agent-Protokoll
    Software/                  Pakete, Versionen (S3), Zuweisungen, Soll/Ist-Abgleich, Paket-Download für Agents
    OsUpdates/                 Update-Richtlinien, Wartungsfenster, Zuweisungen, Planung der Update-Läufe
  Agent/Odm.Agent/             der Agent selbst (Worker-Service für Windows und Linux)
web/                           Svelte 5 + TypeScript + Vite (keine UI-Bibliothek)
```

Regeln für Module:

- Jedes Modul implementiert `IModule` und wird in `Odm.Host/Program.cs` registriert.
- Module referenzieren sich **nicht** gegenseitig, nur `Odm.SharedKernel`.
- Jedes Modul hat ein eigenes DB-Schema (`identity`, `devices`) und eine eigene Migrations-Historie.
- Module sprechen nur über Verträge in `Odm.SharedKernel/Contracts` miteinander (z. B. `IDeviceRegistry`).
- Neue Authentisierungsarten (LDAP, OData) werden als weitere `IAuthenticationProvider` ergänzt.

## Voraussetzungen

- .NET SDK 10 (LTS), Node.js 22+, Docker
- `dotnet tool install --global dotnet-ef`

## Einmalig: EF-Migrationen erzeugen

Die `Initial`-Migrationen aller Module sind eingecheckt. Für eine frische Kopie ohne Migrationen so erzeugen:

```bash
dotnet ef migrations add Initial -p src/Modules/Identity/Odm.Modules.Identity -o Infrastructure/Migrations
dotnet ef migrations add Initial -p src/Modules/Devices/Odm.Modules.Devices  -o Infrastructure/Migrations
dotnet ef migrations add Initial -p src/Modules/Agents/Odm.Modules.Agents    -o Infrastructure/Migrations
dotnet ef migrations add Initial -p src/Modules/Software/Odm.Modules.Software -o Infrastructure/Migrations
dotnet ef migrations add Initial -p src/Modules/OsUpdates/Odm.Modules.OsUpdates -o Infrastructure/Migrations
```

Hast du das Software-Modul schon vor Etappe 4b migriert, kommt eine zweite Migration dazu (neue Paketarten,
Paketeinstellungen, Revisionen, Secrets-Tabelle, `ObjectKey`/`Sha256` jetzt optional):

```bash
dotnet ef migrations add RepoPackages -p src/Modules/Software/Odm.Modules.Software -o Infrastructure/Migrations
```

Ausserdem braucht der Server jetzt einen Schlüssel für die Secrets (`Secrets:MasterKey`, Entwicklung: steht in
`appsettings.Development.json`; Betrieb: `ODM_SECRETS_KEY` in `.env`, erzeugen mit `openssl rand -base64 32`).

**Etappe 4c** erweitert das Software-Modul (Tabellen `profiles`, `profile_items`, `profile_assignments`, Spalte
`RolloutJson` an `assignments`). Dafür eine neue Migration anlegen:

```bash
dotnet ef migrations add SoftwareProfileTranchen -p src/Modules/Software/Odm.Modules.Software -o Infrastructure/Migrations
```

Der Host wendet Migrationen beim Start automatisch an.

Hast du `Initial` schon erzeugt, bevor du Etappe 2 eingespielt hast, lege für die neuen Tabellen und die Spalte
`PrimaryUserOrgUnit` eine zweite Migration an (im Devices-Modul):

```bash
dotnet ef migrations add Clientverwaltung -p src/Modules/Devices/Odm.Modules.Devices -o Infrastructure/Migrations
```

Mit Etappe 3 kommt im Devices-Modul die Spalte `AgentVersion` dazu (neue Migration, z. B. `Agentversion`) und das neue Modul Agents braucht eine eigene `Initial`-Migration (siehe oben).

## Entwicklung

```bash
# 1. Datenbank
docker run -d --name odm-db -p 5432:5432 -e POSTGRES_DB=odm -e POSTGRES_USER=odm -e POSTGRES_PASSWORD=odm postgres:17

# 1b. Paketspeicher (S3-kompatibel, hier RustFS; Zugang wie in appsettings.Development.json)
docker run -d --name odm-rustfs -p 9000:9000 -p 9001:9001 \
  -e RUSTFS_ACCESS_KEY=minioadmin -e RUSTFS_SECRET_KEY=minioadmin \
  -v $(pwd)/data:/data rustfs/rustfs:latest

# 2. Backend (http://localhost:8080, Development: Admin admin / admin-dev-password, Demo-Geräte)
dotnet run --project src/Odm.Host

# 3. Frontend (http://localhost:5173, leitet /api an das Backend weiter)
cd web && npm install && npm run dev
```

## Agent ausprobieren (lokal)

1. Server und Frontend starten (siehe oben), im Browser anmelden.
2. Menü **Registrierung** → Token erstellen und kopieren.
3. Agent starten (im Development reicht http):

```bash
Agent__ServerUrl=http://localhost:8080 \
Agent__AllowInsecureHttp=true \
Agent__RegistrationToken=<token> \
Agent__StateDirectory=/tmp/odm-agent \
dotnet run --project src/Agent/Odm.Agent
```

4. Das Gerät erscheint in der Geräteliste. Unter **Aufträge** → *Ping senden* wird ein Testauftrag erzeugt; das Ergebnis
   erscheint nach dem nächsten Heartbeat (Intervall `Agents:HeartbeatSeconds`, Standard 60 s, Minimum 10 s).

Nach der Registrierung liegen die Zugangsdaten im State-Verzeichnis; das Token wird nicht mehr benötigt und sollte
aus der Konfiguration entfernt werden.

### Agent installieren

Der Server liefert vorgebaute, eigenständige Agent-Pakete (Windows/Linux, x64/arm64) und Installationsskripte aus –
der Agent muss nicht selbst kompiliert werden. Unter **Registrierung** erzeugst du ein Einmal-Token; die Seite zeigt danach
den fertigen Befehl zum Kopieren:

```powershell
# Windows (PowerShell als Administrator)
& ([scriptblock]::Create((irm 'https://odm.example.org/api/agent/install.ps1'))) -Token '<token>'
```

```bash
# Linux (root)
curl -fsSL 'https://odm.example.org/api/agent/install.sh' | sudo sh -s -- --token '<token>'
```

Die Skripte erkennen die Architektur, prüfen die SHA-256-Prüfsumme des Downloads, installieren den Agent
(`C:\Program Files\OpenDeviceManager` bzw. `/opt/odm-agent`), richten den Dienst ein (Neustart bei Fehlern) und starten ihn.
Erneutes Ausführen aktualisiert den Agent; die Registrierung bleibt erhalten (dann ist kein Token nötig).
Das Token steht nur kurz in `agent.json` im geschützten Zustandsverzeichnis und wird vom Agent nach der Registrierung entfernt.
Ohne `https` verweigern Skripte und Agent die Verbindung (nur für Tests: `-AllowInsecureHttp` / `--allow-insecure-http`).
Direkte Downloads samt Prüfsummen stehen ebenfalls auf der Registrierungsseite
(`GET /api/agent/packages`, `GET /api/agent/download/{plattform}`).

#### Agent-Pakete bereitstellen

* **Docker-Image:** `docker build` baut die Pakete mit (Stage `agent`, alle vier Plattformen) nach `/app/agent-packages`.
  Schneller ohne Pakete: `--build-arg AGENT_RIDS=""`.
* **Ohne Docker:** `scripts/publish-agent.sh` (bzw. `.ps1`) erzeugt `odm-agent-<version>-<rid>.zip|tar.gz` in `agent-packages/`.
  Alternativ die Pakete eines GitHub-Releases (Workflow `release-agent.yml`, ausgelöst durch Tags `v*`) in das Verzeichnis legen.
* Konfiguration: `Agents:PackagesPath` (Standard `agent-packages`) und `Agents:PublicBaseUrl`
  (öffentliche Adresse für die Skripte; hinter einem Reverse-Proxy setzen, sonst wird sie aus der Anfrage abgeleitet).
  Je Plattform wird die höchste Version angeboten; ein Update des Servers genügt also, um neue Agents auszurollen.

#### Agents automatisch aktualisieren

Die Selbstaktualisierung ist standardmäßig **aus**. Aktivierung über die Server-Konfiguration (Neustart nötig):

```yaml
Agents__Update__Mode: Latest       # Off | Latest | Pinned
Agents__Update__TargetVersion: ""  # nur bei Pinned, z. B. 0.4.2 (auch Rückstufung)
Agents__Update__RolloutPercent: 10 # Anteil der Geräte (stabil je Gerät); schrittweise auf 100 erhöhen
```

Der Agent erfährt die Soll-Version im Heartbeat, lädt das Paket aus `agent-packages/`, prüft die SHA-256-Prüfsumme und
übergibt den Austausch an ein Hilfsskript (Linux: transiente systemd-Einheit `odm-agent-update`, Windows: PowerShell).
Es stoppt den Dienst, tauscht die Programmdatei und startet ihn neu. Meldet sich der neue Agent nicht innerhalb weniger
Minuten, wird die alte Version wiederhergestellt; die fehlgeschlagene Version wird auf diesem Gerät nicht erneut versucht
(`update-failed` im Zustandsverzeichnis). Ablauf: `logs/update.log` und `logs/agent.log`. Aktualisiert wird nur ein
untätiger Agent. Ein Agent, der nicht mehr startet, muss weiterhin von Hand per Installationsbefehl repariert werden.

#### Manuell

```bash
dotnet publish src/Agent/Odm.Agent -c Release -r linux-x64 --self-contained -o /opt/odm-agent
install -d -m 700 /var/lib/odm-agent
printf '{"Agent":{"ServerUrl":"https://odm.example.org","RegistrationToken":"<token>"}}' > /var/lib/odm-agent/agent.json
chmod 600 /var/lib/odm-agent/agent.json
cp deploy/odm-agent.service /etc/systemd/system/ && systemctl daemon-reload && systemctl enable --now odm-agent
```

Der Agent schreibt zusätzlich eine rotierende Logdatei `agent.log` (5 × 5 MB) nach `C:\ProgramData\OpenDeviceManager\logs` bzw. `/var/lib/odm-agent/logs` (abschaltbar mit `Agent:LogToFile=false`); unter Linux außerdem `journalctl -u odm-agent`, unter Windows in der Ereignisanzeige (Quelle `OpenDeviceManagerAgent`, standardmäßig ab Warnung).

Die Zugangsdaten des Agents liegen unter `C:\ProgramData\OpenDeviceManager` (nur SYSTEM/Administratoren)
bzw. `/var/lib/odm-agent` (0700/0600).

## Software verteilen (PSADT)

1. Menü **Software** → Paket anlegen → ZIP mit dem PSADT-Paket als neue Version hochladen.
   Das ZIP muss `Invoke-AppDeployToolkit.ps1` (PSADT v4) oder `Deploy-Application.ps1` (v3) im Stammverzeichnis
   oder eine Ebene darunter enthalten. Der Agent ruft es mit `-DeploymentType Install -DeployMode Silent` auf.
2. Unter **Zuweisungen** das Paket einer Gruppe, einem Gerät oder einem Benutzer zuweisen
   (*Immer die neueste* = neue Versionen werden automatisch verteilt, *Fest* = nur die gewählte Version).
3. Der Abgleich läuft jede Minute (`Software:ReconcileSeconds`) und sofort nach Änderungen. Der Status steht unter
   **Installationen**; fehlgeschlagene Installationen werden bis zu 3 Mal wiederholt, danach hilft *Erneut versuchen*.

Hinweise:

- Der Agent lädt das Paket über den ODM-Server (nicht direkt von S3); der Server prüft, dass für das Gerät gerade ein
  Installationsauftrag mit genau dieser Version läuft. Der Agent prüft die SHA-256-Prüfsumme vor dem Entpacken.
- Pakete werden in einem geschützten Arbeitsverzeichnis entpackt (nicht in `%TEMP%`), weil sie als SYSTEM laufen.
- Mehrere Zuweisungen desselben Pakets an ein Gerät: Gerät vor Benutzer vor Gruppe.
- PSADT-Pakete werden nur auf Windows-Geräten installiert. Exit-Codes 0, 1641 und 3010 gelten als Erfolg.
- Entfernen einer Zuweisung oder eines Pakets deinstalliert nichts auf den Geräten.
- Maximale Paketgrösse: `Software:MaxPackageMegabytes` (Standard 4096, S3 erlaubt in einem Upload höchstens 5 GB).
  Läuft ein Reverse Proxy davor, muss dessen Upload-Limit ebenfalls passen.

## Repo-Pakete und Secrets

**Paket anlegen:** Menü *Software* → Paketart wählen (Winget, Chocolatey, APT, RPM, Flatpak, Snap) → Paketname/ID
und Einstellungen eintragen. Danach wie bei PSADT einer Gruppe, einem Gerät oder einem Benutzer zuweisen.
Die Pakete kommen aus den Repositories des Geräts; ODM speichert nur die Konfiguration.

- **Version fest oder automatisch:** Ist eine Version eingetragen, wird genau diese installiert. Bleibt das Feld leer,
  installiert der Paketmanager die neueste Version und ODM lässt ihn alle 24 Stunden (`Software:RepoRefreshHours`,
  0 = nie) erneut aktualisieren. Eine Änderung der Einstellungen erzeugt eine neue *Revision*; alle zugewiesenen
  Geräte werden darauf abgeglichen.
- **Immer still:** alle Befehle laufen ohne Rückfragen (Winget `--silent --disable-interactivity`, Chocolatey `-y`,
  APT/DNF `-y`, Flatpak `--noninteractive`).
- **Je Paketmanager:** Winget (Bereich `machine`/`user`, Quelle), Chocolatey (Quelle), Flatpak (Bereich, Remote;
  *Version* = Commit-ID), Snap (Kanal, Classic; *Version* = Revisionsnummer), APT und RPM (Name, Version).
- **Passende Geräte:** Winget/Chocolatey/PSADT nur auf Windows, die übrigen nur auf Linux. APT-Pakete werden
  DEB-basierten, RPM-Pakete RPM-basierten Geräten zugewiesen (erkannt am OS-Namen; unbekannte Distributionen werden
  zugelassen, und der Agent meldet einen klaren Fehler, wenn der Paketmanager fehlt).
- **Zusatzargumente:** werden dem Befehl angehängt (zerlegt wie in einer Shell, aber ohne Shell). Bei PSADT werden sie
  an das Skript übergeben; es muss die Parameter deklarieren.

**Secrets:** unter *Software* → *Secrets* (anlegen und ändern nur Administratoren). In Argumenten steht
`{{secret:LIZENZ}}`. Der Wert wird verschlüsselt gespeichert, nie angezeigt und nicht mit dem Auftrag ausgeliefert:
der Agent holt ihn erst bei der Ausführung ab, und nur für Secrets, die der laufende Auftrag selbst verwendet.
Der Agent entfernt Secret-Werte aus der Ausgabe, bevor er das Ergebnis meldet.
Grenzen: Während der Installation steht der Wert in der Befehlszeile des Installers (für lokale Administratoren
sichtbar), und wer Pakete bearbeiten darf, kann einen Secret-Wert an beliebige Argumente übergeben. Behandle
Secrets deshalb wie Zugangsdaten, die Operatoren zur Verwendung (nicht zur Einsicht) anvertraut werden.

Bekannte Einschränkungen:

- **Winget** gehört zum App-Installer (MSIX) und ist für das SYSTEM-Konto nicht überall verfügbar; der Agent sucht es
  unter `WindowsApps`. Fehlt es, meldet der Auftrag das. *Bereich = Benutzer* installiert für das SYSTEM-Konto, nicht
  für angemeldete Benutzer (das braucht einen Benutzerkontext, siehe spätere Etappen).
- Eine feste Version, die **älter** ist als die installierte, wird nur von Chocolatey und APT (Downgrade erlaubt)
  umgesetzt; bei Winget/Snap/Flatpak muss vorher deinstalliert werden (Deinstallation ist noch nicht Teil von ODM).
- Winget-Rückgabecodes für "schon aktuell" werden als Erfolg gewertet; die genauen Codes hängen von der Winget-Version ab.

## Softwareprofile und Tranchen

**Profile** (Menü *Profile*): eine geordnete Liste von Paketen (PSADT und Repo-Pakete gemischt).

- Ein Profil wird einem Gerät, Benutzer oder einer Gruppe zugewiesen. Auf jedem Gerät startet ein Paket erst, wenn
  alle Pakete davor installiert sind. Schlägt ein Paket endgültig fehl (3 Versuche), warten die folgenden, bis es
  über *Erneut versuchen* beim Paket gelingt.
- Pakete, die für ein Gerät nicht passen (z. B. Winget auf Linux), werden übersprungen und blockieren die Reihenfolge nicht.
- Wird dasselbe Paket mehrfach zugewiesen, gewinnt die spezifischere Quelle: Gerät vor Benutzer vor Gruppe, bei gleichem
  Ziel die einzelne Paketzuweisung vor dem Profil. Die Reihenfolge gilt nur für die gewinnende Quelle.
- Pro Eintrag kann bei PSADT-Paketen eine feste Version gewählt werden (wie bei Einzelzuweisungen).

**Tranchen** (bei Paket- und Profilzuweisungen, Feld *Verteilung in Tranchen*): Zeitpunkt der Zuweisung ist der Start.

- *Nach Prozent*, z. B. 20 % nach 5 Tagen, 40 % nach 10, 100 % nach 15. Welche Geräte dazugehören, ist je Zuweisung
  und Gerät fest (stabiler Hash), die Auswahl wächst also nur. Die letzte Stufe muss 100 % sein. Vor der ersten Stufe
  erhält kein Gerät das Paket.
- *Nach Untergruppen*: jede gewählte Gruppe wird nach ihrer Wartezeit freigegeben. Geräte der Zielgruppe, die in keiner
  Untergruppe sind, folgen nach der letzten Stufe. Eine Untergruppe wird als Gruppe angelegt (z. B. Pilotgeräte).
- Tranchen gelten nur für **neue** Installationen. Geräte, die das Paket schon haben, werden weiter aktualisiert,
  und neue Paketversionen werden nicht erneut gestaffelt.
- Pro Zuweisung ist der Stand sichtbar ("Stufe 2/3 · 40 % freigegeben"). Den Plan ändert man, indem man die Zuweisung
  neu anlegt (die API kann ihn auch per `PUT` ersetzen).

## OS-Updates

1. Menü **OS-Updates** → **Richtlinie** anlegen (*Nur Sicherheitsupdates* oder *Alle*; Neustart *nie* oder *bei Bedarf*)
   und einer Gruppe oder einem Gerät zuweisen. Je Ziel gilt genau eine Richtlinie, ein Gerät geht der Gruppe vor.
2. **Wartungsfenster** anlegen: Wochentage, Beginn, Dauer (15 Minuten bis 24 Stunden, auch über Mitternacht) und Zeitzone
   (Sommerzeit wird berücksichtigt). Fenster Geräten oder Gruppen zuweisen. Direkt einem Gerät zugewiesene Fenster
   ersetzen die Fenster seiner Gruppen; mehrere Gruppenfenster addieren sich.
3. Der Server prüft jede Minute (`OsUpdates:CheckSeconds`), ob für ein Gerät mit Richtlinie ein Fenster offen ist,
   und erteilt dem Agent höchstens einmal je Gerät und Fenster einen Auftrag. **Ohne Richtlinie oder ohne Fenster
   werden keine Updates automatisch installiert.**
4. Unter **Update-Läufe** siehst du Ergebnis, Anzahl, Fehler und ob ein Neustart nötig bzw. geplant ist. Zum Testen
   gibt es bei einem Gerät (*Aufträge*) den Knopf **Updates jetzt ausführen**; er ignoriert das Fenster, nicht die Richtlinie.

Wie der Agent aktualisiert:

- **Windows:** Windows-Update-Agent-API (COM, kein Zusatzmodul), als SYSTEM. *Nur Sicherheitsupdates* = Kategorien
  Security Updates und Critical Updates.
- **Linux (DEB):** `apt-get update`, dann `upgrade`; bei *Nur Sicherheitsupdates* nur Pakete aus `*-security`-Quellen.
- **Linux (RPM):** `dnf` bzw. `yum` (`--security` bei *Nur Sicherheitsupdates*).
- **Neustart:** nur wenn die Richtlinie ihn erlaubt, ein Neustart nötig ist und bis zum Fensterende noch mindestens
  5 Minuten bleiben. Er wird mit 2 Minuten Verzögerung geplant, damit der Agent vorher das Ergebnis melden kann.
  Ein Auftrag, der erst kurz vor Fensterende beim Agent ankommt (Gerät war offline), wird nicht mehr gestartet.
- Auf RPM-Systemen braucht die Neustart-Erkennung `needs-restarting` (Paket `dnf-utils` bzw. `yum-utils`).
- Der Server braucht Zeitzonendaten (`tzdata`); das Standard-Image ist Debian-basiert und enthält sie.

## Tests

```bash
dotnet test
```

Das Testprojekt `tests/Odm.Tests` prüft die Logik, die sich ohne Geräte und Datenbank testen lässt:

- Wartungsfenster: Zeitzonen, Sommerzeit (inklusive der Lücke im Frühjahr), Fenster über Mitternacht, Grenzen
- Gruppenregeln (derselbe Ausdruck, den EF Core in SQL übersetzt) und deren Validierung
- Paketeinstellungen: Bezeichner dürfen nicht wie Optionen des Paketmanagers aussehen, Secret-Verweise, Standardwerte
- Zustandsautomat der Installationen: Wiederholungen, Zeitüberschreitung, automatische Aktualisierung
- Secrets (Verschlüsselung, Manipulation, falscher Name/Schlüssel), Passwort-Hashing, Tokens
- Agent: Zerlegen von Argumenten, Einsetzen und Entfernen von Secrets, Befehlszeilen je Paketmanager

Nicht abgedeckt sind Datenbankzugriffe, HTTP-Endpunkte und die echten Paketmanager auf den Geräten.

## Betrieb mit Docker Compose

```bash
cp .env.example .env     # Werte setzen
docker compose up -d --build
```

Die Anwendung läuft dann auf http://localhost:8080. Der Admin wird beim ersten Start aus
`ODM_ADMIN_USER` / `ODM_ADMIN_PASSWORD` angelegt.

## SQL Server statt Postgres

ODM läuft wahlweise mit Postgres (Standard) oder Microsoft SQL Server (2019 oder neuer). Gewählt wird nur über die Konfiguration:

```json
"Database": {
  "Provider": "SqlServer",
  "ConnectionString": "Server=db.example.org;Database=odm;User Id=odm;Password=…;Encrypt=True"
}
```

(Umgebungsvariablen: `Database__Provider`, `Database__ConnectionString`.)

**Migrationen sind providerspezifisch** (Spaltentypen, Annotationen). Deshalb gibt es zwei getrennte Sätze:
Postgres behält seine Migrationen in den Modulen, SQL Server hat das Projekt `src/Odm.Migrations.SqlServer`. Beim Start wird
nur der Satz des gewählten Providers geladen. Den SQL-Server-Satz erzeugst du einmalig (ohne laufende Datenbank):

```powershell
.\scripts\ef-sqlserver.ps1 Initial        # Windows
```
```bash
scripts/ef-sqlserver.sh Initial            # Linux / macOS
```

Das legt je Modul einen Ordner mit der Migration an; bitte einchecken. **Ab dann braucht jede Änderung am Datenmodell zwei
Migrationen**: eine für Postgres (`dotnet ef migrations add … -p src/Modules/<Modul>/…`, siehe oben) und eine für SQL Server
(`scripts/ef-sqlserver.* <Name>`). Fehlen die SQL-Server-Migrationen, bricht ODM beim Start mit einer entsprechenden Meldung ab.

Hinweise:

- ODM legt die Datenbank beim ersten Start selbst an, wenn das Konto das darf (z. B. `sa` oder `dbcreator`). Sonst die leere
  Datenbank vorher anlegen; das Konto braucht dann `db_owner` auf dieser Datenbank (Schemas und Tabellen werden angelegt).
- Verschlüsselung: Der Treiber verlangt TLS. Mit einem Zertifikat einer vertrauten CA reicht `Encrypt=True`; nur in der
  Entwicklung mit selbstsigniertem Zertifikat `TrustServerCertificate=True` ergänzen.
- Gross-/Kleinschreibung: SQL Server vergleicht in der Standardsortierung ohne Beachtung der Schreibweise, Postgres mit.
  ODM speichert Benutzer- und Zielnamen kleingeschrieben, die Anwendung verhält sich auf beiden gleich.
- Docker: `docker compose -f deploy/docker-compose.mssql.yml --env-file .env up -d --build` startet ODM mit SQL Server 2022
  (Developer-Edition; für den Produktivbetrieb brauchst du eine passende Lizenz). `ODM_DB_PASSWORD` ist dort das `sa`-Passwort
  und muss die SQL-Server-Regeln erfüllen (mindestens 8 Zeichen, Gross-/Kleinbuchstaben, Ziffern) und darf kein `;` enthalten.
  Für den Betrieb ein eigenes Konto mit `db_owner` statt `sa` verwenden.
- Ein Wechsel von Postgres auf SQL Server übernimmt keine Daten; ODM hat keinen Export/Import zwischen den Datenbanken.

## Anmeldung gegen LDAP / Active Directory

Neben den lokalen Konten kann ODM Benutzer gegen ein Verzeichnis prüfen. Standardmässig ist das aus
(`Identity:Ldap:Enabled=false`); lokale Konten funktionieren immer weiter, auch der Bootstrap-Administrator als Notzugang.

```json
"Identity": {
  "Ldap": {
    "Enabled": true,
    "Host": "dc01.example.org",
    "Port": 636,
    "Security": "Ldaps",            // oder "StartTls" (dann meist Port 389)
    "BaseDn": "DC=example,DC=org",
    "BindDn": "CN=svc-odm,OU=Service,DC=example,DC=org",
    "BindPassword": "…",            // besser per Umgebungsvariable Identity__Ldap__BindPassword
    "UserFilter": "(sAMAccountName={0})",   // OpenLDAP: "(uid={0})"
    "AdminGroup": "CN=odm-admins,OU=Groups,DC=example,DC=org",
    "OperatorGroup": "CN=odm-operators,OU=Groups,DC=example,DC=org",
    "ReadOnlyGroup": "CN=odm-readers,OU=Groups,DC=example,DC=org"
  }
}
```

So läuft die Anmeldung:

1. ODM sucht den Benutzer mit dem Dienstkonto (`BindDn`; leer = anonyme Suche, nur wenn das Verzeichnis sie erlaubt)
   und dem `UserFilter`. Der eingegebene Name wird dabei maskiert (keine LDAP-Injection). Bei keinem oder mehreren Treffern: abgelehnt.
2. Das Passwort wird durch eine Anmeldung **als dieser Benutzer** geprüft. Ein leeres Passwort wird nie zugelassen.
3. Die Rolle kommt aus den Gruppen (Attribut `memberOf`): *Admin* vor *Operator* vor *ReadOnly*.
   **Wer in keiner der drei Gruppen ist, kann sich nicht anmelden.** Verschachtelte Gruppen werden nicht aufgelöst;
   der Benutzer muss direkt Mitglied sein (bei AD z. B. über eine eigene Gruppe je Rolle).
4. Gibt es einen **lokalen** Benutzer mit demselben Namen, gilt nur das lokale Konto. Ein falsches Passwort wird also nicht
   durch ein gleichnamiges Verzeichniskonto "gerettet".

Hinweise:

- Es gibt bewusst keine Verbindung ohne TLS. Das Serverzertifikat wird vom System geprüft; unter Linux gilt die
  libldap-Konfiguration (z. B. `TLS_CACERT` in `/etc/ldap/ldap.conf`, bei einer eigenen CA das Zertifikat dort eintragen).
- Das Docker-Image enthält `libldap`. Die Werte lassen sich in `docker-compose.yml` über `ODM_LDAP_*` in der `.env` setzen;
  einen anderen `UserFilter` setzt du als Umgebungsvariable `Identity__Ldap__UserFilter`.
- Rolle und Anzeigename werden bei jeder Anmeldung neu gelesen, ein Entzug der Gruppe wirkt also spätestens, wenn das Token
  abläuft (`Jwt:ExpiryMinutes`, Standard 60). Verzeichnisfehler führen immer zu "abgelehnt".
- Verzeichnisbenutzer erscheinen nicht in einer Benutzerliste; ihre ID ist ein stabiler Hash aus Verfahren und Benutzername.

## Anmeldung über OpenID Connect (Entra ID, authentik, Keycloak …)

Für Single Sign-on mit einem Identitätsanbieter. Es sind mehrere Anbieter gleichzeitig möglich; auf der Anmeldeseite
erscheint je Anbieter ein Knopf ("Anmelden mit …"). Ohne Konfiguration ist die Funktion aus. Lokale Konten (und LDAP)
laufen unabhängig davon weiter.

```json
"Identity": {
  "Oidc": {
    "PublicBaseUrl": "https://odm.example.org",
    "Providers": [
      {
        "Id": "entra",
        "DisplayName": "Microsoft Entra ID",
        "Authority": "https://login.microsoftonline.com/<Verzeichnis-ID>/v2.0",
        "ClientId": "<Anwendungs-ID>",
        "ClientSecret": "…",           // besser per Umgebungsvariable (siehe unten)
        "GroupsClaim": "roles",
        "AdminValues": [ "odm-admin" ],
        "OperatorValues": [ "odm-operator" ],
        "ReadOnlyValues": [ "odm-readonly" ]
      },
      {
        "Id": "authentik",
        "DisplayName": "authentik",
        "Authority": "https://auth.example.org/application/o/odm/",
        "ClientId": "…",
        "ClientSecret": "…",
        "GroupsClaim": "groups",
        "AdminValues": [ "odm-admins" ],
        "OperatorValues": [ "odm-operators" ]
      }
    ]
  }
}
```

Als Umgebungsvariablen (z. B. in `docker-compose.yml` beim Dienst `odm`):
`Identity__Oidc__PublicBaseUrl`, `Identity__Oidc__Providers__0__Id`, `…__0__Authority`, `…__0__ClientId`,
`…__0__ClientSecret`, `…__0__AdminValues__0` usw.

**Rückleite-Adresse (Redirect URI)** beim Anbieter eintragen, genau so: `<PublicBaseUrl>/api/auth/oidc/callback`.
Für die Entwicklung mit `npm run dev`: `PublicBaseUrl = http://localhost:5173` (http ist nur für localhost erlaubt).

**Microsoft Entra ID**

1. *App-Registrierungen → Neue Registrierung*; Plattform *Web*, Redirect URI wie oben.
2. *Zertifikate & Geheimnisse → Neuer geheimer Clientschlüssel*; den **Wert** als `ClientSecret` übernehmen.
3. *App-Rollen* anlegen (zulässige Mitgliedstypen: Benutzer/Gruppen), z. B. mit den Werten `odm-admin`, `odm-operator`, `odm-readonly`.
4. In der *Unternehmensanwendung*: *Benutzer und Gruppen* → Gruppen den Rollen zuweisen; *Zuweisung erforderlich* auf *Ja* stellen.
5. `Authority` mit der **Verzeichnis-ID** deines Mandanten (nicht `common` oder `organizations`), `GroupsClaim = "roles"`.
   Gruppen-Objekt-IDs (`groups`) gehen auch, sind aber ab etwa 200 Gruppen im Token unvollständig; App-Rollen sind robuster.

**authentik**

1. *Anwendungen → Erstellen* mit Anbieter *OAuth2/OpenID*: Client-Typ *vertraulich*, Redirect URI wie oben (*strikt*),
   Bereiche `openid`, `profile`, `email`.
2. `Authority` ist die *OpenID-Konfigurations-Aussteller*-Adresse der Anwendung (`…/application/o/<slug>/`),
   `ClientId` und `ClientSecret` aus dem Anbieter.
3. authentik liefert die Gruppennamen im Anspruch `groups`; die Gruppen trägst du unter `AdminValues` usw. ein.
   Wer keiner der Anwendung zugewiesenen Gruppe angehört, kommt per authentik-Richtlinie gar nicht erst durch.

So läuft die Anmeldung:

- Authorization-Code-Ablauf mit **PKCE**, `state` und `nonce`. Der Server tauscht den Code selbst gegen die Tokens (Rückkanal).
- Das **ID-Token** wird geprüft: Signatur (Schlüssel aus der Discovery, nur asymmetrische Verfahren), Aussteller,
  Empfänger (deine `ClientId`), Ablauf und `nonce`. Schlüsselwechsel beim Anbieter werden automatisch übernommen.
- Die Rolle kommt aus dem `GroupsClaim`: *Admin* vor *Operator* vor *ReadOnly*. **Ohne passenden Wert keine Anmeldung.**
- Die ODM-Benutzer-ID ist ein stabiler Hash aus Anbieter und `sub` (nicht aus dem Namen, der sich ändern kann).
  Angezeigt wird `preferred_username` (sonst `email`, sonst `sub`).
- Der Anmeldezustand liegt in einem kurzlebigen, signierten Cookie (10 Minuten, `HttpOnly`, `SameSite=Lax`). Der Server
  braucht dafür keinen Zwischenspeicher, es funktioniert also auch mit mehreren Instanzen.
- Zurück zur Oberfläche reist das ODM-Token im Fragment (`#…`) der Adresse; es wird nie an einen Server gesendet und von der
  Oberfläche sofort aus der Adresse entfernt.

Grenzen: Die Abmeldung beendet nur die ODM-Sitzung, nicht die beim Anbieter. Rolle und Name werden bei jeder Anmeldung neu
gelesen; ein Entzug wirkt spätestens, wenn das ODM-Token abläuft (`Jwt:ExpiryMinutes`, Standard 60). Mandantenübergreifende
Aussteller (Entra `common`) werden nicht unterstützt.

## Sicherheit (Stand jetzt)

- Passwörter: PBKDF2-HMAC-SHA256, 600 000 Iterationen, zufälliges Salt
- Login: Rate-Limit (10 Versuche/Minute und IP), gleichförmige Antwortzeit bei unbekanntem Benutzer
- Agent-Registrierung: Einmal-Token (nur als SHA-256-Hash gespeichert, begrenzte Gültigkeit, atomar verbraucht), Rate-Limit
- Agent-Betrieb: eigenes Authentisierungsschema mit zufälligem 256-Bit-Secret, serverseitig nur als Hash; Neuinstallation widerruft alte Zugangsdaten desselben Geräts
- Der Agent verlangt https (http nur mit `Agent:AllowInsecureHttp`)
- JWT-Secret mindestens 32 Zeichen, wird beim Start validiert
- Das Token liegt im `sessionStorage` des Browsers (nur für den Tab)
- Hinter einem Reverse Proxy: Forwarded-Headers konfigurieren, sonst gilt das Rate-Limit pro Proxy statt pro Client

## Nächste Etappen

2. ~~Clientverwaltung (Hauptbenutzer, Kostenstelle, dynamische Gruppen)~~ erledigt
3. ~~Agent (Registrierung, Heartbeat, Aufträge) – Windows, danach Linux~~ erledigt (mTLS als Härtung folgt später)
4. Softwareverteilung
   - ~~4a: PSADT-Pakete, S3, Zuweisung, Installation~~ erledigt
   - ~~4b: Repo-Pakete (Winget, Chocolatey, APT, RPM, Flatpak, Snap) mit Zusatzargumenten und Secrets~~ erledigt
   - ~~4c: Softwareprofile (Installationsreihenfolge) und Tranchenverteilung~~ erledigt
5. ~~OS-Updates und Wartungsfenster~~ erledigt
6. Betrieb und Anbindung
   - ~~6a: Anmeldung gegen LDAP/Active Directory~~ erledigt
   - ~~6b: Anmeldung über OpenID Connect (Entra ID, authentik …), ersetzt den OData-Endpoint~~ erledigt
   - ~~6c: MSSQL als zweite Datenbank~~ erledigt (SQL-Server-Migrationen musst du einmalig erzeugen)
   - 6d: Kubernetes (Helm)
