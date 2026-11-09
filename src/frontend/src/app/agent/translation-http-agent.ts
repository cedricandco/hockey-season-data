import { Message, RunAgentInput, RunAgentParameters } from '@ag-ui/client';
import { HttpAgent } from '@ag-ui/client';

type Subscriber = Parameters<HttpAgent['subscribe']>[0];

interface RunAgentResult {
  result: unknown;
  newMessages: Message[];
}

/**
 * HttpAgent that keeps reasoning messages out of the outbound history.
 *
 * The backend AG-UI endpoint maps replayed history with a role whitelist
 * (system/user/assistant/developer/tool) and fails an extra turn with
 * "Unknown chat role: reasoning" when the client replays the reasoning
 * messages created from REASONING_MESSAGE_* events. Removing them before the
 * request keeps every following turn working; they are restored afterwards so
 * the local transcript stays complete.
 */
export class TranslationHttpAgent extends HttpAgent {
  protected override requestInit(input: RunAgentInput): RequestInit {
    const kept = this.messages;
    this.messages = kept.filter((message: Message) => message.role !== 'reasoning');
    try {
      return super.requestInit(input);
    } finally {
      this.messages = kept;
    }
  }

  override async runAgent(
    parameters?: RunAgentParameters,
    subscriber?: Subscriber,
  ): Promise<RunAgentResult> {
    const kept = this.messages;
    this.messages = kept.filter((message: Message) => message.role !== 'reasoning');
    try {
      return await super.runAgent(parameters, subscriber);
    } finally {
      this.messages = kept;
    }
  }
}
