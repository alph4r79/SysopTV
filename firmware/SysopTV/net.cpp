// SysopTV, Copyright (C) 2026 Christoph Scheel
// Licensed under the GNU General Public License v3.0, see LICENSE.
//
// WLAN connection, setup hotspot, web pages, firmware update and /api/usage.
//
// Safety rule: the update page must work in every mode (normal, setup hotspot,
// rescue). The device has no USB data connection, so this page is the only
// way to load new firmware without opening the case.
//
// Protection: with an admin password set, /settings, /update, /save, /rescan
// and /flashbackup need HTTP Basic Auth. Every form also carries a random
// token (CSRF) that is checked on POST, so foreign web pages cannot send
// forms to the cube with the login the browser remembers.
//
// Pages are sent in pieces (chunked) with the CSS and fixed parts straight
// from flash, so no page is ever built as one big string in RAM.

#include "net.h"
#include "config.h"
#include "screen.h"
#include "usage.h"
#include "auth.h"
#include "settings.h"
#include "design.h"
#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266mDNS.h>
#include <DNSServer.h>
#include <WiFiUdp.h>
#include <ArduinoJson.h>
#include <time.h>

static ESP8266WebServer server(80);
static DNSServer dns;

static bool rescueMode = false;
static bool setupMode = false;
static bool hasSavedWlan = false;
static bool wasConnected = false;
static unsigned long apLastClient = 0;

static const int MAX_SCAN = 20;
static String scanList[MAX_SCAN];
static int scanCount = 0;

static String updateError;
static bool uploadStarted = false;
static int uploadDenied = 0;  // 0 allowed, 401 no login, 403 bad token
static unsigned long restartAt = 0;

// ---------------------------------------------------------------------------
// Page frame
// ---------------------------------------------------------------------------

// One stylesheet for every page. Mobile first, wider screens only via min-width.
static const char PAGE_HEAD[] PROGMEM = R"HTML(<!doctype html>
<html lang="de"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>SysopTV</title>
<style>
:root{--bg:#0e1113;--ln:#2a3035;--fg:#dfe3e6;--dim:#8b949a;--ac:#7fd4e6;--ok:#7ccf8a;--er:#ff6b5e}
*{box-sizing:border-box}
body{margin:0;padding:0 16px;background:var(--bg);color:var(--fg);font:16px/1.5 system-ui,"Segoe UI",Roboto,sans-serif}
.wrap{max-width:640px;margin:0 auto;padding:12px 0 32px}
header{padding:4px 0 10px;border-bottom:1px solid var(--ln)}
.brand{font-weight:600}.ver{color:var(--dim);font-size:13px;margin-left:8px}
nav{margin-top:4px}nav a,nav b{margin-right:18px;font-weight:400}nav b{color:var(--fg)}
a{color:var(--ac);text-decoration:none}a:hover{text-decoration:underline}
h1{font-size:20px;font-weight:600;color:var(--ac);margin:20px 0 8px}
h2{font-size:12px;font-weight:600;letter-spacing:.12em;text-transform:uppercase;color:var(--dim);margin:24px 0 8px;padding-bottom:6px;border-bottom:1px solid var(--ln)}
p{margin:8px 0}.dim{color:var(--dim)}.ok{color:var(--ok)}.er{color:var(--er)}
.lim{margin:14px 0}.top{display:flex;justify-content:space-between;align-items:baseline;gap:12px}
.pct{font-size:26px;font-variant-numeric:tabular-nums}.pct small{font-size:14px;color:var(--dim);margin-left:6px}
.bar{height:4px;background:var(--ln);margin:6px 0 4px}.bar i{display:block;height:4px;background:var(--ac)}.bar i.warn{background:var(--er)}
.num{font-variant-numeric:tabular-nums}
dl{display:grid;grid-template-columns:auto 1fr;gap:6px 16px;margin:0}dt{color:var(--dim)}dd{margin:0;font-variant-numeric:tabular-nums;overflow-wrap:anywhere}
label{display:block;margin:10px 0 4px;color:var(--dim)}label.opt{color:var(--fg);margin:8px 0}
input,select,button{font:inherit;color:var(--fg);background:var(--bg);border:1px solid var(--ln);border-radius:2px;padding:10px;width:100%}
input:focus,select:focus{outline:0;border-color:var(--ac)}
input[type=checkbox],input[type=radio]{width:auto;margin:0 8px 0 0;vertical-align:middle}
button{color:var(--ac);border-color:var(--ac);margin-top:16px;cursor:pointer}button.alt{color:var(--dim);border-color:var(--ln)}
.two{display:flex;gap:12px}.two>div{flex:1}
.save{position:sticky;bottom:0;background:var(--bg);border-top:1px solid var(--ln);padding:12px;margin-top:16px;display:flex;align-items:center;gap:12px}
.save span{flex:1}.save button{width:auto;margin:0;padding:10px 24px}
@media(min-width:560px){.pair{display:flex;gap:12px}.pair>div{flex:1}}
code{font-family:ui-monospace,Consolas,monospace;overflow-wrap:anywhere}
@media(min-width:640px){body{padding:0 24px}button{width:auto;padding:10px 24px;margin-right:8px}}
</style></head><body><div class="wrap">
)HTML";

static const char PAGE_FOOT[] PROGMEM = "</div></body></html>";

enum NavItem : uint8_t { NAV_NONE, NAV_HOME, NAV_SETTINGS, NAV_UPDATE };

