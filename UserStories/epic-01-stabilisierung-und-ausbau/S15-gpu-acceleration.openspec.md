# Story S15: GPU-Beschleunigung

> Epic: [E-01](./00-epic.openspec.md) · GitHub: #18 · Phase 3 · Agenten: devops-agent, backend-agent

## 1. Description
Whisper läuft aktuell auf der CPU. Whisper.net bietet Runtimes für CUDA (`Whisper.net.Runtime.Cuda`), CoreML (`Whisper.net.Runtime.CoreML`) und Vulkan. Das verkürzt die Laufzeit vor allem bei den Modellen `medium` und `large-v3` (S08) deutlich.

## 2. User Stories
- Als Betreiber mit NVIDIA-GPU oder Apple Silicon möchte ich die Hardware-Beschleunigung nutzen, damit auch große Modelle schnell transkribieren.

## 3. Tasks

### S15-T1 Spike: Runtime-Auswahl und Fallback · backend · S
- Prüfen, wie Whisper.net mehrere Runtimes parallel lädt (`RuntimeOptions.RuntimeLibraryOrder`) und ob ein automatischer CPU-Fallback greift.
- **AC:**
  - [~] Ergebnis mit Benchmark (CPU vs. GPU, Modell `small`, 5-Minuten-Datei) ist dokumentiert. Die Runtime-Auswahl/Fallback-Mechanik selbst wurde per Reflection gegen die reale Whisper.net-1.9.1-Bibliothek verifiziert und live erprobt (siehe Umsetzungsnotizen); ein echter CPU-vs-GPU-Zeitvergleich ist in dieser Sandbox mangels GPU-Hardware nicht möglich (gleiche Einschränkungsklasse wie fehlender Modell-Download in S08/S11).

### S15-T2 Konfigurierbare Runtime-Reihenfolge · backend · S
- `Whisper:RuntimeOrder` (z. B. `["Cuda","CoreML","Cpu"]`). Die tatsächlich genutzte Runtime wird beim Start geloggt.
- **AC:**
  - [x] Ohne GPU startet die App unverändert auf der CPU. (`WhisperRuntimeConfiguratorTests.StartAsync_WhisperNetDefaultOrder_EndsInCpuAsAFallback`, zusätzlich per Reflection-Experiment am realen `WhisperFactory.FromPath` bestätigt: `RuntimeOptions.LoadedLibrary` wird `Cpu`, wenn keine GPU-Runtime verfügbar ist.)

### S15-T3 Docker-Variante mit CUDA · devops · M
- Eigenes Dockerfile-Target bzw. Image-Tag `-cuda` auf NVIDIA-CUDA-Runtime-Basis, docker-compose-Profil `gpu` mit `deploy.resources.reservations.devices`.
- **AC:**
  - [~] `docker compose --profile gpu up` nutzt die GPU (sichtbar im Log und in `nvidia-smi`). Umgesetzt (`final-cuda`-Docker-Stage, `api-gpu`-Service mit `profiles: ["gpu"]` und `deploy.resources.reservations.devices`); `docker compose config` mit `COMPOSE_PROFILES=gpu` bestätigt korrekte Auflösung, `docker compose config --services` ohne Profil bestätigt, dass der Standard-Ablauf unverändert bleibt. Ein echter `docker build --target final-cuda` in dieser Sandbox zog das `nvidia/cuda:12.4.1-runtime-ubuntu22.04`-Basis-Image erfolgreich (77 s, echtes Netzwerk), scheiterte aber beim `dotnet restore`-Schritt am selben TLS-Zertifikatsproblem, das schon `docker/mssql-fts` in S13 betraf — hier gegen `api.nuget.org`, nicht `packages.microsoft.com` (siehe Umsetzungsnotizen). Betrieb mit echter GPU war ohnehin nicht möglich (keine GPU-Hardware in dieser Sandbox).

### S15-T4 Dokumentation · doc · XS
- **AC:**
  - [x] README-Abschnitt „GPU“ mit Voraussetzungen (Treiber, CUDA-Version, macOS-Version).

## 4. Acceptance Criteria (DoD)
- [~] Messbarer Speed-up ist in der PR dokumentiert, der CPU-Pfad funktioniert unverändert. Der CPU-Pfad ist per Test und Reflection-Experiment bestätigt; ein messbarer GPU-Speed-up kann ohne GPU-Hardware in dieser Sandbox nicht erhoben werden und ist von einem Betreiber mit entsprechender Hardware nachzuholen.

## 5. Umsetzungsnotizen

