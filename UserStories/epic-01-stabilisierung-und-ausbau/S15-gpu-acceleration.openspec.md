# Story S15: GPU-Beschleunigung

> Epic: [E-01](./00-epic.openspec.md) · Phase 3 · Agenten: devops-agent, backend-agent

## 1. Description
Whisper läuft aktuell auf der CPU. Whisper.net bietet Runtimes für CUDA (`Whisper.net.Runtime.Cuda`), CoreML (`Whisper.net.Runtime.CoreML`) und Vulkan. Das verkürzt die Laufzeit vor allem bei den Modellen `medium` und `large-v3` (S08) deutlich.

## 2. User Stories
- Als Betreiber mit NVIDIA-GPU oder Apple Silicon möchte ich die Hardware-Beschleunigung nutzen, damit auch große Modelle schnell transkribieren.

## 3. Tasks

### S15-T1 Spike: Runtime-Auswahl und Fallback · backend · S
- Prüfen, wie Whisper.net mehrere Runtimes parallel lädt (`RuntimeOptions.RuntimeLibraryOrder`) und ob ein automatischer CPU-Fallback greift.
- **AC:**
  - [ ] Ergebnis mit Benchmark (CPU vs. GPU, Modell `small`, 5-Minuten-Datei) ist dokumentiert.

### S15-T2 Konfigurierbare Runtime-Reihenfolge · backend · S
- `Whisper:RuntimeOrder` (z. B. `["Cuda","CoreML","Cpu"]`). Die tatsächlich genutzte Runtime wird beim Start geloggt.
- **AC:**
  - [ ] Ohne GPU startet die App unverändert auf der CPU.

### S15-T3 Docker-Variante mit CUDA · devops · M
- Eigenes Dockerfile-Target bzw. Image-Tag `-cuda` auf NVIDIA-CUDA-Runtime-Basis, docker-compose-Profil `gpu` mit `deploy.resources.reservations.devices`.
- **AC:**
  - [ ] `docker compose --profile gpu up` nutzt die GPU (sichtbar im Log und in `nvidia-smi`).

### S15-T4 Dokumentation · doc · XS
- **AC:**
  - [ ] README-Abschnitt „GPU“ mit Voraussetzungen (Treiber, CUDA-Version, macOS-Version).

## 4. Acceptance Criteria (DoD)
- [ ] Messbarer Speed-up ist in der PR dokumentiert, der CPU-Pfad funktioniert unverändert.
