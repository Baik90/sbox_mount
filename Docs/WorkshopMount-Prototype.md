# S&Box Workshop Mount – Prototyp

Getestet am 02.11.2026 mit s&box **26.10.27c**. Dies ist ein Editor-Prototyp
für alle lokal installierten Steam-Workshop-Abos, kein fertig verteilbares Engine-Plugin.

## Benutzen

1. Dieses Projekt im s&box-Editor öffnen und die Kompilierung abwarten.
2. **Editor → S&Box Mount → Open Browser** wählen. Das registriert und aktiviert
   den Mount und öffnet den eingebauten Mount-Browser in einem eigenen Fenster.
3. Unter **Installed → S&Box** Decay oder RustLegacy aufklappen.
4. **Editor → S&Box Mount → Run Isolation Test** führt den reproduzierbaren
   Ladetest aus und schreibt das Ergebnis in die Konsole sowie
   `.sbox/mount-tests/prototype-runtime.json`.

Der Cache-Suchpfad wird beim Editorstart und vor dem ersten Asset-Ladevorgang
automatisch eingerichtet. Ein erneutes „Enable Prototype“ ist dafür nicht nötig.
Den eigenen, indizierten Browser über das Menü öffnen; dessen Folder-/Flat-
Ansicht durchsucht die vorbereitete Ordnerstruktur statt bei jedem Unterordner
erneut die vollständige Ressourcenliste zu scannen.

Nach einem Update des Prototyps wird ein bereits geöffnetes altes Browserfenster
beim erneuten Menüaufruf auf die korrigierte Browserklasse umgestellt.

```text
S&Box
├── Decay
│   └── Assets
│       ├── Models
│       ├── Materials
│       └── Textures
└── RustLegacy
    └── Assets
        ├── Models
        ├── Materials
        └── Textures
```

Die ursprünglichen Unterordner bleiben innerhalb dieser Kategorien erhalten.
Szenen werden gezählt, aber noch nicht angeboten oder instanziiert. Die drei
gefundenen Szenen benötigen einen eigenen nächsten Implementierungsschritt,
insbesondere für benutzerdefinierte Komponenten und Ressourcenreferenzen.

## Was implementiert ist

- Echter `BaseGameMount` mit `ResourceLoader`-Einträgen und `mount://`-Pfaden.
- Automatische Ermittlung der tatsächlich abonnierten IDs über Steams lokale
  Abonnementverwaltung und ihrer Installationsverzeichnisse. Reine Downloadreste
  ohne Abo werden nicht eingebunden. Es werden keine Downloads oder Abos geändert.
- Aktuell 11.469 Modelle, 5.773 Materialien, 15.472 Texturen und 7.849 Sounds im
  Browser: insgesamt 40.563 registrierte Dateien aus neun Spielpaketen.
  Zwei weitere Abos enthalten lokal ausschließlich Metadaten und tragen keine
  ladbaren Assets bei. Nicht alle Ressourcen wurden testweise geladen.
- Lokales Lesen der vorhandenen Workshop-Installationen. Keine Downloads,
  Paketinstallation oder Ausführung der fremden Spiel-Assemblies.
- Vorbereitung nur der gerade benötigten Ressourcen und ihrer lokalen
  Abhängigkeiten in `.sbox/workshop-mount-cache/v3`.
- Separate native Pfade `sbox_workshop/v3/workshop_<Workshop-ID>/<Originalpfad>`.
  Die beiden bisherigen Testspiele behalten aus Kompatibilitätsgründen ihre
  vorhandenen Pfade `sbox_workshop/v3/<org.ident>/<Originalpfad>`.
- Anpassung von externen Referenzen (RERL, einschließlich Ressourcen-Hashes),
  komprimierten KV3-Stringtabellen sowie internen Materialnamen.
- Workshop-Originale werden nicht verändert. Der Cache enthält ausschließlich
  Asset-Dateien, keine `.bin`-Archive oder Spieleinstellungen.
- Vorschau-Tool für Modelle in einer temporären Render-Szene. Die aktuelle
  Editor-Szene wird dabei nicht geändert oder gespeichert.

Der Standardpfad für lokale Installationen wird relativ zur installierten
Engine ermittelt: `<SteamLibrary>/steamapps/workshop/content/590830`.
Steam liefert die tatsächlichen Installationsordner auch aus anderen Bibliotheken.
Optional kann vor dem Editorstart `SBOX_WORKSHOP_CONTENT_ROOT` diesen Content-
Ordner überschreiben.

Nach neuen Abos oder Downloads **Editor → S&Box Mount → Refresh Subscribed Content**
aufrufen. Jeder Inhalt erhält einen eigenen Titelordner; ungültige Pfadzeichen
werden entfernt und doppelte Titel durch ihre Workshop-ID unterschieden.

