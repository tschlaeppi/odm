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

## 4. Nichtfunktionale Anforderungen

- **Design:** Clean, minimalistisch, Dark-Mode-Unterstützung
- **Sicherheit:** Passwörter müssen gehasht werden
- **Performance:** Keine externen schweren Bibliotheken, wenn nicht nötig
- **Mehrsprachigkeit (i18n):** Deutsch und Englisch zum Start
- **Schnittstellen:** REST zwischen API und UI sowie zwischen API und Agent
- **Datenablage:** PSADT-Pakete werden auf S3-Storage abgelegt
- **Coding Conventions:** Sauber objektorientiert und modular; Start als modularer Monolith
