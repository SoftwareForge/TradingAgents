# TODO (Fork-Branch)

## Status
- Neuer Fork: LM-Studio-Integration umgesetzt; Verbosity-Optimierung umgesetzt.

## Im neuen Fork noch umzusetzen
- [x] Neues CLI-Provider-Label hinzufügen:
  - `LM Studio`
- [x] Für `LM Studio` vorerst fixe Backend-URL setzen:
  - `http://127.0.0.1:1234/v1`
- [x] README ergänzt:
  - LM Studio via bestehendem `ollama`-Pfad
  - Beispielkonfiguration mit `backend_url`
  - Troubleshooting (Server läuft, Modell geladen, Endpoint erreichbar)
  - Docker-Hinweis für Host-LM-Studio:
    - macOS/Windows: `http://host.docker.internal:1234/v1`
    - Linux (typisch): `http://172.17.0.1:1234/v1`

## kompaktere Ausgaben ohne Qualitätsverlust
- [x] Analyst-Prompts auf kompakte, evidenzdichte Struktur umstellen
  - Executive Summary: 5-10 (bis 300 Wörter) Sätze
  - Key Evidence: 5-10 Bullet Points
  - Actionable Implications: 5-10 Bullet Points
  - Tabelle: max. 10 Zeilen
  - Einheitliche ausgabe/struktur erzwingen. Für bessere vergleichbarkeit
- [x] Debattenagenten (Bull/Bear + Risk) mit Turn-Limits versehen
  - pro Turn 150-200 Wörter
  - nur neue/kontrastierende Argumente, Wiederholungen vermeiden
- [x] Structured-Output-Feldanweisungen straffen
  - keine neuen Felder
  - bestehende Header/Parser-Kompatibilität beibehalten
- [x] Optionalen Config-Key `report_verbosity` einführen
  - `standard` (Default), `compact` (optional)
- Aktuelle werte als Referenz (aus einem run)
  - Aggressive Risk Analyst: 890 Wörter
  - Conservative Risk Analyst: 610 Wörter
  - Neutral Risk Analyst: 990 Wörter
  - Portfolio Manager: 660 Wörter

## Akzeptanzkriterien
- [ ] Entscheidungsergebnis bleibt qualitativ gleichwertig (gleiche Kernargumente/Risiken/Aktion).
- [ ] Report-Länge sinkt um ca. 20-30% im `compact`-Modus.
- [ ] Keine Breaking Changes in öffentlichen Interfaces.
- [ ] Bestehende Downstream-Kompatibilität bleibt erhalten (`FINAL TRANSACTION PROPOSAL`, `**Rating**`, etc.).

## Test-Checkliste
- [ ] A/B-Lauf gleicher Ticker/Datum: `standard` vs `compact`
- [ ] Längenmessung pro Abschnitt + gesamt
- [ ] Negativfälle: falscher Endpoint/fehlendes Modell/Structured-Fallback
- [ ] Mehrsprachigkeit weiterhin korrekt

## Zukunftsplan: UI + Management (.NET/Blazor + Python API)

### Zielarchitektur
- [x] Blazor-Frontend (`TradingUI`) für User-Interaktion (MVP in `management/TradingManager`)
- [x] .NET-Manager-Service (`TradingManager`) als Orchestrator (MVP)
  - startet/stoppt Python-TradingAgents-API
  - überwacht Health/Restart
  - stellt Management-Endpoints für die UI bereit
- [x] Python TradingAgents API (`TradingAgentsApi`, FastAPI) für Analyse-Jobs
  - Job-Start
  - Status/Ergebnisse
  - Event-Streaming (optional)