Die Erweiterung wurde mit allen elf Abonnement-IDs geprüft. Bei allen neun
Spielpaketen wurde exemplarisch jeweils ein Modell und eine Sound-Datei geladen,
ohne Audio abzuspielen. Alle 18 Stichproben waren erfolgreich; es wurden keine
fremden Paket-Assemblies geladen. Die eingebaute lokale Editor-/Tool-Assemblies
werden bei dieser Prüfung nicht als fremder Spielcode gezählt. Details:
`.sbox/mount-tests/subscription-smoke.json`.

## Testergebnis

Im frisch gestarteten Editor wurden über die tatsächlichen `mount://`-Pfade
die Bandagenmodelle beider Spiele, Decays Bär und RustLegacys Zombie geladen.
Alle vier Modelle waren gültig und keine Fehlermodelle.

| Prüfung | Ergebnis |
| --- | --- |
| Materialpfade bleiben innerhalb der jeweiligen Spiel-Ident | Bestanden |
| Bandagenmodelle sind separate Ressourcenobjekte | Bestanden |
| Bandagenmaterialien sind separate Ressourcenobjekte | Bestanden |
| Gleicher Texturpfad mit unterschiedlichen Quelldateien: separate Objekte | Bestanden |
| Erneutes Laden in umgekehrter Reihenfolge: Objektidentität bleibt stabil | Bestanden |
| Fremde Assemblies vor und nach dem Test | Jeweils 0 |
| Bär und Zombie als texturierte Vorschau gerendert und visuell geprüft | Bestanden |
| Editor-Kompilierung | Keine Fehler |

Die kollidierende Textur ist
`materials/default/default_skin_diffusewarp_tga_4d3cb601.generated.vtex`.
Beide Varianten wurden gleichzeitig als gültige 512×512-Texturen geladen.
Der Test weist getrennte Ressourcenobjekte und Pfade nach; er behauptet keinen
sichtbaren Unterschied der Pixel nur aufgrund unterschiedlicher Quelldateien.

`Material.FirstTexture` lieferte in den automatisierten Abfragen `null`.
Dieser Wert wird daher nicht als Nachweis vollständiger Texturauflösung genutzt.
Die beiden Modellvorschauen wurden zusätzlich gerendert und geprüft.

Lokale Nachweise (nicht als Spielinhalt veröffentlichen):

- `.sbox/mount-tests/prototype-runtime.json`
- `.sbox/mount-tests/conna.decay-preview.png`
- `.sbox/mount-tests/redfive.rustlegacy-preview.png`

Zusätzlich prüft ein eigenständiges .NET-Programm den Binär-Umschreiber:

```powershell
dotnet run --project Tools/WorkshopMount.Tests/WorkshopMount.Tests.csproj -- 'D:/Steam/steamapps/workshop/content/590830'
```

41 Assertions bestanden: ungültige Header werden abgewiesen, KV3-Pfade und
Größenfelder werden angepasst, RERL-Pfade/IDs werden ersetzt und die Original-
Payloads sowie Workshop-Dateien bleiben unverändert. Zusätzlich werden direkte
und rekursive Abfragen eines großen Ordnerbaums, doppelte Einträge und gespeicherte
Pfade in Kleinbuchstaben geprüft. Die Tests benötigen keine
laufende Engine und laden keinen fremden Spielcode. Ohne Pfadargument laufen
die synthetischen Prüfungen; mit Pfadargument zusätzlich die vier lokalen
Modell-/Material-Fixtures.

## Identische Kopien in der Gesamtansicht ausblenden

**Editor → S&Box Mount → Scan Duplicate Assets** prüft die abonnierten
Originaldateien im Hintergrund. Nach dem Scan werden weitere verifiziert
identische Kopien ausschließlich in der flachen Gesamtansicht bzw. der Suche
am Stammordner **S&Box** ausgeblendet. In den einzelnen Spielordnern, auch deren
flacher Ansicht oder Suche, bleiben sämtliche Assets sichtbar.

**Hide Duplicates in Combined Flat View** aktiviert den Filter,
**Show All Copies** zeigt wieder alle Kopien. Dies gilt für unseren
Prototyp-Browser; die allgemeine Engine-Ansicht „Everything“ wird nicht geändert.

Gleiche Namen oder Herkunft aus derselben Facepunch-Sammlung reichen nicht aus.
Verglichen werden Typ, Dateigröße, SHA-256 der vollständigen kompilierten
Originaldatei und der gesamte lokal aufgelöste RERL-Abhängigkeitsgraph. Ein
identisches Modell mit abweichender Material- oder Texturabhängigkeit bleibt
als eigene Variante sichtbar. Zyklen werden berücksichtigt. Nicht verifizierbare
Ressourcen werden nicht zusammengefasst. Abweichende Build-Metadaten können
deshalb auch optisch identische Varianten weiterhin getrennt halten.

