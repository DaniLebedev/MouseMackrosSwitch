# MouseMackros Macro для VS Code

Подсветка, автодополнение, сниппеты и проверка синтаксиса для файлов `.macro`.

## Установка

1. Закройте VS Code.
2. Запустите из корня репозитория:

```powershell
powershell -ExecutionPolicy Bypass -File tools\install-vscode-extension.ps1
```

3. Откройте VS Code. Файлы `.macro` получат язык **MouseMackros Macro**.

Скрипт копирует расширение в `%USERPROFILE%\.vscode\extensions\mousemackros.mouse-macros-0.1.0`.
После обновления расширения запустите скрипт ещё раз и перезапустите VS Code.

## Что умеет

- Подсветка команд, строк, комментариев, чисел и блоков `repeat` / `if`.
- Автодополнение по пробелу: команды, подкоманды `browser`, `window`, `media`,
  `clipboard`, `profile`, `layer`, `browser tab`.
- Сниппеты для частых команд: `bpage`, `bchangeparam`, `btabnew`, `ifelse` и другие.
- Проверка синтаксиса:
  - неизвестные команды и подкоманды;
  - незакрытые кавычки и блоки `{ ... }`;
  - отсутствующие аргументы;
  - нечисловые значения там, где нужно число.

## Формат файла

Обычный текст, одна команда на строку, `#` — комментарий:

```text
# пример
browser page +1
delay 120
if browser.url contains "nexusmods.com" {
    browser copy_url
} else {
    browser search "nexus mods"
}
```

Расширение не запускает макросы. Оно только помогает редактировать файлы,
которые потом загружаются в MouseMackros через **«Макрос…» → «Загрузить…»**.
