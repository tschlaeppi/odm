# Open Device Manager (ODM) – Requirements

## 1. Ziel

Open Device Manager (ODM) soll eine freie Alternative zu Microsoft Configuration Manager oder Intune werden.
Es unterstützt die IT bei der Verwaltung von Geräten.

Kernpunkte:

- Softwareverteilung und Lifecycle (Updates)
- OS-Update-Management

## 2. Tech Stack

### 2.1 Server

| Bereich | Vorgabe |
|---|---|
| Frontend | Webbasiert, TypeScript |
| Backend | C# 10 |
| Datenbank | PostgreSQL oder MSSQL Server |
| Deployment | Docker oder Kubernetes |

### 2.2 Client (Agent)

- **Programmiersprache:** C# 10
- **Unterstützte Betriebssysteme:**
  - Windows (Server & Client)
  - Linux (DEB- und RPM-basiert, Server & Client)

## 3. Funktionale Anforderungen

### 3.1 Authentisierung

- Lokale Datenbank und/oder Authentisierung gegen LDAP oder einen OData-Endpoint
- Anmeldung über OpenID Connect (z. B. Microsoft Entra ID, authentik); in der Umsetzung anstelle des OData-Endpoints

### 3.2 Client-Verwaltung

- Clients können einem Hauptbenutzer zugeteilt werden
- Clients können einer Kostenstelle zugeordnet werden
- Clients können beliebig gruppiert werden:
  - Nach Name (Filter: beginnt mit, endet mit, enthält)
  - Nach OS und Version
  - Nach Organisationseinheit des Hauptbenutzers

### 3.3 Softwareverteilung

#### PSADT-Pakete

- PSADT-Pakete (gezipptes PSADT) können ins System hochgeladen werden
- PSADT-Pakete können einem Client oder einem User zugewiesen werden
- Neue Versionen von PSADT-Paketen können automatisch oder manuell verteilt werden

#### Repo-Pakete

Repo-Pakete sind Pakete aus anderen Repositories und können im System definiert werden:

- Winget
- Chocolatey
- APT / DEB
- Flatpak
- Snap
- RPM

Weitere Anforderungen:

- Repo-Pakete können auf eine Version fixiert oder vom Paketmanager automatisch aktualisiert werden
- Je nach Repo können Zusatzkommandos angegeben werden, z. B.:
  - Winget-Pakete als System oder User verteilen
  - Unattended-Installation zwingend
  - Optionale Installationsargumente
- Zusatzargumente (auch für PSADT) unterstützen Secrets

#### Softwareprofile

- Ein Softwareprofil ist eine Anzahl von Softwarepaketen, die in einer definierten Reihenfolge installiert werden
- Softwareprofile können einem Client oder einem User zugewiesen werden

#### Verteilung in Tranchen

Wird ein neues Paket einem Client oder einer Gruppe zugewiesen, ist eine Tranchenverteilung möglich:

- Über Subgruppen, oder
- Über Prozentwerte, z. B. 20 % nach 5 Tagen, 40 % nach 10 Tagen, 100 % nach 15 Tagen

### 3.4 OS-Updates

- OS-Updates können konfiguriert werden
- Wartungsfenster können für Clients oder Gruppen definiert werden

### 3.5 Agent-Installation

Als Systemadministrator möchte ich eine einfache Prozedur zur Agenteninstallation (Download-Link oder Convenience-Script),
damit ich den Agent nicht in jeder Version selbst kompilieren muss.

Umsetzung / Akzeptanzkriterien:

- Fertige, selbstenthaltene Agent-Pakete (Windows x64/ARM64, Linux x64/ARM64) werden vom ODM-Server selbst zum Download
  angeboten (auch ohne Internetzugang der Clients) und lassen sich zusätzlich als Release-Artefakte beziehen
- Je ein Convenience-Script für Windows (PowerShell) und Linux (sh), das Paket lädt, die Prüfsumme kontrolliert, den Dienst
  einrichtet und den Agent mit einem Registrierungs-Token anmeldet
