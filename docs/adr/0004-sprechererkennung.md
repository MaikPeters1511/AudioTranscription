# ADR 0004: Sprechererkennung mit sherpa-onnx (pyannote-Segmentierung + Embeddings)

- **Status:** Akzeptiert
- **Datum:** 2026-09-24
- **Kontext-Referenz:** Epic E-01, Story S11 (#19), Task S11-T1 (#72)

## Kontext

S11 verlangt eine Zuordnung von Zeitabschnitten zu anonymen Sprechern („Sprecher 1“, „Sprecher 2“ …), vollständig lokal und offline, ohne die Datenschutz-Eigenschaft der App (S01) zu verletzen. Geprüfte Kandidaten:

1. **sherpa-onnx** (Next-gen-Kaldi-Projekt, C#-Bindings als NuGet-Paket `org.k2fsa.sherpa.onnx`). Bündelt eine `OfflineSpeakerDiarization`-API, die intern ein pyannote-Segmentierungsmodell (ONNX) und ein Sprecher-Embedding-Modell (ONNX) kombiniert und clustert.
2. **pyannote als eigener ONNX-Export mit eigenem Clustering:** Gleiche Modelle wie oben, aber ohne fertige .NET-Anbindung — Segmentierung, Embedding-Extraktion und Clustering (z. B. agglomeratives Clustering) müssten selbst in C# oder über `Microsoft.ML.OnnxRuntime` direkt implementiert werden.
3. **Python-Sidecar-Container** mit `pyannote.audio` direkt (PyTorch), angesprochen über HTTP oder eine Datei-Queue.

## Bewertung

| Kriterium | sherpa-onnx | Eigener ONNX-Export | Python-Sidecar |
|---|---|---|---|
| Offline-Fähigkeit | Ja, reine ONNX-Runtime, keine Laufzeit-Downloads | Ja | Ja, aber zusätzlicher Container/Prozess |
| Lizenz (Code) | Apache 2.0 (sherpa-onnx selbst) | Eigener Code, MIT-kompatibel | Eigener Code |
| Lizenz (Modelle) | Abhängig vom gewählten Modell-Checkpoint (i. d. R. MIT/Apache, teils „pyannote“-Gate mit Zustimmung); vom Betreiber beim Herunterladen zu prüfen | Gleiche Modelle, gleiche Prüfung nötig | Gleiche Modelle, gleiche Prüfung nötig |
| Integrationsaufwand in .NET | Gering: fertige `OfflineSpeakerDiarization`-Klasse, natives Paket pro RID (u. a. `linux-x64`) via NuGet | Hoch: Segmentierung, Sliding-Window, Embedding-Extraktion, Clustering selbst schreiben | Mittel: kein .NET-ML-Code, aber neuer Prozess/Container, Serialisierung, zusätzlicher Fehlerkanal |
| Laufzeit | Nativ, ein Prozess (die API selbst) | Nativ, ein Prozess | Extra Prozess/Container, IPC-Overhead |
| Genauigkeit (DER) | **Nicht gemessen** — die Modell-Dateien werden von HuggingFace bzw. den sherpa-onnx-GitHub-Releases geladen; beide Hosts sind aus dieser Entwicklungsumgebung heraus nicht erreichbar (`403`, siehe unten). Eine DER-Messung an Testaufnahmen ist deshalb hier nicht möglich und bleibt offen (siehe Konsequenzen) | Gleiche Modelle, gleiche Einschränkung | Gleiche Modelle, gleiche Einschränkung |

**Geprüft, nicht nur angenommen:** `dotnet add package org.k2fsa.sherpa.onnx` restauriert erfolgreich Version 1.13.8 inklusive nativer Laufzeitpakete für `linux-x64` (und weitere RIDs). Per Reflection auf `sherpa-onnx.dll` ist die tatsächliche API bestätigt:

```csharp
var config = new OfflineSpeakerDiarizationConfig();
config.Segmentation.Pyannote.Model = "…/segmentation.onnx";
config.Embedding.Model = "…/embedding.onnx";
config.Clustering.NumClusters = -1;   // -1 = automatisch über Threshold
config.Clustering.Threshold = 0.5f;
config.MinDurationOn = 0.3f;
config.MinDurationOff = 0.5f;

using var sd = new OfflineSpeakerDiarization(config);   // sd.SampleRate erwartet i. d. R. 16 kHz
var segments = sd.Process(pcmFloatSamples);              // OfflineSpeakerDiarizationSegment[]
foreach (var s in segments) { /* s.Start, s.End (Sekunden), s.Speaker, s.Confidence */ }
```

Das passt ohne Änderung an die bestehende Audio-Pipeline: `WhisperTranscriptionService` erzeugt für Whisper bereits eine 16-kHz-Mono-WAV-Datei; dieselbe Konvertierung eignet sich für die Diarisierung.

`huggingface.co` und `github.com` sind aus dieser Entwicklungsumgebung heraus mit `403` gesperrt (bereits bei den Whisper-Modellen in S08 beobachtet). Das NuGet-Paket selbst lädt zuverlässig, nur die eigentlichen Modell-Gewichte (Segmentierung, Embedding) müssen vom Betreiber lokal beschafft und über Konfiguration referenziert werden — analog zu den Whisper-GGML-Modellen, nur ohne automatischen Download beim ersten Einsatz.

## Entscheidung

**sherpa-onnx** über das NuGet-Paket `org.k2fsa.sherpa.onnx`.

- Geringster Integrationsaufwand: keine eigene Segmentierungs-/Clustering-Logik, keine zusätzliche Laufzeitumgebung.
- Bleibt strikt offline und im selben .NET-Prozess wie die übrige Transkription; kein zusätzlicher Container, keine IPC-Fehlerquelle wie beim Sidecar-Ansatz.
- `IDiarizationService` kapselt die konkrete Bibliothek, ein Wechsel auf einen eigenen ONNX-Export oder einen Sidecar bliebe später möglich, ohne den Rest der Anwendung zu berühren.

**Verworfen:**
- **Eigener ONNX-Export mit eigenem Clustering:** Deutlich mehr Code (Sliding-Window-Segmentierung, Embedding-Batching, Clustering-Algorithmus) für denselben fachlichen Nutzen wie eine fertige, gepflegte Bibliothek.
- **Python-Sidecar:** Zusätzlicher Container, zusätzliche Betriebsabhängigkeit (Docker-Netzwerk, Healthcheck, Versionsdrift zwischen Sidecar und API) ohne erkennbaren Vorteil gegenüber einer nativen .NET-Bibliothek, die dieselben Modelle nutzt.

## Konsequenzen

- **Konfiguration** (`Diarization`-Abschnitt): `Enabled` (Standard `false`), Pfade zu den beiden ONNX-Modellen (`SegmentationModelPath`, `EmbeddingModelPath`), Clustering-Parameter (`Threshold`, `NumThreads`). Wie bei Ollama (S10) ist Diarisierung ein optionales Feature; ohne konfigurierte Modelle bleibt sie aus, der Upload-Parameter dafür wird dann abgelehnt.
- **Pro Upload:** Ein Flag (`diarize`) entscheidet, ob ein Job diarisiert wird (Vorgabe der Story). Die eigentliche Zuordnung der erkannten Sprecher-Zeiträume zu den Whisper-Transkript-Segmenten erfolgt über die größte zeitliche Überlappung (S11-T3), eine reine, ohne Modell testbare Funktion.
- **Offene Folge-Aufgabe (aus dem Spike):** Die geforderte DER-Messung an 2–3 Testaufnahmen (AC dieser Task) und die Genauigkeitsprüfung im DoD (≥ 85 % Sprechzeit korrekt zugeordnet) sind **nicht** durchgeführt, weil weder Modelle noch eine geeignete Mehrsprecher-Testaufnahme in dieser Umgebung beschaffbar sind. Wer die Modelle lokal ablegt (z. B. `sherpa-onnx-pyannote-segmentation-3-0/model.onnx` und ein kompatibles Embedding-Modell wie `nemo_en_titanet_small.onnx` von den offiziellen sherpa-onnx-Releases), kann die Messung nachholen; die Implementierung (T2–T5) ist unabhängig davon vollständig und getestet, soweit ohne echte Modelle möglich.
- **Lizenz-Prüfung der Modelle** bleibt Aufgabe des Betreibers beim Beschaffen der konkreten Modell-Datei (siehe Tabelle oben), da die Lizenz vom gewählten Checkpoint abhängt und hier nicht heruntergeladen/geprüft werden konnte.
