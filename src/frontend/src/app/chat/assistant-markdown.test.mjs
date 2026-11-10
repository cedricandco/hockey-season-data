import assert from 'node:assert/strict';
import test from 'node:test';
import { assistantMarkdown } from './assistant-markdown.ts';

test('formats coach analysis with headings, emphasis, lists and line breaks', () => {
  const html = assistantMarkdown('## Portrait du duel\n\n**Remparts** : analyse\n\n- **Forme** : deux victoires\n- Discipline');
  assert.match(html, /<h2>Portrait du duel<\/h2>/);
  assert.match(html, /<strong>Remparts<\/strong>/);
  assert.match(html, /<ul>[\s\S]*<li><strong>Forme<\/strong>/);
  assert.match(assistantMarkdown('Première ligne\nDeuxième ligne'), /<br\s*\/?>/);
});

test('does not interpret raw HTML supplied by the agent', () => {
  const html = assistantMarkdown('<img src=x onerror=alert(1)>\n\n<script>alert(1)</script>');
  assert.doesNotMatch(html, /<img|<script/);
  assert.match(html, /&lt;img/);
  assert.match(html, /&lt;script/);
});