// Collects small pieces and sends them as chunks of about 600 bytes.
class Page {
 public:
  void begin(NavItem active, int code = 200) {
    server.setContentLength(CONTENT_LENGTH_UNKNOWN);
    server.send(code, F("text/html; charset=utf-8"), "");
    server.sendContent_P(PAGE_HEAD);
    buf_.reserve(700);
    *this << F("<header><span class=\"brand\">SysopTV</span><span class=\"ver\">" SYSOPTV_VERSION "</span><nav>");
    navLink(active == NAV_HOME, F("/"), setupMode ? F("WLAN") : F("&Uuml;bersicht"));
    if (!rescueMode) {
      navLink(active == NAV_SETTINGS, F("/settings"), F("Einstellungen"));
    }
    navLink(active == NAV_UPDATE, F("/update"), F("Update"));
    *this << F("</nav></header>");
  }
  Page& operator<<(const __FlashStringHelper* s) {
    room(strlen_P(reinterpret_cast<PGM_P>(s)));
    buf_ += s;
    return *this;
  }
  Page& operator<<(const String& s) {
    room(s.length());
    buf_ += s;
    return *this;
  }
  Page& operator<<(const char* s) {
    room(strlen(s));
    buf_ += s;
    return *this;
  }
  void end() {
    flush();
    server.sendContent_P(PAGE_FOOT);
    server.sendContent(emptyString);  // last chunk
  }

 private:
  String buf_;
  void navLink(bool active, const __FlashStringHelper* href, const __FlashStringHelper* text) {
    if (active) {
      *this << F("<b>") << text << F("</b>");
    } else {
      *this << F("<a href=\"") << href << F("\">") << text << F("</a>");
    }
  }
  void room(size_t add) {
    if (buf_.length() + add > 600) {
      flush();
    }
  }
  void flush() {
    if (buf_.length()) {
      server.sendContent(buf_);
      buf_ = "";
    }
  }
};

static String htmlEscape(const String& in) {
  String out;
  out.reserve(in.length() + 8);
  for (unsigned int i = 0; i < in.length(); i++) {
    char c = in[i];
    if (c == '&') out += F("&amp;");
    else if (c == '<') out += F("&lt;");
    else if (c == '>') out += F("&gt;");
    else if (c == '"') out += F("&quot;");
    else out += c;
  }
  return out;
}

// Short page with a heading, one paragraph and a link back.
static void messagePage(NavItem nav, int code, const __FlashStringHelper* title,
                        const String& text, bool error, const __FlashStringHelper* backHref,
                        const __FlashStringHelper* backText) {
  Page p;
  p.begin(nav, code);
  p << F("<h1>") << title << F("</h1><p") << (error ? F(" class=\"er\">") : F(">")) << text
    << F("</p><p><a href=\"") << backHref << F("\">") << backText << F("</a></p>");
  p.end();
}

static void redirect(const __FlashStringHelper* location, int code) {
  server.sendHeader(F("Location"), location, true);
  server.send(code, F("text/plain"), "");
}

static String csrfField() {
  String f = F("<input type=\"hidden\" name=\"csrf\" value=\"");
  f += authCsrfToken();
  f += F("\">");
  return f;
}

// ---------------------------------------------------------------------------
// Access checks
// ---------------------------------------------------------------------------

static bool loggedIn() {
  return authCheckBasic(server.header("Authorization"));
}

// Without a password set, everything stays open.
static bool requireAuth() {
  if (loggedIn()) {
    return true;
  }
  // The browser shows its own login dialog. This page appears after "Cancel".
  String body = FPSTR(PAGE_HEAD);
  body += F("<header><span class=\"brand\">SysopTV</span><span class=\"ver\">" SYSOPTV_VERSION
            "</span></header><h1>Anmeldung erforderlich</h1><p>Diese Seite ist mit dem Admin-Passwort "
            "gesch&uuml;tzt. Der Benutzername ist egal.</p><p><a href=\"/\">Zur &Uuml;bersicht</a></p>");
  body += FPSTR(PAGE_FOOT);
  server.requestAuthentication(BASIC_AUTH, "SysopTV", body);
  return false;
}

static bool requireCsrf() {
  if (authCsrfValid(server.arg("csrf"))) {
    return true;
  }
  messagePage(NAV_NONE, 403, F("Abgelehnt"),
              F("Das Formular ist ung&uuml;ltig oder veraltet, zum Beispiel nach einem Neustart von SysopTV."),
              true, F("/"), F("Seite neu laden"));
  return false;
}

// New features stay off in rescue mode.
static bool blockedInRescue() {
  if (rescueMode) {
    redirect(F("/"), 302);
    return true;
  }
  return false;
}

// ---------------------------------------------------------------------------
// Values for the overview page and /api/info
// ---------------------------------------------------------------------------

static String ageText(uint32_t seconds) {
  if (seconds < 90) {
    return String(seconds) + F(" s");
  }
  if (seconds < 2 * 3600) {
    return String(seconds / 60) + F(" min");
  }
  return String(seconds / 3600) + F(" h");
}

// Data state in one line. Texts in UTF-8, they also go into JSON.
static String dataStatus(bool& ok) {
  UsageStatus st;
  usageGetStatus(st);
  ok = false;
  if (!st.havePacket || st.stale) {
    String t = F("Keine Daten vom PC");
    if (st.havePacket) {
      t += F(" seit ");
      t += ageText(st.ageSeconds);
    }
    t += F(". L\xc3\xa4" "uft der Sender auf dem PC?");
    return t;
  }
  if (st.state == USAGE_STATE_LOGIN) {
    return String(F("Anmeldung von Claude Code abgelaufen, Claude Code kurz benutzen (Meldung vor ")) +
           ageText(st.ageSeconds) + F(")");
  }
  if (st.state == USAGE_STATE_ERROR) {
    return String(F("Der Sender erreicht Anthropic nicht oder wird gebremst (Meldung vor ")) +
           ageText(st.ageSeconds) + F(")");
  }
  ok = true;
  return String(F("Daten vom PC vor ")) + ageText(st.ageSeconds);
}

static String percentText(const UsageColumn& c) {
  return c.present ? String(c.percent) + F(" %") : String(F("--"));
}

// Reset like on the display, with "Reset" in front.
static String resetText(const UsageColumn& c) {
  if (!c.present) {
    return String(c.bottom);
  }
  if (strncmp(c.bottom, "Reset", 5) == 0) {
    return String(c.bottom);
  }
  return String(F("Reset ")) + c.bottom;
}

static const __FlashStringHelper* modeWord(bool showUsed) {
  return showUsed ? F("verbraucht") : F("\xc3\xbc" "brig");
}

