import { marked, Renderer } from 'marked';

const renderer = new Renderer();
renderer.html = (html: string) => html.replace(/[&<>"']/g, character => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
})[character]!);

/** Render agent Markdown; the template applies Angular's HTML sanitizer. */
export function assistantMarkdown(text: string): string {
  const options = { gfm: true, breaks: true, renderer };
  return marked.parser(marked.lexer(text, options), options);
}