### Verantwortlichkeiten
- [x] UI spricht nur mit `TradingManager` und/oder API-Endpunkten, nicht direkt mit interner Prozesssteuerung
- [x] `TradingManager` kapselt Prozessstart (Python), Env-Handling, Working Directory, Port-Management
- [x] TradingAgents-Logik bleibt in Python (keine Portierung der Agentenlogik nach C#)

### Management-Endpoints (MVP)
- [x] `POST /api/manager/start-api`
- [x] `POST /api/manager/stop-api`
- [ ] `POST /api/manager/restart`
- [x] `GET /api/manager/status` (running, pid, uptime/startedAt, port)
- [x] `GET /api/manager/logs?tail=200`
- [x] `GET /api/manager/lmstudio/models`
- [x] `POST /api/manager/lmstudio/load`
- [x] `POST /api/manager/tradingagents/select-model`
- [x] `POST /api/manager/tradingagents/analyze`
- [x] `GET /api/manager/tradingagents/jobs`
- [x] `GET /api/manager/tradingagents/jobs/{jobId}/status`
- [x] `GET /api/manager/tradingagents/jobs/{jobId}/result`
- [x] `GET /api/manager/tradingagents/jobs/{jobId}/events`
- [x] `POST /api/manager/tradingagents/jobs/{jobId}/cancel`
- [x] `POST /api/manager/tradingagents/jobs/{jobId}/summarize`

### Analyse-API (MVP)
- [x] `POST /v1/analyses` (Job anlegen)
- [x] `GET /v1/analyses` (Job-Liste für Resume)
- [x] `GET /v1/analyses/{job_id}` (Status/Fortschritt)
- [x] `GET /v1/analyses/{job_id}/result` (Ergebnis nach Abschluss)
- [x] `POST /v1/analyses/{job_id}/cancel` (JSON-Endpoint, cooperative cancel)
- [x] `GET /v1/analyses/{job_id}/events` (JSON-Events, kein SSE)

### Umsetzungsphasen
- [x] Phase 1: FastAPI + Job-Modell + In-Memory Queue + Start/Status/Result
- [x] Phase 1: .NET Manager mit Prozessstart/-stop, Health-Check (`/health`), Log-Capture
- [x] Phase 1: Blazor Basis-Flow (Start API, Modellwahl, Modell laden/setzen, Analyse starten)
- [x] Phase 2: JSON-Streaming/Polling, Cancel, robustes Error-Mapping
- [ ] Phase 2: Persistenz/Skalierung (Redis/Postgres, Worker-Prozess, Retries)

### Technische Leitplanken
- [x] Asynchrone Analysejobs statt blockierender HTTP-Calls
- [x] Timeouts + Retry-Strategie für Health-Checks und API-Aufrufe
- [x] Klare DTO-Verträge zwischen Blazor, Manager und Python API
- [ ] Auth/Autorisierung und grundlegendes Audit-Logging vor produktivem Einsatz

### Bereits umgesetzt (Detailstand)
- [x] LM Studio Modelle live abrufen, geladenes Modell vorauswählen (v0-Model-State), Modell laden/setzen
- [x] TradingAgents API standardmäßig über Conda-Environment starten (`conda run -n tradingagents ...`)
- [x] API-Status erkennt auch extern laufende Instanzen via `/health` (nicht nur manager-owned Prozess)
- [x] API-Stop beendet auch externe Listener auf Port `8081` (Hard-Stop-Fallback)
- [x] Resume von laufenden Jobs nach UI-Neustart (auto + manueller Button)
- [x] Live Output Stream inkl. Heartbeat-Events und Chunk-Events
- [x] Progress Window im 3-Spalten-Layout (Teams/Agents/Status)
- [x] Live Key-Metrics Extraktion (kompakte Liste, Tooltip-Timestamp)
- [x] Trading Signals Panel (BUY/HOLD/SELL) inkl. Quelle/Kontext
- [x] Ergebnis-Zusammenfassung automatisch nach erfolgreichem Run (ohne Button)

### Neue TODOs (Planungsmodus)
- [x] LM Studio Model-Load Parameter in UI/API ergänzen:
  - `context length` per Dropdown: `32k`, `64k`, `128k`, `256k`
  - optional `max concurrent` setzen (falls LM Studio Endpoint dies unterstützt)
  - Werte beim `load model` Request an LM Studio mitgeben
- [x] System Prompt für End-Zusammenfassung erweitern:
  - Trading-Begriffe und Kürzel kurz erklären (Glossar-Stil)
  - kompakt und lesbar im Summary integrieren

### Neue TODOs: UI-Aufräumen & Flow
- [x] JSON-Ausgaben aus der Standardansicht entfernen:
  - Job-Status/Result/Raw JSON in eigene Logs-Ansicht verschieben
  - Anzeige z. B. in Modal/Drawer nur nach Button-Klick
- [x] Start-Flow streamlinen:
  - erkanntes geladenes LM-Studio-Modell automatisch in TradingManager setzen (falls abweichend)
- [x] Test-/Control-Buttons auf Icon-Buttons umstellen:
  - Start: grünes Play-Icon
  - Stop: rotes Stop-Icon
  - Refresh: Refresh-Icon für Aktualisierungen
- [x] `Aktiven run wiederaufnehmen` Button entfernen (Auto-Resume ist aktiv)
- [x] `TradingAgents starten` soll Stream automatisch starten
- [x] `Job-Status laden` und `Job-Ergebnis laden` Buttons entfernen und Status/Ergebnis automatisiert aktualisieren (falls noch nicht vollständig automatisiert)

### Neues großes TODO: Persistenz pro Run (SQLite, Zero Config)
- [x] Persistenzschicht mit SQLite einführen (standardmäßig lokal, ohne externe Abhängigkeiten)
- [x] Jeden Run als eigene Instanz speichern (Run-Historie):
  - Job-Metadaten (`job_id`, ticker, date, provider, model, research depth, timestamps)
  - finaler Job-Status (`queued/running/succeeded/failed/canceled`)
  - zusammengefasster Output (Summary)
  - Key Metrics (normalisiert als Key/Value + Timestamp)
  - Trading Signals (Signal + Quelle + Kontext + Timestamp)
  - [ ] optional Roh-Events/Logs für spätere Diagnose
- [x] Datenmodell/Schema definieren:
  - Tabelle `runs`
  - Tabelle `run_metrics`
  - Tabelle `run_signals`
  - [ ] optional Tabelle `run_events`
- [x] Speichervorgang in den bestehenden Job-Lifecycle integrieren:
  - bei Job-Start anlegen
  - während Laufzeit Status/Events aktualisieren
  - nach Abschluss Summary/Metrics/Signals final persistieren
- [x] UI-Historie ergänzen:
  - Liste aller Runs (filterbar/sortierbar, z. B. nach Datum, Ticker, Status)
  - Detailansicht pro Run mit Summary, Metrics, Signals, Status
- [x] Restore-/Wiederherstellungsfunktion aus Historie:
  - ausgewählten Run laden und in UI rekonstruieren
  - Stream-/Live-Ansicht für vergangene Runs als read-only Timeline darstellen
- [ ] Technische Leitplanken:
  - [ ] migrationsfähiges Schema (Versionierung)
  - [x] robuste Fehlerbehandlung bei DB-Write/Read
  - [x] klare Trennung zwischen Live-State und persisted State

### Neue TODOs: UI-Umstrukturierung (Konsolidierung & Platzoptimierung)
- [x] UI-Funktionen konsolidieren und klar in Gruppen aufteilen:
  - Group 1: `Init & Settings`
    - API Status
    - API Start/Stop/Refresh
    - Model laden/setzen
    - Start-Parameter (Ticker, Datum, Sprache, Research Depth)
  - Group 2: `Active Run`
    - laufender Run (`job_id`, Status, Progress)
    - Progress Window (Teams/Agents)
    - Live Output
    - Trading Signals
    - Key Metrics
    - Summary
- [x] Settings-Bereich kompakter gestalten:
  - Controls dichter anordnen (mehrspaltig)
  - Labels/Inputs in konsistentem Raster
  - unnötige Wiederholungen reduzieren
- [x] Platznutzung verbessern:
  - horizontalen Platz effektiver nutzen (responsive Grid mit sinnvollen Breakpoints)
  - vertikalen Platzverbrauch reduzieren (kompakte Cards/Rows)
  - große Debug/JSON-Bereiche aus der Standardansicht in Logs/Details verschieben
- [x] Interaktionsfluss vereinfachen:
  - primäre Aktionen klar hervorheben
  - sekundäre/Debug-Aktionen in aufklappbare Bereiche oder Modals verlagern

### Neue TODOs: Queueing & Signal-Entscheidung (Planungsmodus)
- [x] Run-Start bei aktivem Run weiter zulassen, aber klar als Queueing darstellen:
  - Start-Aktion bei `running` nicht blockieren
  - UI-Text/State beim neuen Start: `queued` / `in queue`
  - Queue-Position bzw. Wartestatus sichtbar machen (falls verfügbar)
  - Status-Labels im Active-Run-Bereich für `queued` klar unterscheiden von `running`
- [x] Trading-Signals kompakt aggregieren:
  - Gruppierung als Counts: `Buy xN`, `Hold xN`, `Sell xN`
  - Einzelne Signale untergeordnet/kompakt anzeigen
- [x] Gesamt-Empfehlung aus Signalen berechnen und prominent anzeigen:
  - z. B. gewichtete Logik `Buy=+1`, `Hold=0`, `Sell=-1`
  - Ergebnis als `BUY/HOLD/SELL (x%)` darstellen
  - Prominente Placement im Active-Run-Bereich (oberhalb der Detail-Signale)
- [x] Signal-Details auslagern:
  - Quelltexte/Kontext nicht direkt in der Hauptansicht
  - Details per Klick in Modal anzeigen (Quelle, Kontext, Timestamp)

### Neue TODOs: Direkte Market-Data Integration (ohne LLM, Planungsmodus)
- [ ] Manager-seitigen Market-Data Pfad einführen (Internetzugriff nur im Manager):
  - Ziel: strukturierte Ticker-Daten direkt fuer UI und Persistenz
  - Kein LLM notwendig fuer Basis-Metriken
- [ ] API-Endpunkte (MVP) definieren:
  - `GET /api/manager/market-data/{symbol}/snapshot`
  - optional `GET /api/manager/market-data/{symbol}/history?range=1d|5d|1m`
  - optional `GET /api/manager/market-data/{symbol}/fundamentals`
- [ ] Einheitliches Snapshot-DTO festlegen:
  - `symbol`, `asOf`, `source`
  - `price`, `change`, `changePercent`
  - `marketCap`
  - `pe`, `forwardPe`, `pb`, `ps`, `peg`
  - `eps`, `revenue`, `revenueGrowth`
  - `beta`, `week52High`, `week52Low`, `volume`, `avgVolume`
  - `latencyMs`, `isStale`
- [ ] Provider-Adapter im Manager kapseln:
  - austauschbare Datenquelle (z. B. Finnhub/AlphaVantage/Polygon/IEX/Yahoo-kompatibel)
  - klares Mapping auf internes Snapshot-DTO
- [ ] Robustheit fuer Live-Betrieb:
  - In-Memory Cache mit kurzer TTL (z. B. 10-30s je Symbol)
  - Rate-Limit/Backoff Handling je Provider
  - Fallback-Felder (`null` statt Fehler), wenn einzelne Werte fehlen
  - `isStale=true`, wenn nur gecachte/alte Daten verfuegbar sind
- [ ] UI-Integration:
  - Snapshot-Bereich in Active-Run anzeigen (kompakt, ohne JSON)
  - manuelles Refresh + optional Auto-Refresh Intervall
  - Quelle + `asOf` sichtbar machen
- [ ] Zusammenspiel mit TA/Metrics:
  - direkte Market-Data als Prioritaetsquelle fuer Price/Valuation in der UI
  - geparste TA-Metriken weiterhin anzeigen, aber klar als sekundäre/abgeleitete Quelle markieren
- [ ] Persistenz-Vorbereitung (SQLite):
  - Snapshot pro Run speichern (Zeitpunkt + Quelle + Kernfelder)
  - Historienvergleich zwischen Runs ermoeglichen

### Neues großes TODO: eToro Integration (Readonly, Planungsmodus)
- [ ] Readonly eToro-Connector im Manager einführen:
  - Auth/Token-Konfiguration für eToro anbinden (ohne Trade-Berechtigungen)
  - Endpoint für offene Positionen bereitstellen
  - optional Portfolio-Snapshot (Cash/Equity/Exposure) bereitstellen
- [ ] DTOs für eToro-Daten definieren:
  - `EtoroPosition`: `symbol`, `units`, `avgPrice`, `marketPrice`, `unrealizedPnL`, `openedAt`, `leverage` (falls verfügbar)
  - `EtoroPortfolioSnapshot`: `asOf`, `equity`, `cash`, `positions[]`
- [ ] Manager-Endpoints (MVP) ergänzen:
  - `GET /api/manager/etoro/positions`
  - optional `GET /api/manager/etoro/portfolio`
- [ ] UI: eToro Readonly Ansicht ergänzen:
  - Card `My eToro Positions` mit kompakter Liste
  - Symbol-Filter/Suche
  - visuelle Markierung, wenn aktueller Analyse-Ticker in offenen Positionen enthalten ist
- [ ] Position-aware Ergebnislogik mit TA verbinden:
  - Wenn analysierter Ticker gehalten wird: Positionsdaten in Summary-/Decision-Flow einspeisen
  - Zusatzblock im Endergebnis: `Position-aware recommendation` (z. B. Add / Hold / Reduce / Exit)
  - Begründung inkl. Risiko-/Trigger-Hinweis auf Basis TA + Position
  - Wenn keine Position gehalten wird: Standard-TA-Ergebnis unverändert
- [ ] Persistenz für eToro-Kontext ergänzen:
  - je Run speichern, ob Position vorhanden war
  - Positionssnapshot zum Analysezeitpunkt speichern
  - position-aware Empfehlung im Run-Datensatz persistieren
- [ ] Sicherheits- und Betriebsleitplanken:
  - strikt readonly (keine Order-/Trade-Endpunkte)
  - klare UI-Kennzeichnung: keine automatische Ausführung
  - sensible Daten/Secrets nicht in Logs ausgeben

### Akzeptanzkriterien: eToro Readonly Integration
- [ ] Offene eToro-Positionen werden in der UI zuverlässig angezeigt.
- [ ] Bei Analyse eines gehaltenen Symbols enthält das Endergebnis einen positionsbezogenen Handlungsvorschlag.
- [ ] Ohne gehaltene Position bleibt das Ergebnisverhalten kompatibel mit dem bisherigen TA-Flow.
- [ ] Keine schreibenden Broker-Aktionen sind möglich (readonly-only).

### Hinweis: Erweiterbarkeit nach Readonly-Phase
- [ ] Architektur/Interfaces der eToro-Integration so gestalten, dass später ein dediziertes eToro-Agent-Konto angebunden werden kann.
- [ ] Geplante nächste Phase (nach erfolgreicher Bewährung): kontrollierter Live-Betrieb mit kleinem Kapitalbetrag.
- [ ] Bereits jetzt vorbereiten:
  - klare Trennung `readonly` vs `trading-enabled` im Code/Config
  - Risiko-Limits als verpflichtende Guardrails (Max-Positionsgröße, Max-Tagesverlust, Not-Aus/Kill-Switch)
  - Audit-Trail für Entscheidungen, Signale und (später) ausgeführte Orders
  - stufenweiser Rollout (Paper/Sandbox -> kleiner Echtgeldbetrag -> schrittweise Skalierung)

### Neues großes TODO: Dual-Model Support für TradingAgents (Small + Large)
- [x] Zielbild definieren: zwei Modellrollen parallel unterstützen
  - `small model` für schnelle/operative Aufgaben (z. B. Zwischenanalysen, Extraktion, Komprimierung)
  - `large model` für hochwertige Schlussfolgerungen (z. B. finale Entscheidung/Summary)
- [x] Konfigurationsmodell erweitern:
  - getrennte Model-IDs speichern: `small_model_id`, `large_model_id`
  - [ ] optionale getrennte Load-Parameter je Modell (`context_length`, `num_experts`/`max_concurrent`)
  - [x] Fallback-Regel: wenn nur ein Modell gesetzt ist, beide Rollen auf dasselbe Modell mappen
- [x] Manager-API erweitern:
  - Endpoints/DTOs für duale Modellzuweisung (Small/Large)
  - bestehende Analyse-Start-Requests um Modellrollen ergänzen
  - klare Validierung/Fehlermeldungen bei unvollständiger Konfiguration
- [x] UI-Erweiterung:
  - getrennte Auswahlfelder für Small/Large Model
  - kombinierter Apply-Flow
  - klare Sichtbarkeit, welches Modell aktuell für welche Rolle aktiv ist
- [ ] TradingAgents-Integration:
  - Mapping, welche Agenten/Phasen Small vs. Large nutzen
  - anfängliche Heuristik (MVP):
    - Small: Voranalyse, strukturierte Extraktion, Verdichtung
    - Large: Debatte/Finalentscheidung/Final Summary
  - optional später feinere Zuordnung pro Team/Stage
- [x] Persistenz erweitern (Run-Historie):
  - pro Run beide Modelle + Parameter speichern
  - Auswertung/vergleichbare Historie nach Modellpaaren ermöglichen
- [ ] Ergebnis-Transparenz:
  - im Run-Detail anzeigen, welche Passagen/Phasen mit Small vs. Large erzeugt wurden
  - bei Summary kennzeichnen, welches Modell die Endzusammenfassung erzeugt hat

### Akzeptanzkriterien: Dual-Model Support
- [x] Ein Run kann mit zwei verschiedenen Modellen stabil ausgeführt werden.
- [x] Modellrollen sind in UI und Persistenz nachvollziehbar.
- [x] Bei fehlender Dual-Konfiguration greift ein klarer, dokumentierter Fallback.
- [ ] Vergleich mehrerer Runs nach Modellpaaren ist möglich (Performance/Qualität/Kosten).

### Neues TODO: TA-Live-Telemetrieanzeige (CLI-ähnliche Statuszeile)
- [x] Zielanzeige in der UI ergänzen (an TA-CLI angelehnt), z. B.:
  - `Agents: 0/12 | LLM: 1 | Tools: 1 | Tokens: 1.7k↑ 208↓ | Reports: 0/7 | ⏱ 00:08`
- [x] Datenquellen definieren und mappen:
  - `Agents`: laufende/abgeschlossene Agent-Schritte aus Event-Stream
  - `LLM`: aktive LLM-Calls (konkurrent) oder letzter aktiver LLM-Worker
  - `Tools`: aktive Tool-Calls (konkurrent) oder letzter aktiver Tool-Worker
  - `Tokens`: geschätzte/gelieferte Prompt/Completion Tokens (up/down)
  - `Reports`: abgeschlossene vs. erwartete Report-Abschnitte
  - `⏱`: Laufzeit seit Job-Start
- [x] API-/Event-Erweiterung (falls nötig):
  - [x] fehlende Zähler in TradingAgents-Events ergänzen oder im Manager aggregieren
  - klar dokumentierte Fallbacks, wenn einzelne Metriken nicht verfügbar sind
- [x] UI-Integration:
  - kompakte Statuszeile oberhalb des Progress Windows
  - Live-Update im gleichen Takt wie Stream/Polling
  - responsive Darstellung (auf Mobile als 2 Zeilen umbrechen)
- [x] Persistenz:
  - Telemetrie-Snapshots pro Run speichern (zeitlich, für spätere Analyse/Replay)
  - finale aggregierte Telemetrie-Werte pro Run speichern (Summary-Ebene)
  - [x] im Run-Detail die finalen Gesamtzahlen anzeigen

### Akzeptanzkriterien: TA-Live-Telemetrieanzeige
- [x] Statuszeile aktualisiert sich live während eines Runs ohne manuellen Refresh.
- [x] Werte bleiben bei fehlenden Rohdaten stabil (kein Flackern, sinnvolle Defaults).
- [x] Anzeige ist mit TA-Event-Verlauf konsistent und nachvollziehbar.

### Neues TODO: Markdown Viewer für relevante Ergebnisfelder
- [x] Markdown-Rendering in der UI für relevante Textfelder einführen:
  - [x] primär `Summary`
  - [ ] optional `investment_plan`, `final_trade_decision`, weitere Report-Abschnitte
- [x] Rendering-Strategie definieren:
  - [x] Raw/Rendered Toggle pro Feld (Debugbarkeit behalten)
  - [x] konsistente Typografie für Überschriften, Listen, Tabellen, Code-Blöcke
- [x] Sicherheitsleitplanken:
  - [x] HTML-Sanitizing / XSS-Schutz aktivieren
  - [x] unsichere Inline-Skripte/HTML standardmäßig blockieren
- [ ] UX-Anforderungen:
  - gute Lesbarkeit bei langen Texten (max-height + Scroll)
  - saubere mobile Darstellung
  - Copy-to-Clipboard für Markdown-Rohtext optional
- [ ] Persistenz/Kompatibilität:
  - gespeicherte Inhalte weiterhin als Plain-Text/Markdown persistieren (kein HTML speichern)
  - bestehende API/DTO-Verträge unverändert lassen

### Akzeptanzkriterien: Markdown Viewer
- [ ] Summary wird standardmäßig formatiert als Markdown dargestellt.
- [ ] Nutzer kann bei Bedarf auf Rohtext wechseln.
- [ ] Rendering ist sicher (kein ausführbares HTML/Script).
- [ ] Bestehende Daten und Endpunkte bleiben abwärtskompatibel.

### Neues TODO: Trading-Signal-Detailmodal erweitern
- [ ] Im Trading-Signal-Modal den gesamten relevanten Kontext anzeigen (statt stark gekürztem Ausschnitt).
- [ ] Kontextfeld im Modal als scrollbaren Bereich auslegen (für lange Inhalte).
- [ ] Markdown-Rendering auch im Signal-Detailmodal aktivieren (inkl. Tabellen/Listen).
- [ ] Optionaler Raw/Rendered Toggle im Modal für Diagnose/Debug.

### Akzeptanzkriterien: Trading-Signal-Detailmodal
- [ ] Signal-Details zeigen den vollständigen relevanten Kontext.
- [ ] Lange Inhalte bleiben über Scroll gut nutzbar und sprengen das Layout nicht.
- [ ] Kontext ist sicher als Markdown gerendert (kein unsicheres HTML/Script).
