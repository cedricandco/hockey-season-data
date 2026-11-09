import { Component, effect, ElementRef, inject, signal, viewChild } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { CopilotKit, injectAgentStore } from '@copilotkit/angular';
import { HOCKEY_SEASON_DATA_AGENT_ID } from '../app.config';
import { AuthService } from '../core/auth.service';

type Language = 'fr' | 'en';
type ThemeColor = 'primary' | 'secondary';

const DEFAULT_COLORS: Record<ThemeColor, string> = {
  primary: '#0e3a70',
  secondary: '#3eb838',
};
const THEME_STORAGE_KEY = 'hockey-season-theme';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <header class="site-header">
      <div class="header-inner">
        <a class="brand" href="/" [attr.aria-label]="'APP.TITLE' | translate">
          <span class="brand-mark" aria-hidden="true">
            <svg viewBox="0 0 32 32" fill="none">
              <path d="M7 6l12 15m6-15L13 21M9 24h14M11 27h10" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"/>
              <circle cx="16" cy="21" r="3" fill="currentColor"/>
            </svg>
          </span>
          <span class="brand-name">HOCKEY <span>SEASON DATA</span></span>
        </a>
        <div class="header-actions">
          <button class="header-button language-button" type="button" (click)="toggleLanguage()"
            [attr.aria-label]="'LANGUAGE.BUTTON' | translate">
            <span aria-hidden="true">◎</span> {{ language() === 'fr' ? 'EN' : 'FR' }}
          </button>
          <button class="header-button settings-button" type="button" (click)="settingsOpen.update(open => !open)"
            [attr.aria-expanded]="settingsOpen()" [attr.aria-label]="'THEME.BUTTON' | translate" aria-controls="theme-settings">
            <span aria-hidden="true">⚙</span><span class="settings-label">{{ 'THEME.BUTTON' | translate }}</span>
          </button>
          <button class="header-button logout-button" type="button" (click)="logout()"
            [attr.aria-label]="'LOGOUT.BUTTON' | translate">
            <span aria-hidden="true">⎋</span><span class="settings-label">{{ 'LOGOUT.BUTTON' | translate }}</span>
          </button>
          @if (settingsOpen()) {
            <section id="theme-settings" class="theme-panel" [attr.aria-label]="'THEME.TITLE' | translate">
              <h2>{{ 'THEME.TITLE' | translate }}</h2>
              <p>{{ 'THEME.DESCRIPTION' | translate }}</p>
              <label class="color-setting">
                <span>{{ 'THEME.PRIMARY' | translate }}</span>
                <input type="color" [value]="primaryColor()" (input)="setColor('primary', $event)">
              </label>
              <label class="color-setting">
                <span>{{ 'THEME.SECONDARY' | translate }}</span>
                <input type="color" [value]="secondaryColor()" (input)="setColor('secondary', $event)">
              </label>
              <button class="reset-button" type="button" (click)="resetColors()">{{ 'THEME.RESET' | translate }}</button>
              @if (storageError()) {
                <p class="storage-error" role="alert">{{ 'THEME.STORAGE_ERROR' | translate }}</p>
              }
            </section>
          }
        </div>
      </div>
    </header>

    <main class="main-content">
      <div class="intro">
        <div class="eyebrow"><span></span>{{ 'HOME.EYEBROW' | translate }}</div>
        <h1>{{ 'HOME.TITLE' | translate }} <span>{{ 'HOME.TITLE_ACCENT' | translate }}</span></h1>
        <p>{{ 'HOME.DESCRIPTION' | translate }}</p>
      </div>

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
            @if (message.role !== 'reasoning' && (message.role === 'user' || message.role === 'assistant') && messageText(message.content)) {
              <div class="message-row" [class.user-row]="message.role === 'user'">
                @if (message.role === 'assistant') {
                  <div class="message-avatar" aria-hidden="true">✦</div>
                }
                <div class="message-content">
                  <span class="message-author">{{ (message.role === 'user' ? 'CHAT.YOU' : 'CHAT.AGENT') | translate }}</span>
                  <div class="message-bubble" [class.user-bubble]="message.role === 'user'"
                    [class.assistant-bubble]="message.role === 'assistant'">{{ messageText(message.content) }}</div>
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
            <textarea id="chat-input" rows="2" [value]="draft()" (input)="updateDraft($event)"
              (keydown)="onInputKeydown($event)" [placeholder]="'CHAT.PLACEHOLDER' | translate"></textarea>
            <button type="submit" class="send-button" [disabled]="sending() || !draft().trim()"
              [attr.aria-label]="'CHAT.SEND' | translate">
              <svg viewBox="0 0 24 24" fill="none" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>
            </button>
          </div>
          <div class="composer-hint">{{ 'CHAT.HINT' | translate }}</div>
        </form>
      </section>
      <footer class="page-footer">{{ 'HOME.FOOTER' | translate }}</footer>
    </main>
  `,
})
export class HomeComponent {
  private readonly translate = inject(TranslateService);
  private readonly copilotKit = inject(CopilotKit);
  private readonly auth = inject(AuthService);
  private readonly conversation = viewChild<ElementRef<HTMLElement>>('conversation');
  readonly store = injectAgentStore(HOCKEY_SEASON_DATA_AGENT_ID);
  readonly language = signal<Language>('fr');
  readonly settingsOpen = signal(false);
  readonly primaryColor = signal(DEFAULT_COLORS.primary);
  readonly secondaryColor = signal(DEFAULT_COLORS.secondary);
  readonly storageError = signal(false);
  readonly draft = signal('');
  readonly sending = signal(false);
  readonly error = signal(false);

  constructor() {
    this.loadColors();
    effect(() => {
      this.store().messages();
      this.sending();
      const element = this.conversation()?.nativeElement;
      if (element) {
        requestAnimationFrame(() => element.scrollTop = element.scrollHeight);
      }
    });
  }

  toggleLanguage(): void {
    const next: Language = this.language() === 'fr' ? 'en' : 'fr';
    this.language.set(next);
    document.documentElement.lang = next;
    this.translate.use(next);
  }

  logout(): void {
    this.auth.logout();
  }

  setColor(color: ThemeColor, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.applyColor(color, value);
    this.saveColors();
  }

  resetColors(): void {
    this.applyColor('primary', DEFAULT_COLORS.primary);
    this.applyColor('secondary', DEFAULT_COLORS.secondary);
    this.saveColors();
  }

  updateDraft(event: Event): void {
    this.draft.set((event.target as HTMLTextAreaElement).value);
  }

  onInputKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      void this.send();
    }
  }

  async send(event?: Event): Promise<void> {
    event?.preventDefault();
    const content = this.draft().trim();
    if (!content || this.sending()) return;

    this.store().agent.addMessage({ id: crypto.randomUUID(), role: 'user', content });
    this.draft.set('');
    await this.runAgent();
  }

  async retry(): Promise<void> {
    if (!this.sending()) await this.runAgent();
  }

  messageText(content: unknown): string {
    return typeof content === 'string' ? content : '';
  }

  private async runAgent(): Promise<void> {
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
      if (this.error()) agent.setMessages(messagesBeforeRun);
      this.sending.set(false);
    }
  }

  private applyColor(color: ThemeColor, value: string): void {
    (color === 'primary' ? this.primaryColor : this.secondaryColor).set(value);
    document.documentElement.style.setProperty(`--color-${color}`, value);
  }

  private loadColors(): void {
    try {
      const stored = localStorage.getItem(THEME_STORAGE_KEY);
      if (!stored) return;
      const colors: unknown = JSON.parse(stored);
      if (typeof colors !== 'object' || colors === null) throw new Error('Invalid saved theme');
      for (const color of ['primary', 'secondary'] as const) {
        const value = (colors as Record<string, unknown>)[color];
        if (typeof value !== 'string' || !/^#[0-9a-f]{6}$/i.test(value)) throw new Error('Invalid saved theme color');
      }
      const validColors = colors as Record<ThemeColor, string>;
      this.applyColor('primary', validColors.primary);
      this.applyColor('secondary', validColors.secondary);
    } catch (error) {
      console.error('Unable to load theme', error);
      this.storageError.set(true);
    }
  }

  private saveColors(): void {
    try {
      localStorage.setItem(THEME_STORAGE_KEY, JSON.stringify({
        primary: this.primaryColor(),
        secondary: this.secondaryColor(),
      }));
      this.storageError.set(false);
    } catch (error) {
      console.error('Unable to save theme', error);
      this.storageError.set(true);
    }
  }
}
