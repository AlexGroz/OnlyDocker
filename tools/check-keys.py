#!/usr/bin/env python3
"""Проверяет правило книги: у каждого блока команд, где есть ключи (-x / --flag),
сразу после блока должен идти разбор «**Ключи:** …».

Использование:
    python3 tools/check-keys.py          # все главы
    python3 tools/check-keys.py book/03-*.md

Код возврата: 0 — всё в порядке, 1 — есть блоки без разбора.
Блоки внутри раздела «Шпаргалка» не проверяются: в шпаргалке стоит явное
пояснение, что ключи разобраны в шагах главы.
"""
import re
import sys
import pathlib

FLAG = re.compile(r'(^|\s)-{1,2}[A-Za-z0-9]')


def check(path: pathlib.Path) -> list[str]:
    lines = path.read_text(encoding='utf-8').splitlines()
    problems: list[str] = []
    in_cheatsheet = False
    i = 0
    while i < len(lines):
        line = lines[i]
        if re.match(r'^## .*Шпаргалка', line):
            in_cheatsheet = True
        elif re.match(r'^## ', line):
            in_cheatsheet = False

        if line.startswith('```bash') or line.startswith('```sh'):
            start = i + 2  # номер первой строки содержимого (1-based)
            j = i + 1
            body = []
            while j < len(lines) and not lines[j].startswith('```'):
                stripped = lines[j].strip()
                if stripped and not stripped.startswith('#'):
                    body.append(lines[j])
                j += 1
            after = ' '.join(lines[j + 1:j + 4])
            has_flags = any(FLAG.search(b) for b in body)
            if has_flags and 'Ключи' not in after and not in_cheatsheet:
                first = body[0].strip()[:70] if body else ''
                problems.append(f'{path.name}:{start}: {first}')
            i = j
        i += 1
    return problems


def main() -> int:
    paths = [pathlib.Path(p) for p in sys.argv[1:]] or \
            sorted(pathlib.Path('book').glob('*.md'))
    total = 0
    for path in paths:
        found = check(path)
        total += len(found)
        status = 'OK' if not found else f'{len(found)} без разбора'
        print(f'{path.name:34} {status}')
        for item in found:
            print(f'    {item}')
    return 0 if total == 0 else 1


if __name__ == '__main__':
    sys.exit(main())
