import { RunAgentInput } from '@ag-ui/client';
import { HttpAgent } from '@ag-ui/client';

/**
 * HttpAgent that keeps reasoning messages out of the outbound history and
 * attaches the OIDC-style bearer token to every agent request.
 *
 * The backend AG-UI endpoint maps replayed history with a role whitelist
 * (system/user/assistant/developer/tool) and fails an extra turn with
 * "Unknown chat role: reasoning" when the client replays the reasoning
 * messages created from REASONING_MESSAGE_* events. The filter only applies
 * to the serialized request: the local transcript keeps reasoning entries,
 * and `messages` must stay untouched while a run streams so replies received
 * during the run are replayed in the next turn's history.
 */
export class TranslationHttpAgent extends HttpAgent {
  /** Source of the current access token; set once at app startup. */
  static accessTokenProvider: () => string | null = () => null;

  protected override requestInit(input: RunAgentInput): RequestInit {
    const outbound: RunAgentInput = {
      ...input,
      messages: input.messages.filter((message) => message.role !== 'reasoning'),
    };
    const init = super.requestInit(outbound);
    const token = TranslationHttpAgent.accessTokenProvider();
    if (!token) return init;
    const headers = new Headers(init.headers ?? this.headers);
    headers.set('Authorization', `Bearer ${token}`);
    return { ...init, headers };
  }
}