static String designText() {
  const Settings& s = settingsGet();
  String t = designGet(designActive()).name;
  if (s.cycleSeconds > 0) {
    t += F(", automatischer Wechsel alle ");
    t += String(s.cycleSeconds);
    t += F(" s");
  } else {
    t += F(", automatischer Wechsel aus");
  }
  return t;
}

// Limits for the signal rating: good from -60 dBm, medium down to -70 dBm.
static const __FlashStringHelper* signalWord(int32_t rssi) {
  if (rssi >= -60) return F("gut");
  if (rssi >= -70) return F("mittel");
  return F("schwach");
}

static String signalText() {
  if (setupMode || WiFi.status() != WL_CONNECTED) {
    return F("nicht verbunden");
  }
  int32_t rssi = WiFi.RSSI();
  return String(rssi) + F(" dBm, ") + signalWord(rssi);
}

static String timeText() {
  switch (usageTimeSource()) {
    case TIME_SENDER: return F("ja, vom PC");
    case TIME_NTP: return F("ja, per NTP");
    case TIME_SENDER_OLD: return F("ja, vom PC (seit 15 min nicht mehr abgeglichen)");
    default: return F("nein");
  }
}

static String uptimeText() {
  unsigned long s = millis() / 1000;
  unsigned long d = s / 86400;
  unsigned long h = (s % 86400) / 3600;
  unsigned long m = (s % 3600) / 60;
  String t;
  if (d > 0) {
    t += String(d);
    t += d == 1 ? F(" Tag ") : F(" Tage ");
  }
  t += String(h) + F(" h ") + String(m) + F(" min");
  return t;
}

static String heapText() {
  return String(ESP.getFreeHeap() / 1024) + F(" KB");
}

// ---------------------------------------------------------------------------
// Overview
// ---------------------------------------------------------------------------

static void passwordFields(Page& p) {
  p << F("<label for=\"pw1\">Passwort (mindestens 8 Zeichen)</label>"
         "<input id=\"pw1\" name=\"pw1\" type=\"password\" autocomplete=\"new-password\" minlength=\"8\" maxlength=\"64\">"
         "<label for=\"pw2\">Passwort wiederholen</label>"
         "<input id=\"pw2\" name=\"pw2\" type=\"password\" autocomplete=\"new-password\" minlength=\"8\" maxlength=\"64\">");
}

static void limitRow(Page& p, char id, const __FlashStringHelper* name, const UsageColumn& c, bool showUsed) {
  String sid(id);
  p << F("<div class=\"lim\"><div class=\"top\"><span>") << name << F("</span><span class=\"pct\"><span id=\"")
    << sid << F("p\">") << percentText(c) << F("</span><small id=\"") << sid << F("m\">") << modeWord(showUsed)
    << F("</small></span></div><div class=\"bar\"><i id=\"") << sid << F("b\"") << (c.warn ? F(" class=\"warn\"") : F(""))
    << F(" style=\"width:") << String(c.present ? c.percent : 0) << F("%\"></i></div><div class=\"dim num\" id=\"")
    << sid << F("r\">") << htmlEscape(resetText(c)) << F("</div></div>");
}

static void deviceRow(Page& p, const __FlashStringHelper* name, const char* id, const String& value) {
  p << F("<dt>") << name << F("</dt><dd id=\"") << id << F("\">") << htmlEscape(value) << F("</dd>");
}

// Updates the values every 10 seconds from /api/info. Without JavaScript the
// page keeps the values from when it was loaded.
static const char OVERVIEW_SCRIPT[] PROGMEM = R"JS(<script>
function t(i,v){var e=document.getElementById(i);if(e)e.textContent=v}
function up(){fetch('/api/info').then(function(r){return r.json()}).then(function(j){
var u=j.usage;if(u){['s','w'].forEach(function(k){var c=u[k=='s'?'session':'week'];
t(k+'p',c.text);t(k+'m',u.mode);t(k+'r',c.reset);var b=document.getElementById(k+'b');
if(b){b.style.width=c.bar+'%';b.className=c.warn?'warn':''}});
var d=document.getElementById('ds');if(d){d.textContent=u.status;d.className=u.statusOk?'ok':'er'}
t('dd',j.designText)}
t('i-ssid',j.ssid);t('i-ip',j.ip);t('i-sig',j.signalText);t('i-time',j.timeText);
t('i-up',j.uptimeText);t('i-heap',j.heapKb+' KB');t('i-rst',j.resetReason)}).catch(function(){})}
setInterval(up,10000);
</script>)JS";