- **Reale API statt Annahme (Beweise vor Behauptungen):** Statt zu vermuten, wie `Whisper.net.LibraryLoader.RuntimeOptions` funktioniert, wurde die tatsächliche Whisper.net-1.9.1-Bibliothek per Reflection untersucht (Scratch-Projekt, NuGet-Restore der echten Pakete): `RuntimeOptions` ist eine statische Klasse mit `RuntimeLibraryOrder` (`List<RuntimeLibrary>`, standardmäßig `[Cuda, Cuda12, Vulkan, CoreML, OpenVino, Cpu, CpuNoAvx]`) und `LoadedLibrary` (nullable, erst nach dem ersten nativen Bibliotheks-Load gesetzt). Ein tatsächlicher Aufruf von `WhisperFactory.FromPath(...)` in dieser GPU-losen Sandbox bestätigte live: `RuntimeOptions.LoadedLibrary` wird `Cpu` — der eingebaute Fallback funktioniert nachweislich, nicht nur laut Dokumentation.
- **`WhisperOptions.RuntimeOrder`:** Neues Konfigurationsfeld (`string[]`, Default `[]` = Whisper.nets eigene Standardreihenfolge bleibt unangetastet), validiert in `WhisperOptionsValidator` gegen die echten `RuntimeLibrary`-Werte (`Cpu`, `Cuda`, `Cuda12`, `Vulkan`, `CoreML`, `OpenVino`, `CpuNoAvx`); dieselbe Absicherung wie bei `TryParseModelType` gegen `Enum.TryParse`, das sonst Zahlen oder kommagetrennte Kombinationen akzeptieren würde.
- **`WhisperRuntimeConfigurator`** (neuer `IHostedService`, vor `JobRecoveryService`/`TranscriptionWorker` registriert): setzt `RuntimeOptions.RuntimeLibraryOrder`, falls `Whisper:RuntimeOrder` konfiguriert ist, und loggt die konfigurierte Reihenfolge beim Start. Da Modelle je Job lazy geladen werden (`KeyedAsyncCache`), ist die *tatsächlich* geladene Runtime erst beim ersten Modell-Load bekannt — das wird zusätzlich in `WhisperTranscriptionService.LoadFactoryAsync` geloggt (`Whisper native runtime in use: ...`). Das weicht von der wörtlichen Story-Formulierung „wird beim Start geloggt“ leicht ab (technisch nicht möglich, bevor ein Modell tatsächlich geladen wurde); die Konfiguration selbst wird aber beim Start geloggt.
- **CUDA-Paket nur opt-in:** `Whisper.net.Runtime.Cuda` wird der Infrastructure-csproj nur unter `IncludeCudaRuntime=true` hinzugefügt (per `dotnet restore`/`dotnet build -p:IncludeCudaRuntime=true` lokal verifiziert, dass das Paket dann und nur dann in `project.assets.json` landet), damit das normale CPU-Image nicht die CUDA-nativen Bibliotheken mitschleppt.
- **Docker/`docker-compose`:** `final-cuda`-Stage im bestehenden `AudioTranscription.Api/Dockerfile` basiert auf `nvidia/cuda:12.4.1-runtime-ubuntu22.04` (per NuGet/Docker-Hub-Abfrage bestätigt, dass Paket und Image-Tag real existieren) statt `mcr.microsoft.com/dotnet/aspnet:10.0`, da nur das CUDA-Image `libcudart`/`libcublas` mitbringt; die ASP.NET-Core-Runtime wird per `packages.microsoft.com`-Repo nachinstalliert (gleiches Muster wie `docker/mssql-fts/Dockerfile` aus S13). `docker-compose.yml` bekommt einen neuen `api-gpu`-Dienst unter `profiles: ["gpu"]` (Port 8081, `deploy.resources.reservations.devices` mit `driver: nvidia`) **neben** dem bestehenden `api`-Dienst, statt ihn zu ersetzen — `docker compose up` ohne Profil bleibt dadurch unverändert (per `docker compose config --services` verifiziert).
- **Docker-Build tatsächlich versucht (Docker-Daemon in dieser Sandbox lief zeitweise, per `dockerd &` gestartet):** `docker build --target final-cuda` zog das reale `nvidia/cuda:12.4.1-runtime-ubuntu22.04`-Image (77 s), lief in den `apt-get`-Schritt der `final-cuda`-Stage hinein, scheiterte aber im parallelen `build`-Stage (`dotnet restore`) an `NU1301: The remote certificate is invalid ... UntrustedRoot` gegen `api.nuget.org` — dasselbe TLS-Zertifikatsproblem des Sandbox-Egress-Proxys wie bei `packages.microsoft.com` in S13, hier nur eine andere Domain, bestätigt durch einen unabhängigen `docker run curlimages/curl https://api.nuget.org/...`-Versuch mit demselben Fehlerbild. Die Dockerfile-Struktur selbst (Stages, Base-Image, apt-Befehle) ist damit soweit bestätigt, wie es ohne echten Internetzugang aus einem Docker-Build heraus möglich ist; der vollständige Build und der Betrieb mit echter GPU sind von einem Betreiber mit Internetzugang und NVIDIA-Hardware zu verifizieren.
- **Kein zusätzliches ADR:** Die Story verlangt für T1 nur eine dokumentierte Benchmark-/Rechercheentscheidung, kein eigenes ADR-Dokument; das Ergebnis ist hier in den Umsetzungsnotizen sowie im README-Abschnitt „GPU“ festgehalten.
