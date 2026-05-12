import type * as monaco from "monaco-editor";

/**
 * Register the TankScript language with Monaco.
 * Call once before mounting any Editor that uses language="tankscript".
 */
export function registerTankScript(monacoInstance: typeof monaco) {
  // Only register once
  if (monacoInstance.languages.getLanguages().some((l) => l.id === "tankscript")) return;

  monacoInstance.languages.register({ id: "tankscript" });

  // ── Monarch tokenizer ──
  monacoInstance.languages.setMonarchTokensProvider("tankscript", {
    defaultToken: "",
    ignoreCase: true,

    keywords: [
      "MOVE", "FORWARD",
      "TURN", "ROTATE",
      "BOOST",
      "FIRE", "SHOOT",
      "FIND", "SCAN", "RADAR",
      "WAIT",
      "FOR", "END",
      "IF", "ELSE",
    ],

    conditions: ["SPOTTED", "NOT_SPOTTED"],

    targets: ["E"],

    tokenizer: {
      root: [
        // comments
        [/\/\/.*$/, "comment"],

        // numbers (int and float)
        [/-?\d+(\.\d+)?/, "number"],

        // keywords & conditions
        [/[A-Za-z_]\w*/, {
          cases: {
            "@keywords": "keyword",
            "@conditions": "keyword.condition",
            "@targets": "keyword.target",
            "@default": "identifier",
          },
        }],

        // whitespace
        [/\s+/, "white"],
      ],
    },
  } as monaco.languages.IMonarchLanguage);

  // ── Language configuration (brackets, comments, auto-closing) ──
  monacoInstance.languages.setLanguageConfiguration("tankscript", {
    comments: {
      lineComment: "//",
    },
    brackets: [],
    autoClosingPairs: [],
    folding: {
      markers: {
        start: /^\s*(FOR|IF)\b/i,
        end: /^\s*END\b/i,
      },
    },
  });

  // ── Completion provider ──
  monacoInstance.languages.registerCompletionItemProvider("tankscript", {
    provideCompletionItems: (_model, position) => {
      const range = {
        startLineNumber: position.lineNumber,
        endLineNumber: position.lineNumber,
        startColumn: position.column,
        endColumn: position.column,
      };

      const suggestions: monaco.languages.CompletionItem[] = [
        { label: "MOVE", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "MOVE ${1:5}", insertTextRules: monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: "Move forward N units", range },
        { label: "TURN", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "TURN ${1:90}", insertTextRules: monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: "Rotate N degrees", range },
        { label: "BOOST", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "BOOST", detail: "Sprint forward (2s cooldown)", range },
        { label: "FIRE", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "FIRE", detail: "Launch shell (2s cooldown)", range },
        { label: "FIND E", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "FIND E", detail: "Scan for enemies (5s cooldown)", range },
        { label: "WAIT", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "WAIT ${1:1}", insertTextRules: monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: "Pause N seconds", range },
        { label: "FOR", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "FOR ${1:4}\n  ${2}\nEND", insertTextRules: monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: "Loop N times", range },
        { label: "IF SPOTTED", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "IF SPOTTED\n  ${1}\nEND", insertTextRules: monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: "Conditional: enemy in line of sight", range },
        { label: "IF NOT_SPOTTED", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "IF NOT_SPOTTED\n  ${1}\nEND", insertTextRules: monacoInstance.languages.CompletionItemInsertTextRule.InsertAsSnippet, detail: "Conditional: no enemy in line of sight", range },
        { label: "END", kind: monacoInstance.languages.CompletionItemKind.Keyword, insertText: "END", detail: "Close FOR/IF block", range },
      ];

      return { suggestions };
    },
  });

  // ── Custom theme ──
  monacoInstance.editor.defineTheme("tankscript-dark", {
    base: "vs-dark",
    inherit: true,
    rules: [
      { token: "keyword", foreground: "569cd6", fontStyle: "bold" },
      { token: "keyword.condition", foreground: "c586c0", fontStyle: "bold" },
      { token: "keyword.target", foreground: "4ec9b0" },
      { token: "number", foreground: "ce9178" },
      { token: "comment", foreground: "6a9955", fontStyle: "italic" },
      { token: "identifier", foreground: "d4d4d4" },
    ],
    colors: {
      "editor.background": "#0e0e11",
      "editor.foreground": "#d4d4d4",
      "editorLineNumber.foreground": "#555555",
      "editor.lineHighlightBackground": "#1a1a1f",
      "editorCursor.foreground": "#80cc50",
    },
  });
}
