// Messages produced by the server and the agent are English. The UI shows them in the selected language.
// Patterns may contain {placeholders} which are carried over to the translation.

const de: [string, string][] = [
  ["Unknown package manager '{p.Manager}'.", "Unbekannter Paketmanager '{p.Manager}'."],
  ["A rollout plan needs 1 to {MaxStages} stages.", "Ein Tranchenplan braucht 1 bis {MaxStages} Stufen."],
  ["Each stage needs a percentage from 1 to 100.", "Jede Stufe braucht einen Prozentwert von 1 bis 100."],
  ["Each stage needs a subgroup.", "Jede Stufe braucht eine Untergruppe."],
  ["Each subgroup may appear only once.", "Jede Untergruppe darf nur einmal vorkommen."],
  ["Unknown package kind.", "Unbekannte Paketart."],
  ["The ZIP file is missing.", "Die ZIP-Datei fehlt."],
  ["A profile needs at least one package.", "Ein Profil braucht mindestens ein Paket."],
  ["Each package may appear only once.", "Jedes Paket darf nur einmal vorkommen."],
  ["Unknown secret: {names}.", "Unbekanntes Secret: {names}."],
  ["Unknown time zone.", "Unbekannte Zeitzone."],
  ["Unknown job type.", "Unbekannter Auftragstyp."],
  ["Platform: Windows or Linux.", "Plattform: Windows oder Linux."],
  ["The ZIP contains no Invoke-AppDeployToolkit.ps1 (PSAppDeployToolkit-Linux package).", "Das ZIP enthält keine Invoke-AppDeployToolkit.ps1 (PSAppDeployToolkit-Linux-Paket)."],
  ["Linux needs a PSAppDeployToolkit-Linux package (Invoke-AppDeployToolkit.ps1).", "Linux benötigt ein PSAppDeployToolkit-Linux-Paket (Invoke-AppDeployToolkit.ps1)."],
  ["PowerShell 7.4 or newer (pwsh) is not installed on this device.", "PowerShell 7.4 oder neuer (pwsh) ist auf diesem Gerät nicht installiert."],
  ["No updates available.", "Keine Updates verfügbar."],
  ["User name is required (letters, digits and . _ @ -, at most 128 characters).", "Benutzername ist erforderlich (Buchstaben, Ziffern und . _ @ -, höchstens 128 Zeichen)."],
  ["Password must be 10 to 256 characters long.", "Das Passwort muss 10 bis 256 Zeichen lang sein."],
  ["Invalid role.", "Ungültige Rolle."],
  ["Display name must be at most 256 characters.", "Der Anzeigename darf höchstens 256 Zeichen lang sein."],
  ["A user with this name already exists.", "Ein Benutzer mit diesem Namen existiert bereits."],
  ["You cannot change your own role or deactivate yourself.", "Sie können Ihre eigene Rolle nicht ändern und sich nicht selbst deaktivieren."],
  ["You cannot delete yourself.", "Sie können sich nicht selbst löschen."],
  ["The last active administrator cannot be removed, demoted or deactivated.", "Der letzte aktive Administrator kann nicht entfernt, herabgestuft oder deaktiviert werden."],
  ["Password change is only available for local users.", "Die Passwortänderung ist nur für lokale Benutzer möglich."],
  ["The current password is wrong.", "Das aktuelle Passwort ist falsch."],
  ["The ZIP contains no PSADT script (Invoke-AppDeployToolkit.ps1 or Deploy-Application.ps1).", "Das ZIP enthält kein PSADT-Skript (Invoke-AppDeployToolkit.ps1 oder Deploy-Application.ps1)."],
  ["Only PSADT packages have files; repository packages are changed through their settings.", "Nur PSADT-Pakete haben Dateien; Repo-Pakete werden über ihre Einstellungen geändert."],
  ["winget was not found (App Installer is missing or not available to the SYSTEM account).", "winget wurde nicht gefunden (App-Installer fehlt oder ist für das SYSTEM-Konto nicht verfügbar)."],
  ["dnf/yum and rpm are not available on this device (not an RPM based distribution).", "dnf/yum und rpm sind auf diesem Gerät nicht vorhanden (keine RPM-basierte Distribution)."],
  ["Name is required (at most 128 characters), description at most 1024 characters.", "Name ist erforderlich (höchstens 128 Zeichen), Beschreibung höchstens 1024 Zeichen."],
  ["The maintenance window had (almost) ended at start; the run was not started.", "Das Wartungsfenster war beim Start bereits (fast) abgelaufen; der Lauf wurde nicht gestartet."],
  ["For repository packages the version is set in the package configuration.", "Bei Repo-Paketen wird die Version in der Paketkonfiguration festgelegt."],
  ["dnf/yum is not available on this device (not an RPM based distribution).", "dnf/yum ist auf diesem Gerät nicht vorhanden (keine RPM-basierte Distribution)."],
  ["apt-get is not available on this device (not a DEB based distribution).", "apt-get ist auf diesem Gerät nicht vorhanden (keine DEB-basierte Distribution)."],
  ["Package name/ID is required (letters, digits and . _ + : @ / -).", "Paketname/-ID ist erforderlich (Buchstaben, Ziffern und . _ + : @ / -)."],
  ["The server address is invalid. Configure Agents:PublicBaseUrl.", "Die Server-Adresse ist ungültig. Agents:PublicBaseUrl konfigurieren."],
  ["{payload.Manager} is not supported on this operating system.", "{payload.Manager} wird auf diesem Betriebssystem nicht unterstützt."],
  ["Operating system family: only 'Is' with Windows or Linux.", "Betriebssystemfamilie: nur 'Is' mit Windows oder Linux."],
  ["Delay and percentage must increase from stage to stage.", "Wartezeit und Prozentwert müssen von Stufe zu Stufe steigen."],
  ["A valid pinned version is missing for '{package.Name}'.", "Für '{package.Name}' fehlt eine gültige feste Version."],
  ["The checksum of the downloaded package does not match.", "Prüfsumme des heruntergeladenen Pakets stimmt nicht überein."],
  ["Job type '{job.Type}' is not supported by this agent.", "Auftragstyp '{job.Type}' wird von diesem Agent nicht unterstützt."],
  ["apt-get update failed (exit code {update.ExitCode}).", "apt-get update fehlgeschlagen (Exit-Code {update.ExitCode})."],
  ["Windows Update failed (exit code {result.ExitCode}).", "Windows Update fehlgeschlagen (Exit-Code {result.ExitCode})."],
  ["The chosen version does not belong to this package.", "Die gewählte Version gehört nicht zu diesem Paket."],
  ["No supported package manager found (apt, dnf, yum).", "Kein unterstützter Paketmanager gefunden (apt, dnf, yum)."],
  ["A profile may contain at most {MaxItems} packages.", "Ein Profil darf höchstens {MaxItems} Pakete enthalten."],
  ["The output of the update script could not be read.", "Ausgabe des Update-Skripts konnte nicht gelesen werden."],
  ["Duration must be between 15 minutes and 24 hours.", "Dauer muss zwischen 15 Minuten und 24 Stunden liegen."],
  ["check-update failed (exit code {check.ExitCode}).", "check-update fehlgeschlagen (Exit-Code {check.ExitCode})."],
  ["{parsed.Failed} update(s) could not be installed.", "{parsed.Failed} Update(s) konnten nicht installiert werden."],
  ["A quotation mark in the arguments is not closed.", "Ein Anführungszeichen in den Argumenten ist nicht geschlossen."],
  ["The delay must be between 0 and {MaxDays} days.", "Die Wartezeit muss zwischen 0 und {MaxDays} Tagen liegen."],
  ["No agent packages are available on this server.", "Auf diesem Server sind keine Agent-Pakete hinterlegt."],
  ["Timed out after {timeout.TotalMinutes} minutes.", "Zeitüberschreitung nach {timeout.TotalMinutes} Minuten."],
  ["A version must be chosen for a pinned version.", "Für eine feste Version muss eine Version gewählt werden."],
  ["apt-get failed (exit code {upgrade.ExitCode}).", "apt-get fehlgeschlagen (Exit-Code {upgrade.ExitCode})."],
  ["Exit code {code} (succeeded, restart required)", "Exit-Code {code} (erfolgreich, Neustart erforderlich)"],
  ["PSADT packages are only supported on Windows.", "PSADT-Pakete werden nur unter Windows unterstützt."],
  ["Description must be at most 1024 characters.", "Beschreibung darf höchstens 1024 Zeichen lang sein."],
  ["Value is required (at most 4000 characters).", "Wert ist erforderlich (höchstens 4000 Zeichen)."],
  ["Value is required (at most 256 characters).", "Wert ist erforderlich (höchstens 256 Zeichen)."],
  ["Label is required (at most 128 characters).", "Bezeichnung ist erforderlich (höchstens 128 Zeichen)."],
  ["Chocolatey is not installed on this device.", "Chocolatey ist auf diesem Gerät nicht installiert."],
  ["Name is required (at most 128 characters).", "Name ist erforderlich (höchstens 128 Zeichen)."],
  ["No PSADT script was found in the package.", "Im Paket wurde kein PSADT-Skript gefunden."],
  ["At most {MaxArgumentsLength} characters.", "Höchstens {MaxArgumentsLength} Zeichen."],
  ["No update policy applies to this device.", "Für dieses Gerät gilt keine Update-Richtlinie."],
  ["Flatpak is not installed on this device.", "Flatpak ist auf diesem Gerät nicht installiert."],
  [" failed (exit code {upgrade.ExitCode}).", " fehlgeschlagen (Exit-Code {upgrade.ExitCode})."],
  ["Start must be between 00:00 and 23:59.", "Beginn muss zwischen 00:00 und 23:59 liegen."],
  ["At most {MaxRules} rules are allowed.", "Höchstens {MaxRules} Regeln erlaubt."],
  ["snap is not installed on this device.", "snap ist auf diesem Gerät nicht installiert."],
  ["User name and password are required.", "Benutzername und Passwort sind erforderlich."],
  ["Name must be at most 128 characters.", "Name darf höchstens 128 Zeichen lang sein."],
  ["The file is not a valid ZIP archive.", "Die Datei ist kein gültiges ZIP-Archiv."],
  ["The secret '{name}' is not defined.", "Das Secret '{name}' ist nicht definiert."],
  ["The file exceeds the allowed size.", "Datei überschreitet die erlaubte Grösse."],
  ["Timed out after {minutes} minutes.", "Zeitüberschreitung nach {minutes} Minuten."],
  ["At least one weekday is required.", "Mindestens ein Wochentag ist erforderlich."],
  ["The last stage must reach 100 %.", "Die letzte Stufe muss 100 % erreichen."],
  ["The server address is invalid.", "Die Server-Adresse ist ungültig."],
  ["Invalid scope or target type.", "Ungültiger Bereich oder Zieltyp."],
  ["A subgroup was not found.", "Eine Untergruppe wurde nicht gefunden."],
  ["At most 1024 characters.", "Höchstens 1024 Zeichen."],
  ["Description is too long.", "Beschreibung ist zu lang."],
  ["A package was not found.", "Ein Paket wurde nicht gefunden."],
  ["At most 4000 characters.", "Höchstens 4000 Zeichen."],
  ["Exit code {code} (error)", "Exit-Code {code} (Fehler)"],
  ["At most 256 characters.", "Höchstens 256 Zeichen."],
  ["Invalid version policy.", "Ungültige Versionsregel."],
  ["Scope: machine or user.", "Bereich: machine oder user."],
  ["Exit code 0 (succeeded)", "Exit-Code 0 (erfolgreich)"],
  ["At most 64 characters.", "Höchstens 64 Zeichen."],
  ["Scope: system or user.", "Bereich: system oder user."],
  ["Invalid rollout mode.", "Ungültiger Tranchenmodus."],
  ["Invalid restart rule.", "Ungültige Neustart-Regel."],
  ["Invalid target type.", "Ungültiger Zieltyp."],
  ["Invalid job content.", "Ungültiger Auftragsinhalt."],
  ["Invalid match mode.", "Ungültiger Abgleichmodus."],
  ["Target is required.", "Ziel ist erforderlich."],
  ["Name is required.", "Name ist erforderlich."],
  ["Invalid operator.", "Ungültiger Operator."],
  ["Target not found.", "Ziel nicht gefunden."],
  ["Invalid version.", "Ungültige Version."],
  ["Invalid channel.", "Ungültiger Kanal."],
  ["Invalid weekday.", "Ungültiger Wochentag."],
  ["Exit code {code}", "Exit-Code {code}"],
  ["Invalid action.", "Ungültige Absicht."],
  ["Invalid source.", "Ungültige Quelle."],
  ["Invalid remote.", "Ungültiges Remote."],
  ["Invalid field.", "Ungültiges Feld."],
  ["Invalid mode.", "Ungültiger Modus."],
  ["Invalid ID.", "Ungültige ID."],
  ["Timed out.", "Zeitüberschreitung."],
  ["(error)", "(Fehler)"],
]

const compiled = de.map(([en, german]) => {
  const names: string[] = []
  const source = en
    .split(/(\{[\w.]+\})/)
    .map((part) => {
      const match = /^\{([\w.]+)\}$/.exec(part)
      if (match) {
        names.push(match[1])
        return '(\\S+)'
      }
      return part.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
    })
    .join('')
  return { regex: new RegExp(source, 'g'), names, german }
})

/** Translates the known server / agent messages inside a text; unknown text (e.g. tool output) stays unchanged. */
export function translateServerMessage(text: string, locale: 'de' | 'en'): string {
  if (locale !== 'de') return text
  let result = text
  for (const { regex, names, german } of compiled) {
    result = result.replace(regex, (...args) => {
      let replaced = german
      names.forEach((name, index) => {
        replaced = replaced.replaceAll(`{${name}}`, args[index + 1] as string)
      })
      return replaced
    })
  }
  return result
}
