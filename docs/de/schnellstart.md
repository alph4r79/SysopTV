# Schnellstart

Am Ende dieser Seite zeigt dein Würfel deine Claude-Limits oder die Bestellungen deines Shops an.

Du brauchst:

- einen GeekMagic SmallTV Ultra mit ESP8266 (nicht die Pro-Variante und keine Variante mit ESP32)
- einen PC mit Windows 10 oder 11 im selben Netz
- ein WLAN mit 2,4 GHz
- für die Claude-Anzeige: Claude Code, installiert und angemeldet
- die Dateien aus den [GitHub Releases](https://github.com/alph4r79/SysopTV/releases)

> SysopTV ist ein inoffizielles Projekt ohne Verbindung zu Anthropic, Shopify, WooCommerce oder GeekMagic. Die Claude-Limits kommen über eine nicht dokumentierte Schnittstelle, die jederzeit wegfallen kann. Das Aufspielen geschieht auf eigenes Risiko: Der Würfel hat keinen USB-Datenanschluss. Geht etwas schief und die Update-Seite ist nicht mehr erreichbar, hilft nur ein USB-Seriell-Adapter mit Lötarbeit.

## 1. Firmware aufspielen

1. `sysoptv-<version>.bin` herunterladen.
2. Die Weboberfläche der Original-Firmware im Browser öffnen, mit der IP-Adresse des Würfels (steht in der Geräteliste deines Routers).
3. Ganz unten unter „Firmware update“ die `.bin`-Datei auswählen und hochladen.
4. Der Würfel startet neu und öffnet den offenen Hotspot `SysopTV-Setup-xxxx`.
5. Mit Handy oder Laptop mit diesem Hotspot verbinden und `http://192.168.4.1` öffnen.
6. Dein WLAN wählen, das Passwort eintragen und „Speichern und verbinden“ drücken. Der Würfel zeigt danach „Bereit“ und seine IP-Adresse.
7. Die Adresse des Würfels im Browser öffnen und als Erstes unter „Admin-Passwort“ ein Passwort festlegen (8 bis 64 Zeichen).
8. Dem Würfel im Router eine feste IP-Adresse geben.

Ausführlich auf Englisch: [Installation](../installation.md).

## 2. Programm installieren

1. `sysoptv-app-<version>-setup.exe` herunterladen und starten. Es braucht keine Administratorrechte.
2. Warnt Windows vor einem unbekannten Herausgeber: „Weitere Informationen“ und dann „Trotzdem ausführen“ klicken. Das Programm ist nicht signiert.
3. „Mit Windows starten“ angehakt lassen.
4. Das Programm sitzt unten rechts in der Taskleiste. Ein Doppelklick auf das Symbol öffnet das Fenster.

Ausführlich auf Englisch: [Windows app](../windows-app.md).

## 3. Würfel einrichten

1. Im Fenster die Seite „Würfel“ öffnen und „Suchen“ drücken. Den gefundenen Würfel anklicken, dann steht er in der Liste. Alternativ „Von Hand hinzufügen“ und die IP-Adresse eintragen.
2. Das Admin-Passwort des Würfels eintragen. Dann lädt das Programm die „Einstellungen im Würfel“: Helligkeit, Nachtmodus und Gerätename. Geändertes mit „Auf dem Würfel speichern“ übernehmen.
3. Unter der Liste steht die Firmware des Würfels. Bringt das Programm eine neuere mit, erscheint „Firmware aktualisieren“. Damit spielt das Programm die mitgelieferte Firmware über die Update-Seite des Würfels ein, nach einer Rückfrage. Nur bei Würfeln mit SysopTV, nie bei der Original-Firmware. Bricht ein Update ab, bleibt die alte Firmware erhalten; was dann zu tun ist, steht auf Englisch unter [When an update aborts](../installation.md#when-an-update-aborts).

## 4. Funktion und Design wählen

- **Claude Limits**: zeigt deine 5-Stunden-Session und dein Wochenlimit. Design wählen (zehn zur Auswahl), dazu „verbraucht“ oder „übrig“ und auf Wunsch einen automatischen Wechsel.
- **Shopify** oder **WooCommerce**: zeigt Bestellungen und Umsatz. Unter „Ansicht“ entweder „Großanzeige“ mit Zeitraum, Inhalt und einem von elf Stilen, oder „Details“ mit einer von vier Detailansichten. Die Stile Klassik, Säulen, Phosphor und ANSI zeigen immer mehrere Zeiträume zusammen, dann sind Zeitraum und Inhalt ausgegraut.

Mit „Im Wechsel zeigen“ zeigt ein Würfel zwei Funktionen abwechselnd, zum Beispiel Shop und Claude Limits, alle 10 bis 120 Sekunden (Standard 20). Unter „Bearbeiten“ wählst du, welche der beiden Funktionen die Felder darunter einstellen.

Alle Designs mit Bildern: [Designs](../designs.md).

## 5. Ohne PC

Das Programm zeichnet alles, was der Würfel zeigt. Kommt 3 Minuten lang kein Bild, zeigt der Würfel NO CARRIER mit großer Uhrzeit (aus dem Internet) und seiner IP-Adresse. Beendest du das Programm, meldest du dich bei Windows ab oder geht der PC in den Ruhezustand, schaltet das Programm die Würfel sofort auf NO CARRIER.

## 6. Weitere Einstellungen im Programm

- Seite „Claude“: „Benachrichtigen, wenn das Limit knapp wird“ zeigt eine Windows-Meldung, wenn Session oder Woche eine Schwelle erreichen (70, 80, 90 oder 95 %, Standard 90 %). Höchstens eine Meldung pro Schwelle und Zeitraum. Standardmäßig aus.
- Seite „Programm“: „Einmal täglich nach neuen Versionen suchen“ (standardmäßig aus) oder einmalig „Jetzt nach Updates suchen“ fragt bei GitHub nach neuem Programm und neuer Firmware. Beim installierten Programm erscheint dann „Jetzt aktualisieren“: Das Programm lädt das Setup, prüft es mit `SHA256SUMS.txt`, fragt nach, schaltet die Würfel auf NO CARRIER, installiert ohne Fenster und startet wieder. Eine neuere Firmware aus GitHub bietet die Seite „Würfel“ über „Firmware aktualisieren“ an, ebenfalls geprüft. Die portable Version zeigt nur den Hinweis und den Link.
- Vor jedem Speichern sichert das Programm die alte `config.json` als `config.bak.json`. Ist `config.json` beim Start kaputt, nimmt es die Sicherung.

Den Zugang zum Shop richtest du so ein (auf Englisch):

- [Shopify](../shopify-setup.md): eigene App im Dev Dashboard mit den Scopes `read_orders` und `read_all_orders`, Client-ID und Secret ins Programm.
- [WooCommerce](../woocommerce-setup.md): Plugin hochladen, Schlüssel erzeugen und ins Programm eintragen.

Hilfe bei Problemen: [Troubleshooting](../troubleshooting.md). Alle Anleitungen: [Übersicht](../README.md).
