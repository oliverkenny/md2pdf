# md2pdf

Local Markdown → PDF converter CLI that turns Markdown into styled PDFs using Markdig + Playwright.

## Features

- Convert single files or all Markdown files in a folder
- Optional recursive conversion with folder mirroring
- Theme folders with CSS/template overrides
- Built-in default theme (auto-created on first run)

## Quick start

```bash
md2pdf README.md
md2pdf -a -o ./out
md2pdf -a -r -o ./out --paper letter --margins "20mm 15mm"
```

## Theme workflow

Create or open the local theme folder:

```bash
md2pdf --init-style
md2pdf --style
```

Theme resolution order:

1. `./.md2pdf/styles/<theme>/`
2. `~/.config/md2pdf/styles/<theme>/` (or OS-specific app config folder)
3. Built-in default theme

## Arguments

- `-a`, `--all`: Convert all `*.md` in current folder
- `-r`, `--recursive`: When paired with `-a`, search recursively
- `-o`, `--output <dir>`: Output directory
- `--style`: Open the active theme folder
- `--init-style`: Create the local theme folder
- `--theme <name>`: Select a theme
- `--paper <A4|Letter>`: PDF paper format
- `--margin` / `--margins`: Margins (1–4 values)
- `--toc`: Insert a table of contents
- `--verbose`: Log conversion details
- `--dry-run`: Print output mappings without generating PDFs

## Notes

- Playwright requires browser binaries. Install once with:
  `playwright install`
