import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  FREE_PREVIEW_LIMIT,
  PREVIEW_TRIALS_KEY,
  canPreview,
  previewsLeft,
  previewsUsed,
  recordPreview,
} from './preview-trials.ts';

/** Minimal in-memory Storage stand-in (the helpers only use get/set). */
function memoryStorage(initial = {}) {
  const data = { ...initial };
  return {
    data,
    getItem: (key) => (key in data ? data[key] : null),
    setItem: (key, value) => { data[key] = String(value); },
  };
}

test('a fresh device gets the full number of free previews', () => {
  const storage = memoryStorage();
  assert.equal(previewsUsed(storage), 0);
  assert.equal(previewsLeft(storage), FREE_PREVIEW_LIMIT);
  assert.equal(canPreview(storage), true);
});

test('each preview is counted and the limit closes the gate', () => {
  const storage = memoryStorage();
  for (let attempt = 1; attempt <= FREE_PREVIEW_LIMIT; attempt++) {
    assert.equal(recordPreview(storage), attempt);
    assert.equal(storage.getItem(PREVIEW_TRIALS_KEY), String(attempt));
  }
  assert.equal(previewsLeft(storage), 0);
  assert.equal(canPreview(storage), false);
  // A fourth attempt is refused instead of unlocking another preview.
  assert.equal(recordPreview(storage), FREE_PREVIEW_LIMIT);
});

test('a tampered counter never unlocks more than the limit', () => {
  const storage = memoryStorage({ [PREVIEW_TRIALS_KEY]: '99' });
  assert.equal(previewsLeft(storage), 0);
  assert.equal(recordPreview(storage), FREE_PREVIEW_LIMIT);
  // Garbage falls back to "nothing used yet" rather than NaN.
  assert.equal(previewsUsed(memoryStorage({ [PREVIEW_TRIALS_KEY]: 'abc' })), 0);
  assert.equal(previewsUsed(memoryStorage({ [PREVIEW_TRIALS_KEY]: '-2' })), 0);
});

test('an unavailable storage is silent and never counts', () => {
  assert.equal(previewsUsed(null), 0);
  assert.equal(canPreview(null), true);
  assert.doesNotThrow(() => recordPreview(null));
  assert.equal(previewsLeft(null), FREE_PREVIEW_LIMIT);

  // A storage that throws on write (private mode / quota) still returns the
  // attempted count without crashing the page.
  const failing = { getItem: () => null, setItem: () => { throw new Error('quota'); } };
  assert.equal(recordPreview(failing), 1);
});
