const vscode = require('vscode');

const TOP_LEVEL = [
  'press', 'down', 'up', 'delay', 'wait', 'text', 'type', 'wheel', 'move', 'cursor',
  'browser', 'clipboard', 'run', 'open', 'window', 'volume', 'mute', 'media',
  'screenshot', 'lock', 'sleep', 'profile', 'layer', 'repeat', 'while', 'if',
  'set', 'let', 'inc', 'dec'
];

const BROWSER_OPS = [
  'goto', 'page', 'set_param', 'change_param', 'set_param_url', 'change_param_url',
  'tab', 'select', 'open_in_new_tab', 'back', 'forward', 'stop', 'reload', 'update',
  'zoom', 'find', 'search', 'copy_url', 'scroll'
];

const TAB_OPS = ['new', 'close', 'next', 'prev', 'reopen'];
const WINDOW_OPS = ['activate', 'minimize', 'maximize', 'restore', 'close'];
const MEDIA_OPS = ['next', 'prev', 'play', 'pause', 'stop'];
const CLIPBOARD_OPS = ['set', 'paste', 'clear'];
const PROFILE_OPS = ['next', 'prev', 'set'];
const LAYER_OPS = ['next', 'prev', 'set'];
const SCROLL_OPS = ['top', 'bottom'];
const ZOOM_OPS = ['+1', '-1', 'reset'];
const RELOAD_OPS = ['hard'];

function activate(context) {
  const diagnostics = vscode.languages.createDiagnosticCollection('mousemacros');
  context.subscriptions.push(diagnostics);

  const selector = { language: 'mousemacros' };

  const refresh = (document) => {
    if (document.languageId !== 'mousemacros') return;
    diagnostics.set(document.uri, validate(document));
  };

  context.subscriptions.push(
    vscode.workspace.onDidOpenTextDocument(refresh),
    vscode.workspace.onDidChangeTextDocument(event => refresh(event.document)),
    vscode.workspace.onDidCloseTextDocument(document => diagnostics.delete(document.uri))
  );

  vscode.workspace.textDocuments.forEach(refresh);

  context.subscriptions.push(
    vscode.languages.registerCompletionItemProvider(selector, {
      provideCompletionItems(document, position) {
        return complete(document, position);
      }
    }, ' ')
  );
}

function deactivate() {}

function complete(document, position) {
  const line = document.lineAt(position.line).text;
  const before = line.slice(0, position.character);
  const prefix = before.trimStart();

  let words = null;
  let detail = '';

  if (/^browser\s+tab\s+\S*$/i.test(prefix)) {
    words = TAB_OPS;
    detail = 'Подкоманда browser tab';
  } else if (/^browser\s+scroll\s+\S*$/i.test(prefix)) {
    words = SCROLL_OPS;
    detail = 'Направление прокрутки';
  } else if (/^browser\s+zoom\s+\S*$/i.test(prefix)) {
    words = ZOOM_OPS;
    detail = 'Масштаб страницы';
  } else if (/^browser\s+reload\s+\S*$/i.test(prefix)) {
    words = RELOAD_OPS;
    detail = 'Тип обновления';
  } else if (/^browser\s+\S*$/i.test(prefix)) {
    words = BROWSER_OPS;
    detail = 'Команда browser';
  } else if (/^window\s+\S*$/i.test(prefix)) {
    words = WINDOW_OPS;
    detail = 'Действие с окном';
  } else if (/^media\s+\S*$/i.test(prefix)) {
    words = MEDIA_OPS;
    detail = 'Медиа-команда';
  } else if (/^clipboard\s+\S*$/i.test(prefix)) {
    words = CLIPBOARD_OPS;
    detail = 'Действие с буфером обмена';
  } else if (/^profile\s+\S*$/i.test(prefix)) {
    words = PROFILE_OPS;
    detail = 'Действие с профилем';
  } else if (/^layer\s+\S*$/i.test(prefix)) {
    words = LAYER_OPS;
    detail = 'Действие со слоем';
  } else if (/^[a-zA-Zа-яА-Я]*$/.test(prefix)) {
    words = TOP_LEVEL;
    detail = 'Команда макроса';
  }

  if (!words) return undefined;

  return words.map(word => {
    const item = new vscode.CompletionItem(word, vscode.CompletionItemKind.Keyword);
    item.detail = detail;
    item.documentation = new vscode.MarkdownString('`' + word + '`');
    return item;
  });
}

