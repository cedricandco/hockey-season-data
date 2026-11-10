import { Component, ElementRef, effect, inject, signal, viewChild } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { CopilotKit, injectAgentStore, RenderToolCalls } from '@copilotkit/angular';
import { HOCKEY_SEASON_DATA_AGENT_ID } from '../app.config';
import { registerHockeySeasonComponents } from './game-card-registration';

/**
 * Chat page: conversation with the translation agent. The page fills the
 * space below the header, the transcript scrolls and the composer stays
 * pinned at the bottom of the viewport.
 */
@Component({
  selector: 'app-chat',
  standalone: true,
  imports: [TranslatePipe, RenderToolCalls],
  template: `
    <section class="chat-card" [attr.aria-label]="'CHAT.TITLE' | translate">
      <div class="chat-header">
        <div class="agent-avatar" aria-hidden="true">✦</div>
        <div class="agent-details">
          <h2>{{ 'CHAT.TITLE' | translate }}</h2>
          <p>{{ 'CHAT.SUBTITLE' | translate }}</p>
        </div>
        <span class="agent-badge"><span></span>{{ 'CHAT.BADGE' | translate }}</span>
      </div>
      <div #conversation class="conversation" aria-live="polite" aria-relevant="additions text">
        <div class="welcome-message">
          <div class="message-avatar" aria-hidden="true">✦</div>
          <div>
            <span class="message-author">{{ 'CHAT.AGENT' | translate }}</span>
            <div class="message-bubble assistant-bubble">
              <p>{{ 'CHAT.WELCOME' | translate }}</p>
              <p>{{ 'CHAT.WELCOME_DETAIL' | translate }}</p>
            </div>
          </div>
        </div>
        @for (message of store().messages(); track message.id) {
          @if (message.role === 'user' && messageText(message.content)) {
            <div class="message-row" [class.user-row]="message.role === 'user'">
              <div class="message-content">
                <span class="message-author">{{ 'CHAT.YOU' | translate }}</span>
                <div class="message-bubble user-bubble">{{ messageText(message.content) }}</div>
              </div>
            </div>
          } @else if (message.role === 'assistant' && (messageText(message.content) || message.toolCalls?.length)) {
            <div class="message-row">
              <div class="message-avatar" aria-hidden="true">✦</div>
              <div class="message-content">
                <span class="message-author">{{ 'CHAT.AGENT' | translate }}</span>
                <div class="message-bubble assistant-bubble">
                  @if (messageText(message.content)) {
                    <p>{{ messageText(message.content) }}</p>
                  }
                  @if (message.toolCalls?.length) {
                    <copilot-render-tool-calls [message]="message" [messages]="store().messages()"
                      [isLoading]="sending()" [agentId]="agentId" />
                  }
                </div>
              </div>
            </div>
          }
        }
        @if (sending()) {
          <div class="typing-indicator" role="status"><span></span><span></span><span></span> {{ 'CHAT.THINKING' | translate }}</div>
        }
        @if (error()) {
          <div class="chat-error" role="alert">
            {{ 'CHAT.ERROR' | translate }}
            <button type="button" (click)="retry()" [disabled]="sending()">{{ 'CHAT.RETRY' | translate }}</button>
          </div>
        }
      </div>

      <form class="composer-area" (submit)="send($event)">
        <div class="composer">
          <label class="sr-only" for="chat-input">{{ 'CHAT.INPUT_LABEL' | translate }}</label>
          <textarea #composerInput id="chat-input" rows="2" [value]="draft()" (input)="updateDraft($event)"
            (keydown)="onInputKeydown($event)" [placeholder]="'CHAT.PLACEHOLDER' | translate"></textarea>
          <button type="submit" class="send-button" [disabled]="sending() || !draft().trim()"
            [attr.aria-label]="'CHAT.SEND' | translate">
            <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
          </button>
        </div>
        <div class="composer-hint">{{ 'CHAT.HINT' | translate }}</div>
      </form>
    </section>
  `,
})
export class ChatComponent {
  private readonly copilotKit = inject(CopilotKit);
  private readonly conversation = viewChild<ElementRef<HTMLElement>>('conversation');
  private readonly composerInput = viewChild<ElementRef<HTMLTextAreaElement>>('composerInput');
  readonly store = injectAgentStore(HOCKEY_SEASON_DATA_AGENT_ID);
  readonly agentId = HOCKEY_SEASON_DATA_AGENT_ID;
  readonly draft = signal('');
  readonly sending = signal(false);
  readonly error = signal(false);

  constructor() {
    registerHockeySeasonComponents();
    effect(() => {
      this.store().messages();
      this.sending();
      const element = this.conversation()?.nativeElement;
      if (element) {
        requestAnimationFrame(() => element.scrollTop = element.scrollHeight);
      }
    });
  }

  updateDraft(event: Event): void {
    const textarea = event.target as HTMLTextAreaElement;
    this.draft.set(textarea.value);
    this.autoSize(textarea);
  }

  onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      void this.send();
    }
  }

  /** Grows the composer with its content, up to the CSS max-height. */
  private autoSize(textarea?: HTMLTextAreaElement): void {
    if (!textarea) return;
    textarea.style.height = 'auto';
    textarea.style.height = `${textarea.scrollHeight}px`;
  }

  async send(event?: Event): Promise<void> {
    event?.preventDefault();
    const content = this.draft().trim();
    if (!content || this.sending()) return;

    this.store().agent.addMessage({ id: crypto.randomUUID(), role: 'user', content });
    this.draft.set('');
    this.autoSize(this.composerInput()?.nativeElement);
    await this.runAgent(content);
  }

  async retry(): Promise<void> {
    if (!this.sending()) await this.runAgent();
  }

  messageText(content: unknown): string {
    return typeof content === 'string' ? content : '';
  }

  private async runAgent(restoreOnFailure?: string): Promise<void> {
    this.error.set(false);
    this.sending.set(true);
    const agent = this.store().agent;
    const messagesBeforeRun = [...agent.messages];
    const subscription = agent.subscribe({
      onRunFailed: ({ error }) => {
        console.error('Agent request failed', error);
        this.error.set(true);
      },
      onRunErrorEvent: ({ event }) => {
        console.error('Agent returned an error', event);
        this.error.set(true);
      },
    });
    try {
      await this.copilotKit.core.runAgent({ agent });
    } catch (error) {
      console.error('Agent request failed', error);
      this.error.set(true);
    } finally {
      subscription.unsubscribe();
      if (this.error()) {
        agent.setMessages(messagesBeforeRun);
        if (restoreOnFailure) this.draft.set(restoreOnFailure);
      }
      this.sending.set(false);
    }
  }
}