- Die Web-Oberfläche zeigt nach dem Erstellen eines Tokens den fertigen Installationsbefehl je Betriebssystem
- Erneutes Ausführen des Scripts aktualisiert den Agent auf die Version des Servers (Registrierung bleibt erhalten)
- Das Registrierungs-Token wird nach erfolgreicher Registrierung nicht mehr auf dem Gerät gespeichert

### 3.6 Agent-Selbstaktualisierung

**Als Systemadministrator** möchte ich, dass sich die Agents selbst auf die vom Server bereitgestellte Version aktualisieren, damit ich nicht jedes Gerät von Hand nachziehen muss.

Akzeptanzkriterien:
- Der Server steuert die Soll-Version (Aus / neueste / feste Version) und kann die Verteilung stufenweise freigeben (Prozentsatz der Geräte, stabil je Gerät)
- Standardmäßig ist die Selbstaktualisierung ausgeschaltet
- Der Agent erhält das Angebot im Heartbeat, lädt das Paket und prüft die SHA-256-Prüfsumme
- Aktualisiert wird nur, wenn der Agent keine Aufträge bearbeitet
- Ein Hilfsprozess tauscht die Programmdatei aus und startet den Dienst neu; meldet sich der neue Agent nicht, wird die alte Version wiederhergestellt und die fehlgeschlagene Version nicht erneut versucht
- Registrierung und Zugangsdaten bleiben erhalten; Ablauf und Fehler stehen in der Logdatei des Agents

### 3.7 Agent-Authentisierung per Client-Zertifikat (mTLS)

**Als Systemadministrator** möchte ich, dass sich Agents zusätzlich mit einem Client-Zertifikat ausweisen, damit ein gestohlenes Agent-Secret allein nicht genügt.

Akzeptanzkriterien:
- ODM betreibt eine eigene CA; der private Schlüssel eines Agents verlässt das Gerät nie (der Agent sendet nur eine Zertifikatsanforderung)
- Das Zertifikat wird bei der Registrierung ausgestellt und vor Ablauf automatisch erneuert
- Das Zertifikat ist an die Zugangsdaten des Geräts gebunden (Agent-ID im Subject, Fingerabdruck serverseitig gespeichert)
- Modi: Aus (Standard) / Optional (Geräte mit Zertifikat müssen es vorzeigen) / Erforderlich
- Ein Administrator kann die Zertifikatsbindung eines Geräts zurücksetzen

### 3.8 Deinstallation, Umbenennen, Meldungen

- Eine Paketzuweisung hat die Absicht „Installieren“ oder „Deinstallieren“; deinstalliert wird nur dort, wo ODM das Paket installiert hat
- Deinstallation für PSADT (Uninstall-Modus des Pakets) und alle Repo-Paketmanager; fehlgeschlagene Läufe werden begrenzt wiederholt
- Softwarepakete können umbenannt werden (Name bleibt eindeutig)
- Die vollständige Meldung einer Installation ist auf der Geräte- und Paketseite lesbar (bis 64 000 Zeichen, z. B. PSADT-Protokolle)

### 3.9 Dashboard

- Startseite mit Kennzahlen: Geräte, Online-Status, Geräte ohne Heartbeat (Schwelle wählbar), fehlgeschlagene Installationen
- Verteilungen nach Betriebssystem und Agent-Version; Status der Softwareverteilung insgesamt und je Paket
- Liste der letzten Fehler und der Geräte ohne Heartbeat mit Link zum Gerät


- **Design:** Clean, minimalistisch, Dark-Mode-Unterstützung
- **Sicherheit:** Passwörter müssen gehasht werden
- **Performance:** Keine externen schweren Bibliotheken, wenn nicht nötig
- **Mehrsprachigkeit (i18n):** Deutsch und Englisch zum Start
- **Schnittstellen:** REST zwischen API und UI sowie zwischen API und Agent
- **Datenablage:** PSADT-Pakete werden auf S3-Storage abgelegt
- **Coding Conventions:** Sauber objektorientiert und modular; Start als modularer Monolith