function validate(document) {
  const diagnostics = [];
  const text = document.getText();
  const lines = text.split(/\r?\n/);
  let braceDepth = 0;

  for (let i = 0; i < lines.length; i++) {
    const raw = lines[i];
    const line = raw.trim();
    if (!line) continue;

    const range = new vscode.Range(i, 0, i, Math.max(1, raw.length));
    const push = (message) => diagnostics.push(
      new vscode.Diagnostic(range, message, vscode.DiagnosticSeverity.Error));

    if (line.startsWith('#')) continue;

    const quoteError = checkQuotes(raw);
    if (quoteError) {
      push(quoteError);
      continue;
    }

    const braces = countBraces(raw);
    braceDepth += braces;
    if (braceDepth < 0) {
      push('Лишняя закрывающая скобка }.');
      braceDepth = 0;
    }

    if (line === '}') continue;
    if (/^\}\s*else\s*\{$/i.test(line)) continue;

    if (line.endsWith('{')) {
      const header = line.slice(0, -1).trim();
      if (/^repeat\s+while\b/i.test(header)) {
        if (!header.replace(/^repeat\s+while\b/i, '').trim()) {
          push('После repeat while нужно условие.');
        }
      } else if (/^while\b/i.test(header)) {
        if (!header.replace(/^while\b/i, '').trim()) {
          push('После while нужно условие.');
        }
      } else if (/^repeat\b/i.test(header)) {
        const count = header.replace(/^repeat\b/i, '').trim();
        if (!/^\d+$/.test(count) || Number(count) < 1) {
          push('После repeat нужно положительное число: repeat 5 {');
        }
      } else if (/^if\b/i.test(header)) {
        if (!header.slice(2).trim()) push('После if нужно условие.');
      } else {
        push('Неизвестный блок. Ожидается repeat N {, while условие { или if условие {.');
      }
      continue;
    }

    const parts = splitArgs(line);
    const command = (parts[0] || '').toLowerCase();
    const rest = parts.slice(1);

    switch (command) {
      case 'delay':
      case 'wait':
        if (!/^\d+$/.test((rest[0] || '').trim())) push('delay/wait требует число миллисекунд.');
        break;
      case 'press':
      case 'down':
      case 'up':
        if (rest.length === 0) push(command + ' требует клавишу или mouse:left.');
        break;
      case 'wheel':
        if (rest[0] && !/^(up|down|left|right|вверх|вниз|влево|вправо)$/i.test(rest[0])) {
          push('wheel: направление up/down/left/right.');
        }
        break;
      case 'move':
      case 'cursor':
        if (rest.length === 0) push(command + ' требует координаты или направление.');
        break;
      case 'text':
      case 'type':
        if (rest.length === 0) push('text требует текст после команды.');
        break;
      case 'set':
      case 'let':
        if (rest.length < 2) push(command + ' требует имя и значение.');
        else if (!/^[A-Za-zА-Яа-яЁё_][A-Za-zА-Яа-яЁё0-9_]*$/.test(rest[0])) {
          push('Некорректное имя переменной.');
        }
        break;
      case 'inc':
      case 'dec':
        if (rest.length === 0) push(command + ' требует имя переменной.');
        else if (rest.length > 1 && !/^[+-]?\d+$/.test(rest[1])) {
          push(command + ' принимает только числовой шаг.');
        }
        break;
      case 'while':
        push('У команды while должен быть блок { ... }.');
        break;
      case 'browser':
        checkBrowser(rest, push);
        break;
      case 'clipboard':
        checkClipboard(rest, push);
        break;
      case 'run':
      case 'open':
        if (rest.length === 0) push(command + ' требует путь или программу.');
        break;
      case 'window':
        checkWindow(rest, push);
        break;
      case 'volume':
        if (!/^[+-]?\d+$/.test((rest[0] || '').trim())) push('volume требует число, например volume +5.');
        break;
      case 'mute':
      case 'screenshot':
      case 'lock':
      case 'sleep':
        break;
      case 'media':
        if (!MEDIA_OPS.includes((rest[0] || '').toLowerCase())) {
          push('media: next, prev, play, pause или stop.');
        }
        break;
      case 'profile':
        checkProfile(rest, push);
        break;
      case 'layer':
        checkLayer(rest, push);
        break;
      case 'repeat':
      case 'if':
        push('У команды ' + command + ' должен быть блок { ... }.');
        break;
      default:
        push('Неизвестная команда: ' + parts[0]);
    }
  }

  if (braceDepth > 0) {
    const last = Math.max(0, lines.length - 1);
    diagnostics.push(new vscode.Diagnostic(
      new vscode.Range(last, 0, last, Math.max(1, lines[last].length)),
      'Не закрыт блок {.',
      vscode.DiagnosticSeverity.Error));
  }

  return diagnostics;
}