Es werden weder Workshop-Dateien gelöscht noch Ressourcenpfade umgebogen oder
Loader entfernt. Gespeicherte Szenen und direkte `mount://`-Referenzen behalten
ihre bisherigen Quellen. Als sichtbare Kopie wird deterministisch nach
Workshop-ID und anschließend Pfad gewählt. Die Herkunft aller Kopien steht in:

- `.sbox/mount-tests/duplicates.md` – lesbare Gruppenliste mit Spielnamen und Pfaden
- `.sbox/mount-tests/duplicates.json` – vollständiger Prüfbericht
- `.sbox/mount-tests/duplicate-view-test.json` – Test der Anzeigegrenzen

Der gespeicherte Bericht wird nach erneutem Mounten wiederverwendet, wenn die
Asset-Liste, Dateigrößen und Änderungszeitpunkte unverändert sind und keine
zuvor externe Abhängigkeit neu im Paket aufgetaucht ist. Andernfalls bleibt die
Ansicht bis zu einem neuen Scan ungefiltert. Bei aktivierter roter Testmarkierung
wird der Anzeigefilter vorübergehend pausiert, da die vorbereitete Testtextur
bewusst vom Original abweicht.

Der erste Scan ergab 531 identische Gruppen mit 1.254 weiteren Kopien:
33 Modelle, 40 Materialien, 1.010 Texturen und 171 Sounds. Beispielsweise ist
`models/weapons/sbox_ammo/9mm_ammobox/ammobox_9mm.vmdl_c` in Sandbox und DXRP
einschließlich seiner Abhängigkeiten identisch.

Geprüft: kombinierte Dateiabfrage 40.563 → 39.309, unveränderte vollständige
Abfragen pro Spiel und weiterhin ladbarer ausgeblendeter DXRP-Modellpfad.
Die aktuelle 9mm-Suche des echten Prototyp-Browsers zeigte 14 → 12 Treffer;
andere Varianten blieben sichtbar. Bereits standardmäßig versteckte generierte
Texturen beeinflussen diese sichtbare Trefferzahl nicht.

Die ergänzten Tests prüfen insbesondere identische Modelle mit abweichenden
transitiven Texturen, identische Abhängigkeitszyklen, lokale gegenüber externen
Abhängigkeiten und die Invalidierung des gespeicherten Berichts.

## Sichtbarer Isolationstest: rote Decay-Bandage

**Editor → S&Box Mount → Enable Red Bandage Test** ersetzt ausschließlich
Decays vorbereitete Bandagen-Albedotextur durch eine rote Testtextur. Die
Modelle werden weiterhin über ihre regulären `mount://`-Pfade geladen; es wird
kein Material-Override am Renderer verwendet. RustLegacys Cache-Datei bleibt
unverändert. Die Workshop-Modelle, Materialien und Texturen beider Spiele
werden vor/nach dem Vorgang per SHA-256 verglichen.

Die Markierung bleibt im lokalen Cache aktiv, auch nach einem Neustart.
**Editor → S&Box Mount → Disable Red Bandage Test** stellt die ursprüngliche
Textur aus der Workshop-Installation wieder her. Auch dieser Rücksetzweg wurde
per Hashvergleich geprüft. Es wird keine Szene gespeichert.

Der MCP-Test `workshop_mount_visual_test` erzeugt unter
`.sbox/mount-tests/visual-marker/` die Einzelbilder, Bilder nach umgekehrter
Ladefolge, `result.json` sowie
`comparison-decay-left-rustlegacy-right.png` (**links Decay rot, rechts RustLegacy original**).
Er prüft zusätzlich die Farbtrennung der gerenderten Pixel. Eine geringe
Toleranz berücksichtigt Renderrauschen; byteidentische PNGs werden nicht
vorausgesetzt. Die beiden sichtbaren Varianten blieben nach umgekehrtem Laden
getrennt, und es wurden keine fremden Spiel-Assemblies geladen.

## Bewusste Grenzen

- Editor-Prototyp, noch kein global installiertes Mount-Plugin für Sandbox,
  veröffentlichte Spiele oder Multiplayer.
- Die Workshop-Abfrage verwendet die internen Methoden `Ugc.LocalItemIds(true)`
  und `Ugc.GetItem` per Reflection. Nach einem Engine-Update können Anpassungen
  erforderlich sein. Noch nicht installierte Abos werden im Status gemeldet.
- Szenen, Prefabs und benutzerdefinierte GameResources sind noch nicht
  Teil des Ladewegs. Das Vorhandensein eines `.scene_c` bedeutet ohne Spielcode
  keine allgemeine Szenenkompatibilität.
