import { HttpAgent, type HttpAgentConfig, type RunAgentInput } from '@ag-ui/client';

/**
 * HttpAgent that keeps reasoning messages out of the outbound history and
 * attaches the OIDC-style bearer token to every agent request in both the
 * `Authorization` and `X-Authorization` headers (same dual-header contract
 * as the `authHeaderMirrorInterceptor` used for `/api/` calls).
 *
 * The backend AG-UI endpoint maps replayed history with a role whitelist
 * (system/user/assistant/developer/tool) and fails an extra turn with
 * "Unknown chat role: reasoning" when the client replays the reasoning
 * messages created from REASONING_MESSAGE_* events. The filter only applies
 * to the serialized request: the local transcript keeps reasoning entries,
 * and `messages` must stay untouched while a run streams so replies received
 * during the run are replayed in the next turn's history.
 */
export class HockeyAssistantHttpAgent extends HttpAgent {
  /** Source of the current access token; set once at app startup. */
  static accessTokenProvider: () => Promise<string | undefined> = async () => {
    throw new Error('The hockey assistant access token provider is not configured.');
  };

  constructor(config: HttpAgentConfig) {
    const fetchRequest = config.fetch ?? ((url, init) => fetch(url, init));
    super({
      ...config,
      fetch: async (url, init) => {
        const token = await HockeyAssistantHttpAgent.accessTokenProvider();
        if (!token) throw new Error('No access token is available for the hockey assistant.');
        const headers = new Headers(init.headers);
        headers.set('Authorization', `Bearer ${token}`);
        headers.set('X-Authorization', `Bearer ${token}`);
        return fetchRequest(url, { ...init, headers });
      },
    });
  }

  protected override requestInit(input: RunAgentInput): RequestInit {
    const outbound: RunAgentInput = {
      ...input,
      messages: input.messages.filter((message) => message.role !== 'reasoning'),
    };
    return super.requestInit(outbound);
  }
}