static void handleStatus() {
  Page p;
  p.begin(NAV_HOME);
  if (rescueMode) {
    p << F("<p class=\"er\">Rettungsmodus: Die Firmware ist mehrmals hintereinander abgest&uuml;rzt. "
           "&Uuml;ber die Update-Seite l&auml;sst sich eine funktionierende Version einspielen.</p>");
  }
  if (!rescueMode && !authHasPassword()) {
    p << F("<h2>Admin-Passwort</h2><p class=\"er\">Noch kein Admin-Passwort gesetzt. Ohne Passwort kann jeder "
           "im WLAN Einstellungen &auml;ndern und Firmware einspielen.</p><p class=\"dim\">Beim Anmelden "
           "ist der Benutzername egal.</p><form method=\"post\" action=\"/password\">")
      << csrfField();
    passwordFields(p);
    p << F("<button type=\"submit\">Passwort festlegen</button></form>");
  }

  if (!rescueMode) {
    UsageView view;
    usageBuildView(view);
    bool ok;
    String status = dataStatus(ok);
    p << F("<h2>Anzeige</h2>");
    limitRow(p, 's', F("Session"), view.session, view.showUsed);
    limitRow(p, 'w', F("Woche"), view.week, view.showUsed);
    p << F("<p id=\"ds\" class=\"") << (ok ? F("ok") : F("er")) << F("\">") << htmlEscape(status)
      << F("</p><p class=\"dim\">Design: <span id=\"dd\">") << htmlEscape(designText()) << F("</span></p>");
  }

  p << F("<h2>Ger&auml;t</h2><dl>");
  deviceRow(p, F("WLAN"), "i-ssid", setupMode ? String(F(SYSOPTV_AP_NAME " (Hotspot)")) : WiFi.SSID());
  deviceRow(p, F("IP-Adresse"), "i-ip", setupMode ? WiFi.softAPIP().toString() : WiFi.localIP().toString());
  deviceRow(p, F("mDNS-Name"), "i-mdns", F(SYSOPTV_HOSTNAME ".local"));
  deviceRow(p, F("Signal"), "i-sig", signalText());
  deviceRow(p, F("Uhrzeit synchronisiert"), "i-time", timeText());
  deviceRow(p, F("Laufzeit"), "i-up", uptimeText());
  deviceRow(p, F("Freier Speicher"), "i-heap", heapText());
  deviceRow(p, F("Letzter Neustart"), "i-rst", ESP.getResetReason());
  p << F("</dl>");

  if (!rescueMode) {
    p << F("<h2>Wartung</h2>");
    if (authHasPassword()) {
      p << F("<p><a href=\"/flashbackup\">Flash-Sicherung herunterladen</a></p>");
    } else {
      p << F("<p class=\"dim\">Flash-Sicherung: verf&uuml;gbar, sobald ein Admin-Passwort gesetzt ist.</p>");
    }
    p << F("<p class=\"dim\">Die Sicherung enth&auml;lt den Speicherbereich der Original-Firmware von GeekMagic "
           "mit ihrer Weboberfl&auml;che. Sie ist wichtig vor einer R&uuml;ckkehr zur Original-Firmware.</p>");
    p << FPSTR(OVERVIEW_SCRIPT);
  }
  p.end();
}

static void handlePassword() {
  if (blockedInRescue()) {
    return;
  }
  // Setting the first password needs no login, changing it does.
  if (authHasPassword() && !requireAuth()) {
    return;
  }
  if (!requireCsrf()) {
    return;
  }
  String pw1 = server.arg("pw1");
  String pw2 = server.arg("pw2");
  const __FlashStringHelper* title = F("Nicht gespeichert");
  const __FlashStringHelper* text;
  bool error = true;
  if (pw1 != pw2) {
    text = F("Die beiden Passw&ouml;rter sind nicht gleich.");
  } else if (pw1.length() < SYSOPTV_PASSWORD_MIN || pw1.length() > SYSOPTV_PASSWORD_MAX) {
    text = F("Das Passwort muss 8 bis 64 Zeichen lang sein.");
  } else if (!authSetPassword(pw1)) {
    text = F("Speichern im Flash ist fehlgeschlagen.");
  } else {
    title = F("Passwort gespeichert");
    text = F("Der Browser fragt ab jetzt bei gesch&uuml;tzten Seiten nach dem Passwort, der Benutzername ist "
             "egal. Vergessen? Den W&uuml;rfel dreimal hintereinander innerhalb von 10 Sekunden nach dem "
             "Einstecken wieder abziehen, beim vierten Start werden Passwort und Einstellungen "
             "zur&uuml;ckgesetzt. Das WLAN bleibt.");
    error = false;
  }
  pw1 = "";
  pw2 = "";
  messagePage(NAV_NONE, 200, title, text, error, F("/settings"), F("Zu den Einstellungen"));
}

// ---------------------------------------------------------------------------
// Settings
// ---------------------------------------------------------------------------

static String minutesText(uint16_t minutes) {
  char buf[8];
  snprintf(buf, sizeof(buf), "%02u:%02u", (unsigned)(minutes / 60 % 24), (unsigned)(minutes % 60));
  return String(buf);
}

// "HH:MM" from an <input type="time">. Returns false if it is not a time.
static bool parseMinutes(const String& s, uint16_t& out) {
  if (s.length() < 4 || s.length() > 5) {
    return false;
  }
  int colon = s.indexOf(':');
  if (colon < 1) {
    return false;
  }
  int h = s.substring(0, colon).toInt();
  int m = s.substring(colon + 1).toInt();
  if (h < 0 || h > 23 || m < 0 || m > 59) {
    return false;
  }
  out = (uint16_t)(h * 60 + m);
  return true;
}

// Shows the night mode fields only while the checkbox is set.
static const char NIGHT_SCRIPT[] PROGMEM = R"JS(<script>
(function(){var c=document.getElementById('night');if(!c)return;var n=document.querySelectorAll('.night');
function u(){for(var i=0;i<n.length;i++)n[i].style.display=c.checked?'':'none'}
c.addEventListener('change',u);u()})();
</script>)JS";

static void option(Page& p, int value, int current, const String& text) {
  p << F("<option value=\"") << String(value) << (value == current ? F("\" selected>") : F("\">")) << text
    << F("</option>");
}