- Unterstützt Source-2-Dateiheader v12 und KV3 v2–v4 mit unkomprimierten oder
  LZ4-komprimierten Tabellen. Andere Encodings werden mit Fehler abgebrochen.
  Nicht alle möglichen Asset-Formate und Referenzarten sind getestet.
- Abhängigkeiten, die im jeweiligen Workshop-Paket nicht vorhanden sind,
  behalten ihren Pfad. Das funktioniert für die getesteten Engine-Abhängigkeiten;
  zusätzliche externe Pakete können eine weitere Auflösung benötigen.
- Die Ressourcen bleiben nach dem Test in diesem Editor-Prozess im Cache.
  Nach Workshop-Updates den Editor neu starten; Live-Invalidierung ist noch
  nicht implementiert. Der Cache wird beim nächsten Laden neu vorbereitet.
- Interne Engine-Funktionen werden an drei klar begrenzten Stellen per
  Reflection verwendet: Mount-Registrierung, zusätzlicher Asset-Suchpfad,
  nativer Ressourcen-Hash. Für bereits fehlgeschlagene native Modellpfade werden
  zusätzlich der verwaltete Ressourceneintrag entfernt und die native Ressource
  gezielt neu geladen. Ein Engine-Update kann Anpassungen verlangen.

## Korrekturen: Neustart und Folder-/Flat-Ansicht

Der erste Prototyp richtete den Asset-Suchpfad nur in `Enable()` ein. Ein durch
die Projekteinstellungen bereits aktiver Mount konnte deshalb nach einem
Neustart `CacheRoot == null` haben. Der Loader meldete „Enable the workshop mount
first“ und lieferte Fehler-Modelle. Der Suchpfad wird jetzt über ein Editor-
Frame-Ereignis sowie direkt vor `Prepare()` initialisiert. Bereits fehlgeschlagene
Workshop-Modellreferenzen in der offenen Szene können gezielt wiederhergestellt
werden; beim Start wird dies einmal automatisch versucht. Das speichert keine Szene.

Der indizierte Prototyp-Browser behebt die aufwendige Rekursion der eingebauten
`MountLocation`-Implementierung und behandelt Ordnerpfade unabhängig von ihrer
Groß-/Kleinschreibung. Diese Beschleunigung betrifft das Fenster aus **S&Box
Mount → Open Browser**; der allgemeine Engine-Browser wurde nicht verändert.

Geprüft in der vorhandenen Sitzung:

- Verwalteten Cache-Zustand auf `null` zurückgesetzt; ein direkter Ladeaufruf
  stellt den Suchpfad ohne manuelles Enable wieder her.
- Vorbereitungs-/Wiederherstellungsweg für einen nativen Modellpfad, der vor
  dem Vorhandensein seiner Cache-Datei geladen wurde: erst Fehlermodell, danach
  gültiges Modell.
- Fünf vorhandene Fehler-Modelle der offenen Szene wiederhergestellt, keine
  Szenendatei gespeichert.
- Tatsächlicher Browser-Schalter bei Decay: Flat zeigt 9.935 Asset-Einträge;
  Folder zeigt sieben unmittelbare Unterordner. Beide Aktualisierungen wurden
  vollständig abgeschlossen.
- Der Suchfilter erfasste alle Decay-Modelle in etwa 19 ms, RustLegacy vollständig
  mit 250 Modellen. Dieser Messwert betrifft die Abfrage, nicht das Erzeugen
  sämtlicher Thumbnails.

Ein vollständiger Editor-Neustart nach dieser Korrektur wurde wegen der offenen
ungespeicherten Szene nicht durchgeführt. Der ursprüngliche Fehlerzustand wurde
innerhalb des laufenden Editors gezielt nachgestellt.

## Technische Grundlagen

Die schmalen KV3-Header-/Stringtabellenregeln wurden mit der primären
[ValveResourceFormat-Implementierung](https://github.com/ValveResourceFormat/ValveResourceFormat/blob/master/ValveResourceFormat/Resource/ResourceTypes/BinaryKV3.cs)
abgeglichen. Der Prototyp enthält keine zusätzliche VRF-Abhängigkeit.

Für die installierte `Sandbox.Mounting.dll` wurde REA-Metadatenanalyse genutzt
(SHA-256 `dfa49b6770f68302c3bc57576e17e61f79fb83ed21cae33682b191421df19af5`).
Die größere Engine-DLL überschritt das Darstellungslimit dieses REA-Providers;
die fokussierte weitere Prüfung ihrer Methodensignaturen/CIL erfolgte mit dem
bereits mitgelieferten Mono.Cecil, und das Verhalten wurde im Editor geprüft.
Die Aussagen zu Registrierung und nativen Referenzen stützen sich damit auf
die konkrete installierte Version und die beschriebenen Laufzeittests.
