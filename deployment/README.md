# Deployment, Installer und Auto-Updater

## 1) Release bauen

```powershell
powershell -ExecutionPolicy Bypass -File .\deployment\Build-Release.ps1
```

Ergebnis:
- `artifacts\publish\app` -> fertige App-Dateien
- `artifacts\update-feed` -> Update-Ordner inkl. `manifest.json`

## 2) Installer erstellen (Inno Setup)

1. Inno Setup installieren (`iscc.exe` muss im PATH sein).
2. Installer bauen:

```powershell
iscc /DMyAppVersion=1.0.0 /DSourceDir=".\artifacts\publish\app" /DOutputDir=".\artifacts\installer" ".\deployment\Installer.iss"
```

Ergebnis:
- `artifacts\installer\ZomboidGuide-Setup-<Version>.exe`

## 3) GitHub Releases bereitstellen

Die App bezieht Updates jetzt ausschliesslich aus GitHub Releases.
Als Quelle wird in der App ein Repository angegeben, z. B.:
- `MietzeMatze/ZomboidGuide-Releases`
- oder eine GitHub-URL auf dieses Repo

Die Release-Assets sollen den Installer enthalten.
Empfohlen ist das Asset:
- `ZomboidGuide-Setup-<Version>.exe`

## 4) In der App konfigurieren

- Feld `Update-Pfad` auf GitHub-Repo setzen:
  - `owner/repo` (z. B. `MietzeMatze/ZomboidGuide-Releases`)
  - alternativ GitHub-URL
- `Auto-Updatecheck` aktivieren.
- `Nach Update suchen` klicken (oder beim Start automatisch pruefen).
- Bei verfuegbarer neuer Version: `Update installieren`.

Die App laedt den Installer aus GitHub, startet ihn und beendet sich danach.
Dadurch ist die Installation auch sauber ueber Windows deinstallierbar (Apps & Features).

## 5) Automatisches GitHub Release in separates Repo

Workflow-Datei:
- `.github/workflows/release.yml`

Trigger:
- automatisch bei Push eines Tags im Format `v*` (z. B. `v1.0.1`)

Wichtig: Das Release wird **nicht** im Source-Repo erstellt, sondern in einem separaten Ziel-Repo.

### GitHub Settings im Source-Repo

1. Repository Variable setzen:
- Name: `RELEASE_TARGET_REPOSITORY`
- Wert: `owner/repo` (z. B. `MietzeMatze/ZomboidGuide-Releases`)

2. Repository Secret setzen:
- Name: `ZOMBOIDGUIDERELEASES`
- Wert: Fine-grained PAT (oder klassischer PAT) mit `Contents: write` auf dem Ziel-Repo

Der Workflow:
1. baut die App im Release-Modus
2. erstellt `artifacts/publish/app` und `artifacts/update-feed`
3. erstellt mit Inno Setup einen Installer (`.exe`)
4. erstellt Release im Ziel-Repo und laedt Installer + Checksummen hoch

Beispiel:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

## 6) Auto-Tag + Push (Buildnummer automatisch erhoehen)

Script:
- `deployment/Build-Tag-Push.ps1`

Was es macht:
1. holt Tags von `origin`
2. ermittelt den hoechsten `vX.Y.Z`-Tag
3. erhoeht Patch um `+0.0.1`
4. setzt `Version`, `AssemblyVersion`, `FileVersion` im `.csproj` auf die neue Version
5. baut die Solution
6. committed die Versionsaenderung (nur bei erfolgreichem Build)
7. erstellt Tag nur bei erfolgreichem Build
8. pusht Branch und Tag

Beispiel:

```powershell
powershell -ExecutionPolicy Bypass -File .\deployment\Build-Tag-Push.ps1
```

Dry-Run (ohne Tag/Push):

```powershell
powershell -ExecutionPolicy Bypass -File .\deployment\Build-Tag-Push.ps1 -SkipBuild -DryRun
```