static void handleSettingsForm() {
  if (blockedInRescue() || !requireAuth()) {
    return;
  }
  const Settings& s = settingsGet();
  Page p;
  p.begin(NAV_SETTINGS);
  p << F("<h1>Einstellungen</h1>");

  // Display and brightness share one form with the save bar at the bottom.
  // API key and password have their own forms and buttons further down.
  p << F("<form method=\"post\" action=\"/settings\">") << csrfField();
  p << F("<h2>Anzeige</h2><div class=\"pair\"><div><label for=\"design\">Design</label>"
         "<select id=\"design\" name=\"design\">");
  for (int i = 0; i < DESIGN_COUNT; i++) {
    option(p, i, s.design, designGet(i).htmlName);
  }
  p << F("</select></div><div><label for=\"cycle\">Automatisch wechseln</label><select id=\"cycle\" name=\"cycle\">");
  option(p, 0, s.cycleSeconds, F("aus"));
  option(p, 30, s.cycleSeconds, F("alle 30 Sekunden"));
  option(p, 60, s.cycleSeconds, F("alle 60 Sekunden"));
  p << F("</select></div></div><label>Prozent zeigen</label><label class=\"opt\"><input type=\"radio\" name=\"used\" value=\"1\"")
    << (s.showUsed ? F(" checked") : F("")) << F(">verbraucht (wie auf claude.ai)</label>")
    << F("<label class=\"opt\"><input type=\"radio\" name=\"used\" value=\"0\"") << (s.showUsed ? F("") : F(" checked"))
    << F(">&uuml;brig</label><p class=\"dim\">Die Warnfarbe kommt immer, wenn weniger als 20&nbsp;% "
         "&uuml;brig sind.</p>");

  // Fields with class "night" are hidden by the script below while night
  // mode is off. Without JavaScript they stay visible.
  p << F("<h2>Helligkeit</h2><label class=\"opt\"><input type=\"checkbox\" id=\"night\" name=\"night\" value=\"1\"")
    << (s.nightEnabled ? F(" checked") : F("")) << F(">Nachtmodus</label><div class=\"pair\"><div>"
         "<label for=\"bright\">Helligkeit in Prozent (10 bis 100)</label>"
         "<input id=\"bright\" name=\"bright\" type=\"number\" min=\"10\" max=\"100\" step=\"5\" value=\"")
    << String(s.brightness) << F("\"></div><div class=\"night\"><label for=\"nbright\">Helligkeit nachts (0 = Display aus)</label>"
         "<input id=\"nbright\" name=\"nbright\" type=\"number\" min=\"0\" max=\"100\" step=\"5\" value=\"")
    << String(s.nightBrightness) << F("\"></div></div><div class=\"two night\"><div>"
         "<label for=\"nfrom\">Von</label><input id=\"nfrom\" name=\"nfrom\" type=\"time\" value=\"")
    << minutesText(s.nightFrom) << F("\"></div><div><label for=\"nto\">Bis</label>"
         "<input id=\"nto\" name=\"nto\" type=\"time\" value=\"") << minutesText(s.nightTo)
    << F("\"></div></div><p class=\"dim night\">Der Nachtmodus braucht die Uhrzeit, also Daten "
         "vom PC oder Internet f&uuml;r die Zeit per NTP.</p>");

  p << F("<div class=\"save\"><span");
  if (server.hasArg("saved")) {
    p << F(" class=\"ok\">Gespeichert.");
  } else if (server.hasArg("error")) {
    p << F(" class=\"er\">Speichern im Flash ist fehlgeschlagen.");
  } else {
    p << F(">");
  }
  p << F("</span><button type=\"submit\">Speichern</button></div></form>") << FPSTR(NIGHT_SCRIPT);

  p << F("<h2>API-Schl&uuml;ssel</h2>");
  if (server.hasArg("keysaved")) {
    p << F("<p class=\"ok\">Gespeichert.</p>");
  }
  if (server.hasArg("keyerror")) {
    p << F("<p class=\"er\">Speichern im Flash ist fehlgeschlagen.</p>");
  }
  if (authHasApiKey()) {
    p << F("<p>Der Sender muss diesen Schl&uuml;ssel mitschicken:</p><p><code>") << authApiKey()
      << F("</code></p><p class=\"dim\">Aufruf: <code>sysoptv-sender.ps1 -ApiKey ") << authApiKey()
      << F("</code></p>");
  } else {
    p << F("<p class=\"dim\">Kein Schl&uuml;ssel gesetzt, <code>/api/usage</code> nimmt Daten von jedem im "
           "WLAN an.</p>");
  }
  p << F("<form method=\"post\" action=\"/settings/key\">") << csrfField()
    << F("<button type=\"submit\" name=\"action\" value=\"new\">Neuen Schl&uuml;ssel erzeugen</button>");
  if (authHasApiKey()) {
    p << F("<button type=\"submit\" name=\"action\" value=\"clear\" class=\"alt\">Schl&uuml;ssel l&ouml;schen</button>");
  }
  p << F("</form>");

  p << F("<h2>Admin-Passwort</h2><form method=\"post\" action=\"/password\">") << csrfField();
  passwordFields(p);
  p << (authHasPassword() ? F("<button type=\"submit\">Passwort &auml;ndern</button>")
                          : F("<button type=\"submit\">Passwort festlegen</button>"))
    << F("</form>");
  p.end();
}

static void handleSettingsSave() {
  if (blockedInRescue() || !requireAuth() || !requireCsrf()) {
    return;
  }
  Settings s = settingsGet();
  s.design = (uint8_t)server.arg("design").toInt();
  s.cycleSeconds = (uint8_t)server.arg("cycle").toInt();
  s.showUsed = server.arg("used") == "1" ? 1 : 0;
  s.brightness = (uint8_t)constrain(server.arg("bright").toInt(), 10, 100);
  s.nightEnabled = server.hasArg("night") ? 1 : 0;
  s.nightBrightness = (uint8_t)constrain(server.arg("nbright").toInt(), 0, 100);
  uint16_t m;
  if (parseMinutes(server.arg("nfrom"), m)) {
    s.nightFrom = m;
  }
  if (parseMinutes(server.arg("nto"), m)) {
    s.nightTo = m;
  }
  if (settingsUpdate(s)) {
    redirect(F("/settings?saved=1"), 303);
  } else {
    redirect(F("/settings?error=1"), 303);
  }
}

static void handleApiKey() {
  if (blockedInRescue() || !requireAuth() || !requireCsrf()) {
    return;
  }
  String action = server.arg("action");
  bool ok = true;
  if (action == "new") {
    ok = authNewApiKey();
  } else if (action == "clear") {
    ok = authClearApiKey();
  }
  if (ok) {
    redirect(F("/settings?keysaved=1"), 303);
  } else {
    redirect(F("/settings?keyerror=1"), 303);
  }
}

// ---------------------------------------------------------------------------
// WLAN setup
// ---------------------------------------------------------------------------

