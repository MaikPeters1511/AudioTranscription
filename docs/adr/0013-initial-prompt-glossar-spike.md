# ADR 0013: Wirkung des Initial-Prompts (Glossar-Spike)

- **Status:** Akzeptiert
- **Datum:** 2026-11-12
- **Kontext-Referenz:** Epic E-02, Story S22 (#133), Task S22-T1 (#134)

## Kontext

S22 reicht ein Glossar als Initial-Prompt an Whisper (`WithPrompt` in Whisper.net). Die Umsetzung war fertig, aber die Wirkung nie gemessen worden. Dieser Spike misst sie und leitet eine Empfehlung für die Formulierung ab.

## Messaufbau

- **Audio:** ein deutscher Beispieltext mit schwierigen Eigennamen und Fachbegriffen, erzeugt mit der macOS-Stimme „Anna“ (`say`), 16 kHz mono (ca. 14 s).
  Text: „Frau Dr. Wojciechowski von der Kubernetes-Gruppe sprach mit Herrn Szczepaniak über Wolverine, Qdrant und die Aspire-Anbindung. Danach erklärte Kowalczyk das bge-m3 Modell und den Hangfire Dashboard Zugang.“
- **Modelle:** Whisper `Base` und `Small` (GGML), Whisper.net 1.9.1, CPU, Sprache fest `de`, ein Lauf je Variante.
- **Varianten:** ohne Prompt; kommagetrennte Begriffe; ein Satz mit denselben Begriffen.
- **Bewertung:** Von den 9 Glossarbegriffen (Wojciechowski, Szczepaniak, Kowalczyk, Kubernetes, Wolverine, Qdrant, Aspire, bge-m3, Hangfire) zählt ein Begriff als richtig, wenn er in der Ausgabe exakt (ohne Beachtung von Groß-/Kleinschreibung und Bindestrichen) vorkommt.

## Ergebnis

| Modell | ohne Prompt | kommagetrennt | Satz |
| --- | --- | --- | --- |
| Base | 2 / 9 | 5 / 9 | 6 / 9 |
| Small | 2 / 9 | 9 / 9 | 9 / 9 |

Beispiele (Small): ohne Prompt „Frau Dr. Volchekovsky … Ščepanjak … Wohlverine, Ktbrandt … das BGM-3-Modell“, mit Prompt „Frau Dr. Wojciechowski … Szczepaniak … Wolverine, Qdrant … das bge-m3-Modell“.

Der Prompt wirkt deutlich: Bei `Small` werden aus 2 von 9 alle 9 Begriffe richtig, bei `Base` aus 2 von 9 fünf bis sechs. Begriffe, die nicht im Glossar stehen (hier „Dashboard“), werden nicht besser: „Hangfire-Despo“ blieb in allen Prompt-Varianten.

## Entscheidung / Empfehlung

1. Die Umsetzung aus S22 bleibt: Glossarbegriffe kommagetrennt als Initial-Prompt. Kommagetrennt reicht bei `Small`; bei `Base` brachte ein einleitender Satz mit den Begriffen einen Treffer mehr. Ein Satz kostet aber mehr Tokens vom Budget (224), deshalb wird die Formulierung **nicht** umgestellt.
2. Nutzerhinweis (Glossar-Seite): Das Glossar hilft vor allem bei **Eigennamen und Fachwörtern in der Schreibweise, die man im Text haben will**; Begriffe müssen genau so geschrieben werden. Kleine Modelle (`Base`) profitieren weniger, `Small` und größer sind empfohlen, wenn das Glossar wichtig ist.
3. Nur Begriffe aufnehmen, die tatsächlich falsch erkannt werden; jeder zusätzliche Begriff verbraucht Token.

## Einschränkungen

- Ein synthetisches Audio mit einer Stimme und ein Lauf je Variante: Die Zahlen zeigen die Größenordnung, keine statistisch belastbare Verbesserung. Echte Sprecher, Hintergrundgeräusche und Dialekte können anders ausfallen.
- Nur Deutsch, nur `Base` und `Small`; `Medium` und `LargeV3` wurden nicht gemessen.
- Whisper kann einen Prompt mit Begriffen, die nicht im Audio vorkommen, gelegentlich in die Ausgabe einfließen lassen; das wurde in diesem Beispiel nicht beobachtet, aber auch nicht gezielt geprüft.

## Konsequenzen

Keine Codeänderung. Die Empfehlungen 2 und 3 können als Hilfetext in die Glossar-Seite einfließen (eigene, kleine Aufgabe, falls gewünscht).
