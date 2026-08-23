#!/usr/bin/env node

/**
 * md-to-bbcode.js
 * Converts Markdown to Steam BBCode.
 *
 * Usage (stdin → stdout):
 *   echo "# Hello" | node md-to-bbcode.js
 *   node md-to-bbcode.js input.md
 *   node md-to-bbcode.js input.md output.txt
 *
 * GitHub Actions example:
 *   - run: node md-to-bbcode.js CHANGELOG.md > release_notes.txt
 */

"use strict";

const fs = require("fs");
const path = require("path");

// ---------------------------------------------------------------------------
// Core conversion
// ---------------------------------------------------------------------------

/**
 * Convert a Markdown string to Steam BBCode.
 * @param {string} md
 * @returns {string}
 */
function markdownToBBCode(md) {
  // Normalise line endings
  let out = md.replace(/\r\n/g, "\n").replace(/\r/g, "\n");

  // ── Fenced code blocks (``` … ```) ─────────────────────────────────────
  // Extracted early to protect their contents from further processing.
  const codeBlocks = [];
  out = out.replace(/```[\w]*\n([\s\S]*?)```/g, (_, code) => {
    const ph = `\x00CODE${codeBlocks.length}\x00`;
    codeBlocks.push(`[code]${code.trimEnd()}[/code]`);
    return ph;
  });

  // ── Inline code (`code`) ────────────────────────────────────────────────
  const inlineCodes = [];
  out = out.replace(/`([^`]+)`/g, (_, code) => {
    const ph = `\x00INLINE${inlineCodes.length}\x00`;
    inlineCodes.push(`[code]${code}[/code]`);
    return ph;
  });

  // ── Horizontal rules (---, ***, ___) ───────────────────────────────────
  // Replace with a placeholder to avoid confusing later link-regex passes.
  const HR = "\x00HR\x00";
  out = out.replace(/^[ \t]*(?:[-*_][ \t]*){3,}$/gm, HR);

  // ── Setext-style headings (must run BEFORE ATX headings) ────────────────
  out = out.replace(/^([^\n\x00]+)\n={3,}$/gm, "[h1]$1[/h1]");
  out = out.replace(/^([^\n\x00]+)\n-{3,}$/gm, "[h2]$1[/h2]");

  // ── ATX Headings (#, ##, ###) ───────────────────────────────────────────
  out = out.replace(/^### (.+)$/gm, "[h3]$1[/h3]");
  out = out.replace(/^## (.+)$/gm, "[h2]$1[/h2]");
  out = out.replace(/^# (.+)$/gm, "[h1]$1[/h1]");

  // ── Blockquotes (> …) ──────────────────────────────────────────────────
  // Supports optional "author" via > **Author:** or > *Author:*
  out = out.replace(
    /^> \*{1,2}([^*\n]+):\*{1,2} *([^\n]*(?:\n> [^\n]*)*)$/gm,
    (_, author, rest) => {
      const text = rest.replace(/\n> /g, "\n").trim();
      return `[quote=${author.trim()}]${text}[/quote]`;
    },
  );
  // Plain blockquote (no author)
  out = out.replace(/^((?:> [^\n]*\n?)+)/gm, (block) => {
    const inner = block.replace(/^> ?/gm, "").trim();
    return `[quote]${inner}[/quote]`;
  });

  // ── Ordered lists ───────────────────────────────────────────────────────
  out = out.replace(/((?:^\d+\. .+\n?)+)/gm, (block) => {
    const items = block
      .trim()
      .split("\n")
      .map((line) => `    [*]${line.replace(/^\d+\. /, "").trim()}`)
      .join("\n");
    return `[olist]\n${items}\n[/olist]`;
  });

  // ── Unordered lists (-, *, +) ───────────────────────────────────────────
  out = out.replace(/((?:^[ \t]*[-*+] .+\n?)+)/gm, (block) => {
    const items = block
      .trim()
      .split("\n")
      .map((line) => `    [*]${line.replace(/^[ \t]*[-*+] /, "").trim()}`)
      .join("\n");
    return `[list]\n${items}\n[/list]`;
  });

  // ── Images → plain URL (no image-embed tag in Steam BBCode) ─────────────
  // Must run before inline links.
  out = out.replace(/!\[([^\]]*)\]\(([^)]+)\)/g, "$2");

  // ── Inline links [text](url) ────────────────────────────────────────────
  out = out.replace(/\[([^\]]+)\]\(([^)]+)\)/g, "[url=$2]$1[/url]");

  // ── Reference-style links [text][ref] + [ref]: url ──────────────────────
  const refs = {};
  out = out.replace(/^\[([^\]]+)\]: (\S+).*$/gm, (_, ref, url) => {
    refs[ref.toLowerCase()] = url;
    return "";
  });
  out = out.replace(/\[([^\]]+)\]\[([^\]]*)\]/g, (match, text, ref) => {
    const key = (ref || text).toLowerCase();
    // Only replace when we have an actual reference definition; otherwise
    // leave the brackets intact so already-emitted BBCode tags aren't eaten.
    return refs[key] ? `[url=${refs[key]}]${text}[/url]` : match;
  });

  // ── Autolinks <url> ─────────────────────────────────────────────────────
  out = out.replace(/<(https?:\/\/[^>]+)>/g, "[url=$1]$1[/url]");

  // ── Bold + italic (***text*** or ___text___) ────────────────────────────
  out = out.replace(/\*{3}([^*\n]+)\*{3}/g, "[b][i]$1[/i][/b]");
  out = out.replace(/_{3}([^_\n]+)_{3}/g, "[b][i]$1[/i][/b]");

  // ── Bold (**text** or __text__) ─────────────────────────────────────────
  out = out.replace(/\*{2}([^*\n]+)\*{2}/g, "[b]$1[/b]");
  out = out.replace(/_{2}([^_\n]+)_{2}/g, "[b]$1[/b]");

  // ── Italic (*text* or _text_) ───────────────────────────────────────────
  // Negative lookbehind/ahead prevents matching [*] list markers.
  out = out.replace(/(?<!\[)\*([^*\n]+)\*(?!\])/g, "[i]$1[/i]");
  out = out.replace(/(?<!\w)_([^_\n]+)_(?!\w)/g, "[i]$1[/i]");

  // ── Strikethrough (~~text~~) ────────────────────────────────────────────
  out = out.replace(/~~([^~\n]+)~~/g, "[strike]$1[/strike]");

  // ── Restore placeholders ────────────────────────────────────────────────
  out = out.replace(/\x00HR\x00/g, "[hr][/hr]");
  out = out.replace(/\x00INLINE(\d+)\x00/g, (_, i) => inlineCodes[+i]);
  out = out.replace(/\x00CODE(\d+)\x00/g, (_, i) => codeBlocks[+i]);

  // ── Clean up extra blank lines ───────────────────────────────────────────
  out = out.replace(/\n{3,}/g, "\n\n");

  return out.trim();
}

// ---------------------------------------------------------------------------
// CLI entry-point
// ---------------------------------------------------------------------------

function main() {
  const [, , inputArg, outputArg] = process.argv;

  let input;

  if (inputArg) {
    const inputPath = path.resolve(inputArg);
    if (!fs.existsSync(inputPath)) {
      console.error(`::error:: file not found: ${inputPath}`);
      process.exit(1);
    }
    input = fs.readFileSync(inputPath, "utf8");
  } else {
    try {
      input = fs.readFileSync("/dev/stdin", "utf8");
    } catch {
      console.error(
        "::error:: provide an input file or pipe content via stdin.",
      );
      process.exit(1);
    }
  }

  const result = markdownToBBCode(input);

  if (outputArg) {
    fs.writeFileSync(path.resolve(outputArg), result, "utf8");
    console.error(`Written to ${outputArg}`); // stderr keeps stdout clean
  } else {
    process.stdout.write(result + "\n");
  }
}

// Allow importing as a module too
if (require.main === module) {
  main();
}

module.exports = { markdownToBBCode };
