import assert from 'node:assert/strict';
import test from 'node:test';
import { lastValueFrom } from 'rxjs';
import { HockeyAssistantHttpAgent } from './hockey-assistant-http-agent.ts';

const input = {
  threadId: 'thread', runId: 'run', state: {}, tools: [], context: [], forwardedProps: {},
  messages: [
    { id: 'user', role: 'user', content: 'Hello' },
    { id: 'reasoning', role: 'reasoning', content: 'Thinking' },
  ],
};

const response = () => new Response(
  'data: {"type":"RUN_STARTED","threadId":"thread","runId":"run"}\n\n' +
  'data: {"type":"RUN_FINISHED","threadId":"thread","runId":"run"}\n\n',
  { headers: { 'Content-Type': 'text/event-stream' } },
);

test('agent and its clone await a fresh token for every request and preserve history locally', async () => {
  const requests = [];
  let token = 'first';
  HockeyAssistantHttpAgent.accessTokenProvider = async () => {
    await new Promise((resolve) => setTimeout(resolve, 5));
    return token;
  };
  const agent = new HockeyAssistantHttpAgent({
    url: '/api/agui/test',
    headers: { 'X-Custom': 'value' },
    fetch: async (url, init) => {
      requests.push({ url, init });
      return response();
    },
  });

  await lastValueFrom(agent.run(input));
  token = 'renewed';
  await lastValueFrom(agent.clone().run(input));

  assert.equal(requests.length, 2);
  for (const [index, request] of requests.entries()) {
    const headers = new Headers(request.init.headers);
    const expected = `Bearer ${index === 0 ? 'first' : 'renewed'}`;
    assert.equal(headers.get('Authorization'), expected);
    assert.equal(headers.get('X-Authorization'), expected);
    assert.equal(headers.get('X-Custom'), 'value');
    assert.deepEqual(JSON.parse(request.init.body).messages.map((message) => message.role), ['user']);
  }
  assert.equal(input.messages.length, 2);
});

test('token acquisition failures do not send an anonymous request', async () => {
  let called = false;
  const agent = new HockeyAssistantHttpAgent({
    url: '/api/agui/test',
    fetch: async () => { called = true; return response(); },
  });
  HockeyAssistantHttpAgent.accessTokenProvider = async () => { throw new Error('Auth0 unavailable'); };
  await assert.rejects(lastValueFrom(agent.run(input)), /Auth0 unavailable/);
  HockeyAssistantHttpAgent.accessTokenProvider = async () => '';
  await assert.rejects(lastValueFrom(agent.run(input)), /No access token/);
  assert.equal(called, false);
});