static void handleSetupForm() {
  Page p;
  p.begin(NAV_HOME);
  p << F("<h1>WLAN einrichten</h1><p>W&auml;hle dein WLAN aus. SysopTV kann nur 2,4-GHz-Netze.</p>"
         "<form method=\"post\" action=\"/save\">") << csrfField()
    << F("<label for=\"ssid\">WLAN</label><select id=\"ssid\" name=\"ssid\">");
  for (int i = 0; i < scanCount; i++) {
    String e = htmlEscape(scanList[i]);
    p << F("<option value=\"") << e << F("\">") << e << F("</option>");
  }
  p << F("</select><label for=\"manual\">Oder Name von Hand eingeben (z.&nbsp;B. verstecktes WLAN)</label>"
         "<input id=\"manual\" name=\"manual\" autocomplete=\"off\"><label for=\"pass\">Passwort</label>"
         "<input id=\"pass\" name=\"pass\" type=\"password\" autocomplete=\"off\">"
         "<button type=\"submit\">Speichern und verbinden</button></form>"
         "<form method=\"post\" action=\"/rescan\">") << csrfField()
    << F("<button type=\"submit\" class=\"alt\">Netze neu suchen</button></form>");
  if (authHasPassword()) {
    p << F("<p class=\"dim\">Zum Speichern fragt der Browser nach dem Admin-Passwort.</p>");
  }
  p.end();
}

static void scheduleRestart(unsigned long ms) {
  restartAt = millis() + ms;
  if (restartAt == 0) {
    restartAt = 1;
  }
}

static void handleRescan() {
  if (!requireAuth() || !requireCsrf()) {
    return;
  }
  // The scan only runs while the hotspot starts, so a fresh list needs a restart.
  // Outside the hotspot there is nothing to scan for.
  if (!setupMode) {
    redirect(F("/"), 303);
    return;
  }
  messagePage(NAV_NONE, 200, F("Netze neu suchen"),
              F("SysopTV sucht neu und startet daf&uuml;r neu. Verbinde dich danach wieder mit " SYSOPTV_AP_NAME "."),
              false, F("/"), F("Zur Einrichtung"));
  scheduleRestart(2000);
}

static void handleRoot() {
  if (setupMode) {
    handleSetupForm();
  } else {
    handleStatus();
  }
}

static void handleSave() {
  if (!requireAuth() || !requireCsrf()) {
    return;
  }
  String ssid = server.arg("manual");
  ssid.trim();
  if (ssid.length() == 0) {
    ssid = server.arg("ssid");
  }
  String pass = server.arg("pass");
  if (ssid.length() == 0) {
    messagePage(NAV_NONE, 200, F("Nicht gespeichert"), F("Kein WLAN gew&auml;hlt."), true, F("/"), F("Zur&uuml;ck"));
    return;
  }
  // Limits of the WLAN standard: SSID up to 32 bytes, WPA passphrase 8 to 63
  // characters. An empty password is fine for open networks.
  if (ssid.length() > 32) {
    messagePage(NAV_NONE, 200, F("Nicht gespeichert"),
                F("Der WLAN-Name ist zu lang. Erlaubt sind h&ouml;chstens 32 Zeichen."), true, F("/"), F("Zur&uuml;ck"));
    return;
  }
  if (pass.length() > 0 && (pass.length() < 8 || pass.length() > 63)) {
    messagePage(NAV_NONE, 200, F("Nicht gespeichert"),
                F("Das Passwort muss 8 bis 63 Zeichen lang sein. F&uuml;r ein offenes WLAN das Feld leer lassen."),
                true, F("/"), F("Zur&uuml;ck"));
    return;
  }
  String text = F("SysopTV verbindet sich jetzt mit <b>");
  text += htmlEscape(ssid);
  text += F("</b> und startet neu. Die neue IP-Adresse steht danach auf dem Display.");
  messagePage(NAV_NONE, 200, F("Gespeichert"), text, false, F("/"), F("Zur&uuml;ck"));
  delay(500);

  WiFi.persistent(true);
  WiFi.mode(WIFI_STA);
  WiFi.begin(ssid.c_str(), pass.c_str());
  delay(500);
  ESP.restart();
}

// ---------------------------------------------------------------------------
// Firmware update
// ---------------------------------------------------------------------------

static void handleUpdateForm() {
  if (!requireAuth()) {
    return;
  }
  Page p;
  p.begin(NAV_UPDATE);
  // The token goes into the address: the server reads the query before the
  // file, so the upload can be refused before anything is written.
  p << F("<h1>Firmware aktualisieren</h1><p>W&auml;hle eine .bin-Datei aus.</p><p class=\"er\">Die R&uuml;ckkehr zur "
         "Original-Firmware ist noch nicht getestet. Lade vorher unter &Uuml;bersicht die "
         "Flash-Sicherung herunter.</p><form method=\"post\" action=\"/update?csrf=")
    << authCsrfToken()
    << F("\" enctype=\"multipart/form-data\"><input type=\"file\" name=\"firmware\" accept=\".bin\">"
         "<button type=\"submit\">Hochladen</button></form>"
         "<p class=\"er\">W&auml;hrend des Updates nicht den Strom trennen.</p>");
  p.end();
}

static void handleUpdateUpload() {
  HTTPUpload& up = server.upload();
  if (up.status == UPLOAD_FILE_START) {
    updateError = "";
    uploadStarted = true;
    uploadDenied = 0;
    // Check login and token before touching the flash.
    if (!loggedIn()) {
      uploadDenied = 401;
    } else if (!authCsrfValid(server.arg("csrf"))) {
      uploadDenied = 403;
    }
    if (uploadDenied) {
      return;
    }
    WiFiUDP::stopAll();
    screenUpdateStart();
    uint32_t maxSpace = (ESP.getFreeSketchSpace() - 0x1000) & 0xFFFFF000;
    if (!Update.begin(maxSpace, U_FLASH)) {
      updateError = Update.getErrorString();
    }
  } else if (uploadDenied) {
    // Ignore the rest of a refused upload.
  } else if (up.status == UPLOAD_FILE_WRITE) {
    if (updateError.length() == 0) {
      if (Update.write(up.buf, up.currentSize) != up.currentSize) {
        updateError = Update.getErrorString();
      } else {
        screenUpdateProgress(up.totalSize);
      }
    }
  } else if (up.status == UPLOAD_FILE_END) {
    if (updateError.length() == 0 && !Update.end(true)) {
      updateError = Update.getErrorString();
    }
  } else if (up.status == UPLOAD_FILE_ABORTED) {
    Update.end();
    updateError = F("Upload abgebrochen");
  }
  yield();
}

