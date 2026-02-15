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

## 3) Update-Feed bereitstellen

Die App liest Updates aus einem Ordnerpfad (lokal oder Netzwerkpfad), z. B.:
- `D:\ZomboidGuideUpdates`
- `\\NAS\share\ZomboidGuideUpdates`

Struktur:

```text
<UpdatePfad>\
  manifest.json
  package\
    ZomboidGuide.exe
    ...
```

Beispiel `manifest.json`:

```json
{
  "version": "1.0.1",
  "packagePath": "package",
  "exeName": "ZomboidGuide.exe",
  "notes": "Bugfixes"
}
```

## 4) In der App konfigurieren

- Feld `Update-Pfad` setzen (Ordner mit `manifest.json`).
- `Auto-Updatecheck` aktivieren.
- `Nach Update suchen` klicken (oder beim Start automatisch pruefen).
- Bei verfuegbarer neuer Version: `Update installieren`.

Die App beendet sich, kopiert die Dateien aus `package` ueber die Installation und startet neu.

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

2. Optional Repository Variable:
- Name: `RELEASE_TARGET_COMMITISH`
- Wert: Branch im Ziel-Repo, Standard ist `main`

3. Repository Secret setzen:
- Name: `ZOMBOIDGUIDERELEASES`
- Wert: Fine-grained PAT (oder klassischer PAT) mit `Contents: write` auf dem Ziel-Repo

Der Workflow:
1. baut die App im Release-Modus
2. erstellt `artifacts/publish/app` und `artifacts/update-feed`
3. packt ZIP-Artefakte
4. erstellt Release im Ziel-Repo und laedt die Dateien hoch

Beispiel:

```powershell
git tag v1.0.1
git push origin v1.0.1
```
