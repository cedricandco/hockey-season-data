import { Component, inject } from '@angular/core';
import { TranslatePipe } from '@ngx-translate/core';
import { ThemeService } from '../core/theme.service';

/**
 * Configuration page: per-device primary/secondary color choices, persisted
 * in local storage by the ThemeService.
 */
@Component({
  selector: 'app-config',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <section class="page-container" aria-labelledby="config-title">
      <h1 id="config-title" class="page-title">{{ 'THEME.TITLE' | translate }}</h1>
      <p class="page-subtitle">{{ 'THEME.DESCRIPTION' | translate }}</p>
      <div class="config-card">
        <label class="setting-row">
          <span>{{ 'THEME.PRIMARY' | translate }}</span>
          <input type="color" [value]="theme.primaryColor()" (input)="theme.setColor('primary', $event)">
        </label>
        <label class="setting-row">
          <span>{{ 'THEME.SECONDARY' | translate }}</span>
          <input type="color" [value]="theme.secondaryColor()" (input)="theme.setColor('secondary', $event)">
        </label>
        <button class="reset-button" type="button" (click)="theme.resetColors()">{{ 'THEME.RESET' | translate }}</button>
        @if (theme.storageError()) {
          <p class="storage-error" role="alert">{{ 'THEME.STORAGE_ERROR' | translate }}</p>
        }
      </div>
    </section>
  `,
})
export class ConfigurationComponent {
  readonly theme = inject(ThemeService);
}
