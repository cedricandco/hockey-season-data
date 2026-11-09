import { Component, computed, inject, signal } from '@angular/core';
import {
  IonButton,
  IonButtons,
  IonContent,
  IonHeader,
  IonTitle,
  IonToolbar,
  IonList,
  IonItem,
  IonLabel,
  IonNote,
} from '@ionic/angular';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import {
  COPILOT_CHAT_DEFAULT_LABELS,
  COPILOT_CHAT_LABELS,
  CopilotSidebar,
} from '@copilotkit/angular';
import { ApiService } from '../core/api.service';

type Status = 'idle' | 'ok' | 'error';
type Language = 'fr' | 'en';

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [
    TranslatePipe,
    IonHeader,
    IonToolbar,
    IonButtons,
    IonButton,
    IonTitle,
    IonContent,
    IonList,
    IonItem,
    IonLabel,
    IonNote,
    CopilotSidebar,
  ],
  // Resolved lazily when the chat is first created (translations are loaded by
  // then); the @if language guard below recreates the chat on language switch.
  providers: [
    {
      provide: COPILOT_CHAT_LABELS,
      useFactory: (translate: TranslateService) => ({
        ...COPILOT_CHAT_DEFAULT_LABELS,
        chatInputPlaceholder: translate.instant('HOME.CHAT.PLACEHOLDER'),
        welcomeMessageText: translate.instant('HOME.CHAT.TITLE'),
      }),
      deps: [TranslateService],
    },
  ],
  template: `
    <ion-header [translucent]="true">
      <ion-toolbar>
        <ion-title>{{ 'APP.TITLE' | translate }}</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="toggleLanguage()" fill="solid" size="small" color="medium">
            {{ language() === 'fr' ? 'EN' : 'FR' }}
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true" class="ion-padding">
      <h1>{{ 'HOME.TITLE' | translate }}</h1>
      <p>{{ 'HOME.DESCRIPTION' | translate }}</p>

      <ion-button (click)="pingApi()" [disabled]="loading()" expand="block">
        {{ 'HOME.PING_BUTTON' | translate }}
      </ion-button>

      @if (status() !== 'idle') {
        <p>
          <span class="status-chip" [class.ok]="status() === 'ok'" [class.error]="status() === 'error'">
            {{ (status() === 'ok' ? 'HOME.STATUS.OK' : 'HOME.STATUS.ERROR') | translate }}
          </span>
        </p>
      }

      @if (serverTimeDisplay(); as display) {
        <ion-list inset="true">
          <ion-item>
            <ion-label>{{ 'HOME.SERVER_TIME' | translate }}</ion-label>
            <ion-note slot="end">{{ display }}</ion-note>
          </ion-item>
        </ion-list>
      }

      <p>{{ 'HOME.CHAT.DESCRIPTION' | translate }}</p>
      <ion-button (click)="chatOpen.set(true)" fill="solid" color="primary" expand="block">
        {{ 'HOME.CHAT.OPEN' | translate }}
      </ion-button>

      <p class="app-footer-note">
        {{ 'LANGUAGE.SWITCH' | translate }}
      </p>
    </ion-content>

    <!-- CopilotSidebar projects no content and renders its own chat, which
         resolves the agent from the ambient chat configuration. -->
    @if (chatOpen()) {
      @if (language()) {
        <copilot-sidebar
          [(open)]="chatOpen"
          mode="overlay"
          position="right"
          [title]="'HOME.CHAT.TITLE' | translate"
        />
      }
    }
  `,
})
export class HomeComponent {
  private readonly api = inject(ApiService);
  private readonly translateService = inject(TranslateService);

  readonly loading = signal(false);
  readonly status = signal<Status>('idle');
  readonly language = signal<Language>('fr');

  readonly chatOpen = signal(false);

  private readonly serverTime = signal<string | null>(null);

  /** Server time formatted for the active language (UTC). */
  readonly serverTimeDisplay = computed(() => {
    const value = this.serverTime();
    if (!value) {
      return null;
    }
    const locale = this.language() === 'fr' ? 'fr-CA' : 'en-CA';
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return value;
    }
    return new Intl.DateTimeFormat(locale, {
      dateStyle: 'long',
      timeStyle: 'medium',
      timeZone: 'UTC',
    }).format(date);
  });

  toggleLanguage(): void {
    const next: Language = this.language() === 'fr' ? 'en' : 'fr';
    this.language.set(next);
    document.documentElement.lang = next;
    // use() loads translations over HTTP and updates the language internally.
    this.translateService.use(next);
  }

  pingApi(): void {
    this.loading.set(true);
    this.api.getHealth().subscribe({
      next: (report) => {
        this.serverTime.set(report.time?.utcNow ?? null);
        this.status.set('ok');
        this.loading.set(false);
      },
      error: () => {
        this.serverTime.set(null);
        this.status.set('error');
        this.loading.set(false);
      },
    });
  }
}
