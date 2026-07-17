using Microsoft.Playwright;

namespace CopilotBridge.Browser;

internal static class RenderedMarkdownExtractor
{
    private const string ExtractScript = """
        root => {
          const inline = node => {
            if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? '';
            if (node.nodeType !== Node.ELEMENT_NODE) return '';
            const element = node;
            const tag = element.tagName.toLowerCase();
            const content = Array.from(element.childNodes).map(inline).join('');
            if (tag === 'br') return '\n';
            if (tag === 'a') return `[${content.trim()}](${element.href})`;
            if (tag === 'code' && element.parentElement?.tagName.toLowerCase() !== 'pre') return `\`${content}\``;
            if (tag === 'strong' || tag === 'b') return `**${content}**`;
            if (tag === 'em' || tag === 'i') return `*${content}*`;
            return content;
          };

          const list = (element, depth) => {
            const ordered = element.tagName.toLowerCase() === 'ol';
            const items = Array.from(element.children).filter(child => child.tagName.toLowerCase() === 'li');
            return items.map((item, index) => {
              const nested = Array.from(item.children).filter(child => ['ul', 'ol'].includes(child.tagName.toLowerCase()));
              const body = Array.from(item.childNodes)
                .filter(child => !(child.nodeType === Node.ELEMENT_NODE && ['ul', 'ol'].includes(child.tagName.toLowerCase())))
                .map(inline).join('').trim();
              const prefix = ordered ? `${index + 1}. ` : '- ';
              const continuation = nested.map(child => list(child, depth + 1)).join('');
              return `${'  '.repeat(depth)}${prefix}${body}\n${continuation}`;
            }).join('');
          };

          const table = element => {
            const rows = Array.from(element.querySelectorAll('tr')).map(row =>
              Array.from(row.querySelectorAll(':scope > th, :scope > td')).map(cell => inline(cell).trim()));
            if (!rows.length || !rows[0].length) return '';
            const width = rows[0].length;
            const render = row => `| ${Array.from({length: width}, (_, i) => row[i] ?? '').join(' | ')} |\n`;
            return render(rows[0]) + render(Array(width).fill('---')) + rows.slice(1).map(render).join('') + '\n';
          };

          const block = node => {
            if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? '';
            if (node.nodeType !== Node.ELEMENT_NODE) return '';
            const element = node;
            const tag = element.tagName.toLowerCase();
            if (/^h[1-6]$/.test(tag)) return `${'#'.repeat(Number(tag[1]))} ${inline(element).trim()}\n\n`;
            if (tag === 'p') return `${inline(element).trim()}\n\n`;
            if (tag === 'ul' || tag === 'ol') return `${list(element, 0)}\n`;
            if (tag === 'pre') {
              const code = element.querySelector('code');
              const language = (code?.className.match(/language-([\w+-]+)/) ?? [])[1] ?? '';
              return `\`\`\`${language}\n${(code?.textContent ?? element.textContent ?? '').trimEnd()}\n\`\`\`\n\n`;
            }
            if (tag === 'table') return table(element);
            if (tag === 'blockquote') return `${inline(element).trim().split('\n').map(line => `> ${line}`).join('\n')}\n\n`;
            if (['div', 'section', 'article', 'main'].includes(tag)) {
              return Array.from(element.childNodes).map(block).join('');
            }
            return inline(element);
          };

          return Array.from(root.childNodes).map(block).join('')
            .replace(/[ \t]+\n/g, '\n')
            .replace(/\n{3,}/g, '\n\n')
            .trim();
        }
        """;

    internal static Task<string> ExtractAsync(ILocator root) =>
        root.EvaluateAsync<string>(ExtractScript);
}