static void handleUpdateDone() {
  bool started = uploadStarted;
  int denied = uploadDenied;
  uploadStarted = false;
  uploadDenied = 0;
  // A refused or empty request changed nothing, so no restart either.
  if (denied == 401) {
    requireAuth();  // still not logged in, answers with 401
    return;
  }
  if (denied == 403) {
    requireCsrf();  // token still wrong, answers with 403
    return;
  }
  if (!started) {
    if (requireAuth() && requireCsrf()) {
      messagePage(NAV_UPDATE, 200, F("Keine Datei"), F("Es wurde keine Firmware-Datei mitgeschickt."), true,
                  F("/update"), F("Zur&uuml;ck"));
    }
    return;
  }

  bool ok = updateError.length() == 0 && !Update.hasError();
  if (ok) {
    messagePage(NAV_NONE, 200, F("Update erfolgreich"), F("SysopTV startet neu. Das dauert ein paar Sekunden."),
                false, F("/"), F("Zur &Uuml;bersicht"));
    screenUpdateResult(true, "");
    delay(1000);
    ESP.restart();
    return;
  }
  String text = htmlEscape(updateError);
  text += F(". Die bisherige Firmware bleibt erhalten. SysopTV startet in ein paar Sekunden neu.");
  messagePage(NAV_NONE, 200, F("Update fehlgeschlagen"), text, true, F("/update"), F("Nochmal versuchen"));
  screenUpdateResult(false, updateError);
  // WiFiUDP::stopAll() at the start of the upload also stopped mDNS and the
  // hotspot DNS server. The old firmware is still intact, so a restart is the
  // simplest way to get everything running again.
  scheduleRestart(SYSOPTV_UPDATE_FAIL_RESTART_MS);
}

// ---------------------------------------------------------------------------
// Flash backup
// ---------------------------------------------------------------------------

// Read-only raw copy of the filesystem area of the original firmware
// (0x100000 to 0x3FA000), streamed in 1 KB blocks. Only with a password,
// because that area may hold the WLAN data of the original firmware.
static void handleFlashBackup() {
  if (blockedInRescue() || !requireAuth()) {
    return;
  }
  if (!authHasPassword()) {
    messagePage(NAV_NONE, 403, F("Erst ein Passwort festlegen"),
                F("Die Sicherung kann Zugangsdaten der Original-Firmware enthalten und ist deshalb nur mit "
                  "Admin-Passwort verf&uuml;gbar."), true, F("/"), F("Zur &Uuml;bersicht"));
    return;
  }
  const uint32_t size = SYSOPTV_BACKUP_END - SYSOPTV_BACKUP_START;
  server.sendHeader(F("Content-Disposition"), F("attachment; filename=\"flashbackup-geekmagic-100000-3fa000.bin\""));
  server.sendHeader(F("Cache-Control"), F("no-store"));
  server.setContentLength(size);
  server.send(200, F("application/octet-stream"), "");
  WiFiClient client = server.client();
  static uint32_t block[256];
  for (uint32_t addr = SYSOPTV_BACKUP_START; addr < SYSOPTV_BACKUP_END; addr += sizeof(block)) {
    if (!ESP.flashRead(addr, block, sizeof(block))) {
      break;
    }
    if (client.write(reinterpret_cast<const uint8_t*>(block), sizeof(block)) != sizeof(block)) {
      break;
    }
    yield();
  }
}

// ---------------------------------------------------------------------------
// API
// ---------------------------------------------------------------------------

static void sendJsonError(int code, const char* message) {
  String j;
  j += F("{\"ok\":false,\"error\":\"");
  j += message;
  j += F("\"}");
  server.send(code, F("application/json"), j);
}

// POST /api/usage from the sender script. Open unless an API key is set.
static void handleUsage() {
  if (rescueMode) {
    sendJsonError(503, "Rettungsmodus");
    return;
  }
  if (!authApiKeyValid(server.header("X-SysopTV-Key"))) {
    sendJsonError(401, "Schluessel fehlt oder falsch");
    return;
  }
  // The web server has already read the whole body at this point.
  if (!server.hasArg("plain")) {
    sendJsonError(400, "kein JSON");
    return;
  }
  const String& body = server.arg("plain");
  if (body.length() > SYSOPTV_USAGE_MAX_BODY) {
    sendJsonError(413, "zu gross");
    return;
  }
  UsagePacket packet;
  const char* error = usageParse(body.c_str(), body.length(), packet);
  if (error) {
    sendJsonError(400, error);
    return;
  }
  usageApply(packet);
  server.send(200, F("application/json"), F("{\"ok\":true}"));
}

static void limitJson(JsonObject o, const UsageColumn& c) {
  o["present"] = c.present;
  o["percent"] = c.percent;
  o["warn"] = c.warn;
  o["bar"] = c.present ? c.percent : 0;
  o["text"] = percentText(c);
  o["reset"] = resetText(c);
}

