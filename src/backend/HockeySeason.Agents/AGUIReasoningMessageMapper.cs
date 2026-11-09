using AGUI.Abstractions;

namespace HockeySeason.Agents;

/// <summary>
/// Fixes AG-UI history replay for reasoning models.
///
/// From the second turn on, AG-UI 1.x clients replay the
/// <see cref="AGUIReasoningMessage"/> (role "reasoning") entries they built
/// from the REASONING_MESSAGE_* events emitted for reasoning models, but the
/// AG-UI server conversion only maps system/user/assistant/developer/tool and
/// fails with "Unknown chat role: reasoning" (HTTP 500). Dropping those
/// entries from the inbound history keeps every turn working.
/// </summary>
public static class AGUIReasoningMessageMapper
{
    public static RunAgentInput DropReasoningHistory(RunAgentInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.Messages.OfType<AGUIReasoningMessage>().Any())
        {
            input.Messages = [.. input.Messages.Where(message => message is not AGUIReasoningMessage)];
        }

        return input;
    }
}