function checkBrowser(args, push) {
  const op = (args[0] || '').toLowerCase();
  const rest = args.slice(1);
  switch (op) {
    case 'goto':
      if (rest.length === 0) push('browser goto требует URL.');
      break;
    case 'page':
      if (rest.length === 0) push('browser page требует +1, -1 или номер.');
      break;
    case 'set_param':
      if (rest.length < 2) push('browser set_param "имя" "значение".');
      break;
    case 'change_param':
      if (rest.length < 2) push('browser change_param "имя" +1.');
      else if (!/^[+-]?\d+$/.test(rest[1])) push('change_param работает только с числами.');
      break;
    case 'set_param_url':
      if (rest.length < 2) push('browser set_param_url N "значение".');
      else if (!/^\d+$/.test(rest[0])) push('Номер сегмента пути должен быть числом.');
      break;
    case 'change_param_url':
      if (rest.length < 2) push('browser change_param_url N +1.');
      else if (!/^\d+$/.test(rest[0]) || !/^[+-]?\d+$/.test(rest[1])) {
        push('change_param_url N +1: N и шаг должны быть числами.');
      }
      break;
    case 'tab':
      checkTab(rest, push);
      break;
    case 'select':
      if (!/^\d+$/.test((rest[0] || '').trim())) push('browser select требует номер вкладки.');
      break;
    case 'open_in_new_tab':
      break;
    case 'back':
    case 'forward':
    case 'stop':
    case 'copy_url':
    case 'update':
      break;
    case 'reload':
      if (rest[0] && !/^hard$/i.test(rest[0])) push('browser reload [hard].');
      break;
    case 'zoom':
      if (rest[0] && !/^(\+1|-1|reset)$/i.test(rest[0])) push('browser zoom +1, -1 или reset.');
      break;
    case 'find':
    case 'search':
      if (rest.length === 0) push('browser ' + op + ' требует текст.');
      break;
    case 'scroll':
      if (!/^(top|bottom)$/i.test((rest[0] || '').trim())) push('browser scroll top или bottom.');
      break;
    default:
      push('Неизвестная команда browser: ' + (op || '(пусто)'));
  }
}

function checkTab(args, push) {
  const sub = (args[0] || '').toLowerCase();
  const rest = args.slice(1);
  switch (sub) {
    case 'new':
      break;
    case 'close':
      if (rest[0] && !/^\d+$/.test(rest[0])) push('browser tab close [номер].');
      break;
    case 'next':
    case 'prev':
    case 'reopen':
      break;
    default:
      push('browser tab new/close/next/prev/reopen.');
  }
}

function checkClipboard(args, push) {
  const op = (args[0] || '').toLowerCase();
  const rest = args.slice(1);
  switch (op) {
    case 'set':
      if (rest.length === 0) push('clipboard set требует текст.');
      break;
    case 'paste':
    case 'clear':
      break;
    default:
      push('clipboard set/paste/clear.');
  }
}

function checkWindow(args, push) {
  const op = (args[0] || '').toLowerCase();
  const rest = args.slice(1);
  switch (op) {
    case 'activate':
      if (rest.length === 0) push('window activate требует заголовок.');
      break;
    case 'minimize':
    case 'maximize':
    case 'restore':
    case 'close':
      break;
    default:
      push('window activate/minimize/maximize/restore/close.');
  }
}

function checkProfile(args, push) {
  const op = (args[0] || '').toLowerCase();
  if (op === 'next' || op === 'prev' || op === 'previous' || op === 'set') return;
  if (args.length !== 1) push('profile next/prev/set <id>.');
}

function checkLayer(args, push) {
  const op = (args[0] || '').toLowerCase();
  if (op === 'next' || op === 'prev' || op === 'previous') return;
  if (op === 'set') {
    if (!/^\d+$/.test((args[1] || '').trim())) push('layer set требует номер слоя.');
    return;
  }
  if (!/^\d+$/.test(op)) push('layer next/prev/set N.');
}

function checkQuotes(line) {
  let quote = null;
  let tokenStart = true;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quote) {
      if (ch === '\\') i++;
      else if (ch === quote) quote = null;
      continue;
    }
    if ((ch === '"' || ch === '\'') && tokenStart) {
      quote = ch;
      tokenStart = false;
      continue;
    }
    if (/\s/.test(ch) || ch === '{' || ch === '}' || ch === '=') tokenStart = true;
    else tokenStart = false;
  }
  return quote ? 'Не закрыта кавычка.' : null;
}

function countBraces(line) {
  let depth = 0;
  let quote = null;
  let tokenStart = true;
  for (let i = 0; i < line.length; i++) {
    const ch = line[i];
    if (quote) {
      if (ch === '\\') i++;
      else if (ch === quote) quote = null;
      continue;
    }
    if ((ch === '"' || ch === '\'') && tokenStart) {
      quote = ch;
      tokenStart = false;
      continue;
    }
    if (ch === '{') depth++;
    else if (ch === '}') depth--;
    if (/\s/.test(ch) || ch === '{' || ch === '}' || ch === '=') tokenStart = true;
    else tokenStart = false;
  }
  return depth;
}

function splitArgs(text) {
  const result = [];
  let i = 0;
  while (i < text.length) {
    while (i < text.length && /\s/.test(text[i])) i++;
    if (i >= text.length) break;
    const quote = text[i] === '"' || text[i] === '\'' ? text[i] : null;
    if (quote) i++;
    let value = '';
    while (i < text.length) {
      const ch = text[i];
      if (quote && ch === quote) { i++; break; }
      if (!quote && /\s/.test(ch)) break;
      if (ch === '\\' && i + 1 < text.length && (text[i + 1] === '\\' || text[i + 1] === '"' || text[i + 1] === '\'')) {
        i++;
        value += text[i++];
        continue;
      }
      value += ch;
      i++;
    }
    result.push(value);
  }
  return result;
}

module.exports = { activate, deactivate };
