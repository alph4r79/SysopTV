# SysopTV, Copyright (C) 2026 Christoph Scheel
# Licensed under the GNU General Public License v3.0 or later, see LICENSE.
#
# Builds the WordPress plugin "SysopTV for WooCommerce" without extra tools:
#   1. collects the translatable strings from the PHP files into the .pot
#   2. checks that the German .po translates all of them
#   3. compiles the .po into the .mo (GNU gettext format, little endian)
#   4. runs "php -l" on every PHP file, if PHP is installed
#   5. writes dist/sysoptv-woocommerce-<version>.zip with the folder
#      sysoptv-woocommerce/ inside, ready for "Plugins, Add New, Upload Plugin"
#
# Call from the root of the repository: python wordpress/tools/build-plugin.py

import ast
import os
import re
import shutil
import struct
import subprocess
import sys
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
PLUGIN = os.path.join(ROOT, 'wordpress', 'sysoptv-woocommerce')
LANG = os.path.join(PLUGIN, 'languages')
DOMAIN = 'sysoptv-woocommerce'


def php_files():
    for folder, _, files in os.walk(PLUGIN):
        for name in sorted(files):
            if name.endswith('.php'):
                yield os.path.join(folder, name)


def version():
    text = open(os.path.join(PLUGIN, 'sysoptv-woocommerce.php'), encoding='utf-8').read()
    return re.search(r'^\s*\*\s*Version:\s*(\S+)', text, re.M).group(1)


def collect():
    """Strings of __(), esc_html__(), esc_attr__() and the plugin header, in order."""
    found = []
    call = re.compile(r"(?:__|esc_html__|esc_attr__|esc_html_e|_e)\(\s*'((?:[^'\\]|\\.)*)'\s*,\s*'" + DOMAIN + r"'\s*\)")
    main = os.path.join(PLUGIN, 'sysoptv-woocommerce.php')
    header = open(main, encoding='utf-8').read()
    for field in ('Plugin Name', 'Description'):
        value = re.search(r'^\s*\*\s*' + field + r':\s*(.+)$', header, re.M).group(1).strip()
        found.append((value, 'sysoptv-woocommerce.php'))
    for path in php_files():
        rel = os.path.relpath(path, PLUGIN).replace(os.sep, '/')
        for m in call.finditer(open(path, encoding='utf-8').read()):
            found.append((m.group(1).replace("\\'", "'"), rel))
    seen, result = set(), []
    for text, where in found:
        if text not in seen:
            seen.add(text)
            result.append((text, where))
    return result


def quote(s):
    return '"' + s.replace('\\', '\\\\').replace('"', '\\"') + '"'


def write_pot(strings, ver):
    lines = [
        '# Copyright (C) 2026 Christoph Scheel',
        '# This file is distributed under the GPL-3.0-or-later.',
        'msgid ""',
        'msgstr ""',
        quote('Project-Id-Version: SysopTV for WooCommerce ' + ver + '\\n'),
        quote('MIME-Version: 1.0\\n'),
        quote('Content-Type: text/plain; charset=UTF-8\\n'),
        quote('Content-Transfer-Encoding: 8bit\\n'),
        quote('X-Domain: ' + DOMAIN + '\\n'),
        '',
    ]
    for text, where in strings:
        lines += ['#: ' + where, 'msgid ' + quote(text), 'msgstr ""', '']
    open(os.path.join(LANG, DOMAIN + '.pot'), 'w', encoding='utf-8', newline='\n').write('\n'.join(lines))


def read_po(path):
    """Pairs of msgid and msgstr, the header has an empty msgid."""
    entries, msgid, msgstr, target = [], None, None, None
    for line in open(path, encoding='utf-8'):
        line = line.strip()
        if line.startswith('msgid '):
            if msgid is not None:
                entries.append((msgid, msgstr))
            msgid, msgstr, target = ast.literal_eval(line[6:]), '', 'id'
        elif line.startswith('msgstr '):
            msgstr, target = ast.literal_eval(line[7:]), 'str'
        elif line.startswith('"'):
            part = ast.literal_eval(line)
            if target == 'id':
                msgid += part
            else:
                msgstr += part
    if msgid is not None:
        entries.append((msgid, msgstr))
    return entries


def write_mo(entries, path):
    entries = sorted((k, v) for k, v in entries if v)
    ids = [k.encode('utf-8') for k, _ in entries]
    strs = [v.encode('utf-8') for _, v in entries]
    n = len(entries)
    start = 7 * 4 + 16 * n
    data, offsets_ids, offsets_strs = b'', [], []
    for b in ids:
        offsets_ids.append((len(b), start + len(data)))
        data += b + b'\0'
    for b in strs:
        offsets_strs.append((len(b), start + len(data)))
        data += b + b'\0'
    out = struct.pack('<7I', 0x950412de, 0, n, 7 * 4, 7 * 4 + 8 * n, 0, 0)
    for length, offset in offsets_ids + offsets_strs:
        out += struct.pack('<2I', length, offset)
    open(path, 'wb').write(out + data)


def main():
    ver = version()
    strings = collect()
    write_pot(strings, ver)
    po = os.path.join(LANG, DOMAIN + '-de_DE.po')
    entries = read_po(po)
    translated = {k for k, v in entries if k and v}
    missing = [s for s, _ in strings if s not in translated]
    if missing:
        print('Übersetzung fehlt für:')
        for s in missing:
            print('  ' + s)
        sys.exit(1)
    write_mo(entries, os.path.join(LANG, DOMAIN + '-de_DE.mo'))
    print('Übersetzung: %d Texte, .pot und .mo geschrieben' % len(strings))

    php = shutil.which('php')
    if php:
        for path in php_files():
            r = subprocess.run([php, '-l', path], capture_output=True, text=True)
            if r.returncode != 0:
                print(r.stdout + r.stderr)
                sys.exit(1)
        print('php -l: alle Dateien ohne Fehler')
    else:
        print('PHP nicht gefunden, php -l übersprungen')

    os.makedirs(os.path.join(ROOT, 'dist'), exist_ok=True)
    target = os.path.join(ROOT, 'dist', 'sysoptv-woocommerce-%s.zip' % ver)
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as z:
        for folder, _, files in os.walk(PLUGIN):
            for name in sorted(files):
                path = os.path.join(folder, name)
                rel = os.path.relpath(path, os.path.dirname(PLUGIN)).replace(os.sep, '/')
                z.write(path, rel)
        z.write(os.path.join(ROOT, 'LICENSE'), 'sysoptv-woocommerce/LICENSE')
    print('Plugin: %s (%d Byte)' % (os.path.relpath(target, ROOT).replace(os.sep, '/'), os.path.getsize(target)))


if __name__ == '__main__':
    main()