// GET /api/info, always open. No secrets: no password hash, no API key,
// no WLAN password. The first seven fields keep their old meaning for existing clients.
static void handleInfo() {
  JsonDocument doc;
  doc["name"] = "SysopTV";
  doc["version"] = SYSOPTV_VERSION;
  doc["ip"] = setupMode ? WiFi.softAPIP().toString() : WiFi.localIP().toString();
  doc["rssi"] = WiFi.RSSI();
  doc["uptime"] = millis() / 1000;
  doc["heap"] = ESP.getFreeHeap();
  doc["rescue"] = rescueMode;

  doc["hostname"] = SYSOPTV_HOSTNAME ".local";
  doc["ssid"] = setupMode ? String(F(SYSOPTV_AP_NAME " (Hotspot)")) : WiFi.SSID();
  doc["signalText"] = signalText();
  doc["timeSynced"] = usageTimeSource() != TIME_NONE;
  doc["timeText"] = timeText();
  doc["uptimeText"] = uptimeText();
  doc["heapKb"] = ESP.getFreeHeap() / 1024;
  doc["resetReason"] = ESP.getResetReason();
  doc["design"] = designGet(designActive()).name;
  doc["designCycle"] = settingsGet().cycleSeconds;
  doc["designText"] = designText();
  if (!rescueMode) {
    UsageView view;
    usageBuildView(view);
    bool ok;
    JsonObject u = doc["usage"].to<JsonObject>();
    u["status"] = dataStatus(ok);
    u["statusOk"] = ok;
    u["mode"] = modeWord(view.showUsed);
    limitJson(u["session"].to<JsonObject>(), view.session);
    limitJson(u["week"].to<JsonObject>(), view.week);
  }
  String out;
  serializeJson(doc, out);
  server.send(200, F("application/json; charset=utf-8"), out);
}

static void handleNotFound() {
  if (setupMode) {
    // Captive portal: send every unknown address to the setup page.
    server.sendHeader(F("Location"), F("http://192.168.4.1/"), true);
    server.send(302, F("text/plain"), "");
    return;
  }
  server.send(404, F("text/plain"), F("Nicht gefunden"));
}

// ---------------------------------------------------------------------------
// WLAN
// ---------------------------------------------------------------------------

static void drawCurrentScreen() {
  if (setupMode) {
    screenSetup(SYSOPTV_AP_NAME);
  } else if (WiFi.status() == WL_CONNECTED) {
    // Once usage data has arrived, the design owns the screen and redraws
    // itself as soon as netScreenFree() is true again.
    if (rescueMode || !usageScreenActive()) {
      screenReady(WiFi.localIP().toString(), rescueMode);
    }
  } else {
    screenLost();
  }
}

static void scanNetworks() {
  int found = WiFi.scanNetworks();
  scanCount = 0;
  for (int i = 0; i < found && scanCount < MAX_SCAN; i++) {
    String name = WiFi.SSID(i);
    if (name.length() == 0) {
      continue;
    }
    bool dup = false;
    for (int k = 0; k < scanCount; k++) {
      if (scanList[k] == name) {
        dup = true;
        break;
      }
    }
    if (!dup) {
      scanList[scanCount++] = name;
    }
  }
  WiFi.scanDelete();
}

static void startSetupHotspot() {
  WiFi.mode(WIFI_STA);
  scanNetworks();
  WiFi.mode(WIFI_AP);
  WiFi.softAP(SYSOPTV_AP_NAME);
  dns.start(53, "*", WiFi.softAPIP());
  setupMode = true;
  apLastClient = millis();
  screenSetup(SYSOPTV_AP_NAME);
}

static void startStation() {
  setupMode = false;
  wasConnected = true;
  MDNS.begin(SYSOPTV_HOSTNAME);
  MDNS.addService("http", "tcp", 80);
  if (!rescueMode) {
    // Fallback clock when the PC is off. The sender time has priority, see usage.cpp.
    configTime(SYSOPTV_TZ, SYSOPTV_NTP_SERVER);
  }
  screenReady(WiFi.localIP().toString(), rescueMode);
}

void netBegin(bool rescue) {
  rescueMode = rescue;

  // Mode changes are not written to flash. Credentials are only saved in handleSave().
  WiFi.persistent(false);
  WiFi.mode(WIFI_STA);
  WiFi.hostname(SYSOPTV_HOSTNAME);
  WiFi.setAutoReconnect(true);

  String saved = WiFi.SSID();
  hasSavedWlan = saved.length() > 0;

  bool connected = false;
  if (hasSavedWlan) {
    screenConnecting(saved);
    WiFi.begin();
    unsigned long start = millis();
    while (millis() - start < SYSOPTV_CONNECT_TIMEOUT_MS) {
      if (WiFi.status() == WL_CONNECTED) {
        connected = true;
        break;
      }
      delay(250);
    }
  }

  if (connected) {
    startStation();
  } else {
    startSetupHotspot();
  }

  // Authorization is always collected by the web server, the API key header is extra.
  server.collectHeaders("X-SysopTV-Key");

  server.on("/", HTTP_GET, handleRoot);
  server.on("/password", HTTP_POST, handlePassword);
  server.on("/settings", HTTP_GET, handleSettingsForm);
  server.on("/settings", HTTP_POST, handleSettingsSave);
  server.on("/settings/key", HTTP_POST, handleApiKey);
  server.on("/save", HTTP_POST, handleSave);
  server.on("/rescan", HTTP_POST, handleRescan);
  server.on("/update", HTTP_GET, handleUpdateForm);
  server.on("/update", HTTP_POST, handleUpdateDone, handleUpdateUpload);
  server.on("/flashbackup", HTTP_GET, handleFlashBackup);
  server.on("/api/usage", HTTP_POST, handleUsage);
  server.on("/api/info", HTTP_GET, handleInfo);
  server.onNotFound(handleNotFound);
  server.begin();
}

void netIpText(char* out, size_t size) {
  String ip = setupMode ? WiFi.softAPIP().toString() : WiFi.localIP().toString();
  snprintf(out, size, "%s", ip.c_str());
}

bool netScreenFree() {
  return !setupMode && wasConnected && restartAt == 0;
}

void netLoop() {
  server.handleClient();

  if (setupMode) {
    dns.processNextRequest();
    if (WiFi.softAPgetStationNum() > 0) {
      apLastClient = millis();
    } else if (hasSavedWlan && millis() - apLastClient > SYSOPTV_AP_IDLE_RESTART_MS) {
      // Nobody is using the hotspot: maybe the router was just off. Try the saved WLAN again.
      ESP.restart();
    }
  } else {
    MDNS.update();
    bool connected = WiFi.status() == WL_CONNECTED;
    if (connected != wasConnected) {
      wasConnected = connected;
      drawCurrentScreen();
    }
  }

  if (restartAt != 0 && (long)(millis() - restartAt) >= 0) {
    ESP.restart();
  }
}
